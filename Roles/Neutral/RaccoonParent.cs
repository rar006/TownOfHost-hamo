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
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            77030,
            SetupOptionItem,
            "racp",
            "#a0785a",
            (7, 2),
            true,
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
    static OptionItem OptionRaccoonChildSection;
    static OptionItem OptionRaccoonChildKillCooldown;
    static OptionItem OptionRaccoonChildKillOnMeetingIfGrabbing;

    enum OptionName
    {
        RaccoonParentKillOnMeetingIfGrabbing,
        RaccoonParentKillCooldownAfterEnd,
        RaccoonParentMakeChildCooldown,
    }

        static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 1);

        OptionKillOnMeetingIfGrabbing = BooleanOptionItem.Create(RoleInfo, 10, OptionName.RaccoonParentKillOnMeetingIfGrabbing, false, false);
        OptionKillCooldownAfterEnd = FloatOptionItem.Create(RoleInfo, 11, OptionName.RaccoonParentKillCooldownAfterEnd, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMakeChildCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.RaccoonParentMakeChildCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);

        OptionRaccoonChildSection = ObjectOptionitem.Create(RoleInfo.ConfigId + 13, "RaccoonChildSettings", true, null, RoleInfo.Tab)
            .SetOptionName(() => "【アライグマの子設定】")
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.RaccoonParent])
            .SetParentRole(CustomRoles.RaccoonParent);
        OptionRaccoonChildKillCooldown = FloatOptionItem.Create(RoleInfo.ConfigId + 14, "RaccoonChildKillCooldown", new(0f, 180f, 0.5f), 35f, RoleInfo.Tab, false)
            .SetOptionName(() => "キルクール")
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(OptionRaccoonChildSection)
            .SetParentRole(CustomRoles.RaccoonParent);
        OptionRaccoonChildKillOnMeetingIfGrabbing = BooleanOptionItem.Create(RoleInfo.ConfigId + 15, "RaccoonChildKillOnMeetingIfGrabbing", false, RoleInfo.Tab, false)
            .SetOptionName(() => "掴んだ相手を会議開始時にキルするか")
            .SetParent(OptionRaccoonChildSection)
            .SetParentRole(CustomRoles.RaccoonParent);
    }

    public byte GrabbedTargetId;
    byte InvestigatedTargetId;
    bool investigateReady;
    bool CanMakeChild;
    byte PendingChildTargetId;
    int grabFollowState;
    float meetingStartTimer = -1f;

    public static float GetKillCooldown() => OptionKillCooldownAfterEnd.GetFloat();
    public static float GetChildKillCooldown() => OptionRaccoonChildKillCooldown?.GetFloat() ?? 35f;
    public static bool ChildKillOnMeetingIfGrabbing => OptionRaccoonChildKillOnMeetingIfGrabbing?.GetBool() ?? false;
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
        // 対象の位置はFixedUpdateで強制追従させる。
        // CanMoveをfalseにすると対象のキルボタンまで無効化されるため、
        // 移動床だけを禁止してキル・防御などの能力操作は許可する。
        target.GetPlayerState().CanMove = true;
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
    /// 共通のキル後処理から呼び出されるため、対象を倒した役職のキルボタンも通常状態へ戻る。
    /// </summary>
    public bool ReleaseGrabIfInvolves(PlayerControl player)
    {
        if (player == null) return false;
        if (Player.PlayerId != player.PlayerId && GrabbedTargetId != player.PlayerId) return false;

        ReleaseGrab();
        return true;
    }

    // ===== 修正 =====
    // 到達済み(investigateReady=true)の場合は部屋タスクの再割り当てを止める。
    // 以前は到達するたびに次のランダムな部屋がまた割り当てられ続けていたが、
    // 1回到達したらそれ以上は部屋を巡回させない。
    public bool IsAssignRoomTask() => GrabbedTargetId != byte.MaxValue && !investigateReady;

    public void OnComplete(int completeroom)
    {
        // 指定された部屋に到着した時点で役職を調査し、対象はすぐに自動解放する。
        if (GrabbedTargetId == byte.MaxValue) return;
        InvestigatedTargetId = GrabbedTargetId;
        investigateReady = true;
        ReleaseGrab(keepInvestigation: true);
    }

    // 会議の画面が出てからだと「会議中のキル」として弾かれるので、通報された時点(会議に入る前)にキルする
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!AmongUsClient.Instance.AmHost || GrabbedTargetId == byte.MaxValue || !OptionKillOnMeetingIfGrabbing.GetBool()) return;
        var grabbed = GrabbedTargetId.GetPlayerControl();
        if (grabbed == null || !grabbed.IsAlive() || !Player.IsAlive()) return;
        CustomRoleManager.OnCheckMurder(Player, grabbed, grabbed, grabbed, true, false, 1, CustomDeathReason.Kill);
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
        // インポスター・マッドメイト・第三陣営を含む、すべての生存役職を子作り対象にする。
        // アライグマ親子同士だけは対象外にして循環した役職変更を防ぐ。
        if (target == null || !target.IsAlive()
            || target.Is(CustomRoles.RaccoonParent) || target.Is(CustomRoles.RaccoonChild))
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
    /// アライグマ親子の掴み対象が死亡する直前・直後に、ホストで残存する掴み状態を解除する。
    /// 通常キル、役職固有キル、追放・切断経路からも同じAPIを利用できる。
    /// </summary>
    public static bool ReleaseForPlayer(PlayerControl player)
    {
        if (player == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;

        bool released = false;
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc?.GetRoleClass() is RaccoonParent parent)
                released |= parent.ReleaseGrabIfInvolves(player);
            else if (pc?.GetRoleClass() is RaccoonChild child)
                released |= child.ReleaseGrabIfInvolves(player);
        }

        if (released)
            Logger.Info($"{player.GetNameWithRole().RemoveHtmlTags()} に関連する掴み状態を解除しました。", "RaccoonGrab");

        return released;
    }

    /// <summary>
    /// アライグマ陣営(親・子)の独立した勝利判定。
    /// 条件1: アライグマ陣営以外のキル可能人外（インポスターまたはIKiller）が全滅
    /// 条件2: 生存クルーメイト数と生存アライグマ陣営数（親＋子）が同数
    /// 両方成立した時だけ勝利するジャッカル型の勝利条件。
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
                && (pc.GetCustomRole().IsImpostor() || pc.GetRoleClass() is IKiller))
            .ToList();

        // 特殊クルー役職も含め、クルーメイト陣営全体の生存数で比較する。
        var aliveCrewCount = PlayerCatch.AllAlivePlayerControls
            .Count(pc => pc != null && pc.GetCustomRole().IsCrewmate());

        // 例: クルー2人・アライグマ親子2人、かつキル可能人外が0人なら勝利。
        bool win = otherKillers.Count == 0 && raccoons.Count == aliveCrewCount;
        if (!win) return false;

        var winner = raccoons.First();
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.RaccoonParent, winner.PlayerId))
        {
            foreach (var pc in raccoons)
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(pc.PlayerId);
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            }
            reason = GameOverReason.ImpostorsByKill;
            return true;
        }
        return false;
    }
}
