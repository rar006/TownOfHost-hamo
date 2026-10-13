using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Madmate;

// ===== インフェクデッド (Infected) =====
// 判定：クルーメイト / 陣営：マッドメイト / カウント：クルーメイト
// イントロ：感染してしまった...
//
// インフェクトによって作成されたマッドメイト。インポスター勝利に追加勝利する。
// この役職に変化すると元の勝利条件を失う。
// 設定数のタスクを完了させると感染が発動する。この役職の死体を通報したプレイヤーを
// 感染状態にし、設定ターン数が経つとインフェクデッドへ役職が変化する
// (対象は自身が感染状態になっていることに気づかない)。
public sealed class Infected : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Infected),
            player => new Infected(player),
            CustomRoles.Infected,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            96900,
            SetupOptionItem,
            "Ifd",
            "#8B0000",
            (5, 9),
            from: From.TownOfHost_hamo
        );

    public Infected(PlayerControl player) : base(RoleInfo, player)
    {
        taskCountToActivate = OptionTaskCountToActivate.GetInt();
        turnsToTransform = OptionTurnsToTransform.GetInt();
        includeImpostor = OptionIncludeImpostor.GetBool();
        abilityActivated = false;
    }

    static OptionItem OptionTaskCountToActivate;
    static OptionItem OptionTurnsToTransform;
    static OptionItem OptionIncludeImpostor;

    enum OptionName
    {
        InfectedTaskCountToActivate,
        InfectedTurnsToTransform,
        InfectedIncludeImpostor,
    }

    private readonly int taskCountToActivate;
    private readonly int turnsToTransform;
    private readonly bool includeImpostor;

    // 感染能力が(タスク進行によって)発動済みかどうか
    private bool abilityActivated;

    static void SetupOptionItem()
    {
        // 要望により、インフェクデッド自身の設定項目は表示せず、
        // インフェクトの設定の中にまとめて表示する
        // (インフェクデッドは直接選べる役職ではなく、インフェクトの能力で
        //  変化してなる役職のため)。
        var parent = TownOfHost.Roles.Impostor.Infect.RoleInfo.RoleOption;

        OptionTaskCountToActivate = IntegerOptionItem.Create(RoleInfo, 10, OptionName.InfectedTaskCountToActivate,
            new(0, 99, 1), 4, false, parent)
            .SetValueFormat(OptionFormat.Pieces);
        OptionTurnsToTransform = IntegerOptionItem.Create(RoleInfo, 11, OptionName.InfectedTurnsToTransform,
            new(0, 99, 1), 1, false, parent)
            .SetValueFormat(OptionFormat.Times);
        OptionIncludeImpostor = BooleanOptionItem.Create(RoleInfo, 12, OptionName.InfectedIncludeImpostor, true, false, parent);
    }

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        // インポスター陣営の勝利に相乗りする追加勝利者。単独では勝利判定を行わない。
        if (Player?.IsAlive() != true) return false;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Impostor) return false;

        winnerRole = CustomRoles.Infected;
        CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
        return true;
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!Is(player) || abilityActivated) return;
        if (!player.IsAlive()) return;
        if (taskCountToActivate <= 0) return;
        if (MyTaskState.CompletedTasksCount < taskCountToActivate) return;

        abilityActivated = true;
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!abilityActivated) return;
        if (target?.Object == null || !Is(target.Object)) return;
        if (reporter == null || reporter.PlayerId == Player.PlayerId) return;
        if (!includeImpostor && reporter.GetCustomRole().IsImpostor()) return;

        TownOfHost.Modules.InfectedTracker.Infect(reporter.PlayerId, turnsToTransform);

        UtilsGameLog.AddGameLog("Infected",
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
