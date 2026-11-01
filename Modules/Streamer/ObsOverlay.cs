using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost;

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class ObsOverlay
{
    public const int Port = 52525;
    public static string Url => $"http://localhost:{Port}/";
    public static string MapUrl => $"http://localhost:{Port}/map";
    public static string CommentsUrl => $"http://localhost:{Port}/comments";

    private static TcpListener listener;
    private static Thread thread;
    private static volatile string stateJson = "{}";
    private static float nextUpdateAt;

    public static string RoleModeLabel(int mode) => mode switch { 1 => "死んだら", 2 => "いつも", _ => "出さない" };

    // ===== 状態を作る(ゲームのスレッド) =====
    public static void Postfix()
    {
        try
        {
            // マップ・コメント欄だけ ON にしている時も、OBS 用のページを出す(今までは「OBS表示」も ON でないと開けなかった)
            if (!Main.ObsOverlay.Value && !Main.ObsShowMap.Value && !Main.ObsComments.Value) { Stop(); return; }
            Start();
            TrackPositions();
            if (Time.time < nextUpdateAt) return;
            nextUpdateAt = Time.time + 0.5f;
            stateJson = BuildState();
            mapJson = BuildMapState();
            commentJson = BuildComments();
        }
        catch (Exception e) { Logger.Warn($"OBS表示の更新に失敗: {e.Message}", "ObsOverlay"); }
    }

    private static string BuildState()
    {
        var local = PlayerControl.LocalPlayer;
        var mode = Main.ObsShowRoles.Value;
        var showRoles = mode == 2 || (mode == 1 && local != null && !local.IsAlive() && GameStates.InGame);
        var phase = GameStates.IsLobby ? "ロビー" : GameStates.IsMeeting ? "会議中" : GameStates.InGame ? "タスク中" : "";
        var map = GameStates.IsLobby ? "" : ((MapNames)Main.NormalOptions.MapId).ToString();
        var players = PlayerControl.AllPlayerControls.ToArray()
            .Where(p => p != null && p.Data != null && !p.Data.Disconnected)
            .OrderBy(p => p.PlayerId)
            .Select(p =>
            {
                string role = "", roleColor = "#ffffff";
                if (!GameStates.IsLobby && (showRoles || p.AmOwner))
                {
                    var r = p.GetCustomRole();
                    role = UtilsRoleText.GetRoleName(r).RemoveHtmlTags();
                    roleColor = UtilsRoleText.GetRoleColorCode(r);
                }
                var task = GameStates.IsLobby ? null : p.GetPlayerTaskState();
                var colorId = p.Data.DefaultOutfit?.ColorId ?? 0;
                var color = colorId >= 0 && colorId < Palette.PlayerColors.Length ? "#" + ColorUtility.ToHtmlStringRGB(Palette.PlayerColors[colorId]) : "#888888";
                return new
                {
                    name = (Main.AllPlayerNames.TryGetValue(p.PlayerId, out var n) ? n : p.Data.PlayerName).RemoveHtmlTags(),
                    color,
                    dead = !GameStates.IsLobby && !p.IsAlive(),
                    tasks = !GameStates.IsLobby && task != null && task.hasTasks && task.AllTasksCount > 0 ? $"{task.CompletedTasksCount}/{task.AllTasksCount}" : "",
                    role,
                    roleColor,
                };
            }).ToArray();
        return JsonSerializer.Serialize(new { phase, map, alive = players.Count(p => !p.dead), total = players.Length, players });
    }

    // ===== マップ(会議が始まった時の各プレイヤーの位置。死んだ人は死んだ場所) =====
    private static volatile string mapJson = "{}";
    private static volatile string commentJson = "{\"on\":false,\"items\":[]}";

    private static string BuildComments()
    {
        object[] items;
        lock (StreamerInfo.RecentComments)
            items = StreamerInfo.RecentComments.Select(c => (object)new { name = c.Name ?? "", text = c.Text ?? "" }).ToArray();
        return JsonSerializer.Serialize(new { on = Main.ObsComments.Value, items });
    }
    private static volatile byte[] mapPng;
    private static bool mapMissingLogged;
    private static int mapPngId = -1;
    // ワールド座標 → 画像上の位置(0〜1)への変換。uv = origin + x * axisX + y * axisY
    private static Vector2 uvOrigin, uvAxisX, uvAxisY;
    // 生きている間の最後の位置(死んだら死んだ場所のまま残る)
    private static readonly Dictionary<byte, Vector2> lastAlivePos = new();
    private static readonly Dictionary<byte, Vector2> meetingPos = new();
    private static bool wasMeeting;
    private static int meetingCount;

    private static void TrackPositions()
    {
        if (!Main.ObsShowMap.Value || !GameStates.InGame || GameStates.IsLobby)
        {
            if (GameStates.IsLobby && (lastAlivePos.Count > 0 || meetingPos.Count > 0 || mapPngId != -1))
            {
                // 試合ごとにマップを取り直す(同じマップでも、前の試合の部品は消えている)
                mapPngId = -1;
                mapPng = null;
                mapMissingLogged = false;
                lastAlivePos.Clear();
                meetingPos.Clear();
                meetingCount = 0;
            }
            wasMeeting = false;
            return;
        }
        var isMeeting = GameStates.IsMeeting;
        if (!isMeeting)
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected || !pc.IsAlive()) continue;
                lastAlivePos[pc.PlayerId] = pc.GetTruePosition();
            }
        }
        else if (!wasMeeting)
        {
            // 会議が始まった瞬間の位置を残す(会議の後は全員が湧き直すので、この時点が「最終位置」)
            meetingPos.Clear();
            foreach (var pair in lastAlivePos) meetingPos[pair.Key] = pair.Value;
            meetingCount++;
        }
        wasMeeting = isMeeting;
    }

    private static string BuildMapState()
    {
        if (!Main.ObsShowMap.Value || GameStates.IsLobby || !GameStates.InGame) return JsonSerializer.Serialize(new { ready = false, id = -1 });
        var mapId = (int)Main.NormalOptions.MapId;
        if ((mapPngId != mapId && !TryCaptureMap(mapId)) || mapPng == null) return JsonSerializer.Serialize(new { ready = false, id = -1 });

        var players = PlayerControl.AllPlayerControls.ToArray()
            .Where(p => p != null && p.Data != null && meetingPos.ContainsKey(p.PlayerId))
            .OrderBy(p => p.PlayerId)
            .Select(p =>
            {
                var w = meetingPos[p.PlayerId];
                var uv = uvOrigin + w.x * uvAxisX + w.y * uvAxisY;
                var colorId = p.Data.DefaultOutfit?.ColorId ?? 0;
                var color = colorId >= 0 && colorId < Palette.PlayerColors.Length ? "#" + ColorUtility.ToHtmlStringRGB(Palette.PlayerColors[colorId]) : "#888888";
                return new
                {
                    name = (Main.AllPlayerNames.TryGetValue(p.PlayerId, out var n) ? n : p.Data.PlayerName).RemoveHtmlTags(),
                    color,
                    dead = !p.IsAlive() || p.Data.Disconnected,
                    x = Mathf.Clamp01(uv.x),
                    y = Mathf.Clamp01(1f - uv.y),
                };
            }).ToArray();
        return JsonSerializer.Serialize(new { ready = true, id = mapId, meeting = meetingCount, players });
    }

    // 今のマップのミニマップ画像を取り出して、座標の変換も決めておく
    private static bool TryCaptureMap(int mapId)
    {
        var ship = ShipStatus.Instance;
        if (ship == null) return false;
        // 元の部品(MapPrefab)は画面に置かれておらず位置の計算ができなかったので、ゲームが作る本物のマップを使う(無ければ作らせる)
        if (MapBehaviour.Instance == null) { try { HudManager.Instance?.InitMap(); } catch { } }
        var map = MapBehaviour.Instance;
        if (map == null || map.HerePoint == null)
        {
            if (!mapMissingLogged) Logger.Warn($"OBSのマップ: ゲームのマップが見つかりません(map={mapId})", "ObsOverlay");
            mapMissingLogged = true;
            return false;
        }
        try
        {
            SpriteRenderer rend = map.ColorControl != null ? map.ColorControl.GetComponent<SpriteRenderer>() : null;
            if (rend == null || rend.sprite == null)
                rend = map.GetComponentsInChildren<SpriteRenderer>(true)
                    .Where(r => r != null && r.sprite != null)
                    .OrderByDescending(r => r.sprite.bounds.size.x * r.sprite.bounds.size.y)
                    .FirstOrDefault();
            if (rend == null) return false;

            var sprite = rend.sprite;
            var bounds = sprite.bounds;
            var hereParent = map.HerePoint.transform.parent ?? map.transform;
            var scale = ship.MapScale;
            var sign = Mathf.Sign(ship.transform.localScale.x);
            Vector2 ToUv(Vector2 world)
            {
                // ゲームのミニマップが自分の位置(HerePoint)を置くのと同じ計算
                var local = new Vector3(world.x / scale * sign, world.y / scale, 0f);
                var onBg = rend.transform.InverseTransformPoint(hereParent.TransformPoint(local));
                return new((onBg.x - bounds.min.x) / bounds.size.x, (onBg.y - bounds.min.y) / bounds.size.y);
            }
            uvOrigin = ToUv(Vector2.zero);
            uvAxisX = ToUv(Vector2.right) - uvOrigin;
            uvAxisY = ToUv(Vector2.up) - uvOrigin;

            mapPng = SpriteToPng(sprite);
            mapPngId = mapId;
            Logger.Info($"OBSのマップ画像を作りました: map={mapId} size={mapPng?.Length ?? 0}", "ObsOverlay");
            return mapPng != null;
        }
        catch (Exception e)
        {
            Logger.Warn($"OBSのマップ画像を作れませんでした: {e.Message}", "ObsOverlay");
            mapPngId = mapId;
            mapPng = null;
            return false;
        }
    }

    // 読み取り不可のテクスチャでも取り出せるよう、一度描き写してから読む
    private static byte[] SpriteToPng(Sprite sprite)
    {
        var tex = sprite.texture;
        var rect = sprite.textureRect;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
            copy.Apply();
            return copy.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (copy != null) UnityEngine.Object.Destroy(copy);
        }
    }

    // ===== 小さなWebサーバー(別スレッド・自分のPCからだけ) =====
    private static void Start()
    {
        if (listener != null) return;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            thread = new Thread(Serve) { IsBackground = true, Name = "TOH-hamo OBS" };
            thread.Start();
            Logger.Info($"OBS表示を開始しました: {Url}", "ObsOverlay");
        }
        catch (Exception e)
        {
            Logger.Warn($"OBS表示を開始できませんでした(ポート{Port}が使用中かもしれません): {e.Message}", "ObsOverlay");
            listener = null;
            Main.ObsOverlay.Value = false;
        }
    }

    private static void Stop()
    {
        if (listener == null) return;
        try { listener.Stop(); } catch { }
        listener = null;
        thread = null;
    }

    private static void Serve()
    {
        var l = listener;
        while (l != null && listener == l)
        {
            try
            {
                using var client = l.AcceptTcpClient();
                client.ReceiveTimeout = 2000;
                using var stream = client.GetStream();
                var buf = new byte[2048];
                var read = stream.Read(buf, 0, buf.Length);
                var request = Encoding.ASCII.GetString(buf, 0, Math.Max(0, read));
                var path = request.Split(' ').Skip(1).FirstOrDefault() ?? "/";
                byte[] body;
                string type;
                if (path.StartsWith("/commentstate")) { body = Encoding.UTF8.GetBytes(commentJson); type = "application/json; charset=utf-8"; }
                else if (path.StartsWith("/comments")) { body = Encoding.UTF8.GetBytes(CommentsPage); type = "text/html; charset=utf-8"; }
                else if (path.StartsWith("/mapstate")) { body = Encoding.UTF8.GetBytes(mapJson); type = "application/json; charset=utf-8"; }
                else if (path.StartsWith("/map.png")) { body = mapPng ?? Array.Empty<byte>(); type = "image/png"; }
                else if (path.StartsWith("/map")) { body = Encoding.UTF8.GetBytes(MapPage); type = "text/html; charset=utf-8"; }
                else if (path.StartsWith("/state")) { body = Encoding.UTF8.GetBytes(stateJson); type = "application/json; charset=utf-8"; }
                else { body = Encoding.UTF8.GetBytes(Page); type = "text/html; charset=utf-8"; }
                var header = "HTTP/1.1 200 OK\r\n" +
                             $"Content-Type: {type}\r\n" +
                             "Cache-Control: no-store\r\n" +
                             $"Content-Length: {body.Length}\r\n" +
                             "Connection: close\r\n\r\n";
                var head = Encoding.ASCII.GetBytes(header);
                stream.Write(head, 0, head.Length);
                stream.Write(body, 0, body.Length);
            }
            catch (SocketException) { if (listener != l) break; }
            catch (Exception) { }
        }
    }

    // OBS のブラウザソースで読む表示。暗い背景・大きめの太字・役職は色の札で、配信の画面の上でも読みやすくする
    private const string Page = @"<!doctype html>
