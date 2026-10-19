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

    private const string Page = @"<!doctype html>
<html lang=""ja""><head><meta charset=""utf-8""><title>TOH-hamo</title>
<style>
body{margin:0;background:transparent;font-family:'Yu Gothic UI','Meiryo',sans-serif;color:#fff}
#box{display:inline-block;padding:10px 14px;background:rgba(0,0,0,.55);border-radius:10px;min-width:260px}
#head{font-weight:bold;margin-bottom:6px;font-size:18px}
.p{display:flex;align-items:center;gap:8px;font-size:16px;margin:2px 0;text-shadow:0 0 3px #000}
.dot{width:14px;height:14px;border-radius:50%;border:2px solid #000;flex:none}
.dead{opacity:.45;text-decoration:line-through}
.role{font-size:13px;font-weight:bold}
.t{font-size:12px;color:#ccc;margin-left:auto}
</style></head><body><div id=""box""><div id=""head"">TOH-hamo</div><div id=""list""></div></div>
<script>
function esc(s){return String(s).replace(/[&<>""]/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c];});}
async function tick(){
  try{
    var r=await fetch('/state',{cache:'no-store'});var s=await r.json();
    var head=(s.phase||'')+(s.map?' / '+s.map:'')+(s.total?' / 生存 '+s.alive+'/'+s.total:'');
    document.getElementById('head').textContent=head||'TOH-hamo';
    var h='';
    (s.players||[]).forEach(function(p){
      h+='<div class=""p'+(p.dead?' dead':'')+'""><span class=""dot"" style=""background:'+esc(p.color)+'""></span><span>'+esc(p.name)+'</span>'
        +(p.role?'<span class=""role"" style=""color:'+esc(p.roleColor)+'"">'+esc(p.role)+'</span>':'')
        +(p.tasks?'<span class=""t"">'+esc(p.tasks)+'</span>':'')+'</div>';
    });
    document.getElementById('list').innerHTML=h;
  }catch(e){}
}
setInterval(tick,500);tick();
</script></body></html>";
}
