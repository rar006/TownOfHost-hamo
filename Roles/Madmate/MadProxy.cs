using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Impostor;
using static UnityEngine.GraphicsBuffer;

namespace TownOfHost.Roles.Madmate;

public sealed class MadProxy : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadProxy),
            player => new MadProxy(player),
            CustomRoles.MadProxy,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Madmate,
            95100,
            SetupOptionItem,
            "Mpx",
            "#bd5429",
            (0, 0),
            assignInfo: new RoleAssignInfo(CustomRoles.MadProxy, CustomRoleTypes.Madmate)
            {
                IsInitiallyAssignableCallBack = () => false,
                AssignCountRule = new(0, 0, 1)
            },
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_hamo
        );

    public static float KillCooldown => Surrogate.MadProxyKillCooldown;

    public MadProxy(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    private static void SetupOptionItem()
    {
        // 通常配役されない子役職のため、独立した出現率・最大数・設定行は表示しない。
        // キルクールと変身時間はサロゲート欄の見出し付き設定を使用する。
        if (Options.CustomRoleSpawnChances != null && Options.CustomRoleSpawnChances.TryGetValue(CustomRoles.MadProxy, out var spawn))
        {
            spawn.SetValue(0, true, false);
            spawn.SetHidden(true);
        }
        if (Options.CustomRoleCounts != null && Options.CustomRoleCounts.TryGetValue(CustomRoles.MadProxy, out var count))
        {
            count.SetValue(0, true, false);
            count.SetHidden(true);
        }
    }

    public override RoleTypes? AfterMeetingRole => RoleTypes.Impostor;
    public bool CanUseKillButton() => Player.IsAlive();
    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseImpostorVentButton() => Options.SkMadCanUseVent.GetBool();
    public bool CanUseSabotageButton() => false;

    public bool OverrideKillButton(out string text)
    {
        text = "Surrogate_Kill";
        return true;
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterDuration = Surrogate.OptionMadProxyTransformDuration?.GetFloat() ?? 12f;
    }
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        // 主であるサロゲートはキル対象にならない。ガード演出だけを返す。
        if (SurrogateSystem.TryGetOwner(Player.PlayerId, out var owner) && info.AttemptKiller?.PlayerId == owner.PlayerId)
        {
            info.CanKill = false;
            info.DoKill = false;

            float TurnTimer = 0;
            IUsePhantomButton.IPPlayerKillCooldown.TryGetValue(Player.PlayerId, out TurnTimer);
            Main.AllPlayerKillCooldown.TryGetValue(Player.PlayerId, out var killcool);
            if (MeetingStates.FirstMeeting && !Options.FixFirstKillCooldown.GetBool() && killcool > 10 &&
            (PlayerState.GetByPlayerId(Player.PlayerId)?.Is10secKillButton == true))
                killcool = 10;
            float cooldown = killcool - TurnTimer;
            if (cooldown <= 1) cooldown = 0.005f;

            Player.SetKillCooldown(cooldown);
            return false;
        }
        return true;
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        SurrogateSystem.ShareKillCooldownAfterKill(Player);
        SurrogateSystem.TransformProxyAfterKill(Player);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (SurrogateSystem.TryGetOwner(Player.PlayerId, out var owner))
            SurrogateSystem.CheckOwnerDeath(owner);
    }
}