<html lang=""ja""><head><meta charset=""utf-8""><title>TOH-hamo</title>
<style>
body{margin:0;background:transparent;font-family:'BIZ UDPGothic','Yu Gothic UI','Meiryo',sans-serif;color:#fff;-webkit-font-smoothing:antialiased}
#box{display:inline-block;padding:12px 16px 10px;background:rgba(10,14,24,.82);border:2px solid rgba(255,255,255,.18);border-radius:12px;min-width:320px;box-shadow:0 4px 16px rgba(0,0,0,.5)}
#head{font-weight:800;font-size:20px;letter-spacing:.04em;padding-bottom:6px;margin-bottom:6px;border-bottom:2px solid rgba(255,255,255,.18)}
#list{display:grid;grid-template-columns:auto 1fr auto auto;column-gap:10px;row-gap:5px;align-items:center}
.dot{width:18px;height:18px;border-radius:50%;border:2px solid #fff;box-shadow:0 0 0 2px #000}
.name{font-size:19px;font-weight:700;white-space:nowrap;text-shadow:0 0 2px #000,0 0 4px #000,1px 1px 0 #000}
.role{font-size:15px;font-weight:800;padding:2px 9px;border-radius:999px;white-space:nowrap;border:1px solid rgba(255,255,255,.35)}
.t{font-size:15px;font-weight:700;color:#e8edf5;font-variant-numeric:tabular-nums;text-align:right;white-space:nowrap;text-shadow:0 0 3px #000}
.t.done{color:#7cff7c}
.dead{opacity:.5}
.tag{font-size:12px;font-weight:800;color:#fff;background:#b3262e;border-radius:4px;padding:1px 5px;margin-left:6px;vertical-align:middle}
</style></head><body><div id=""box""><div id=""head"">TOH-hamo</div><div id=""list""></div></div>
<script>
function esc(s){return String(s).replace(/[&<>""]/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c];});}
function ink(hex){var m=/^#?([0-9a-f]{6})/i.exec(hex||'');if(!m)return '#fff';var n=parseInt(m[1],16),r=n>>16&255,g=n>>8&255,b=n&255;return (0.299*r+0.587*g+0.114*b)>150?'#111':'#fff';}
async function tick(){
  try{
    var r=await fetch('/state',{cache:'no-store'});var s=await r.json();
    var head=(s.phase||'')+(s.map?'  |  '+s.map:'')+(s.total?'  |  生存 '+s.alive+' / '+s.total:'');
    document.getElementById('head').textContent=head||'TOH-hamo';
    var h='';
    (s.players||[]).forEach(function(p){
      var d=p.dead?' dead':'';
      var parts=(p.tasks||'').split('/');
      var done=parts.length==2&&parts[0]===parts[1];
      h+='<span class=""dot'+d+'"" style=""background:'+esc(p.color)+'""></span>'
        +'<span class=""name'+d+'"">'+esc(p.name)+(p.dead?'<span class=""tag"">死亡</span>':'')+'</span>'
        +(p.role?'<span class=""role'+d+'"" style=""background:'+esc(p.roleColor)+';color:'+ink(p.roleColor)+'"">'+esc(p.role)+'</span>':'<span></span>')
        +'<span class=""t'+(done?' done':'')+d+'"">'+esc(p.tasks||'')+'</span>';
    });
    document.getElementById('list').innerHTML=h;
  }catch(e){}
}
setInterval(tick,500);tick();
</script></body></html>";

    // OBS のブラウザソースで読むコメント欄。ゲーム画面には出さないので、ゲームの録画にはコメントが映らない
    private const string CommentsPage = @"<!doctype html>
<html lang=""ja""><head><meta charset=""utf-8""><title>TOH-hamo Comments</title>
<style>
body{margin:0;background:transparent;font-family:'BIZ UDPGothic','Yu Gothic UI','Meiryo',sans-serif;color:#fff;-webkit-font-smoothing:antialiased}
#box{display:none;width:calc(100vw - 8px);background:rgba(10,14,24,.82);border:2px solid rgba(255,255,255,.18);border-radius:12px;box-shadow:0 4px 16px rgba(0,0,0,.5);overflow:hidden}
#head{font-weight:800;font-size:18px;padding:8px 14px;background:rgba(255,255,255,.06);border-bottom:2px solid rgba(255,255,255,.14)}
#head .live{color:#ff4d4d}
#head .sub{font-size:13px;color:#aab;margin-left:6px}
#list{padding:8px 14px 10px;display:flex;flex-direction:column;gap:6px}
.c{font-size:17px;line-height:1.35;word-break:break-word;text-shadow:0 0 2px #000,0 0 4px #000}
.n{font-weight:800;color:#ffd36e;margin-right:8px}
</style></head><body><div id=""box""><div id=""head""><span class=""live"">●</span> YouTube<span class=""sub"">LIVE CHAT</span></div><div id=""list""></div></div>
<script>
function esc(s){return String(s).replace(/[&<>""]/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c];});}
var shown='';
async function tick(){
  try{
    var r=await fetch('/commentstate',{cache:'no-store'});var s=await r.json();
    document.getElementById('box').style.display=s.on?'block':'none';
    var items=(s.items||[]).slice(-12);
    var h='';
    items.forEach(function(c){h+='<div class=""c""><span class=""n"">'+esc(c.name)+'</span>'+esc(c.text)+'</div>';});
    if(h!==shown){document.getElementById('list').innerHTML=h;shown=h;}
  }catch(e){}
}
setInterval(tick,1000);tick();
</script></body></html>";

    // OBS のブラウザソースで読むマップ。会議が始まった時の位置に色の丸と名前、死んだ人は×で出す
    private const string MapPage = @"<!doctype html>
<html lang=""ja""><head><meta charset=""utf-8""><title>TOH-hamo Map</title>
<style>
body{margin:0;background:transparent;font-family:'BIZ UDPGothic','Yu Gothic UI','Meiryo',sans-serif;color:#fff}
#box{position:relative;display:none;background:rgba(10,14,24,.82);border:2px solid rgba(255,255,255,.18);border-radius:12px;padding:8px}
#img{display:block;width:100%;height:auto;filter:drop-shadow(0 0 2px #000)}
#wrap{position:relative}
.p{position:absolute;transform:translate(-50%,-50%);text-align:center;white-space:nowrap}
.dot{width:20px;height:20px;border-radius:50%;border:2px solid #fff;box-shadow:0 0 0 2px #000;margin:0 auto}
.x{font-size:24px;font-weight:900;line-height:20px;text-shadow:0 0 2px #000,0 0 4px #000}
.n{font-size:14px;font-weight:700;text-shadow:0 0 2px #000,0 0 4px #000,1px 1px 0 #000;margin-top:2px}
.dead .n{opacity:.7}
</style></head><body><div id=""box"" style=""width:96vw""><div id=""wrap""><img id=""img""><div id=""pins""></div></div></div>
<script>
function esc(s){return String(s).replace(/[&<>""]/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c];});}
var shownId=-1;
async function tick(){
  try{
    var r=await fetch('/mapstate',{cache:'no-store'});var s=await r.json();
    var box=document.getElementById('box');
    if(!s.ready){box.style.display='none';shownId=-1;return;}
    if(shownId!==s.id){document.getElementById('img').src='/map.png?'+s.id+'_'+Date.now();shownId=s.id;}
    box.style.display='block';
    var h='';
    (s.players||[]).forEach(function(p){
      var mark=p.dead?'<div class=""x"" style=""color:'+esc(p.color)+'"">×</div>':'<div class=""dot"" style=""background:'+esc(p.color)+'""></div>';
      h+='<div class=""p'+(p.dead?' dead':'')+'"" style=""left:'+(p.x*100)+'%;top:'+(p.y*100)+'%"">'+mark+'<div class=""n"">'+esc(p.name)+'</div></div>';
    });
    document.getElementById('pins').innerHTML=h;
  }catch(e){}
}
setInterval(tick,1000);tick();
</script></body></html>";
}
