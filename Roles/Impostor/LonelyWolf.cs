using AmongUs.GameOptions;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

/// <summary>
/// 孤独な狼(インポスター)
/// ・仲間のインポスターが分からない。仲間を認識できる他のインポスターからは「孤独な狼」として見える
/// ・キル可能な人外をキルしようとすると、代わりに対象をマッドメイトに変える(お互いが分かる)
///   インポスターに対しては設定次第: ON=マッドメイトに変える / OFF=キルが弾かれる
/// </summary>
public sealed class LonelyWolf : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(LonelyWolf),
            player => new LonelyWolf(player),
            CustomRoles.LonelyWolf,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            92600,
            SetupOptionItem,
            "lw",
            "#FF1919",
            isCantSeeTeammates: true,
            isNewRole: true
        );

    static OptionItem OptionKillCooldown;
    static OptionItem OptionMadImpostor;
    enum OptionName { LonelyWolfMadImpostor }

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0.5f, 180f, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionMadImpostor = BooleanOptionItem.Create(RoleInfo, 11, OptionName.LonelyWolfMadImpostor, true, false);
    }

    public LonelyWolf(PlayerControl player) : base(RoleInfo, player) { }

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();

    /// <summary>キル可能な人外か(クルー・マッドメイト以外で、キルボタンを使える)</summary>
    static bool IsKillableNonCrew(PlayerControl target)
    {
        var role = target.GetCustomRole();
        if (role.IsCrewmate() || role.IsMadmate()) return false;
        if (role.IsImpostor()) return true;
        return target.GetRoleClass() is IKiller k && k.CanUseKillButton();
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        if (killer.PlayerId != Player.PlayerId || target == null || !IsKillableNonCrew(target)) return;
        info.DoKill = false;
        if (target.GetCustomRole().IsImpostor() && !OptionMadImpostor.GetBool())
        {
            // キルが弾かれる
            killer.RpcProtectedMurderPlayer(target);
            killer.SetKillCooldown();
            return;
        }
        MakeMadmate(target);
    }

    void MakeMadmate(PlayerControl target)
    {
        Player.RpcProtectedMurderPlayer(target);
        target.RpcProtectedMurderPlayer(target);
        _ = new LateTask(() => Player.SetKillCooldown(target: target), Main.LagTime, "LonelyWolf.Kc", true);
        UtilsGameLog.AddGameLog("SideKick", string.Format(GetString("log.Sidekick"), UtilsName.GetPlayerColor(target, true) + $"({UtilsRoleText.GetTrueRoleName(target.PlayerId)})", UtilsName.GetPlayerColor(Player, true)));
        target.RpcSetCustomRole(CustomRoles.SKMadmate, log: null);
        // お互いが分かる
        NameColorManager.Add(Player.PlayerId, target.PlayerId, "#ff1919");
        NameColorManager.Add(target.PlayerId, Player.PlayerId, "#ff1919");
        if (!Utils.RoleSendList.Contains(target.PlayerId)) Utils.RoleSendList.Add(target.PlayerId);
        target.RpcSetRole(Options.SkMadCanUseVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate, true);
        PlayerCatch.SKMadmateNowCount++;
        target.MarkDirtySettings();
        UtilsNotifyRoles.NotifyRoles();
    }

    /// <summary>仲間を認識できる他のインポスターからは「孤独な狼」として見える</summary>
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        if (seer == null || seer.PlayerId == Player.PlayerId) return;
        var r = seer.GetCustomRole();
        if (r.IsImpostor() && r.GetRoleInfo()?.IsCantSeeTeammates != true && !seer.Is(CustomRoles.OneWolf)) enabled = true;
    }
}
