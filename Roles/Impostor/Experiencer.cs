using System.Collections.Generic;
using System.Drawing;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using MS.Internal.Xml.XPath;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Experiencer : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Experiencer),
            player => new Experiencer(player),
            CustomRoles.Experiencer,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            95600,
            SetupOptionItem,
            "ex",
            OptionSort: (3, 12),
            from: From.TownOfHost_hamo
        );
    public Experiencer(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillPoint = OptionKillPoint.GetInt();
        TaskPoint = OptionTaskPoint.GetInt();
        DelayTime = OptDelayTime.GetFloat();
        CantReportPlayers.Clear();
    }
    public override void Add()
    {
        KillCool = KillCoolDown.GetFloat();

        Point = 0;
        Delay = false;
        NoDeadbody = false;
        NoReport = false;
        EGuesser = false;
        Magic = false;
        Tansyuku = false;
    }
    static HashSet<byte> CantReportPlayers = new();

    #region オプション
    enum OptionName
    {
        ExperiencerKillPoint,
        ExperiencerTaskPoint,
        ExperiencerTansyukuPoint,
        ExperiencerGuesserPoint,
        ExperiencerMagicPoint,
        ExperiencerDelayPoint,
        ExperiencerNoreportPoint,
        ExperiencerNoDeadBodyPoint,
        VampireKillDelay,
        BountyGuesserSuccessKillCooldown,
    }
    static OptionItem KillCoolDown;
    static OptionItem OptionKillPoint;
    static OptionItem OptionTaskPoint;
    static OptionItem Tansuku;
    static OptionItem Guesser;
    static OptionItem Magich;
    static OptionItem DelayPoint;
    static OptionItem NoreportKill;
    static OptionItem ExileKill;
    static OptionItem OptDelayTime;
    static OptionItem GensyoKillcool;
    static int KillPoint;
    static int TaskPoint;
    static float DelayTime;
    int Point;
    bool Delay;
    bool NoReport;
    bool NoDeadbody;
    bool EGuesser;
    bool Magic;
    bool Tansyuku;
    float KillCool;
    private static void SetupOptionItem()
    {
        KillCoolDown = FloatOptionItem.Create(RoleInfo, 3, GeneralOption.KillCooldown, new(0, 180, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);

        OptionKillPoint = IntegerOptionItem.Create(RoleInfo, 4, OptionName.ExperiencerKillPoint, new(0, 200, 1), 10, false);
        OptionTaskPoint = IntegerOptionItem.Create(RoleInfo, 5, OptionName.ExperiencerTaskPoint, new(0, 200, 1), 5, false);

        Tansuku = IntegerOptionItem.Create(RoleInfo, 6, OptionName.ExperiencerTansyukuPoint, new(0, 200, 1), 25, false)
            .SetZeroNotation(OptionZeroNotation.Off);
        GensyoKillcool = FloatOptionItem.Create(RoleInfo, 13, OptionName.BountyGuesserSuccessKillCooldown, new(0f, 180f, 0.5f), 30f, false, Tansuku)
            .SetValueFormat(OptionFormat.Seconds)
            .SetEnabled(() => Tansuku.GetInt() > 0);

        Guesser = IntegerOptionItem.Create(RoleInfo, 7, OptionName.ExperiencerGuesserPoint, new(0, 200, 1), 40, false)
            .SetZeroNotation(OptionZeroNotation.Off);
        Magich = IntegerOptionItem.Create(RoleInfo, 8, OptionName.ExperiencerMagicPoint, new(0, 200, 1), 10, false)
            .SetZeroNotation(OptionZeroNotation.Off);

        DelayPoint = IntegerOptionItem.Create(RoleInfo, 9, OptionName.ExperiencerDelayPoint, new(0, 200, 1), 40, false)
            .SetZeroNotation(OptionZeroNotation.Off);
        OptDelayTime = FloatOptionItem.Create(RoleInfo, 10, OptionName.VampireKillDelay, new(0f, 180f, 0.5f), 10f, false, DelayPoint)
            .SetValueFormat(OptionFormat.Seconds)
            .SetEnabled(() => DelayPoint.GetInt() > 0);

        NoreportKill = IntegerOptionItem.Create(RoleInfo, 11, OptionName.ExperiencerNoreportPoint, new(0, 200, 1), 55, false)
            .SetZeroNotation(OptionZeroNotation.Off);
        ExileKill = IntegerOptionItem.Create(RoleInfo, 12, OptionName.ExperiencerNoDeadBodyPoint, new(0, 200, 1), 70, false)
            .SetZeroNotation(OptionZeroNotation.Off);
    }
    public float CalculateKillCooldown() => KillCoolDown.GetFloat();
    #endregion
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        if (!Is(killer)) return;

        if (NoDeadbody && info.GuardPower < 1)
        {
            info.DoKill = false;
            target.RpcExileV3();
            Player.SetKillCooldown();

            Point = Point + KillPoint;
            RPC.PlaySoundRPC(killer.PlayerId, Sounds.TaskComplete);
            if (NoReport)
            {
                CantReportPlayers.Add(target.PlayerId);
            }
            SendRPC();
        }
    }
    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        if (!Is(killer)) return;

        Point = Point + KillPoint;
        RPC.PlaySoundRPC(killer.PlayerId, Sounds.TaskComplete);
        if (NoReport)
        {
            CantReportPlayers.Add(target.PlayerId);
        }
        SendRPC();
    }
    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportreson reportreson)
    {
        if (CantReportPlayers.Contains(target.PlayerId))
        {
            reportreson = DontReportreson.Impostor;
            return true;
        }
        return false;
    }

    public static void Tryentrant(int id, PlayerControl player)
    {
        if (player.GetRoleClass() is not Experiencer experiencer) return;
        if (id > 6 || id < 1)
        {
            Utils.SendMessage(GetString("ExperiencerInvalidEnchant"), player.PlayerId);
            return;
        }
        if (!GameStates.IsMeeting)
        {
            Utils.SendMessage(GetString("ExperiencerMeetingOnly"), player.PlayerId);
            return;
        }

        switch (id)
        {
            case 1:
                if (Tansuku.GetInt() == 0)
                {
                    Utils.SendMessage(GetString("ExperiencerOff"), player.PlayerId);
                    break;
                }
                if (experiencer.Tansyuku)
                {
                    Utils.SendMessage(GetString("ExperiencerAlreadyHave"), player.PlayerId);
                    break;
                }
                if (experiencer.Point < Tansuku.GetInt())
                {
                    Utils.SendMessage(string.Format(GetString("ExperiencerNotEnoughPoints"), Tansuku.GetInt(), experiencer.Point), player.PlayerId);
                    break;
                }
                experiencer.Tansyuku = true;
                experiencer.Point = experiencer.Point - Tansuku.GetInt();
                experiencer.KillCool = experiencer.KillCool - GensyoKillcool.GetFloat();
                Utils.SendMessage(string.Format(GetString("ExperiencerEnchantSuccess"), id, Tansuku.GetInt(), experiencer.Point), player.PlayerId);
                RPC.PlaySoundRPC(player.PlayerId, Sounds.TaskComplete);
                Main.AllPlayerKillCooldown[player.PlayerId] = experiencer.KillCool;
                player.SyncSettings();
                player.RpcProtectedMurderPlayer();
                break;
            case 2:
                if (Guesser.GetInt() == 0)
                {
                    Utils.SendMessage(GetString("ExperiencerOff"), player.PlayerId);
                    break;
                }
                if (experiencer.EGuesser)
                {
                    Utils.SendMessage(GetString("ExperiencerAlreadyHave"), player.PlayerId);
                    break;
                }
                if (experiencer.Point < Guesser.GetInt())
                {
                    Utils.SendMessage(string.Format(GetString("ExperiencerNotEnoughPoints"), Guesser.GetInt(), experiencer.Point), player.PlayerId);
                    break;
                }
                experiencer.EGuesser = true;
                experiencer.Point = experiencer.Point - Guesser.GetInt();
                player.RpcSetCustomRole(CustomRoles.Guesser);
                Utils.SendMessage(string.Format(GetString("ExperiencerEnchantSuccess"), id, Guesser.GetInt(), experiencer.Point), player.PlayerId);
                RPC.PlaySoundRPC(player.PlayerId, Sounds.TaskComplete);
                player.RpcProtectedMurderPlayer();
                break;
            case 3:
                if (Magich.GetInt() == 0)
                {
                    Utils.SendMessage(GetString("ExperiencerOff"), player.PlayerId);
                    break;
                }
                if (experiencer.Magic)
                {
                    Utils.SendMessage(GetString("ExperiencerAlreadyHave"), player.PlayerId);
                    break;
                }
                if (experiencer.Point < Magich.GetInt())
                {
                    Utils.SendMessage(string.Format(GetString("ExperiencerNotEnoughPoints"), Magich.GetInt(), experiencer.Point), player.PlayerId);
                    break;
                }
                experiencer.Magic = true;
                experiencer.Point = experiencer.Point - Magich.GetInt();
                player.RpcSetCustomRole(CustomRoles.MagicHand);
                Utils.SendMessage(string.Format(GetString("ExperiencerEnchantSuccess"), id, Magich.GetInt(), experiencer.Point), player.PlayerId);
                RPC.PlaySoundRPC(player.PlayerId, Sounds.TaskComplete);
                player.RpcProtectedMurderPlayer();
                break;
            case 4:
                if (DelayPoint.GetInt() == 0)
                {
                    Utils.SendMessage(GetString("ExperiencerOff"), player.PlayerId);
                    break;
                }
                if (experiencer.Delay)
                {
                    Utils.SendMessage(GetString("ExperiencerAlreadyHave"), player.PlayerId);
                    break;
                }
                if (experiencer.Point < DelayPoint.GetInt())
                {
                    Utils.SendMessage(string.Format(GetString("ExperiencerNotEnoughPoints"), DelayPoint.GetInt(), experiencer.Point), player.PlayerId);
                    break;
                }
                experiencer.Delay = true;
                experiencer.Point = experiencer.Point - DelayPoint.GetInt();
                Utils.SendMessage(string.Format(GetString("ExperiencerEnchantSuccess"), id, DelayPoint.GetInt(), experiencer.Point), player.PlayerId);
                RPC.PlaySoundRPC(player.PlayerId, Sounds.TaskComplete);
                player.RpcProtectedMurderPlayer();
                break;
            case 5:
                if (NoreportKill.GetInt() == 0)
                {
                    Utils.SendMessage(GetString("ExperiencerOff"), player.PlayerId);
                    break;
                }
                if (experiencer.NoReport)
                {
                    Utils.SendMessage(GetString("ExperiencerAlreadyHave"), player.PlayerId);
                    break;
                }
                if (experiencer.Point < NoreportKill.GetInt())
                {
                    Utils.SendMessage(string.Format(GetString("ExperiencerNotEnoughPoints"), NoreportKill.GetInt(), experiencer.Point), player.PlayerId);
                    break;
                }
                experiencer.NoReport = true;
                experiencer.Point = experiencer.Point - NoreportKill.GetInt();
                Utils.SendMessage(string.Format(GetString("ExperiencerEnchantSuccess"), id, NoreportKill.GetInt(), experiencer.Point), player.PlayerId);
                RPC.PlaySoundRPC(player.PlayerId, Sounds.TaskComplete);
                player.RpcProtectedMurderPlayer();
                break;
            case 6:
                if (ExileKill.GetInt() == 0)
                {
                    Utils.SendMessage(GetString("ExperiencerOff"), player.PlayerId);
                    break;
                }
                if (experiencer.NoDeadbody)
                {
                    Utils.SendMessage(GetString("ExperiencerAlreadyHave"), player.PlayerId);
                    break;
                }
                if (experiencer.Point < ExileKill.GetInt())
                {
                    Utils.SendMessage(string.Format(GetString("ExperiencerNotEnoughPoints"), ExileKill.GetInt(), experiencer.Point), player.PlayerId);
                    break;
                }
                experiencer.NoDeadbody = true;
                experiencer.Point = experiencer.Point - ExileKill.GetInt();
                Utils.SendMessage(string.Format(GetString("ExperiencerEnchantSuccess"), id, ExileKill.GetInt(), experiencer.Point), player.PlayerId);
                RPC.PlaySoundRPC(player.PlayerId, Sounds.TaskComplete);
                player.RpcProtectedMurderPlayer();
                break;
        }
        experiencer.SendRPC();
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(Point);
        sender.Writer.Write(Tansyuku);
        sender.Writer.Write(EGuesser);
        sender.Writer.Write(Magic);
        sender.Writer.Write(Delay);
        sender.Writer.Write(NoReport);
        sender.Writer.Write(NoDeadbody);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        Point = reader.ReadInt32();
        Tansyuku = reader.ReadBoolean();
        EGuesser = reader.ReadBoolean();
        Magic = reader.ReadBoolean();
        Delay = reader.ReadBoolean();
        NoReport = reader.ReadBoolean();
        NoDeadbody = reader.ReadBoolean();
    }
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        return $"<#ff1919>({Point})</color>";
    }
}