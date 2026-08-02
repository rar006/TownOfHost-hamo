using System.Linq;
using HarmonyLib;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using UnityEngine;

namespace TownOfHost.Patches;

/// <summary>
/// GM自動憑依まわりの共通設定・状態を持つクラス。
/// 「会議終了」を基準にした待機時間管理を行う。
/// </summary>
public static class GMAutoPossessTiming
{
    /// <summary>AirShipの場合、会議終了から憑依処理を始めるまでの待機時間(秒)。</summary>
    public const float AirshipDelaySeconds = 15f;

    /// <summary>AirShip以外のマップの場合、会議終了から憑依処理を始めるまでの待機時間(秒)。</summary>
    public const float OtherMapDelaySeconds = 3f;

    /// <summary>直近の会議が終了した時刻(realtimeSinceStartup)。MeetingHudPatch側で設定する。</summary>
    public static float LastMeetingEndTime = -999f;

    public static bool IsAirship()
        => (MapNames)Main.NormalOptions.MapId == MapNames.Airship;

    /// <summary>マップに応じた、会議終了後の待機時間を返す。</summary>
    public static float GetDelaySeconds()
        => IsAirship() ? AirshipDelaySeconds : OtherMapDelaySeconds;

    /// <summary>
    /// 今、自動憑依処理を始めてよいかどうか。
    /// 直近の会議終了時刻から、マップに応じた待機時間が経過しているかで判定する。
    /// (会議が一度も無かった場合=ゲーム開始直後は、待機時間なしで許可する)
    /// </summary>
    public static bool CanStartAutoPossess()
    {
        if (LastMeetingEndTime < 0f) return true;
        return Time.realtimeSinceStartup - LastMeetingEndTime >= GetDelaySeconds();
    }
}

// 会議終了(MeetingHudが破棄される瞬間)を検知して、待機時間の起点を記録する。
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
public static class GMAutoPossessMeetingEndPatch
{
    public static void Postfix()
    {
        GMAutoPossessTiming.LastMeetingEndTime = Time.realtimeSinceStartup;
    }
}

// ===== GM自動憑依 =====
// GMが死亡している間、毎フレーム憑依メニュー(HauntMenuMinigame)が
// 見つからないか探し続け、見つかったらその場でターゲットへのクリック(SetHauntTarget)を行うだけの
// シンプルな方式。待機時間は「直近の会議終了時刻」からの経過秒数で判定する。
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class GMAutoPossessPatch
{
    static PlayerControl currentTarget = null;
    static PlayerControl lastTarget = null;
    static float searchTimer = 0f;

    public static void Postfix(PlayerControl __instance)
    {
        if (__instance != PlayerControl.LocalPlayer) return;
        if (!Options.OptionGMAutoPossess.GetBool()) return;
        if (__instance.IsAlive() || MeetingHud.Instance != null) return;
        if (__instance.GetCustomRole() != CustomRoles.GM) return;

        // 「初手会議」設定がONで、まだ1日目(最初の会議明け前)の場合は自動憑依を一切行わない
        if (Options.firstturnmeeting && UtilsGameLog.day <= 1) return;

        // 直近の会議終了から、マップに応じた待機時間が経過するまでは何もしない
        // (AirShip: 15秒 / それ以外: 3秒)
        if (!GMAutoPossessTiming.CanStartAutoPossess()) return;

        if (currentTarget == null || !currentTarget.IsAlive())
        {
            searchTimer += Time.fixedDeltaTime;
            if (searchTimer > 1f)
            {
                searchTimer = 0f;
                var targets = PlayerCatch.AllAlivePlayerControls
                    .Where(p => p.PlayerId != __instance.PlayerId)
                    .ToList();
                if (targets.Count > 0)
                    currentTarget = targets[UnityEngine.Random.Range(0, targets.Count)];
            }
        }

        // メニューを開こうとはせず、単純に毎フレーム探すだけ。
        // 自然に開いた瞬間、このFindObjectOfTypeが非nullを返すようになる。
        // includeInactive: trueにして、非アクティブ状態のHauntMenuMinigameも検出できるようにする
        // (生成はされているが、何らかの理由で非表示になっているケースを取りこぼさないため)。
        var hauntMenu = UnityEngine.Object.FindObjectOfType<HauntMenuMinigame>(true);

        if (hauntMenu != null && currentTarget != null && currentTarget.IsAlive())
        {
            if (currentTarget != lastTarget)
            {
                hauntMenu.SetHauntTarget(currentTarget);
                lastTarget = currentTarget;
                Logger.Info($"GM自動憑依: {currentTarget.name}をターゲットに設定しました", "GMAutoPossessPatch");
            }
        }
    }
}

// ===== 予備経路 =====
// HauntMenuMinigame.FixedUpdate自体にも同じ処理を仕込んでおく。
// (PlayerControl.FixedUpdate側のFindObjectOfTypeが何らかの理由で
//  対象を見つけられない場合の保険。こちらはHauntMenuMinigame自身に
//  直接パッチが当たるため、__instanceとして確実にインスタンスを受け取れる)
[HarmonyPatch(typeof(HauntMenuMinigame), nameof(HauntMenuMinigame.FixedUpdate))]
public static class GMAutoPossessHauntMenuPatch
{
    static PlayerControl currentTarget = null;
    static PlayerControl lastTarget = null;
    static float searchTimer = 0f;

    public static void Prefix(HauntMenuMinigame __instance)
    {
        var local = PlayerControl.LocalPlayer;
        if (local == null) return;
        if (!Options.OptionGMAutoPossess.GetBool()) return;
        if (local.IsAlive() || MeetingHud.Instance != null) return;
        if (local.GetCustomRole() != CustomRoles.GM) return;

        // 「初手会議」設定がONで、まだ1日目(最初の会議明け前)の場合は自動憑依を一切行わない
        if (Options.firstturnmeeting && UtilsGameLog.day <= 1) return;

        // 直近の会議終了から、マップに応じた待機時間が経過するまでは何もしない
        // (AirShip: 15秒 / それ以外: 3秒)
        if (!GMAutoPossessTiming.CanStartAutoPossess()) return;

        if (currentTarget == null || !currentTarget.IsAlive())
        {
            searchTimer += Time.fixedDeltaTime;
            if (searchTimer > 1f)
            {
                searchTimer = 0f;
                var targets = PlayerCatch.AllAlivePlayerControls
                    .Where(p => p.PlayerId != local.PlayerId)
                    .ToList();
                if (targets.Count > 0)
                    currentTarget = targets[UnityEngine.Random.Range(0, targets.Count)];
            }
        }

        if (currentTarget != null && currentTarget.IsAlive() && currentTarget != lastTarget)
        {
            __instance.SetHauntTarget(currentTarget);
            lastTarget = currentTarget;
        }
    }
}
