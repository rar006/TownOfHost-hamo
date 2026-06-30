using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Modules;

namespace TownOfHost.Patches;

public sealed class LobbyTitleDummy : CustomNetObject
{
    private static LobbyTitleDummy _instance;

    private static readonly Vector2 SpawnPosition = new(0f, 10f);

    private const string NameSizePercent = "500%";
    private const string VersionSizePercent = "220%";

    private static string BuildTitle()
    {
        const string nameText = "TownOfHost-Pko";
        string versionText = $"v{Main.PluginVersion}";

        Color[] stops =
        [
            new Color(1.00f, 0.42f, 0.62f),
            new Color(1.00f, 0.75f, 0.30f),
            new Color(0.30f, 1.00f, 0.60f),
            new Color(0.30f, 0.75f, 1.00f),
        ];

        var sb = new StringBuilder();
        sb.Append("<line-height=7500%>\n<line-height=100%>");
        sb.Append($"<size={NameSizePercent}><b><i>");

        for (int i = 0; i < nameText.Length; i++)
        {
            float t = (float)i / (nameText.Length - 1) * (stops.Length - 1);
            int idx = Mathf.Clamp(Mathf.FloorToInt(t), 0, stops.Length - 2);
            Color c = Color.Lerp(stops[idx], stops[idx + 1], t - idx);
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{nameText[i]}</color>");
        }

        sb.Append($"</i></b></size>");
        sb.Append($"\n<size={VersionSizePercent}><color=#BBCCFF>{versionText}</color></size>");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────
    //  リフレクション（SpawnQueue/DoCreate は元の CustomNetObject に存在）
    // ──────────────────────────────────────────────────────────────────
    private static readonly FieldInfo _spawnQueueField = typeof(CustomNetObject)
        .GetField("SpawnQueue", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo _processQueueMethod = typeof(CustomNetObject)
        .GetMethod("ProcessQueue", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo _doCreateMethod = typeof(CustomNetObject)
        .GetMethod("DoCreate", BindingFlags.NonPublic | BindingFlags.Instance);

    private void SpawnInLobby(Vector2 position)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (GameStates.IsEnded) return;

        if (_spawnQueueField?.GetValue(null) is not Queue<Action> queue
            || _doCreateMethod == null || _processQueueMethod == null)
        {
            Logger.Error("[LobbyTitleDummy] リフレクション失敗", "LobbyTitleDummy");
            return;
        }

        var self = this;
        queue.Enqueue(() => _doCreateMethod.Invoke(self, [position]));
        _processQueueMethod.Invoke(null, null);
    }

    // ──────────────────────────────────────────────────────────────────
    //  召喚 / 破棄
    // ──────────────────────────────────────────────────────────────────
    public static void Spawn()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (_instance != null) return;
        _instance = new LobbyTitleDummy();
        _instance.SpawnInLobby(SpawnPosition);
    }

    public static void DespawnInstance()
    {
        if (_instance == null) return;
        _instance.Despawn();
        _instance = null;
    }

    public static void ResetInstance() => _instance = null;

    // ──────────────────────────────────────────────────────────────────
    //  OnCreated — BeginnerImpostorDummy と同じ方式に統一
    //
    //  ❌ 旧: Shapeshift トリック（バニラクライアントに届かない）
    //  ✅ 新: RpcSetColor + RawSetColor → バニラクライアントにも色が届く
    //         SetName → 名前テキストを設定
    // ──────────────────────────────────────────────────────────────────
    protected override void OnCreated()
    {
        if (PlayerControl == null) return;

        SnapToPosition(SpawnPosition);

        // ★ ボディの色を黒(6)に設定
        //   RpcSetColor: 全クライアント（バニラ含む）に届く標準 RPC
        //   RawSetColor: ホスト側の描画を即座に更新
        const byte bodyColor = 6; // Black
        try
        {
            PlayerControl.RpcSetColor(bodyColor);
            PlayerControl.RawSetColor(bodyColor);
        }
        catch (Exception e)
        {
            Logger.Error(e.ToString(), "LobbyTitleDummy.SetColor");
        }

        // ボディを非表示（ホスト側のみ — 他クライアントはカラーで代替）
        try
        {
            PlayerControl.cosmetics.currentBodySprite.BodySprite.color = Color.clear;
        }
        catch { }

        // 名前（タイトル文字列）を設定
        _ = new LateTask(() =>
        {
            if (PlayerControl == null) return;
            SetName(BuildTitle());
        }, 0.2f, "LobbyTitle.SetName", true);
    }

    public override void OnMeeting() { }
}

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
internal static class LobbyTitleSpawnPatch
{
    public static void Postfix()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        LobbyTitleDummy.ResetInstance();
        _ = new LateTask(LobbyTitleDummy.Spawn, 0.8f, "LobbyTitle.Spawn", true);
    }
}

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
internal static class LobbyTitleDespawnPatch
{
    public static void Prefix() => LobbyTitleDummy.DespawnInstance();
}