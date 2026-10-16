using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

/// <summary>
/// メッセンジャー: 会議中に /cmd mes ID 内容 で、好きなプレイヤーに一方的な秘匿チャットを送れる。
/// 受け取った側には、誰から届いたかが分かる。返信はできない。
/// </summary>
public sealed class Messenger : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Messenger),
            player => new Messenger(player),
            CustomRoles.Messenger,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            87200,
            SetupOptionItem,
            "mes",
            "#00BCFF"
        );

    static OptionItem OptionPerMeeting;
    static OptionItem OptionTotal;
    enum OptionName { MessengerPerMeeting, MessengerTotal }

    static void SetupOptionItem()
    {
        OptionPerMeeting = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MessengerPerMeeting, new(1, 99, 1), 2, false).SetValueFormat(OptionFormat.Times);
        OptionTotal = IntegerOptionItem.Create(RoleInfo, 11, OptionName.MessengerTotal, new(1, 99, 1), 8, false).SetValueFormat(OptionFormat.Times);
    }

    public Messenger(PlayerControl player) : base(RoleInfo, player)
    {
        usedTotal = 0;
        usedThisMeeting = 0;
    }

    int usedTotal;
    int usedThisMeeting;

    public override void OnStartMeeting() => usedThisMeeting = 0;

    /// <summary>/cmd mes ID 内容(ホストが処理)</summary>
    public void HandleCommand(string[] args)
    {
        var me = Player.PlayerId;
        if (!GameStates.IsMeeting) { Utils.SendMessage(GetString("MessengerOnlyMeeting"), me); return; }
        if (!Player.IsAlive()) { Utils.SendMessage(GetString("MessengerDead"), me); return; }
        if (args.Length < 3 || !byte.TryParse(args[1], out var targetId))
        {
            Utils.SendMessage(GetString("MessengerUsage"), me);
            return;
        }
        var target = PlayerCatch.GetPlayerById(targetId);
        if (target == null || target.PlayerId == me) { Utils.SendMessage(GetString("MessengerNoTarget"), me); return; }
        if (usedTotal >= OptionTotal.GetInt() || usedThisMeeting >= OptionPerMeeting.GetInt())
        {
            Utils.SendMessage(GetString("MessengerNoUses"), me);
            return;
        }
        var text = string.Join(" ", args, 2, args.Length - 2);
        usedTotal++;
        usedThisMeeting++;
        SendRPC();
        var title = Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("MessengerFrom"), Player.Data?.PlayerName));
        Utils.SendMessage(text, target.PlayerId, title);
        Utils.SendMessage(string.Format(GetString("MessengerSent"), target.Data?.PlayerName, OptionTotal.GetInt() - usedTotal), me);
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(RoleInfo.RoleColor, $"({OptionTotal.GetInt() - usedTotal})");

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(usedTotal);
    }
    public override void ReceiveRPC(MessageReader reader) => usedTotal = reader.ReadInt32();
}
