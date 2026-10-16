using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

/// <summary>
/// ヴィラン(インポスター) ※第三陣営の「ヴィラン」が既にあるため、内部名は ArchVillain
/// ・インポスター陣営(相方が分かる人)からは「ヴィラン」として見える
/// ・誤爆・道連れ・後追い以外では死なない(キル・追放を無効化)。仲間のインポスターが全滅すると後追いで死ぬ
/// ・キル/追放を無効化した次のターンはキルできない
/// ・設定で、キルを防いだ時に相手へ自分の役職を伝える
/// ・配役時にインポスターがヴィランだけなら、通常のインポスターになる
/// </summary>
public sealed class ArchVillain : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(ArchVillain),
            player => new ArchVillain(player),
            CustomRoles.ArchVillain,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            92500,
            SetupOptionItem,
            "avi",
            "#FF0000",
            isNewRole: true
        );

    static OptionItem OptionKillCooldown;
    static OptionItem OptionRevealToMadmate;
    static OptionItem OptionRevealOnGuard;
    enum OptionName { ArchVillainRevealToMadmate, ArchVillainRevealOnGuard }

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0.5f, 180f, 0.5f), 29.5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionRevealToMadmate = BooleanOptionItem.Create(RoleInfo, 11, OptionName.ArchVillainRevealToMadmate, true, false);
        OptionRevealOnGuard = BooleanOptionItem.Create(RoleInfo, 12, OptionName.ArchVillainRevealOnGuard, true, false);
    }

    public ArchVillain(PlayerControl player) : base(RoleInfo, player)
    {
        cantKillDay = -1;
    }

    public bool CanBeLastImpostor { get; } = false;
    /// <summary>この日(UtilsGameLog.day)はキルできない</summary>
    int cantKillDay;
    /// <summary>このターン中に死亡を無効化した → 次のターンはキル不可</summary>
    bool nullifiedThisTurn;

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();
    public bool CanUseKillButton() => Player.IsAlive() && cantKillDay != UtilsGameLog.day;

    static bool IsAllowedDeath(CustomDeathReason r) => r is CustomDeathReason.Misfire or CustomDeathReason.Revenge or CustomDeathReason.FollowingSuicide;

    public override void StartGameTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // インポスターが自分だけなら、通常のインポスターになる
        var others = PlayerCatch.AllPlayerControls.Any(p => p.PlayerId != Player.PlayerId && p.GetCustomRole().IsImpostor());
        if (!others)
        {
            Logger.Info("ヴィラン以外のインポスターがいないため、インポスターに置き換え", "ArchVillain");
            Player.RpcSetCustomRole(CustomRoles.Impostor);
        }
    }

    /// <summary>キルを無効化(誤爆・道連れ・後追い以外)</summary>
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (IsAllowedDeath(info.DeathReason) || info.IsSuicide) return true;
        var killer = info.AttemptKiller;
        nullifiedThisTurn = true;
        killer?.SetKillCooldown();
        if (killer != null && OptionRevealOnGuard.GetBool())
            Utils.SendMessage(string.Format(GetString("ArchVillainRevealed"), Player.Data?.PlayerName), killer.PlayerId, Utils.ColorString(RoleInfo.RoleColor, GetString("ArchVillain")));
        return false;
    }

    /// <summary>追放を無効化</summary>
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (Exiled == null || Exiled.PlayerId != Player.PlayerId) return false;
        Exiled = null;
        IsTie = false;
        nullifiedThisTurn = true;
        return true;
    }

    public override void AfterMeetingTasks()
    {
        // 無効化した次のターンはキルできない
        // (この処理は日付が進む前に呼ばれるので、次の日 = day + 1。初手会議の直後だけは日付が進まない)
        var dayAdvances = !Options.firstturnmeeting || !MeetingStates.First;
        if (nullifiedThisTurn) cantKillDay = UtilsGameLog.day + (dayAdvances ? 1 : 0);
        nullifiedThisTurn = false;
        SendRPC();
    }

    /// <summary>毎フレーム(ホスト): 仲間のインポスターが全滅したら後追い</summary>
    public void CheckFollow()
    {
        if (!Player.IsAlive()) return;
        if (PlayerCatch.AllAlivePlayerControls.Any(p => p.PlayerId != Player.PlayerId && p.GetCustomRole().IsImpostor())) return;
        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.FollowingSuicide;
        Player.RpcMurderPlayerV2(Player);
    }

    /// <summary>インポスター陣営(とマッドメイト、設定次第)にはヴィランだと分かる</summary>
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        if (seer == null) return;
        var r = seer.GetCustomRole();
        if (r.IsImpostor() && r.GetRoleInfo()?.IsCantSeeTeammates != true && !seer.Is(CustomRoles.OneWolf)) enabled = true;
        else if (r.IsMadmate() && OptionRevealToMadmate.GetBool()) enabled = true;
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => cantKillDay == UtilsGameLog.day ? Utils.ColorString(ModColors.NeutralGray, "×") : "";

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(cantKillDay);
    }
    public override void ReceiveRPC(MessageReader reader) => cantKillDay = reader.ReadInt32();
}
