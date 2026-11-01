using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.Recorder;

// TOH-hamo の録画: ゲームの画面を、配信用の表示(コメント欄など)を消した状態で描き直して MP4 にする。
// 自分の画面だけの処理なので、ホストでなくても(MOD を入れていれば)使える。音は設定で選ぶ
public static class GameRecorder
{
    public static readonly string[] QualityNames = { "720p", "1080p", "画面と同じ" };
    public static readonly int[] VolumeSteps = { 0, 25, 50, 75, 100 };

    static RecorderSession session;
    static RenderTexture target;
    static Texture2D readback;
    static float nextFrameAt;
    static TextMeshPro indicator;
    static bool directReadFailed;
    static string lastSavedPath;
    static bool quitHooked;
    public static bool IsRecording => session != null;
    public static string LastSavedPath => lastSavedPath;
    public static TimeSpan Elapsed => session?.Elapsed ?? TimeSpan.Zero;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TownOfHost-hamo");

    // 録画に映さない表示(配信の時だけ要るもの)。持ち主が登録する
    static readonly List<Func<GameObject>> hiddenInRecording = new() { () => StreamCommentFrame.FrameObject };
    public static void HideInRecording(Func<GameObject> target) => hiddenInRecording.Add(target);

    public static void Toggle()
    {
        if (IsRecording) Stop();
        else Start();
    }

