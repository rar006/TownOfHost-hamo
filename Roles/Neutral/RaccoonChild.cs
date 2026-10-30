using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

public sealed class RaccoonChild : RoleBase, IKiller, IRoomTasker
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(RaccoonChild),
            player => new RaccoonChild(player),
            CustomRoles.RaccoonChild,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            77060,
            SetupOptionItem,
            "racc",
            "#c9a27a",
            OptionSort: (7, 3),
            isDesyncImpostor: true,
            from: From.TownOfHost_hamo
        );

    public RaccoonChild(PlayerControl player) : base(RoleInfo, player)
    {
        GrabbedTargetId = byte.MaxValue;
        InvestigatedTargetId = byte.MaxValue;
        Awakened = false;
        OriginalRole = CustomRoles.NotAssigned;
    }

    static void SetupOptionItem()
    {
        // アライグマの子は親が作る子役職。設定はアライグマ親内へ統合する。
        if (Options.CustomRoleSpawnChances != null && Options.CustomRoleSpawnChances.TryGetValue(CustomRoles.RaccoonChild, out var spawn))
        {
            spawn.SetValue(0, true, false);
            spawn.SetHidden(true);
        }
        if (Options.CustomRoleCounts != null && Options.CustomRoleCounts.TryGetValue(CustomRoles.RaccoonChild, out var count))
        {
            count.SetValue(0, true, false);
            count.SetHidden(true);
        }
    }

    public byte GrabbedTargetId;
    byte InvestigatedTargetId;
    public bool Awakened;
    CustomRoles OriginalRole;
    bool investigateReady;
    int grabFollowState;
    float meetingStartTimer = -1f;

    public override RoleTypes? AfterMeetingRole => Awakened ? RoleTypes.Impostor : OriginalRole.GetRoleInfo()?.BaseRoleType?.Invoke() ?? RoleTypes.Crewmate;

    // 自覚前はSKされる直前の役職として表示を維持する。
    // 自覚後は実際のアライグマの子として役職名・能力が見える。
    public override CustomRoles Misidentify() => Awakened || OriginalRole is CustomRoles.NotAssigned ? CustomRoles.NotAssigned : OriginalRole;

    public void SetOriginalRole(CustomRoles role)
    {
        OriginalRole = role;
        SendRpc();
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        if (Awakened) AURoleOptions.KillCooldown = RaccoonParent.GetChildKillCooldown();
    }

    public bool CanUseSabotageButton() => false;

    // 会議の画面が出てからだと「会議中のキル」として弾かれるので、通報された時点(会議に入る前)にキルする
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!AmongUsClient.Instance.AmHost || GrabbedTargetId == byte.MaxValue || !RaccoonParent.ChildKillOnMeetingIfGrabbing) return;
        var grabbed = GrabbedTargetId.GetPlayerControl();
        if (grabbed == null || !grabbed.IsAlive() || !Player.IsAlive()) return;
        CustomRoleManager.OnCheckMurder(Player, grabbed, grabbed, grabbed, true, false, 1, CustomDeathReason.Kill);
    }
    public override void OnStartMeeting()
    {
        if (GrabbedTargetId != byte.MaxValue && RaccoonParent.ChildKillOnMeetingIfGrabbing)
        {
            var target = GrabbedTargetId.GetPlayerControl();
            if (target != null && target.IsAlive() && AmongUsClient.Instance.AmHost)
            {
                CustomRoleManager.OnCheckMurder(Player, target, target, target, true, false, 1, CustomDeathReason.Kill);
            }
        }
        // ===== 修正 =====
        // 親(RaccoonParent)と同様、既に役職を調べ終えている(investigateReady=true)
        // 場合はGrabbedTargetIdをリセットせず、会議の最初だけ相手の役職を
        // 確認できるようにする。まだ掴んでいる途中(未到達)の場合は従来通り
        // 会議入りでリセットする。
        if (!investigateReady)
        {
            ReleaseGrab();
        }
        else
        {
            meetingStartTimer = 0f;
        }
    }

    public void AwakenFromSidekick()
    {
        // 親の保留SK処理から、会議終了時に一度だけ呼ばれる。
        Awakened = true;
        OriginalRole = CustomRoles.NotAssigned;
        SendRpc();
        Player.MarkDirtySettings();
        Player.RpcResetAbilityCooldown(Sync: true);
        UtilsOption.MarkEveryoneDirtySettings();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
    }

    public override void AfterMeetingTasks()
    {
        // 保留SK以外で作られた既存の子にも互換性を保つ。
        if (!AmongUsClient.Instance.AmHost || Awakened || !Player.IsAlive()) return;
        AwakenFromSidekick();
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

        // 親と同じく、ペンギン方式の間隔・ホスト補正で掴んだ対象を同期する。
        if (!target.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
        {
            const int syncInterval = 3;
            grabFollowState++;
            if (grabFollowState % syncInterval == 0)
            {
                var position = Player.transform.position;
                // 親と同様、掴み側で即時に表示を追従させてから全クライアントへ同期する。
                target.NetTransform.SnapTo(position, (ushort)(target.NetTransform.lastSequenceId + 1));
                target.RpcSnapToForced(position, SendOption.None);
            }
        }
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Awakened) { info.DoKill = false; return; }
        if (!Is(info.AttemptKiller) || info.AttemptKiller == info.AttemptTarget) return;

        // アライグマ親を掴もうとした子は、その瞬間に確実に自殺する。
        if (info.AttemptTarget.Is(CustomRoles.RaccoonParent))
        {
            info.DoKill = false;
            if (AmongUsClient.Instance.AmHost && Player.IsAlive())
            {
                MyState.DeathReason = CustomDeathReason.Suicide;
                Player.SetRealKiller(Player);
                Player.RpcMurderPlayer(Player);
                MyState.SetDead();
            }
            return;
        }

        // ===== 修正 =====
        // 親(RaccoonParent)と同じ「掴む→もう一度キルボタンで実際にキルする」
        // 二段階方式に統一する。
        if (GrabbedTargetId == byte.MaxValue)
        {
            // まだ誰も掴んでいない: キルせず掴む
            info.DoKill = false;
            GrabbedTargetId = info.AttemptTarget.PlayerId;
            InvestigatedTargetId = byte.MaxValue;
            investigateReady = false;
            grabFollowState = 0;
            var target = info.AttemptTarget;
            // 位置はFixedUpdateで運搬側へ強制追従させる。
            // CanMoveをfalseにすると掴まれたキラーのキルボタンも止まるため、
            // 能力操作は維持し、移動床のみ禁止する。
            target.GetPlayerState().CanMove = true;
            target.GetPlayerState().CanUseMovingPlatform = false;
            MyState.CanUseMovingPlatform = false;
            target.MarkDirtySettings();
            ((IRoomTasker)this).AddRoomTaker(Player.PlayerId);
            SendRpc();
            Player.RpcResetAbilityCooldown(Sync: true);
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

    public override void OnDestroy()
    {
        ReleaseGrab();
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
        // 掴み側のキル・移動オプションも即時に再計算する。
        // 対象が別役職に倒された場合でも、古い掴み状態でボタンが止まらないようにする。
        Player.MarkDirtySettings();
        grabFollowState = 0;
        GrabbedTargetId = byte.MaxValue;
        if (!keepInvestigation)
        {
            InvestigatedTargetId = byte.MaxValue;
            investigateReady = false;
        }
        SendRpc();
    }

    /// <summary>
    /// 掴み側または掴み対象が死亡・追放・切断した際に、残った掴み状態を即時に解除する。
    /// 親の共通解除処理から呼び出される。
    /// </summary>
    public bool ReleaseGrabIfInvolves(PlayerControl player)
    {
        if (player == null) return false;
        if (Player.PlayerId != player.PlayerId && GrabbedTargetId != player.PlayerId) return false;

        ReleaseGrab();
        return true;
    }

    public bool IsAssignRoomTask() => Awakened && GrabbedTargetId != byte.MaxValue && !investigateReady;

    public void OnComplete(int completeroom)
    {
        // 指定部屋へ到達したら役職を調査し、掴んでいる対象は自動で解放する。
        if (GrabbedTargetId == byte.MaxValue) return;
        InvestigatedTargetId = GrabbedTargetId;
        investigateReady = true;
        ReleaseGrab(keepInvestigation: true);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive() || !Awakened) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        /*if (!Awakened)
        {
            if (isForMeeting) return "";
            return $"{size}<color={color}>{GetString("RaccoonChildLowerTextUnaware")}</color>";
        }*/

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
        sender.Writer.Write(Awakened);
        sender.Writer.WritePacked((int)OriginalRole);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        GrabbedTargetId = reader.ReadByte();
        investigateReady = reader.ReadBoolean();
        InvestigatedTargetId = reader.BytesRemaining > 1 ? reader.ReadByte() : byte.MaxValue;
        Awakened = reader.ReadBoolean();
        OriginalRole = reader.BytesRemaining > 0 ? (CustomRoles)reader.ReadPackedInt32() : CustomRoles.NotAssigned;
    }
}
