using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Madmate;

public sealed class MadCount : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadCount),
            player => new MadCount(player),
            CustomRoles.MadCount,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            76950,
            SetupOptionItem,
            "mdc",
            "#be7c35",
            (3, 2),
            true,
            from: From.TownOfHost_hamo
        );

    public MadCount(PlayerControl player) : base(RoleInfo, player)
    {
        count = 0f;
        canVent = OptionCanVent.GetBool();
    }

    static OptionItem OptionCanVent;
    bool canVent;
    float count;

    enum OptionName { MadCountCanVent }

    static void SetupOptionItem()
    {
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 10, OptionName.MadCountCanVent, true, false);
        RoleAddAddons.Create(RoleInfo, 30, MadMate: true, DefaaultOn: true);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;
        if (GameStates.IsMeeting) return;

        var seesImpostor = Player.GetPlayersInAbilityRangeSorted(false)
            .Any(pc => pc != null && pc.IsAlive() && pc.GetCustomRole().IsImpostor());

        if (seesImpostor)
        {
            count += UnityEngine.Time.fixedDeltaTime;
            SendRpc();
        }
    }

    public override void OnStartMeeting()
    {
        // 会議開始時に、マッドカウント本人だけへ近接していた秒数を通知する。
        if (AmongUsClient.Instance.AmHost)
            Utils.SendMessage(string.Format(GetString("MadCountMeetingMessage"), (int)count), Player.PlayerId);

        // 通知後は次の会議に向けてリセットする。
        count = 0f;
        SendRpc();
    }

    public override bool CanClickUseVentButton => canVent;

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        // 会議中の下部表示ではなく、会議開始時の本人限定チャットでのみ通知する。
        return "";
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(count);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        count = reader.ReadSingle();
    }
}
