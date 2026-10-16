using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;

namespace TownOfHost.Roles.Impostor;

/// <summary>
/// トラウマ(インポスター)
/// 最初はキルクールが少し短いインポスター。サーマウントがタスクをするたびに
/// キルクール増加・視界低下のデバフが付いていく(デクレッシェンドのように不利になっていく)。
/// ・サーマウントを自分でキル → デバフを全部乗り越える(キルクールは「乗り越えた後のキルクール」、視界は元通り)
/// ・サーマウントが吊られる   → キルクールだけ戻る(「吊り時の乗り越え後キルクール」)。視界はそのまま
/// ・サーマウントが他の人にキルされる → 乗り越えられず、試合終了までそのまま
/// ・会議のたびに、サーマウントが最後にいた場所が分かる
/// </summary>
public sealed class Trauma : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Trauma),
            player => new Trauma(player),
            CustomRoles.Trauma,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            87000,
            SetupOptionItem,
            "tra",
            "#8b4c6a",
            (3, 4),
            tab: TabGroup.Combinations,
            assignInfo: new RoleAssignInfo(CustomRoles.Trauma, CustomRoleTypes.Impostor)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = [CustomRoles.Trauma, CustomRoles.Surmount]
            },
            combination: CombinationRoles.TraumaandSurmount,
            isNewRole: true
        );

    public enum DebuffType { Vision, KillCooldown }

    static OptionItem OptionInitialKillCooldown;
    static OptionItem OptionKillCooldownRate;
    static OptionItem OptionVisionRate;
    internal static OptionItem OptionFirstDebuff;
    internal static OptionItem OptionSecondDebuff;
    static OptionItem OptionMaxKillCooldown;
    static OptionItem OptionMinVision;
    static OptionItem OptionOvercomeKillCooldown;
    static OptionItem OptionHangOvercomeKillCooldown;

    enum OptionName
    {
        TraumaInitialKillCooldown,
        TraumaKillCooldownRate,
        TraumaVisionRate,
        TraumaFirstDebuff,
        TraumaSecondDebuff,
        TraumaMaxKillCooldown,
        TraumaMinVision,
        TraumaOvercomeKillCooldown,
        TraumaHangOvercomeKillCooldown,
    }

    static readonly string[] DebuffNames = { "TraumaDebuffVision", "TraumaDebuffKillCooldown" };

    static void SetupOptionItem()
    {
        OptionInitialKillCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.TraumaInitialKillCooldown, new(10f, 180f, 0.5f), 27.5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionKillCooldownRate = FloatOptionItem.Create(RoleInfo, 11, OptionName.TraumaKillCooldownRate, new(0.02f, 2f, 0.02f), 0.4f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionVisionRate = FloatOptionItem.Create(RoleInfo, 12, OptionName.TraumaVisionRate, new(0.05f, 0.9f, 0.05f), 0.2f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionFirstDebuff = StringOptionItem.Create(RoleInfo, 13, OptionName.TraumaFirstDebuff, DebuffNames, 0, false);
        OptionSecondDebuff = StringOptionItem.Create(RoleInfo, 14, OptionName.TraumaSecondDebuff, DebuffNames, 1, false);
        OptionMaxKillCooldown = FloatOptionItem.Create(RoleInfo, 15, OptionName.TraumaMaxKillCooldown, new(10f, 180f, 0.5f), 60f, false).SetValueFormat(OptionFormat.Seconds);
        OptionMinVision = FloatOptionItem.Create(RoleInfo, 16, OptionName.TraumaMinVision, new(0f, 3f, 0.05f), 0.2f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionOvercomeKillCooldown = FloatOptionItem.Create(RoleInfo, 17, OptionName.TraumaOvercomeKillCooldown, new(10f, 180f, 0.5f), 25f, false).SetValueFormat(OptionFormat.Seconds);
        OptionHangOvercomeKillCooldown = FloatOptionItem.Create(RoleInfo, 18, OptionName.TraumaHangOvercomeKillCooldown, new(10f, 180f, 0.5f), 32.5f, false).SetValueFormat(OptionFormat.Seconds);
        OverrideTasksData.Create(RoleInfo, 20, CustomRoles.Surmount); // サーマウントのタスク数上書き
    }

    public Trauma(PlayerControl player) : base(RoleInfo, player)
    {
        baseKillCooldown = OptionInitialKillCooldown.GetFloat();
        killCooldownSteps = 0;
        visionSteps = 0;
        state = OvercomeState.None;
    }

    enum OvercomeState : byte { None, Overcome, HangOvercome, Locked }

    float baseKillCooldown;
    int killCooldownSteps;
    int visionSteps;
    OvercomeState state;
    /// <summary>会議で教える、サーマウントが最後にいた場所</summary>
    string surmountLastRoom = "";

    public bool CanBeLastImpostor { get; } = false;

    /// <summary>このタスクで与えるデバフの種類(前半は1番目、後半は2番目)</summary>
    public static DebuffType DebuffForTask(int completed, int all)
    {
        var first = (DebuffType)OptionFirstDebuff.GetValue();
        var second = (DebuffType)OptionSecondDebuff.GetValue();
        if (all <= 1) return first;
        return completed * 2 <= all ? first : second;
    }

    /// <summary>サーマウントがタスクをしてデバフを与えた(ホスト)</summary>
    public void ReceiveDebuff(DebuffType type)
    {
        if (!Player.IsAlive()) return;
        if (type == DebuffType.KillCooldown) killCooldownSteps++;
        else visionSteps++;
        Logger.Info($"トラウマにデバフ: {type} (キルク段階{killCooldownSteps} / 視界段階{visionSteps})", "Trauma");
        SyncAll();
    }

    /// <summary>サーマウントが死亡した(ホスト)</summary>
    public void OnSurmountDead(bool killedByMe, bool exiled)
    {
        if (state is OvercomeState.Overcome or OvercomeState.Locked) return;
        if (killedByMe)
        {
            // デバフを全部乗り越える
            state = OvercomeState.Overcome;
            baseKillCooldown = OptionOvercomeKillCooldown.GetFloat();
            killCooldownSteps = 0;
            visionSteps = 0;
            Utils.SendMessage(GetString("TraumaOvercomeMessage"), Player.PlayerId);
        }
        else if (exiled)
        {
            // キルクールだけ戻る
            state = OvercomeState.HangOvercome;
            baseKillCooldown = OptionHangOvercomeKillCooldown.GetFloat();
            killCooldownSteps = 0;
            Utils.SendMessage(GetString("TraumaHangOvercomeMessage"), Player.PlayerId);
        }
        else
        {
            // 他の人にキルされた → 乗り越えられないまま
            state = OvercomeState.Locked;
        }
        SyncAll();
    }

    float CurrentKillCooldown
    {
        get
        {
            var kc = baseKillCooldown;
            for (int i = 0; i < killCooldownSteps; i++) kc *= 1f + OptionKillCooldownRate.GetFloat();
            return Mathf.Min(kc, Mathf.Max(OptionMaxKillCooldown.GetFloat(), baseKillCooldown));
        }
    }

    float CurrentVision(float normal)
    {
        var v = normal;
        for (int i = 0; i < visionSteps; i++) v *= 1f - OptionVisionRate.GetFloat();
        return Mathf.Max(v, Mathf.Min(OptionMinVision.GetFloat(), normal));
    }

    public float CalculateKillCooldown() => CurrentKillCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        if (visionSteps <= 0) return;
        var normal = Main.DefaultImpostorVision;
        opt.SetFloat(FloatOptionNames.ImpostorLightMod, CurrentVision(normal));
    }

    void SyncAll()
    {
        SendRPC();
        if (!AmongUsClient.Instance.AmHost) return;
        Player.ResetKillCooldown();
        Player.MarkDirtySettings();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    // ===== 会議: サーマウントが最後にいた場所を教える =====
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        surmountLastRoom = "";
        var s = Surmount.Instance;
        if (s?.Player == null || !s.Player.IsAlive()) return;
        var room = s.Player.GetPlainShipRoom();
        surmountLastRoom = room != null ? GetString($"{room.RoomId}") : GetString("TraumaRoomUnknown");
    }

    public override void OnStartMeeting()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var s = Surmount.Instance;
        string text;
        if (s?.Player == null) return;
        if (!s.Player.IsAlive()) text = GetString("TraumaSurmountDead");
        else text = string.Format(GetString("TraumaSurmountLastRoom"), surmountLastRoom);
        var id = Player.PlayerId;
        _ = new LateTask(() => Utils.SendMessage(text, id, Utils.ColorString(RoleInfo.RoleColor, GetString("Trauma"))), 4f, "Trauma.MeetingInfo", true);
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        var text = state switch
        {
            OvercomeState.Overcome => "★",
            OvercomeState.Locked => "×",
            _ => $"({CurrentKillCooldown:0.#}s)",
        };
        return Utils.ColorString(RoleInfo.RoleColor, text);
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(baseKillCooldown);
        sender.Writer.Write(killCooldownSteps);
        sender.Writer.Write(visionSteps);
        sender.Writer.Write((byte)state);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        baseKillCooldown = reader.ReadSingle();
        killCooldownSteps = reader.ReadInt32();
        visionSteps = reader.ReadInt32();
        state = (OvercomeState)reader.ReadByte();
    }
}