    public static void Start()
    {
        if (IsRecording) return;
        try
        {
            var (w, h) = Size();
            var fps = Main.RecordFps.Value >= 60 ? 60 : 30;
            var bitrate = (h >= 1000 ? 10_000_000 : 6_000_000) * (fps >= 60 ? 3 : 2) / 2;
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"TOH-hamo_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            var audio = new[]
            {
                (AudioSourceKind.Game, Main.RecordGameVolume.Value / 100f),
                (AudioSourceKind.Other, Main.RecordOtherVolume.Value / 100f),
                (AudioSourceKind.Mic, Main.RecordMicVolume.Value / 100f),
            };
            target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "TOH-hamo Recorder" };
            target.Create();
            readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
            session = new RecorderSession(path, w, h, fps, bitrate, audio, m => Logger.Info(m, "Recorder"), bottomUpRgba: true);
            session.Start();
            nextFrameAt = 0f;
            // ゲームを閉じる時は、録画を最後まで書いてから終わる(途中で切ると動画が開けなくなる)
            if (!quitHooked)
            {
                quitHooked = true;
                Application.add_quitting((Il2CppSystem.Action)(Action)(() => Stop(true)));
            }
            AmongUsClient.Instance.StartCoroutine(CoCapture(session).WrapToIl2Cpp());
            Logger.Info($"録画を始めました: {path} ({w}x{h} {fps}fps / 音 ゲーム{Main.RecordGameVolume.Value}% ほか{Main.RecordOtherVolume.Value}% マイク{Main.RecordMicVolume.Value}%)", "Recorder");
            Logger.seeingame("録画を始めました(Shift+F9 で止めます)");
        }
        catch (Exception e)
        {
            Logger.Error($"録画を始められませんでした: {e}", "Recorder");
            Logger.seeingame("録画を始められませんでした");
            Cleanup();
        }
    }

    public static void Stop() => Stop(false);

    // wait: ゲームを閉じる時だけ、書き終わるまで待つ(普段は待たずに、書き終わったら知らせる)
    static void Stop(bool wait)
    {
        var s = session;
        if (s == null) return;
        session = null;
        s.Stop();
        Cleanup();
        lastSavedPath = s.Path;
        if (wait) { s.Wait(5000); return; }
        Logger.seeingame("録画を保存しています…");
        AmongUsClient.Instance.StartCoroutine(CoReportSaved(s).WrapToIl2Cpp());
    }

    static IEnumerator CoReportSaved(RecorderSession s)
    {
        while (s.IsRunning) yield return null;
        if (s.Error != null)
        {
            Logger.seeingame("録画の保存に失敗しました");
            Logger.Error($"録画の保存に失敗: {s.Error}", "Recorder");
            yield break;
        }
        Logger.Info($"録画を保存しました: {s.Path} ({s.Elapsed:mm\\:ss} / 間に合わず飛ばした絵 {s.DroppedFrames}枚)", "Recorder");
        Logger.seeingame($"録画を保存しました: {Path.GetFileName(s.Path)}");
    }

    static void Cleanup()
    {
        if (target != null) { target.Release(); Object.Destroy(target); target = null; }
        if (readback != null) { Object.Destroy(readback); readback = null; }
        if (indicator != null) indicator.gameObject.SetActive(false);
    }

    static (int w, int h) Size()
    {
        var sw = Mathf.Max(Screen.width, 2);
        var sh = Mathf.Max(Screen.height, 2);
        var h = Main.RecordQuality.Value switch { 0 => 720, 1 => 1080, _ => sh };
        // 画面より大きくはしない
        h = Mathf.Min(h, sh);
        var w = Mathf.RoundToInt(h * (float)sw / sh);
        return (w & ~1, h & ~1);
    }

    static IEnumerator CoCapture(RecorderSession s)
    {
        while (session == s)
        {
            yield return new WaitForEndOfFrame();
            if (session != s) yield break;
            if (Time.unscaledTime < nextFrameAt) continue;
            nextFrameAt = Mathf.Max(nextFrameAt + 1f / s.Fps, Time.unscaledTime - 0.5f / s.Fps);
            try { Capture(s); }
            catch (Exception e)
            {
                Logger.Error($"録画の画面を取り込めませんでした: {e}", "Recorder");
                Stop();
                yield break;
            }
        }
    }

    static readonly List<Renderer> hiddenRenderers = new();

    static void Capture(RecorderSession s)
    {
        if (!s.WantsFrame) { s.PushFrame(s.RentBuffer()); return; }
        // 配信用の表示を一時的に消して、画面に映っているカメラを順に描き直す
        hiddenRenderers.Clear();
        if (Main.RecordHideStreamUi.Value)
        {
            foreach (var get in hiddenInRecording)
            {
                var go = get();
                if (go == null || !go.activeInHierarchy) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    if (r != null && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
            }
        }
        if (indicator != null && indicator.gameObject.activeSelf)
            foreach (var r in indicator.GetComponentsInChildren<Renderer>())
                if (r != null && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
        try
        {
            var cams = Camera.allCameras.Where(c => c != null && c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToArray();
            var first = true;
            foreach (var cam in cams)
            {
                // いちばん奥のカメラの前に、前の絵を消しておく
                if (first)
                {
                    var prev = RenderTexture.active;
                    RenderTexture.active = target;
                    GL.Clear(true, true, Color.black);
                    RenderTexture.active = prev;
                    first = false;
                }
                cam.targetTexture = target;
                cam.Render();
                cam.targetTexture = null;
            }
        }
        finally
        {
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            hiddenRenderers.Clear();
        }

        var active = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, s.Width, s.Height), 0, 0, false);
        RenderTexture.active = active;

        var buffer = s.RentBuffer();
        CopyPixels(buffer);
        s.PushFrame(buffer);
    }

    // 画素をそのまま読む(毎回大きな配列を作ると、ゴミ集めでゲームも動画も一瞬止まるので、中身の場所を直接読む)
    static void CopyPixels(byte[] buffer)
    {
        if (!directReadFailed)
        {
            try
            {
                var ptr = readback.GetWritableImageData(0);
                if (ptr != IntPtr.Zero && (long)readback.GetRawImageDataSize() >= buffer.Length)
                {
                    Marshal.Copy(ptr, buffer, 0, buffer.Length);
                    return;
                }
                throw new InvalidOperationException("画素の場所が取れません");
            }
            catch (Exception e)
            {
                directReadFailed = true;
                Logger.Warn($"画素の直接読み込みが使えないので、配列を作って読みます(少し重くなります): {e.Message}", "Recorder");
            }
        }
        var array = readback.GetRawTextureData();
        // il2cpp の配列は、頭の 4 つ分(型・ロック・範囲・長さ)の後ろに中身が並んでいる
        Marshal.Copy(IntPtr.Add(array.Pointer, 4 * IntPtr.Size), buffer, 0, buffer.Length);
    }

    // 録画中は左上に赤い印と時間を出す(録画そのものには映さない)
    internal static void UpdateIndicator(HudManager hud)
    {
        if (!IsRecording)
        {
            if (indicator != null && indicator.gameObject.activeSelf) indicator.gameObject.SetActive(false);
            return;
        }
        if (indicator == null)
        {
            indicator = Templates.TMPTemplate.Create("RecorderIndicator", "", Color.white, 1.6f, TextAlignmentOptions.TopLeft, true, hud.transform);
            var font = hud.TaskPanel?.taskText?.font;
            if (font != null) indicator.font = font;
            indicator.rectTransform.pivot = new Vector2(0f, 1f);
            indicator.SetOutlineColor(Color.black);
            indicator.SetOutlineThickness(0.15f);
            var aspect = indicator.gameObject.AddComponent<AspectPosition>();
            aspect.Alignment = AspectPosition.EdgeAlignments.LeftTop;
            aspect.DistanceFromEdge = new Vector3(0.35f, 0.25f, -400f);
            aspect.AdjustPosition();
            var r = indicator.GetComponent<Renderer>();
            if (r != null) r.sortingOrder = 1000;
        }
        if (!indicator.gameObject.activeSelf) indicator.gameObject.SetActive(true);
        var on = (int)(Time.unscaledTime * 2f) % 2 == 0;
        indicator.text = $"<color={(on ? "#ff3b3b" : "#7a1d1d")}>●</color> REC {Elapsed:mm\\:ss}";
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class GameRecorderHudPatch
{
    static void Postfix(HudManager __instance)
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.F9) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                GameRecorder.Toggle();
            GameRecorder.UpdateIndicator(__instance);
        }
        catch (Exception e) { Logger.Warn($"録画の表示を更新できませんでした: {e.Message}", "Recorder"); }
    }
}