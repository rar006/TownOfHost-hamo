using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// ===== 悪役の狼 (VillainWolf) =====
// From: TownOfHost_hamo
// イントロ：この舞台の終幕を望みつ
// 陣営：インポスター / 判定：インポスター
//
// ゲーム開始から設定ターン数経過 or 設定キル数到達のどちらか早い方まで、
// 悪役の狼が行ったキルの死体にマークが付き、「悪役の狼生存中」の通知が定期的に流れる。
// 条件達成で覚醒し、キルクールダウンが短縮される。設定次第で覚醒時に属性(AddOn)を1つ付与できる。
public sealed class VillainWolf : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(VillainWolf),
            player => new VillainWolf(player),
            CustomRoles.VillainWolf,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            9000,
            SetupOptionItem,
            "vw",
            "#ff1919",
            (8, 3),
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_hamo
        );

    public VillainWolf(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldownBefore = OptionKillCooldown.GetFloat();
        KillCooldownAfter = OptionAwakenedKillCooldown.GetFloat();
        AwakenTurns = OptionAwakenTurns.GetInt();
        AwakenKills = OptionAwakenKills.GetInt();
        GiveAttribute = OptionGiveAttributeOnAwaken.GetBool();
        AttributeToGive = AttributeChoices[OptionAttributeToGive.GetValue()];

        KillCount = 0;
        TurnCount = 0;
        Awakened = false;
        MarkedVictims = new();
    }

    static OptionItem OptionAwakenTurns;
    static OptionItem OptionAwakenKills;
    static OptionItem OptionKillCooldown;
    static OptionItem OptionAwakenedKillCooldown;
    static OptionItem OptionGiveAttributeOnAwaken;
    static OptionItem OptionAttributeToGive;

    enum OptionName
    {
        VillainWolfAwakenTurns,
        VillainWolfAwakenKills,
        VillainWolfAwakenedKillCooldown,
        VillainWolfGiveAttributeOnAwaken,
        VillainWolfAttributeToGive
    }

    private float KillCooldownBefore;
    private float KillCooldownAfter;
    private int AwakenTurns;
    private int AwakenKills;
    private bool GiveAttribute;
    private CustomRoles AttributeToGive;

    private int KillCount;
    private int TurnCount;
    public bool Awakened { get; private set; }
    private List<byte> MarkedVictims;

    // 覚醒時に選べる属性(AddOn)の候補。任意で拡張可。
    private static readonly CustomRoles[] AttributeChoices =
    {
        CustomRoles.Powerful,
        CustomRoles.Speeding,
        CustomRoles.Guarding,
        CustomRoles.Watching,
    };

    private static void SetupOptionItem()
    {
        OptionAwakenTurns = IntegerOptionItem.Create(RoleInfo, 10, OptionName.VillainWolfAwakenTurns, new(1, 10, 1), 4, false)
            .SetValueFormat(OptionFormat.Times);
        OptionAwakenKills = IntegerOptionItem.Create(RoleInfo, 11, OptionName.VillainWolfAwakenKills, new(1, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 12, GeneralOption.KillCooldown, new(0.5f, 60f, 0.5f), 29.5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAwakenedKillCooldown = FloatOptionItem.Create(RoleInfo, 13, OptionName.VillainWolfAwakenedKillCooldown, new(0.5f, 60f, 0.5f), 22.5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionGiveAttributeOnAwaken = BooleanOptionItem.Create(RoleInfo, 14, OptionName.VillainWolfGiveAttributeOnAwaken, true, false);
        OptionAttributeToGive = StringOptionItem.Create(RoleInfo, 15, OptionName.VillainWolfAttributeToGive,
            AttributeChoices.Select(x => x.ToString()).ToArray(), 0, false)
            .SetParent(OptionGiveAttributeOnAwaken);
    }

    public override void ApplyGameOptions(IGameOptions opt) { }

    public bool CanUseSabotageButton() => true;
    public float CalculateKillCooldown() => Awakened ? KillCooldownAfter : KillCooldownBefore;

    private enum RPC_type
    {
        SetAwakened,
        AddMarkedVictim,
        SetProgress
    }
    private void SendRPC(RPC_type type, byte targetId = byte.MaxValue)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)type);
        switch (type)
        {
            case RPC_type.AddMarkedVictim:
                sender.Writer.Write(targetId);
                break;
            case RPC_type.SetProgress:
                sender.Writer.Write(TurnCount);
                sender.Writer.Write(KillCount);
                break;
        }
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        var type = (RPC_type)reader.ReadByte();
        switch (type)
        {
            case RPC_type.SetAwakened:
                Awakened = true;
                break;
            case RPC_type.AddMarkedVictim:
                var id = reader.ReadByte();
                if (!MarkedVictims.Contains(id)) MarkedVictims.Add(id);
                break;
            case RPC_type.SetProgress:
                TurnCount = reader.ReadInt32();
                KillCount = reader.ReadInt32();
                break;
        }
    }

    // キル成功後: 覚醒前ならキル数を加算し死体にマークを付ける
    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Awakened) return;

        var target = info.AttemptTarget;
        if (!MarkedVictims.Contains(target.PlayerId))
        {
            MarkedVictims.Add(target.PlayerId);
            SendRPC(RPC_type.AddMarkedVictim, target.PlayerId);
        }

        KillCount++;
        SendRPC(RPC_type.SetProgress);
        CheckAwaken();
    }

    // 会議終了 = 1ターン経過
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Awakened) return;

        TurnCount++;
        SendRPC(RPC_type.SetProgress);
        CheckAwaken();
    }

    private void CheckAwaken()
    {
        if (Awakened) return;
        if (TurnCount < AwakenTurns && KillCount < AwakenKills) return;

        Awakened = true;
        SendRPC(RPC_type.SetAwakened);

        Player.ResetKillCooldown();
        Player.SetKillCooldown();

        if (GiveAttribute && AttributeToGive != CustomRoles.NotAssigned)
        {
            PlayerState.GetByPlayerId(Player.PlayerId).SetSubRole(AttributeToGive);
        }

        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が覚醒した (turns={TurnCount}, kills={KillCount})", "VillainWolf");
    }

    // 覚醒前のみ、会議が始まったタイミングで「悪役の狼生存中」通知を全員に送る
    public override void OnStartMeeting()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Awakened) return;
        if (Player?.IsAlive() != true) return;

        Utils.SendMessage(GetString("VillainWolfAliveNotice"));
    }

    // 覚醒前に自分が殺した死体にマークを表示
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (MarkedVictims.Contains(seen.PlayerId))
            return Utils.ColorString(RoleInfo.RoleColor, "☾");

        return "";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        if (isForMeeting) return "";
        seen ??= seer;
        if (!Is(seer) || !Is(seen)) return "";

        return Awakened
            ? Utils.ColorString(RoleInfo.RoleColor, GetString("VillainWolfAwakenedText"))
            : Utils.ColorString(RoleInfo.RoleColor, $"{GetString("VillainWolfBeforeAwakenText")} ({TurnCount}/{AwakenTurns} , {KillCount}/{AwakenKills})");
    }
}
