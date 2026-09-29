//バグってるから(
/*using AmongUs.GameOptions;
using TownOfHost;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Greedier : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Greedier),
            player => new Greedier(player),
            CustomRoles.Greedier,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            77700,
            SetupOptionItem,
            "gr",
            OptionSort: (7, 8),
            from: From.TownOfHost_Y,
            isNewRole: true
        );
    public Greedier(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        DefaultKillCooldown = OptionDefaultKillCooldown.GetFloat();
        OddKillCooldown = OptionOddKillCooldown.GetFloat();
        EvenKillCooldown = OptionEvenKillCooldown.GetFloat();
    }
    private static OptionItem OptionDefaultKillCooldown;
    private static OptionItem OptionOddKillCooldown;
    private static OptionItem OptionEvenKillCooldown;
    enum OptionName
    {
        GreedierDefaultKillCooldown,
        GreedierOddKillCooldown,
        GreedierEvenKillCooldown,
    }
    private static float DefaultKillCooldown;
    private static float OddKillCooldown;
    private static float EvenKillCooldown;
    bool IsOdd = true;
    public bool CanBeLastImpostor { get; } = false;
    public static void SetupOptionItem()
    {
        OptionDefaultKillCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.GreedierDefaultKillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionOddKillCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.GreedierOddKillCooldown, new(0f, 180f, 2.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionEvenKillCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.GreedierEvenKillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public override void Add()
    {
        var playerId = Player.PlayerId;
        IsOdd = true;
    }
    public float CalculateKillCooldown() => DefaultKillCooldown;

    public override void OnStartMeeting()
    {
        IsOdd = true;
        Main.AllPlayerKillCooldown[Player.PlayerId] = DefaultKillCooldown;
        Player.SyncSettings();
    }
    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!info.IsSuicide)
        {
            (var killer, var target) = info.AttemptTuple;

            if (!IsOdd)
            {
                Logger.Info($"{killer?.Data?.PlayerName}:偶数回目のキル", "Greedier");
                Main.AllPlayerKillCooldown[killer.PlayerId] = EvenKillCooldown;
            }
            else
            {
                Logger.Info($"{killer?.Data?.PlayerName}:奇数回目のキル", "Greedier");
                Main.AllPlayerKillCooldown[killer.PlayerId] = OddKillCooldown;   
            }
            IsOdd = !IsOdd;
        }
    }
}*/