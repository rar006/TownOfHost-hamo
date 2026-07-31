using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// ===== イビルコマンダー (Evil Commander) =====
// イントロ：仲間の背中を後押ししよう
// 陣営：インポスター / 置き換え：ファントム
//
// ワンクリックボタンを使用すると、全てのインポスター(設定次第でマッドメイト系も)の
// キルクールを一括で「上書き後のキルクール」秒に上書きする。
// キルクールが0秒のクイックキラーに対して使っても、その場で設定秒数へ上書きされる。
public sealed class EvilCommander : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilCommander),
            player => new EvilCommander(player),
            CustomRoles.EvilCommander,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            84100,
            SetupOptionItem,
            "evc",
            "#FF0000",
            (2, 20),
            introSound: () => GetIntroSound(RoleTypes.Phantom),
            assignInfo: new RoleAssignInfo(CustomRoles.EvilCommander, CustomRoleTypes.Impostor),
            from: From.TownOfHost_hamo
        );

    public EvilCommander(PlayerControl player) : base(RoleInfo, player)
    {
        AbilityCooldown = OptionAbilityCooldown.GetFloat();
        remainingUses = OptionAbilityUseLimit.GetInt();
    }

    static OptionItem OptionAbilityCooldown;
    static OptionItem OptionAbilityUseLimit;
    static OptionItem OptionOverrideKillCooldown;
    static OptionItem OptionApplyToMadmate;

    static float AbilityCooldown;
    int remainingUses;

    enum OptionName
    {
        EvilCommanderAbilityCooldown,
        EvilCommanderAbilityUseLimit,
        EvilCommanderOverrideKillCooldown,
        EvilCommanderApplyToMadmate,
    }

    static void SetupOptionItem()
    {
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.EvilCommanderAbilityCooldown,
            new(0.5f, 180f, 0.5f), 29.5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityUseLimit = IntegerOptionItem.Create(RoleInfo, 11, OptionName.EvilCommanderAbilityUseLimit,
            new(1, 99, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionOverrideKillCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.EvilCommanderOverrideKillCooldown,
            new(0.5f, 180f, 0.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionApplyToMadmate = BooleanOptionItem.Create(RoleInfo, 13, OptionName.EvilCommanderApplyToMadmate, false, false);
    }

    public override void OnSpawn(bool initialState = false)
    {
        remainingUses = OptionAbilityUseLimit.GetInt();
        Player.RpcResetAbilityCooldown(Sync: true);
    }

    public float CalculateKillCooldown() => Main.NormalOptions.KillCooldown;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;

    bool IUsePhantomButton.IsPhantomRole => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.UseOneclickButton => true;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        // 能力を使用しても、自分自身の進行中のキルクールは変化させない。
        AdjustKillCooldown = true;
        ResetCooldown = false;

        if (!Player.IsAlive()) return;
        if (remainingUses <= 0) return;

        remainingUses--;
        var overrideSeconds = OptionOverrideKillCooldown.GetFloat();
        var applyToMadmate = OptionApplyToMadmate.GetBool();

        foreach (var pc in GetTargets(applyToMadmate))
        {
            // player.SetKillCooldown(...) は内部で RpcProtectedMurderPlayer
            // (キル処理相当のRPC)も呼んでしまうため、キルクールの値だけを
            // 変更したい今回の用途には使わない。
            // 代わりに Main.AllPlayerKillCooldown を直接書き換えて SyncSettings で同期する。
            // (キルクールが0秒のクイックキラーに対しても同じ経路でそのまま上書きできる)
            if (!Main.AllPlayerKillCooldown.ContainsKey(pc.PlayerId))
                Main.AllPlayerKillCooldown.Add(pc.PlayerId, overrideSeconds);
            else
                Main.AllPlayerKillCooldown[pc.PlayerId] = overrideSeconds;
            pc.SyncSettings();
        }

        ResetCooldown = true;
        UtilsGameLog.AddGameLog("EvilCommander",
            $"{UtilsName.GetPlayerColor(Player)} が仲間のキルクールを{overrideSeconds:0.0}秒に上書きした" +
            (applyToMadmate ? "(マッドメイト系にも適用)" : ""));
        UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
    }

    private static IEnumerable<PlayerControl> GetTargets(bool applyToMadmate)
    {
        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            if (pc == null || !pc.IsAlive()) continue;
            var isTarget = pc.Is(CustomRoleTypes.Impostor)
                || (applyToMadmate && pc.Is(CustomRoleTypes.Madmate));
            if (isTarget) yield return pc;
        }
    }
}
