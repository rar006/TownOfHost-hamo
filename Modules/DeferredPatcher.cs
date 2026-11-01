using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace TownOfHost;

/// <summary>
/// 起動高速化: Harmony.PatchAll(約350クラス)は起動時に約5秒かかる。
/// メインメニューでは使われない「試合中専用」のパッチ(PlayerControl/MeetingHud等)は、
/// 起動時には登録せず、メインメニューが開いた後に少しずつ登録する。
/// 「プレイ」等を押した時点で未登録分が残っていれば、その場で全て登録する(FinishNow)ので、
/// 試合・ロビーに入る時点では必ず全パッチが有効になっている。
/// </summary>
public static class DeferredPatcher
{
    // メインメニューでは実行されない、試合/ロビー専用のバニラ型
    private static readonly HashSet<string> DeferredTargets = new()
    {
        "PlayerControl", "MeetingHud", "LobbyBehaviour", "HudManager", "ChatController", "ShipStatus",
        "PlayerPhysics", "IntroCutscene", "EndGameManager", "ExileController", "Vent", "NetworkedPlayerInfo",
        "SpawnInMinigame", "SabotageButton", "MovingPlatformBehaviour", "LogicGameFlowNormal", "ElectricTask",
        "GameManager", "TaskAddButton",
        // ロビーの設定画面・試合中の画面(どれもメインメニューでは開かない)
        "GameStartManager", "GameSettingMenu", "GameOptionsMenu", "StringOption", "NumberOption", "ToggleOption",
        "ExileController", "MapBehaviour", "VentilationSystem", "HeliSabotageSystem", "MushroomMixupSabotageSystem",
        "SwitchSystem", "SecurityCameraSystemType", "VitalsMinigame", "SurveillanceMinigame", "SecurityLogGame",
        "ShapeshifterMinigame", "ShapeshifterPanel", "TaskAdderGame", "TaskPanelBehaviour", "ViewSettingsInfoPanel",
        "BanMenu", "DetectiveRole",
        "RoleManager", "SabotageSystemType", "RolesSettingsMenu", "TutorialManager", "CreateGameOptions",
        "JoinGameButton", "EnterCodeManager", "GuessManager", "CustomRoleManager", "CustomRolesHelper",
    };

    // AmongUsClient・InnerNetClient のうち、部屋の中でしか呼ばれないもの(OnGameJoined は入る時に使うので今すぐ登録する)
    private static readonly HashSet<string> RoomOnlyClientMethods = new()
    {
        "OnGameEnd", "OnPlayerJoined", "OnPlayerLeft", "StartGame", "CoStartGame", "KickPlayer", "CreatePlayer",
        "CanBan", "StartRpcImmediately", "FinishRpcImmediately", "SendAllStreamedObjects",
    };

    private static readonly Queue<Type> pending = new();
    private static Harmony harmony;
    private static bool begun;
    private static int deferredTotal;
    private static readonly Stopwatch totalWatch = new();

    /// <summary>Harmony.PatchAll の代わり。試合専用パッチは保留にして、それ以外を今すぐ登録する。</summary>
    public static void PatchAllDeferred(Harmony instance, Assembly assembly)
    {
        harmony = instance;
        var watch = Stopwatch.StartNew();
        var eager = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
        {
            try
            {
                // パッチではないクラス(約2800個)は Harmony に渡さない(渡すだけで起動が遅くなっていた)
                if (!type.IsDefined(typeof(HarmonyAttribute), false)) continue;
                if (IsDeferred(type)) { pending.Enqueue(type); continue; }
                harmony.CreateClassProcessor(type).Patch();
                eager++;
            }
            catch (Exception e)
            {
                Logger.Error($"パッチ登録に失敗: {type.FullName}\n{e}", "DeferredPatcher", false);
            }
        }
        deferredTotal = pending.Count;
        Logger.Info($"起動時に登録: {eager}クラス / 保留: {deferredTotal}クラス ({watch.ElapsedMilliseconds}ms)", "DeferredPatcher");
    }

    private static bool IsDeferred(Type type)
    {
        var attrs = HarmonyMethodExtensions.GetFromType(type);
        if (attrs == null || attrs.Count == 0) return false;
        var merged = HarmonyMethod.Merge(attrs);
        var target = merged?.declaringType;
        if (target == null) return false;
        if (DeferredTargets.Contains(target.Name)) return true;
        return target.Name is "AmongUsClient" or "InnerNetClient" && merged.methodName != null && RoomOnlyClientMethods.Contains(merged.methodName);
    }

    /// <summary>メインメニューが開いた後、フレームごとに少しずつ保留分を登録する(1フレーム約12msまで)</summary>
    public static void BeginBackgroundApply()
    {
        if (begun || pending.Count == 0) return;
        begun = true;
        totalWatch.Start();
        Tick();
    }

    private static void Tick()
    {
        _ = new LateTask(() =>
        {
            if (pending.Count == 0) return;
            var frame = Stopwatch.StartNew();
            while (pending.Count > 0 && frame.ElapsedMilliseconds < 12)
            {
                PatchOne(pending.Dequeue());
            }
            if (pending.Count > 0) Tick();
            else Logger.Info($"保留パッチの登録が完了 ({deferredTotal}クラス / {totalWatch.ElapsedMilliseconds}ms)", "DeferredPatcher");
        }, 0.02f, "DeferredPatcherTick", true);
    }

    /// <summary>未登録のパッチを今すぐ全て登録する。ロビー/試合/フリープレイに入る前に必ず呼ぶ。</summary>
    public static void FinishNow()
    {
        if (pending.Count == 0) return;
        var watch = Stopwatch.StartNew();
        var count = pending.Count;
        while (pending.Count > 0) PatchOne(pending.Dequeue());
        Logger.Info($"保留パッチを即時登録: {count}クラス ({watch.ElapsedMilliseconds}ms)", "DeferredPatcher");
    }

    private static void PatchOne(Type type)
    {
        try { harmony.CreateClassProcessor(type).Patch(); }
        catch (Exception e) { Logger.Error($"保留パッチの登録に失敗: {type.FullName}\n{e}", "DeferredPatcher", false); }
    }
}

// 招待や自動の部屋の立て直しなど、メニューのボタンを通らずに部屋へ入った時も、保留中のパッチを先に全部登録する
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
static class DeferredPatcherOnGameJoinedPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix() => DeferredPatcher.FinishNow();
}
