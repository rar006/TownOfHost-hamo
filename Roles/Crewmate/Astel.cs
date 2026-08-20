using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

public sealed class Astel : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Astel),
            player => new Astel(player),
            CustomRoles.Astel,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            33120,
            SetupOptionItem,
            "atl",
            "#ffe066",
            (6, 10),
            from: From.TownOfHost_hamo
        );

    public Astel(PlayerControl player) : base(RoleInfo, player)
    {
        PetActionManager.Register(Player.PlayerId, OnPet);
    }

    static OptionItem OptionRequiredTaskCount;
    static OptionItem OptionStarDuration;

    enum OptionName { AstelRequiredTaskCount, AstelStarDuration }

    static void SetupOptionItem()
    {
        OptionRequiredTaskCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.AstelRequiredTaskCount, new(0, 99, 1), 7, false);
        OptionStarDuration = FloatOptionItem.Create(RoleInfo, 11, OptionName.AstelStarDuration, new(0.5f, 255f, 0.5f), 8f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    AstelStar currentStar;

    void OnPet()
    {
        if (!Player.IsAlive()) return;
        if (!AmongUsClient.Instance.AmHost) return;

        var completed = Player.GetPlayerTaskState().CompletedTasksCount;
        if (completed < OptionRequiredTaskCount.GetInt()) return;

        currentStar?.Remove();
        currentStar = new AstelStar(Player, OptionStarDuration.GetFloat());
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        currentStar?.Tick(UnityEngine.Time.fixedDeltaTime);
        if (currentStar is { IsAlive: false }) currentStar = null;
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
        currentStar?.Remove();
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        if (isForMeeting) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;
        var completed = Player.GetPlayerTaskState().CompletedTasksCount;
        var required = OptionRequiredTaskCount.GetInt();
        return completed >= required
            ? $"{size}<color={color}>{GetString("AstelLowerTextReady")}</color>"
            : $"{size}<color={color}>{string.Format(GetString("AstelLowerTextNotReady"), completed, required)}</color>";
    }
}
