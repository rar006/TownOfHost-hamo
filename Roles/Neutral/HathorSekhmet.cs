using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Neutral;

/// <summary>
/// 会議ごとにハトホル（C）とセクメト（N）を切り替える第三陣営役職。
/// 基本役職はハトホル時クルーメイト、セクメト時インポスターだが、
/// カスタム陣営は常に第三陣営として扱う。
/// </summary>
public sealed class HathorSekhmet : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(HathorSekhmet),
            player => new HathorSekhmet(player),
            CustomRoles.HathorSekhmet,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            95300,
            SetupOptionItem,
            "Hs",
            "#d8ad32",
            (7, 10),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            countType: CountTypes.None,
            // セクメト時はインポスター・マッドメイトを含む全陣営をキル対象にできるよう、
            // デシンクインポスター基盤でターゲット選択を行う。
            isDesyncImpostor: true,
            from: From.TownOfHost_hamo,
            isNewRole: false
        );

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionSuicideAfterKillingImpostor;

    public static float KillCooldown => OptionKillCooldown?.GetFloat() ?? 35f;
    private static bool SuicideAfterKillingImpostor => OptionSuicideAfterKillingImpostor?.GetBool() ?? true;

    // false: ハトホル（クルーフェーズ）、true: セクメト（第三陣営フェーズ）
    private bool IsSekhmet;
    /// <summary>イントロ・説明表示用の現在の神格状態。</summary>
    public bool IsSekhmetPhase => IsSekhmet;
    // 死亡時点の神格を保存し、以後の会議による神格変化で勝利条件を変えない。
    private bool? WinningAsSekhmet;

    private static readonly Color HathorColor = UtilsRoleText.GetRoleColor(CustomRoles.Crewmate);
    private static readonly Color SekhmetColor = UtilsRoleText.GetRoleColor(CustomRoles.Impostor);

    public HathorSekhmet(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        // クルーフェーズでもこの役職のタスクはクルーのタスク勝利へ加算しない。
        IsSekhmet = false;
        WinningAsSekhmet = null;
    }

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSuicideAfterKillingImpostor = BooleanOptionItem.Create(RoleInfo, 11, "HathorSekhmetSuicideAfterKillingImpostor", true, false);
    }

    /// <summary>
    /// 現在の神格に対応する基本役職を返す。会議後フックとの実行順差はAfterMeetingTasks側の
    /// 明示的なSetRole同期でも吸収し、役職名・キル能力・基本役職が同じフェーズになるようにする。
    /// </summary>
    public override RoleTypes? AfterMeetingRole => IsSekhmet ? RoleTypes.Impostor : RoleTypes.Crewmate;

    bool IKiller.CanKill => IsSekhmet;
    bool IKiller.CanUseKillButton() => Player.IsAlive() && IsSekhmet;
    float IKiller.CalculateKillCooldown() => KillCooldown;
    bool IKiller.CanUseSabotageButton() => false;
    bool IKiller.CanUseImpostorVentButton() => false;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // セクメト時のみ、基本インポスター役職へ反映されるキルクールを設定する。
        // これにより会議後の役職切替直後もキルボタンが待機状態のまま残らない。
        if (IsSekhmet)
            AURoleOptions.KillCooldown = KillCooldown;
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || WinningAsSekhmet.HasValue) return;

        IsSekhmet = !IsSekhmet;
        var nextBaseRole = IsSekhmet ? RoleTypes.Impostor : RoleTypes.Crewmate;
        Player.RpcSetRole(nextBaseRole, true);
        Player.GetPlayerState().NowRoleType = nextBaseRole;
        // 神格状態を先に同期してから、基本役職・キルクール・能力ボタンを再計算する。
        // セクメトへ変化した直後に、クライアント側へクルー時のキル不可状態が残る問題を防ぐ。
        SendRPC();
        Player.MarkDirtySettings();
        UtilsOption.MarkEveryoneDirtySettings();
        Player.RpcResetAbilityCooldown(Sync: true);

        // 会議後の役職再同期が完了してから名前・記号を更新する。
        _ = new LateTask(
            () =>
            {
                if (AmongUsClient.Instance.AmHost && Player != null && Player.IsAlive())
                    UtilsNotifyRoles.NotifyRoles(NoCache: true, ForceLoop: true);
            },
            Main.LagTime,
            "HathorSekhmet.SwitchDivinity",
            true
        );
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost || !IsSekhmet || !SuicideAfterKillingImpostor) return;
        if (info.AttemptTarget == null || !info.AttemptTarget.GetCustomRole().IsImpostor()) return;

        LockWinningPhase();
        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayerV2(Player);
    }

    public override void OnMurderPlayerAsTarget(MurderInfo info)
        => LockWinningPhase();

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (exiled?.PlayerId == Player.PlayerId)
            LockWinningPhase();
    }

    private void LockWinningPhase()
    {
        if (WinningAsSekhmet.HasValue) return;
        WinningAsSekhmet = IsSekhmet;
        if (AmongUsClient.Instance.AmHost) SendRPC();
    }

    public override void CheckWinner(GameOverReason reason)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (CustomWinnerHolder.CantWinPlayerIds.Contains(Player.PlayerId)) return;

        bool winsAsSekhmet = WinningAsSekhmet ?? IsSekhmet;
        bool shouldAddWin = winsAsSekhmet
            ? CustomWinnerHolder.WinnerTeam is not (CustomWinner.Default or CustomWinner.Draw or CustomWinner.None or CustomWinner.Crewmate)
            : CustomWinnerHolder.WinnerTeam is CustomWinner.Crewmate;

        if (!shouldAddWin) return;

        CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.HathorSekhmet);
        CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
    }

    public override void OverrideTrueRoleName(ref Color roleColor, ref string roleText)
    {
        roleColor = IsSekhmet ? SekhmetColor : HathorColor;
        roleText = GetString(IsSekhmet ? "Sekhmet" : "Hathor");
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
        => Utils.ColorString(IsSekhmet ? SekhmetColor : HathorColor, GetString(IsSekhmet ? "HathorSekhmetNeutralMark" : "HathorSekhmetCrewMark"));

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != Player.PlayerId || isForMeeting) return "";

        string key = IsSekhmet ? "SekhmetIntro" : "HathorIntro";
        return isForHud ? GetString(key) : $"<size=60%>{GetString(key)}</size>";
    }

    public void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(IsSekhmet);
        sender.Writer.Write(WinningAsSekhmet.HasValue);
        if (WinningAsSekhmet.HasValue) sender.Writer.Write(WinningAsSekhmet.Value);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        IsSekhmet = reader.ReadBoolean();
        WinningAsSekhmet = reader.ReadBoolean() ? reader.ReadBoolean() : null;
    }
}
