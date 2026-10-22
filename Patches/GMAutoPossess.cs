using System.Linq;

using BepInEx.Unity.IL2CPP.Utils.Collections;

using HarmonyLib;

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



    /// <summary>ラウンド開始(会議が一度も無い状態)のとき、憑依を始めるまでの猶予時間(秒)。</summary>

    public const float RoundStartGraceSeconds = 5f;



    /// <summary>直近の会議が終了した時刻(realtimeSinceStartup)。MeetingHudPatch側で設定する。</summary>

    public static float LastMeetingEndTime = -999f;



    /// <summary>ラウンド開始の基準時刻。CanStartAutoPossessが最初に呼ばれた時に一度だけ記録する。</summary>

    private static float? _roundStartCheckedAt;



    public static bool IsAirship()

        => (MapNames)Main.NormalOptions.MapId == MapNames.Airship;



    /// <summary>マップに応じた、会議終了後の待機時間を返す。</summary>

    public static float GetDelaySeconds()

        => IsAirship() ? AirshipDelaySeconds : OtherMapDelaySeconds;



    /// <summary>

    /// 今、自動憑依処理を始めてよいかどうか。

    /// 直近の会議終了時刻から、マップに応じた待機時間が経過しているかで判定する。

    /// (会議が一度も無かった場合=ゲーム開始直後は、固定の猶予時間で判定する)

    /// </summary>

    public static bool CanStartAutoPossess()

    {

        if (LastMeetingEndTime < 0f)

        {

            _roundStartCheckedAt ??= Time.realtimeSinceStartup;

            return Time.realtimeSinceStartup - _roundStartCheckedAt.Value >= RoundStartGraceSeconds;

        }

        return Time.realtimeSinceStartup - LastMeetingEndTime >= GetDelaySeconds();

    }



    /// <summary>新しいゲーム開始時に呼ぶ想定(ラウンド開始猶予の基準時刻をリセットする)。</summary>

    public static void ResetForNewGame()

    {

        _roundStartCheckedAt = null;

        LastMeetingEndTime = -999f;

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

// GMの「憑依」ボタン(アビリティボタン)を自動で1回押すだけの処理。

// ボタンを押した後の「誰に憑依するか」の選択自体は、ネイティブのHauntMenuMinigame UIを

// そのままプレイヤー自身に操作してもらう(対象を自由に切り替えられるようにするため、

// ここで勝手にターゲットを決めたりはしない)。

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]

public static class GMAutoPossessPatch

{

    // 一度ボタンを押したら、次にリセットされるまでは再度押さない

    // (会議終了時にfalseへ戻し、次の待機時間経過後にまた1回だけ押す)

    static bool alreadyClickedThisWindow = false;
    static float logThrottle = 0f;
    // 押した後に憑依の画面が本当に開いたかを確かめる。開かなかったら、少し待ってから押し直す
    const float VerifySeconds = 1.5f;
    const int MaxClickAttempts = 5;
    static float clickedAt = -1f;
    static bool verified;
    static int clickAttempts;
    static bool IsHauntMenuOpen()
    {
        try { return Minigame.Instance != null && Minigame.Instance.TryCast<HauntMenuMinigame>() != null; }
        catch { return false; }
    }

    // ===== canInteract=false 長時間固着に対する救済 =====
    // 「時々、いつまで経っても自動憑依されない」不具合の実体は、待機時間を過ぎても
    // AbilityButton.canInteract が false のまま固まってしまうケースであることをログで確認した。
    // (原因はバニラ側のボタン状態更新の取りこぼしと見られ、こちら側で確実な再現条件は
    //  特定できていないため、症状側から強制的に復旧させる対処とする)
    // 待機時間経過後、これだけ経ってもクリックできないままなら、クールダウンを
    // 強制的に0にしてボタンを復帰させる。
    const float StuckRecoverySeconds = 20f;
    static float _stuckSince = -1f;




    public static void Postfix(PlayerControl __instance)

