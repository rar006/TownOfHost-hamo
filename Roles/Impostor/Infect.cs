using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Modules;

namespace TownOfHost.Roles.Impostor;

// ===== インフェクト (Infect) =====
// 判定：ファントム / 陣営：インポスター / カウント：インポスター
// イントロ：君は既に感染している
//
// 自身の死体を通報したプレイヤーの役職をインフェクデッドに変化させる。
// この役職の死体を通報したプレイヤーは、設定ターン数が経つとターン開始時に
// インフェクデッドへ役職が上書きされる(通報したプレイヤー自身はそのことに気づかない)。
// 変化する前に死亡した場合は役職の変化はない。
// ワンクリックの「自殺」ボタンを持ち、自殺時の死体通報でも感染は発動する。
public sealed class Infect : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Infect),
            player => new Infect(player),
            CustomRoles.Infect,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            96800,
            SetupOptionItem,
            "Inf",
            "#8B0000",
            (5, 8),
            isDesyncImpostor: true,
            from: From.TownOfHost_hamo
        );

    public Infect(PlayerControl player) : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        phantomCooldown = OptionPhantomCooldown.GetFloat();
        turnsToTransform = OptionTurnsToTransform.GetInt();
        includeImpostor = OptionIncludeImpostor.GetBool();

        // ゲーム開始時、前回のゲームの感染記録が残らないようにする。
        InfectedTracker.Reset();
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionPhantomCooldown;
    static OptionItem OptionTurnsToTransform;
    static OptionItem OptionIncludeImpostor;

    enum OptionName
    {
        InfectTurnsToTransform,
        InfectIncludeImpostor,
    }

    private readonly float killCooldown;
    private readonly float phantomCooldown;
    private readonly int turnsToTransform;
    private readonly bool includeImpostor;

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown,
            new(0f, 180f, 1f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionPhantomCooldown = FloatOptionItem.Create(RoleInfo, 11, GeneralOption.Cooldown,
            new(0f, 255f, 1f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionTurnsToTransform = IntegerOptionItem.Create(RoleInfo, 12, OptionName.InfectTurnsToTransform,
            new(0, 99, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionIncludeImpostor = BooleanOptionItem.Create(RoleInfo, 13, OptionName.InfectIncludeImpostor, true, false);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = phantomCooldown;
    }

    public bool CanUseKillButton() => true;

    // 【重要・バグ修正】インフェクトは isDesyncImpostor(Phantomベース)のため、
    // 通常のファントム系ロジックを流用した結果、本来同じインポスター陣営を
    // キルできないはずなのに、インポスターも普通にキルできてしまっていた。
    // 明示的にインポスター陣営はキル対象から除外する。
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.AttemptTarget != null && info.AttemptTarget.GetCustomRole().GetCustomRoleTypes() == CustomRoleTypes.Impostor)
        {
            info.CanKill = false;
        }
    }
    public float CalculateKillCooldown() => killCooldown;
    // インポスター陣営の通常役職として、サボタージュボタンは通常通り使用できる。
    public bool CanUseSabotageButton() => true;

    public override bool OverrideAbilityButton(out string text)
    {
        text = GetString("Infect.SuicideButtonText");
        return true;
    }

    /// <summary>能力ボタン(自殺)が押された時に呼ぶ。AbilityButtonDoClickPatchから呼び出す。</summary>
    public void OnSuicideButtonClicked()
    {
        if (!Player.IsAlive()) return;
        MyState.DeathReason = CustomDeathReason.Suicide;
        Player.SetRealKiller(Player);
        Player.RpcMurderPlayer(Player);

        UtilsGameLog.AddGameLog("Infect",
            $"{UtilsName.GetPlayerColor(Player)}が自殺した");
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (target?.Object == null || !Is(target.Object)) return;
        if (reporter == null || reporter.PlayerId == Player.PlayerId) return;
        if (!includeImpostor && reporter.GetCustomRole().IsImpostor()) return;

        InfectedTracker.Infect(reporter.PlayerId, turnsToTransform);

        UtilsGameLog.AddGameLog("Infect",
            $"{UtilsName.GetPlayerColor(reporter)}が{UtilsName.GetPlayerColor(Player)}の死体を通報し感染した");
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
