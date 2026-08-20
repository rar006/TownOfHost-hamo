using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Professional : RoleBase, IImpostor, IKiller, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Professional),
            player => new Professional(player),
            CustomRoles.Professional,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            77120,
            SetupOptionItem,
            "pf",
            "#FF1919",
            OptionSort: (3, 3),
            from: From.TownOfHost_hamo
        );

    public Professional(PlayerControl player)
    : base(RoleInfo, player)
    {
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionNormalKillCooldown;
    private static OptionItem OptionAutoReportSeconds;

    private enum OptionName
    {
        ProfessionalNormalKillCooldown,
        ProfessionalAutoReportSeconds
    }

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        // ===== 追加 =====
        // 特殊キル(ワンクリックボタン、通報不可)とは別に、普通のキルボタンを
        // 実装する。こちらは通常通り死体を通報できる。
        OptionNormalKillCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.ProfessionalNormalKillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAutoReportSeconds = FloatOptionItem.Create(RoleInfo, 11, OptionName.ProfessionalAutoReportSeconds, new(0.5f, 90f, 0.5f), 25f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // ワンクリック特殊キルはファントム能力のクールダウンを使う。
        AURoleOptions.PhantomCooldown = OptionKillCooldown.GetFloat();
        // 通常キルはIKiller.CalculateKillCooldownだけに依存せず、各プレイヤーへ送る
        // 個別ゲーム設定にも明示して、ロビー設定値で上書きされないようにする。
        opt.SetFloat(FloatOptionNames.KillCooldown, OptionNormalKillCooldown.GetFloat());
    }

    public float CalculateKillCooldown() => OptionNormalKillCooldown.GetFloat();
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;

    /// <summary>
    /// 普通のキルボタン: 通常通りキルする(死体は通報可能)。
    /// </summary>
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        // 通常キルの死体は必ず通報可能にする。ゲーム開始時の初期値や過去の特殊キルで
        // 通報禁止フラグが残っていても、通常キル対象についてはここで解除する。
        if (info.AttemptTarget != null && ReportDeadBodyPatch.IgnoreBodyids != null)
            ReportDeadBodyPatch.IgnoreBodyids[info.AttemptTarget.PlayerId] = false;

        // DoKill はデフォルトのまま true とし、通常のキル処理を通す。
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        if (!Player.IsAlive()) return;

        var target = Player.GetKillTarget(true);
        if (target == null) return;

        AdjustKillCooldown = false;
        ResetCooldown = true;

        var victimId = target.PlayerId;
        // 通常キル用フックが通報禁止を解除しても、特殊キルでは直後に必ず再設定する。
        if (ReportDeadBodyPatch.IgnoreBodyids != null)
            ReportDeadBodyPatch.IgnoreBodyids[victimId] = true;

        if (!CustomRoleManager.OnCheckMurder(Player, target, target, target, true, false, 1, CustomDeathReason.Kill))
        {
            if (ReportDeadBodyPatch.IgnoreBodyids != null)
                ReportDeadBodyPatch.IgnoreBodyids[victimId] = false;
            ResetCooldown = false;
            return;
        }

        var reportSeconds = OptionAutoReportSeconds.GetFloat();
        // OnCheckMurder内の通常キルフックが後から実行される構成でも、キル確定後に
        // 通報禁止を再適用する。これにより透明ボタンの特殊キルだけが通報不可になる。
        _ = new LateTask(() =>
        {
            var victim = victimId.GetPlayerControl();
            if (victim == null || victim.IsAlive()) return;
            if (ReportDeadBodyPatch.IgnoreBodyids != null)
                ReportDeadBodyPatch.IgnoreBodyids[victimId] = true;

            _ = new LateTask(() =>
            {
                if (ReportDeadBodyPatch.IgnoreBodyids != null && ReportDeadBodyPatch.IgnoreBodyids.ContainsKey(victimId))
                    ReportDeadBodyPatch.IgnoreBodyids[victimId] = false;

                // 設定秒数後、被害者自身が死体を通報したことにする(証拠が残る)。
                var victimInfo = PlayerCatch.GetPlayerInfoById(victimId);
                if (victim != null && victimInfo != null && GameStates.IsInTask)
                    victim.ReportDeadBody(victimInfo);
            }, reportSeconds, "Professional.AutoReport", true);
        }, 0.15f, "Professional.ApplyBodyBlock", true);

        // 特殊キル後も通常キルボタン側は「通常キルのクールダウン」設定で同期する。
        Player.SetKillCooldown(OptionNormalKillCooldown.GetFloat(), delay: true);
    }

    bool IUsePhantomButton.IsPhantomRole => Player.IsAlive();
    bool IUsePhantomButton.IsresetAfterKill => false;

    public override string GetAbilityButtonText() => GetString("ProfessionalAbilityText");
    public override bool OverrideAbilityButton(out string text)
    {
        // MOD導入者のローカルHUDでは、特殊キル専用の画像を使う。
        text = "Professional_SpecialKill";
        return true;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";

        if (isForHud) return GetString("ProfessionalLowerText");
        return $"<size=50%>{GetString("ProfessionalLowerText")}</size>";
    }
}