    {

        if (__instance != PlayerControl.LocalPlayer) return;

        if (!Options.OptionGMAutoPossess.GetBool()) return;

        if (!GameStates.IsInGame) return; // ゲーム終了後の後処理ループ中に誤発火するのを防ぐ

        if (__instance.IsAlive() || MeetingHud.Instance != null) return;

        if (__instance.GetCustomRole() != CustomRoles.GM) return;



        // 「初手会議」設定がONで、まだ1日目(最初の会議明け前)の場合は自動憑依を一切行わない

        if (Options.firstturnmeeting && UtilsGameLog.day <= 1) return;



        // 直近の会議終了から、マップに応じた待機時間が経過するまでは何もしない

        if (!GMAutoPossessTiming.CanStartAutoPossess())
        {
            _stuckSince = -1f; // 待機時間内は固着カウントを始めない
            return;
        }



        if (alreadyClickedThisWindow)
        {
            if (verified) return;
            if (IsHauntMenuOpen()) { verified = true; return; }
            // 押したのに画面が開かなかった(ボタンの状態の取りこぼしなど)。少し待ってから押し直す
            if (Time.realtimeSinceStartup - clickedAt < VerifySeconds || clickAttempts >= MaxClickAttempts) return;
            Logger.Info($"GM自動憑依: 憑依の画面が開かなかったため押し直します({clickAttempts}/{MaxClickAttempts})", "GMAutoPossess");
            alreadyClickedThisWindow = false;
        }



        // ログが毎フレーム出ないよう、1秒に1回だけ状況を出力する。

        logThrottle += Time.fixedDeltaTime;

        bool shouldLog = logThrottle >= 1f;

        if (shouldLog) logThrottle = 0f;



        var hud = HudManager.Instance;

        if (hud == null)

        {

            if (shouldLog) Logger.Info("GM自動憑依: HudManager.Instanceがnullのためスキップ", "GMAutoPossess");

            return;

        }

        if (hud.AbilityButton == null)

        {

            if (shouldLog) Logger.Info("GM自動憑依: AbilityButtonがnullのためスキップ", "GMAutoPossess");

            return;

        }

        if (!hud.AbilityButton.gameObject.activeInHierarchy)

        {

            if (shouldLog) Logger.Info("GM自動憑依: AbilityButtonが非アクティブ(非表示)のためスキップ", "GMAutoPossess");

            _stuckSince = -1f;

            return;

        }

        if (!hud.AbilityButton.canInteract)

        {

            // 待機時間はとっくに経過しているのに操作不能な状態が続いている場合、
            // 一定時間で見切りをつけてクールダウンを強制的に解除する。
            if (_stuckSince < 0f) _stuckSince = Time.realtimeSinceStartup;
            var stuckFor = Time.realtimeSinceStartup - _stuckSince;
            if (stuckFor >= StuckRecoverySeconds)
            {
                Logger.Info($"GM自動憑依: AbilityButtonのcanInteract=falseが{StuckRecoverySeconds}秒以上続いたため、クールダウンを強制解除します", "GMAutoPossess");
                try
                {
                    // Timer/MaxCoolDownは直接アクセスできない(非公開または存在しない)ため、
                    // 実際にボタン内で読まれている isCoolingDown フラグを直接falseにする。
                    // (Patches/ActionButtonPatch.cs の AbilityButtonDoClickPatch でも
                    //  この isCoolingDown が判定に使われていることを確認済み)
                    hud.AbilityButton.isCoolingDown = false;
                }
                catch (System.Exception ex)
                {
                    Logger.Error($"GM自動憑依: クールダウン強制解除に失敗しました: {ex.Message}", "GMAutoPossess");
                }
                _stuckSince = -1f;
                // 強制解除した直後の同フレームではまだcanInteractが反映されていない可能性があるため、
                // 実際のクリックは次のFixedUpdateに委ねる(このフレームはここで終了)。
                return;
            }

            if (shouldLog) Logger.Info("GM自動憑依: AbilityButtonがcanInteract=falseのためスキップ", "GMAutoPossess");

            return;

        }

        _stuckSince = -1f; // 正常に操作可能な状態に戻ったので固着カウントをリセット



        // 憑依できる対象(自分以外の生存者)が誰もいない状態でクリックすると、

        // ネイティブのHauntMenuMinigame.Begin側で例外が発生してしまうため、事前にガードする。

        bool hasTarget = false;

        foreach (var p in PlayerCatch.AllAlivePlayerControls)

        {

            if (p != null && p.PlayerId != __instance.PlayerId) { hasTarget = true; break; }

        }

        if (!hasTarget)

        {

            if (shouldLog) Logger.Info("GM自動憑依: 憑依できる対象(生存者)がいないためスキップ", "GMAutoPossess");

            return;

        }



        Logger.Info("GM自動憑依: AbilityButtonをクリックします", "GMAutoPossess");

        hud.AbilityButton.DoClick();
        alreadyClickedThisWindow = true;
        clickedAt = Time.realtimeSinceStartup;
        clickAttempts++;
        verified = IsHauntMenuOpen();



        if (Options.OptionGMAutoPossessPreferImpostor.GetBool())

        {

            hud.StartCoroutine(CoPreferImpostorTarget().WrapToIl2Cpp());

        }

    }



    // Haunt画面が開いた直後、対象候補にインポスターが居れば優先的に選択する。

    // (実験的な機能。ネイティブのHauntMenuMinigame.HauntTargetプロパティを直接書き換える)

    private static System.Collections.IEnumerator CoPreferImpostorTarget()

    {

        for (int i = 0; i < 30; i++) // 最大0.5秒ほど待つ

        {

            var hauntMenu = UnityEngine.Object.FindObjectOfType<HauntMenuMinigame>(true);

            if (hauntMenu != null)

            {

                var pool = Options.OptionGMAutoPossessAliveOnly.GetBool()

                    ? PlayerCatch.AllAlivePlayerControls

                    : PlayerControl.AllPlayerControls.ToArray().Where(p => p != null && !p.Data.Disconnected);

                var impostor = pool.FirstOrDefault(p => p != null && p.Is(CustomRoleTypes.Impostor));

                if (impostor != null)

                {

                    hauntMenu.HauntTarget = impostor;

                    Logger.Info($"GM自動憑依: インポスター({impostor.GetRealName()})を優先的に選択しました", "GMAutoPossess");

                }

                yield break;

            }

            yield return null;

        }

    }



    // 次の会議が終わったら、また1回だけ自動で押せるように戻す。

    public static void ResetClickLatch()
    {
        alreadyClickedThisWindow = false;
        verified = false;
        clickAttempts = 0;
        clickedAt = -1f;
    }

}



// 会議終了のたびに、自動憑依ボタンの「1回押した」ラッチをリセットする。

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]

public static class GMAutoPossessResetLatchPatch

{

    public static void Postfix() => GMAutoPossessPatch.ResetClickLatch();

}

