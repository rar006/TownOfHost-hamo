using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;

namespace TownOfHost.Roles.Neutral;

public sealed class RaccoonParent : RoleBase, IKiller, IRoomTasker, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(RaccoonParent),
            player => new RaccoonParent(player),
            CustomRoles.RaccoonParent,
            // 子作りはファントム（透明）ボタンで行う。
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            77030,
            SetupOptionItem,
            "racp",
            "#a0785a",
            OptionSort: (7, 2),
            from: From.TownOfHost_hamo
        );

    public RaccoonParent(PlayerControl player) : base(RoleInfo, player)
    {
        GrabbedTargetId = byte.MaxValue;
        InvestigatedTargetId = byte.MaxValue;
        CanMakeChild = true;
        PendingChildTargetId = byte.MaxValue;
    }

    static OptionItem OptionKillCooldownAfterEnd;
    static OptionItem OptionKillOnMeetingIfGrabbing;
    static OptionItem OptionMakeChildCooldown;

    enum OptionName
    {
        RaccoonParentKillOnMeetingIfGrabbing,
        RaccoonParentKillCooldownAfterEnd,
        RaccoonParentMakeChildCooldown,
    }

    static void SetupOptionItem()
    {
        OptionKillOnMeetingIfGrabbing = BooleanOptionItem.Create(RoleInfo, 10, OptionName.RaccoonParentKillOnMeetingIfGrabbing, false, false);
        OptionKillCooldownAfterEnd = FloatOptionItem.Create(RoleInfo, 11, OptionName.RaccoonParentKillCooldownAfterEnd, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMakeChildCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.RaccoonParentMakeChildCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public byte GrabbedTargetId;
    byte InvestigatedTargetId;
    bool investigateReady;
    bool CanMakeChild;
    byte PendingChildTargetId;
    int grabFollowState;
    float meetingStartTimer = -1f;

    public static float GetKillCooldown() => OptionKillCooldownAfterEnd.GetFloat();

    // SK完了後はファントム能力を使わないため、親本人をインポスター基盤で維持する。
    public override RoleTypes? AfterMeetingRole => CanMakeChild ? RoleTypes.Phantom : RoleTypes.Impostor;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.KillCooldown = GetKillCooldown();
        AURoleOptions.PhantomCooldown = CanMakeChild ? OptionMakeChildCooldown.GetFloat() : 200f;
    }

    public bool CanUseSabotageButton() => false;

    /// <summary>
    /// キルボタン: まだ何も掴んでいなければ「掴む」、既に掴んでいれば「キルする」。
    /// </summary>
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.AttemptKiller == info.AttemptTarget) return;

        // 親を掴もうとしたアライグマは自殺する（子と同じ相互の掴み防止）。
        if (info.AttemptTarget.Is(CustomRoles.RaccoonParent))
        {
            info.DoKill = false;
            if (AmongUsClient.Instance.AmHost)
                CustomRoleManager.OnCheckMurder(Player, Player, Player, Player, true, false, 1, CustomDeathReason.Suicide);
            return;
        }

        if (GrabbedTargetId == byte.MaxValue)
        {
            // まだ誰も掴んでいない: キルせず掴む
            info.DoKill = false;
            Grab(info.AttemptTarget);
        }
        else if (GrabbedTargetId == info.AttemptTarget.PlayerId)
        {
            // 通常キルの死体は必ず通報可能にする。
            if (ReportDeadBodyPatch.IgnoreBodyids != null)
                ReportDeadBodyPatch.IgnoreBodyids[info.AttemptTarget.PlayerId] = false;
            // 既に掴んでいる相手をもう一度キルボタン: キルする
            info.DoKill = true;
            ReleaseGrab();
        }
        else
        {
            // 掴んでいない別の相手を狙った場合は何もしない
            info.DoKill = false;
        }
    }

    void Grab(PlayerControl target)
    {
        GrabbedTargetId = target.PlayerId;
        InvestigatedTargetId = byte.MaxValue;
        investigateReady = false;
        grabFollowState = 0;
        // ペンギンと同様に、掴まれた側は移動・移動床を使えない。
        target.GetPlayerState().CanMove = false;
        target.GetPlayerState().CanUseMovingPlatform = false;
        MyState.CanUseMovingPlatform = false;
        target.MarkDirtySettings();
        ((IRoomTasker)this).AddRoomTaker(Player.PlayerId);
        SendRpc();
        Player.RpcResetAbilityCooldown(Sync: true);
    }

    void ReleaseGrab(bool keepInvestigation = false)
    {
        var target = GrabbedTargetId.GetPlayerControl();
        if (target != null)
        {
            target.GetPlayerState().CanMove = true;
            target.GetPlayerState().CanUseMovingPlatform = true;
            target.MarkDirtySettings();
        }
        MyState.CanUseMovingPlatform = true;
        grabFollowState = 0;
        GrabbedTargetId = byte.MaxValue;
        if (!keepInvestigation)
        {
            InvestigatedTargetId = byte.MaxValue;
            investigateReady = false;
        }
        SendRpc();
    }

    // ===== 修正 =====
    // 到達済み(investigateReady=true)の場合は部屋タスクの再割り当てを止める。
    // 以前は到達するたびに次のランダムな部屋がまた割り当てられ続けていたが、
    // 1回到達したらそれ以上は部屋を巡回させない。
    public bool IsAssignRoomTask() => GrabbedTargetId != byte.MaxValue && !investigateReady;

    public void ChangeRoom(PlainShipRoom TaskPSR)
    {
    }

    public void OnComplete(int completeroom)
    {
        // 指定された部屋に到着した時点で役職を調査し、対象はすぐに自動解放する。
        if (GrabbedTargetId == byte.MaxValue) return;
        InvestigatedTargetId = GrabbedTargetId;
        investigateReady = true;
        ReleaseGrab(keepInvestigation: true);
    }

    public override void OnStartMeeting()
    {
        if (GrabbedTargetId != byte.MaxValue && OptionKillOnMeetingIfGrabbing.GetBool())
        {
            var target = GrabbedTargetId.GetPlayerControl();
            if (target != null && target.IsAlive() && AmongUsClient.Instance.AmHost)
            {
                CustomRoleManager.OnCheckMurder(Player, target, target, target, true, false, 1, CustomDeathReason.Kill);
            }
        }
        // ===== 修正 =====
        // 会議に入っても、既に役職を調べ終えている(investigateReady=true)場合は
        // GrabbedTargetIdをリセットせず、会議中も相手の役職を確認できるようにする。
        // まだ掴んでいる途中(未到達)の場合は、従来通り会議入りでリセットする。
        if (!investigateReady)
        {
            ReleaseGrab();
        }
        else
        {
            // 到達済みの場合、会議の最初だけ自分に見えるようにするための
            // タイマーを開始する。
            meetingStartTimer = 0f;
        }
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (meetingStartTimer >= 0f)
            meetingStartTimer += UnityEngine.Time.fixedDeltaTime;

        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask || GrabbedTargetId == byte.MaxValue) return;

        var target = GrabbedTargetId.GetPlayerControl();
        if (!Player.IsAlive() || target == null || !target.IsAlive())
        {
            ReleaseGrab();
            return;
        }

        // ペンギンの拉致と同じ間隔・ホスト補正で同期し、全視点で同じ位置に表示する。
        if (!target.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
        {
            const int syncInterval = 3;
            grabFollowState++;
            if (grabFollowState % syncInterval == 0)
            {
                var position = Player.transform.position;
                // 掴み側（ホスト）でも即座に見えるよう、ローカル位置を先に同期する。
                // そのうえでRPCを送るため、他視点の位置同期も維持できる。
                target.NetTransform.SnapTo(position, (ushort)(target.NetTransform.lastSequenceId + 1));
                target.RpcSnapToForced(position, SendOption.None);
            }
        }
    }

    public override void OnDestroy()
    {
        ReleaseGrab();
    }

    /// <summary>
    /// ファントム（透明）ボタンで、ジャッカルのサイドキックと同じ保護・同期手順を
    /// 通して照準中のクルーメイトをアライグマの子へ変更する。
    /// </summary>
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || !CanMakeChild) return;

        var target = Player.GetKillTarget(true);
        // ジャッカルのSKと同じく、基本クルーメイトに限定せずクルー陣営の役職全体を対象にする。
        if (target == null || !target.IsAlive() || !target.GetCustomRole().IsCrewmate())
        {
            ResetCooldown = false;
            return;
        }
        if (Walkure.TryRejectRoleChange(Player, target, Walkure.RoleChangeSource.Jackal)) return;

        // ジャッカルのSKと同じく、役職変更時の保護演出・クライアント同期を先に行う。
        CanMakeChild = false;
        // SKは会議終了時まで対象を変更しないため、対象側に見えるガード演出も送らない。
        UtilsGameLog.AddGameLog("RaccoonParent", string.Format(GetString("log.RaccoonParentMakeChild"), UtilsName.GetPlayerColor(target, true)));
        // SK直後は対象の役職・能力・表示を一切変えない。
        // 次の会議終了時にAfterMeetingTasksで初めてアライグマの子へ切り替える。
        PendingChildTargetId = target.PlayerId;

        // 子作りが完了した親は、以後のキル操作をファントムではなくインポスター基盤で行う。
        // すべての導入者視点へ即時に反映する。
        foreach (var client in AmongUsClient.Instance.allClients)
            Player.RpcSetRoleDesync(RoleTypes.Impostor, client.Id, SendOption.None);

        if (!Utils.RoleSendList.Contains(target.PlayerId)) Utils.RoleSendList.Add(target.PlayerId);
        UtilsOption.MarkEveryoneDirtySettings();
        SendRpc();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || PendingChildTargetId == byte.MaxValue) return;

        var target = PendingChildTargetId.GetPlayerControl();
        PendingChildTargetId = byte.MaxValue;
        if (target == null || !target.IsAlive())
        {
            SendRpc();
            return;
        }

        // 会議終了後に初めて子へ変更し、その時点で本人も子であることを自覚する。
        target.RpcSetCustomRole(CustomRoles.RaccoonChild, log: null);
        if (target.GetRoleClass() is RaccoonChild child)
            child.AwakenFromSidekick();

        if (!Utils.RoleSendList.Contains(target.PlayerId)) Utils.RoleSendList.Add(target.PlayerId);
        UtilsOption.MarkEveryoneDirtySettings();
        SendRpc();
    }

    public override bool CanUseAbilityButton() => CanMakeChild;
    bool IUsePhantomButton.IsPhantomRole => CanMakeChild;
    bool IUsePhantomButton.IsresetAfterKill => false;

    public override string GetAbilityButtonText() => GetString("RaccoonParentMakeChild");
    public override bool OverrideAbilityButton(out string text)
    {
        // MOD導入者のローカルHUDでは、SK専用のアライグマ画像を使う。
        text = "Raccoon_MakeChild";
        return true;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        if (investigateReady && InvestigatedTargetId != byte.MaxValue)
        {
            // 調査完了後は対象を解放しても、会議中を含めて結果を保持・表示する。
            var investigated = InvestigatedTargetId.GetPlayerControl();
            var investigatedName = investigated?.name ?? "?";
            var targetRole = investigated?.GetCustomRole() ?? CustomRoles.NotAssigned;
            return $"{size}<color={color}>{string.Format(GetString("RaccoonParentLowerTextResult"), investigatedName, UtilsRoleText.GetRoleName(targetRole))}</color>";
        }

        if (GrabbedTargetId == byte.MaxValue)
        {
            if (isForMeeting) return "";
            return $"{size}<color={color}>{GetString("RaccoonParentLowerTextIdle")}</color>";
        }

        var targetName = GrabbedTargetId.GetPlayerControl()?.name ?? "?";
        if (isForMeeting) return "";

        // ===== 修正 =====
        // 「指定の部屋へ向かおう」という固定文言ではなく、ウォーカーと同じ
        // IRoomTasker共通の部屋案内機能を使い、毎回ランダムに選ばれた
        // 具体的な部屋名(例:「キッチンに行け」)を表示するようにする。
        var roomText = (seer.GetRoleClass() as IRoomTasker)?.GetLowerText(seer, color);
        if (!string.IsNullOrEmpty(roomText))
            return $"{size}{roomText}";

        return $"{size}<color={color}>{string.Format(GetString("RaccoonParentLowerTextGrabbing"), targetName)}</color>";
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(GrabbedTargetId);
        sender.Writer.Write(investigateReady);
        sender.Writer.Write(InvestigatedTargetId);
        sender.Writer.Write(CanMakeChild);
        sender.Writer.Write(PendingChildTargetId);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        GrabbedTargetId = reader.ReadByte();
        investigateReady = reader.ReadBoolean();
        InvestigatedTargetId = reader.BytesRemaining > 0 ? reader.ReadByte() : byte.MaxValue;
        CanMakeChild = reader.BytesRemaining > 0 ? reader.ReadBoolean() : true;
        PendingChildTargetId = reader.BytesRemaining > 0 ? reader.ReadByte() : byte.MaxValue;
    }

    /// <summary>
    /// アライグマ陣営(親・子)の独立した勝利判定。
    /// 条件1: アライグマ陣営以外のキル可能役職(IKiller)が全滅
    /// 条件2: アライグマ陣営の人数がクルーメイトの人数以下になる
    /// どちらか一方でも成立すれば勝利。他陣営の判定には一切干渉しない。
    /// </summary>
    public static bool CheckWin(ref GameOverReason reason)
    {
        var raccoons = PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc != null && (pc.Is(CustomRoles.RaccoonParent) || pc.Is(CustomRoles.RaccoonChild)))
            .ToList();
        if (raccoons.Count == 0) return false;

        var otherKillers = PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc != null
                && !pc.Is(CustomRoles.RaccoonParent) && !pc.Is(CustomRoles.RaccoonChild)
                && pc.GetRoleClass() is IKiller)
            .ToList();

        var aliveCrewCount = PlayerCatch.AllAlivePlayerControls
            .Count(pc => pc != null && pc.GetCustomRole() is CustomRoles.Crewmate);

        bool win = otherKillers.Count == 0 || raccoons.Count <= aliveCrewCount;
        if (!win) return false;

        var winner = raccoons.First();
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.RaccoonParent, winner.PlayerId))
        {
            foreach (var pc in raccoons)
                CustomWinnerHolder.NeutralWinnerIds.Add(pc.PlayerId);
            reason = GameOverReason.ImpostorsByKill;
            return true;
        }
        return false;
    }
}
