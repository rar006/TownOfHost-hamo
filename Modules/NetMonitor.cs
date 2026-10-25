using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Modules;

// 通信モニター(Shift+F3 で表示/非表示)。誰でも使える。
// FPS・Ping・送った量・テレポートの本数(サーバーにキックされる境目まで)などを、小さなグラフつきで出す。
// 枠はデバッグの枠と同じで、帯をつかんで移動、右下の「◢」で大きさを変えられる。
public static class NetMonitor
{
    private const int History = 40;
    private const float SampleInterval = 0.5f;

    private static readonly DebugWindow window = new DebugWindow("NetMon", $"NET MONITOR <size=75%><color={UiSkin.DimHex}>通信モニター</color></size>", 0.35f, 0.25f, 4.8f, 2.4f).WithLockAndFront();
    private static bool visible;
    private static bool loaded;
    private static float nextSampleAt;
    private static float smoothDelta = 0.016f;
    private static string body = "";

    private static readonly float[] fpsHist = new float[History];
    private static readonly float[] pingHist = new float[History];
    private static readonly float[] sendHist = new float[History];
    private static int histPos;
    private static int histCount;

    // 送った量(NetworkSafety の送信処理で数える)
    private static int sentBytes, sentPackets;
    private static int lastNotifyCount;
    private static float lastSendRate, lastPacketRate, lastNotifyRate;

    public static void CountSend(int bytes)
    {
        sentBytes += bytes;
        sentPackets++;
    }

    public static void Update(HudManager hud)
    {
        if (hud == null) return;
        if (!loaded)
        {
            loaded = true;
            try { visible = PlayerPrefs.GetInt("TOH_NetMon_Visible", 0) == 1; } catch { }
        }
        smoothDelta += (Time.unscaledDeltaTime - smoothDelta) * 0.1f;
        if (Input.GetKeyDown(KeyCode.F3) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            visible = !visible;
            try { PlayerPrefs.SetInt("TOH_NetMon_Visible", visible ? 1 : 0); PlayerPrefs.Save(); } catch { }
            nextSampleAt = 0f;
        }
        if (Time.unscaledTime >= nextSampleAt) Sample();

        var chatOpen = hud.Chat != null && hud.Chat.IsOpenOrOpening;
        if (!visible || chatOpen) { window.Hide(); return; }
        window.Show(hud);
        // デバッグの枠が出ている時は、そちらがクリックの印を毎フレーム戻すので、ここでは戻さない
        if (!DebugOverlay.Active) DebugWindow.ClickTaken = false;
        window.HandleMouse();
        window.SetBody(body);
    }

    // 0.5秒ごとに数えた量を記録する(非表示の時も記録だけは続けて、開いた時にすぐグラフが出るようにする)
    private static void Sample()
    {
        var elapsed = nextSampleAt <= 0f ? SampleInterval : SampleInterval + (Time.unscaledTime - nextSampleAt);
        nextSampleAt = Time.unscaledTime + SampleInterval;
        elapsed = Mathf.Max(elapsed, 0.1f);

        lastSendRate = sentBytes / elapsed;
        lastPacketRate = sentPackets / elapsed;
        var notify = HudManagerPatch.NowCallNotifyRolesCount;
        lastNotifyRate = Mathf.Max(0, notify - lastNotifyCount) / elapsed;
        lastNotifyCount = notify;
        sentBytes = sentPackets = 0;

        var fps = 1f / Mathf.Max(smoothDelta, 0.0001f);
        var ping = AmongUsClient.Instance && GameStates.IsOnlineGame ? AmongUsClient.Instance.Ping : 0;
        fpsHist[histPos] = fps;
        pingHist[histPos] = ping;
        sendHist[histPos] = lastSendRate;
        histPos = (histPos + 1) % History;
        if (histCount < History) histCount++;

        if (visible) body = Build(fps, ping);
    }

    // 1 行 = 名前・値・グラフの 3 列(列の位置をそろえて、計測器のように見せる)
    private const string ValueCol = "<pos=19%>";
    private const string GraphCol = "<pos=45%>";
    private static string Label(string name) => $"<color={UiSkin.DimHex}><size=82%><cspace=0.04em>{name}</cspace></size></color>";
    private static string Sub(string text) => $"<size=78%><color={UiSkin.DimHex}>{text}</color></size>";

