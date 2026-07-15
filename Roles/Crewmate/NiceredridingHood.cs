<<<<<<< HEAD
/*using AmongUs.GameOptions;
=======
/*
using AmongUs.GameOptions;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
using Hazel;
using TownOfHost.Roles.Core;
using System.Collections.Generic;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Crewmate;

<<<<<<< HEAD
/// <summary>
/// ナイス赤ずきん (SNR移植)
/// 自分を殺した相手が追放（またはゲーム中に死亡）すると復活する。
/// </summary>
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
public sealed class NiceredridingHood : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(NiceredridingHood),
            player => new NiceredridingHood(player),
            CustomRoles.NiceRedRidingHood,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
<<<<<<< HEAD
            37200,
            SetupOptionItem,
            "nrrh",
            "#fa8072",   // サーモン色
=======
            362000,
            SetupOptionItem,
            "nrrh",
            "#fa8072",
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            (5, 3),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.SuperNewRoles
        );

    public NiceredridingHood(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.True)
    {
        remainingReviveCount = OptionReviveCount.GetInt();
        isKillerDeathRevive = OptionIsKillerDeathRevive.GetBool();
        killerPlayerId = byte.MaxValue;
        isRevivable = false;
        killerExiledThisMeeting = false;
    }

    static OptionItem OptionReviveCount;
    static OptionItem OptionIsKillerDeathRevive;

    enum OptionName
    {
        NiceRedRidingHoodReviveCount,
        NiceRedRidingHoodIsKillerDeathRevive,
    }

    int remainingReviveCount;
    bool isKillerDeathRevive;
    byte killerPlayerId;
    bool isRevivable;
    bool killerExiledThisMeeting;

    static void SetupOptionItem()
    {
        OptionReviveCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.NiceRedRidingHoodReviveCount,
            new(1, 15, 1), 1, false).SetValueFormat(OptionFormat.Times);
        OptionIsKillerDeathRevive = BooleanOptionItem.Create(RoleInfo, 11,
            OptionName.NiceRedRidingHoodIsKillerDeathRevive, true, false);
        OverrideTasksData.Create(RoleInfo, 20);
    }

<<<<<<< HEAD
    // ─── 自分がキルされた瞬間にキラーを記録 ──────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (remainingReviveCount <= 0) return true;

        (var killer, _) = info.AttemptTuple;
<<<<<<< HEAD
        // 自殺・ゲームシステムによる死亡は除外
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        if (killer == null || killer.PlayerId == Player.PlayerId) return true;

        killerPlayerId = killer.PlayerId;
        isRevivable = true;

        Logger.Info(
            $"[NiceRedRidingHood] {Player.Data.GetLogPlayerName()} が " +
            $"{killer.Data.GetLogPlayerName()} に殺された (復活待機)",
            "NiceRedRidingHood");
        SendRpc();

<<<<<<< HEAD
        return true; // キルを通す
    }

    // ─── 追放者がキラーかどうかを記録 ────────────────────
    //   VotingResults は追放確定タイミングで呼ばれる
=======
        return true;
    }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie,
        Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (!isRevivable || killerPlayerId == byte.MaxValue) return false;

        if (Exiled != null && Exiled.PlayerId == killerPlayerId)
        {
            killerExiledThisMeeting = true;
            Logger.Info("[NiceRedRidingHood] キラー追放確認 → 復活フラグON", "NiceRedRidingHood");
        }

        return false;
    }

<<<<<<< HEAD
    // ─── 会議終了後に復活判定 ─────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!isRevivable || killerPlayerId == byte.MaxValue) return;

        bool shouldRevive = killerExiledThisMeeting;
        killerExiledThisMeeting = false;

        if (!shouldRevive && isKillerDeathRevive)
        {
            var killer = GetPlayerById(killerPlayerId);
<<<<<<< HEAD
            // ゲーム中死亡（サボ・キルなど）も含めてキラーが死んでいれば復活
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            if (killer != null && !killer.IsAlive())
            {
                shouldRevive = true;
                Logger.Info("[NiceRedRidingHood] キラー死亡確認 → 復活フラグON", "NiceRedRidingHood");
            }
        }

        if (shouldRevive) DoRevive();
    }

<<<<<<< HEAD
    // ─── 復活処理 ─────────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    private void DoRevive()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!isRevivable || remainingReviveCount <= 0) return;

        remainingReviveCount--;
        isRevivable = false;
        killerPlayerId = byte.MaxValue;
        SendRpc();

<<<<<<< HEAD
        // LateTask でゲーム状態が落ち着いてから復活する
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        _ = new LateTask(() =>
        {
            if (Player == null) return;

<<<<<<< HEAD
            // ★ TOH-P の PlayerState をリセット
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            var state = PlayerState.GetByPlayerId(Player.PlayerId);
            if (state != null)
            {
                state.IsDead = false;
<<<<<<< HEAD
                state.DeathReason = CustomDeathReason.etc;   // リセット
            }

            // ★ Among Us の本体 Revive を呼ぶ
=======
                state.DeathReason = CustomDeathReason.etc;
            }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            Player.Revive();
            Player.Data.IsDead = false;

            UtilsGameLog.AddGameLog("NiceRedRidingHood",
                $"{UtilsName.GetPlayerColor(Player)} が復活した (残り{remainingReviveCount}回)");
            UtilsNotifyRoles.NotifyRoles();

<<<<<<< HEAD
            // 本人に通知
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            Utils.SendMessage(GetString("NiceRedRidingHoodRevived"), Player.PlayerId);

            Logger.Info(
                $"[NiceRedRidingHood] 復活完了 残り{remainingReviveCount}回",
                "NiceRedRidingHood");

        }, Main.LagTime + 0.2f, "NiceRedRidingHood.Revive", true);
    }

<<<<<<< HEAD
    // ─── 表示 ─────────────────────────────────────────────
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (remainingReviveCount <= 0) return "";
        // 復活待機中は赤、通常はロールカラー
=======
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (remainingReviveCount <= 0) return "";
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        string color = isRevivable ? "#ff4444" : RoleInfo.RoleColorCode;
        return $"<color={color}>({remainingReviveCount})</color>";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId) return "";

<<<<<<< HEAD
        // 死亡中かつ復活待機中：キラー情報を表示
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        if (!Player.IsAlive() && isRevivable && killerPlayerId != byte.MaxValue)
        {
            var killer = GetPlayerById(killerPlayerId);
            string killerName = killer?.Data?.PlayerName ?? "???";
            string size = isForHud ? "" : "<size=60%>";
            string cond = isKillerDeathRevive ? "死亡" : "追放";
            return $"{size}<color={RoleInfo.RoleColorCode}>{killerName} の{cond}で復活！</color>";
        }

<<<<<<< HEAD
        // 生存中：説明
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        if (Player.IsAlive() && !isForMeeting)
        {
            string size = isForHud ? "" : "<size=60%>";
            return $"{size}<color={RoleInfo.RoleColorCode}>" +
                   $"自分を殺した相手が" +
                   $"{(isKillerDeathRevive ? "死亡" : "追放")}されると復活</color>";
        }

        return "";
    }

<<<<<<< HEAD
    // ─── RPC ─────────────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(remainingReviveCount);
        sender.Writer.Write(isRevivable);
        sender.Writer.Write(killerPlayerId);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        remainingReviveCount = reader.ReadInt32();
        isRevivable = reader.ReadBoolean();
        killerPlayerId = reader.ReadByte();
    }
<<<<<<< HEAD
}*/
=======
}
*/
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
