using System;
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
            if (!Main.ObsOverlay.Value) { Stop(); return; }
            Start();
            if (Time.time < nextUpdateAt) return;
            nextUpdateAt = Time.time + 0.5f;
            stateJson = BuildState();
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
                var task = p.GetPlayerTaskState();
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
                var isState = path.StartsWith("/state");
                var body = Encoding.UTF8.GetBytes(isState ? stateJson : Page);
                var header = "HTTP/1.1 200 OK\r\n" +
                             $"Content-Type: {(isState ? "application/json" : "text/html")}; charset=utf-8\r\n" +
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
}