    private static string Build(float fps, int ping)
    {
        var sb = new StringBuilder();
        sb.Append(Label("FPS")).Append(ValueCol).Append($"<b><color={Level(fps, 50, 30, true)}>{Mathf.RoundToInt(fps)}</color></b>");
        sb.Append(GraphCol).Append(Spark(fpsHist, 0f, 120f, "#7cd0ff")).Append('\n');

        sb.Append(Label("PING")).Append(ValueCol);
        if (ping > 0)
        {
            var (min, avg, max) = Stats(pingHist);
            sb.Append($"<b><color={Level(ping, 120, 250, false)}>{ping}</color></b>{Sub("ms")}");
            if (ping >= 1000) sb.Append(' ').Append($"<size=78%><color=#ff6b6b>回線が詰まっています</color></size>");
            sb.Append(GraphCol).Append(Spark(pingHist, 0f, Mathf.Max(200f, max), "#ffd36e"));
            sb.Append(' ').Append(Sub($"{min:0}/{avg:0}/{max:0}")).Append('\n');
        }
        else sb.Append($"<color={UiSkin.DimHex}>-</color>\n");

        // 目安: 送る量は 8KB/s まで安全・24KB/s を超えると重い人が出始める。回数は別に色を付ける
        sb.Append(Label("TX")).Append(ValueCol).Append($"<b><color={Level(lastSendRate, TxGood, TxBad, false)}>{FormatBytes(lastSendRate)}</color></b>{Sub("/s")}");
        sb.Append(GraphCol).Append(Spark(sendHist, 0f, Mathf.Max(TxBad, sendHist.Max()), "#c08cff"));
        sb.Append(' ').Append($"<size=78%><color={Level(lastPacketRate, PktGood, PktBad, false)}>{lastPacketRate:0} pkt/s</color></size>").Append('\n');

        if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost && GameStates.IsOnlineGame)
        {
            var tp = NetworkSafety.RecentTeleports;
            var ratio = (float)tp / NetworkSafety.TeleportLimit;
            sb.Append(Label("SNAP")).Append(ValueCol).Append($"<b>{tp}</b>{Sub($"/{NetworkSafety.TeleportLimit}")}");
            sb.Append(GraphCol).Append(Meter(ratio)).Append(' ').Append(Sub("テレポート 6秒")).Append('\n');
            sb.Append(Label("SYNC")).Append(ValueCol).Append($"<b>{lastNotifyRate:0.0}</b>{Sub("/s")}");
            sb.Append(GraphCol).Append(Sub("名前の更新")).Append($"　{Label("DUMMY")} <b>{CustomNetObject.AllObjects.Count}</b>").Append('\n');
        }
        // TX の目安(枠が低い時は、ここから先に切れる)
        sb.Append(Sub($"TX目安 <color=#7cff7c>~{FormatBytes(TxGood)}</color> / <color=#ffd36e>~{FormatBytes(TxBad)}</color> / <color=#ff6b6b>それ以上は危険</color>  ({PktGood}/{PktBad} pkt/s)"));
        return sb.ToString();
    }

    // 縦棒の高さで値を表すグラフ(文字の大きさを変えると下揃えのまま高さが変わる)
    private static string Spark(float[] hist, float min, float max, string color)
    {
        if (histCount == 0) return "";
        var sb = new StringBuilder();
        sb.Append($"<color={color}><cspace=-0.12em>");
        var start = (histPos - histCount + History) % History;
        for (var i = 0; i < histCount; i++)
        {
            var v = hist[(start + i) % History];
            var t = Mathf.Clamp01((v - min) / Mathf.Max(max - min, 0.0001f));
            sb.Append($"<size={Mathf.RoundToInt(20 + t * 90)}%>|</size>");
        }
        sb.Append("</cspace></color>");
        return sb.ToString();
    }

    private static string Meter(float ratio)
    {
        const int cells = 10;
        var filled = Mathf.Clamp(Mathf.CeilToInt(ratio * cells), 0, cells);
        var color = ratio >= 0.8f ? "#ff6b6b" : ratio >= 0.5f ? "#ffd36e" : "#7cff7c";
        return $"<cspace=-0.05em><color={color}>{new string('■', filled)}</color><color=#26303d>{new string('■', cells - filled)}</color></cspace>";
    }

    private static (float min, float avg, float max) Stats(float[] hist)
    {
        float min = float.MaxValue, max = 0f, sum = 0f;
        var n = 0;
        var start = (histPos - histCount + History) % History;
        for (var i = 0; i < histCount; i++)
        {
            var v = hist[(start + i) % History];
            if (v <= 0f) continue;
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
            sum += v;
            n++;
        }
        return n == 0 ? (0f, 0f, 0f) : (min, sum / n, max);
    }

    private const float TxGood = 8f * 1024f;
    private const float TxBad = 24f * 1024f;
    private const int PktGood = 30;
    private const int PktBad = 60;

    // good の向き: higherIsBetter が true なら大きいほど良い
    private static string Level(float v, float good, float bad, bool higherIsBetter)
    {
        if (higherIsBetter) return v >= good ? "#7cff7c" : v >= bad ? "#ffd36e" : "#ff6b6b";
        return v <= good ? "#7cff7c" : v <= bad ? "#ffd36e" : "#ff6b6b";
    }

    private static string FormatBytes(float b)
        => b >= 1024f * 1024f ? $"{b / (1024f * 1024f):0.0}MB" : b >= 1024f ? $"{b / 1024f:0.0}KB" : $"{b:0}B";
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class NetMonitorPatch
{
    private static bool errorLogged;
    public static void Postfix(HudManager __instance)
    {
        try { NetMonitor.Update(__instance); }
        catch (Exception e)
        {
            if (!errorLogged) Logger.Warn($"NetMonitor: {e}", "NetMonitor");
            errorLogged = true;
        }
    }
}
