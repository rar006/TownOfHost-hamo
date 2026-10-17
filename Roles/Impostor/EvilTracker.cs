using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Impostor;

public sealed class EvilTracker : RoleBase, IImpostor, IKillFlashSeeable, ISidekickable, IUsePhantomButton, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilTracker),
            player => new EvilTracker(player),
            CustomRoles.EvilTracker,
            () => (TargetMode)OptionTargetMode.GetValue() == TargetMode.Never || SelfVote.GetBool() ? RoleTypes.Impostor : IsOneclick.GetBool() ? RoleTypes.Phantom : RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            3200,
            SetupOptionItem,
            "etk",
            OptionSort: (2, 1),
            canMakeMadmate: () => OptionCanCreateSideKick.GetBool(),
            Desc: () =>
            {
                string desc = GetString("EvilTrackerDesc");
                if (IsOneclick.GetBool())
                {
                    desc = GetString("EvilTrackerDescPhantom");
                }
                if (SelfVote.GetBool())
                {
                    desc = GetString("EvilTrackerDescOriginal");
                }

                return desc;
            },
            from: From.TOR_GM_Haoming_Edition
        );

    public EvilTracker(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        timer = 0;
        disting = "";
        EvilTrackers.Clear();
        canSeeMurderRoom = OptionCanSeeMurderRoom.GetBool();
        CanSeeKillFlash = OptionCanSeeKillFlash.GetBool();
        CurrentTargetMode = (TargetMode)OptionTargetMode.GetValue();
        CanSeeLastRoomInMeeting = true /*OptionCanSeeLastRoomInMeeting.GetBool()*/;
        CanCreateSideKick = OptionCanCreateSideKick.GetBool() && CurrentTargetMode != TargetMode.Never;
        CanSeeDistance = OptionCanSeeDistance.GetBool() && CurrentTargetMode != TargetMode.Never;

        TargetId = byte.MaxValue;
        CanSetTarget = CurrentTargetMode != TargetMode.Never;
        //ImpostorsIdはEvilTracker内で共有
        ImpostorsId.Clear();
        var playerId = player.PlayerId;
        foreach (var target in PlayerCatch.AllAlivePlayerControls)
        {
            var targetId = target.PlayerId;
            if (targetId != playerId && target.IsTeammate(Player))
            {
                ImpostorsId.Add(targetId);
                TargetArrow.Add(playerId, targetId);
            }
        }

        CustomRoleManager.OnMurderPlayerOthers.Add(HandleMurderRoomNotify);
    }

    private static OptionItem OptionCanSeeKillFlash;
    private static OptionItem OptionCanSeeMurderRoom;
    private static OptionItem OptionTargetMode;
    private static OptionItem OptionCanSeeDistance;
    ///private static OptionItem OptionCanSeeLastRoomInMeeting;
    private static OptionItem OptionCanCreateSideKick;
    private static OptionItem CoolDown;
    private static OptionItem IsOneclick;
    private static OptionItem SelfVote;
    enum OptionName
    {
        EvilTrackerCanSeeKillFlash,
        EvilTrackerTargetMode,
        EvilTrackerCanSeeLastRoomInMeeting,
        EvilHackerCanSeeMurderRoom,
        EvilHackerCanSeeDistance,
        EvilTrackerOneClick,
        EvilTrackerSelfVote
    }
    static bool CanSeeKillFlash;
    static bool canSeeMurderRoom;
    static TargetMode CurrentTargetMode;
    static bool CanSeeDistance;
    static bool CanSeeLastRoomInMeeting;
    static bool CanCreateSideKick;

    public byte TargetId;
    public bool CanSetTarget;
    HashSet<byte> ImpostorsId = new(3);
    static HashSet<EvilTracker> EvilTrackers = new();
    HashSet<EvilHacker.MurderNotify> activeNotifies = new(2);
    float timer;
    string disting;
    public bool CanUseVoted() => CanTarget() && SelfVote.GetBool();
    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!SelfVote.GetBool() || !Is(voter)) return true;

        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (!CanTarget())
            {
                SetMode(Player, false);
                Utils.SendMessage(GetString("ET.Error.CantTarget"), Player.PlayerId);
                return false;
            }
            if (status is VoteStatus.Self)
                Utils.SendMessage(string.Format(GetString("SkillMode"), GetString("Mode.ET"), GetString("Vote.ET")) + GetString("VoteSkillMode"), Player.PlayerId);
            if (status is VoteStatus.Skip)
                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            if (status is VoteStatus.Vote)
            {
                var target = PlayerCatch.GetPlayerById(votedForId);
                SetTarget(target.PlayerId);
                Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}のターゲットを{target.GetNameWithRole().RemoveHtmlTags()}に設定", "EvilTrackerTarget");
                Player.MarkDirtySettings();

                var sendtext = string.Format(GetString("Skill.EvilTracker"), UtilsName.GetPlayerColor(PlayerCatch.GetPlayerById(votedForId), true));
                Utils.SendMessage(sendtext, Player.PlayerId);
            }
            SetMode(Player, status is VoteStatus.Self);
            return false;
        }
        return true;
    }
    private enum TargetMode
    {
        Never,
        OnceInGame,
        EveryMeeting,
        Always,
    };
    private static readonly string[] TargetModeText =
    {
        "EvilTrackerTargetMode.Never",
        "EvilTrackerTargetMode.OnceInGame",
        "EvilTrackerTargetMode.EveryMeeting",
        "EvilTrackerTargetMode.Always",
    };
    private enum TargetOperation : byte
    {
        /// <summary>
        /// ターゲット再設定可能にする
        /// </summary>
        ReEnableTargeting,
        /// <summary>
        /// ターゲットを削除する
        /// </summary>
        RemoveTarget,
        /// <summary>
        /// ターゲットを設定する
        /// </summary>
        SetTarget,
        /// <summary>
        /// インポスターがキルした時に受け取るRPC
        /// </summary>
        ImpostorKill
    }

    private static void SetupOptionItem()
    {
        OptionCanSeeKillFlash = BooleanOptionItem.Create(RoleInfo, 10, OptionName.EvilTrackerCanSeeKillFlash, true, false);
        OptionCanSeeMurderRoom = BooleanOptionItem.Create(RoleInfo, 14, OptionName.EvilHackerCanSeeMurderRoom, false, false, OptionCanSeeKillFlash);
        OptionTargetMode = StringOptionItem.Create(RoleInfo, 11, OptionName.EvilTrackerTargetMode, TargetModeText, 2, false);
        OptionCanSeeDistance = BooleanOptionItem.Create(RoleInfo, 15, OptionName.EvilHackerCanSeeDistance, false, false, OptionTargetMode);
        OptionCanCreateSideKick = BooleanOptionItem.Create(RoleInfo, 12, GeneralOption.CanCreateSideKick, false, false, OptionTargetMode)
            .SetEnabled(() => !IsOneclick.GetBool() && !SelfVote.GetBool());
        SelfVote = BooleanOptionItem.Create(RoleInfo, 18, OptionName.EvilTrackerSelfVote, true, false)
            .SetEnabled(() => (TargetMode)OptionTargetMode.GetValue() != TargetMode.Never && (TargetMode)OptionTargetMode.GetValue() != TargetMode.Always/* && !IsOneclick.GetBool()*/)
            .SetInfo(Translator.GetString("ETSVInfo"));
        IsOneclick = BooleanOptionItem.Create(RoleInfo, 17, OptionName.EvilTrackerOneClick, false, false)
            .SetEnabled(() => (TargetMode)OptionTargetMode.GetValue() != TargetMode.Never/* && !SelfVote.GetBool()*/)
            .SetInfo(Translator.GetString("ETOCInfo"));
        CoolDown = FloatOptionItem.Create(RoleInfo, 16, GeneralOption.Cooldown, OptionBaseCoolTime, 15f, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetEnabled(() => (TargetMode)OptionTargetMode.GetValue() != TargetMode.Never);
        //OptionCanSeeLastRoomInMeeting = BooleanOptionItem.Create(RoleInfo, 13, OptionName.EvilTrackerCanSeeLastRoomInMeeting, false, false);
    }
    public override void Add() => EvilTrackers.Add(this);
    public override void OnDestroy() => EvilTrackers.Remove(this);

    public bool? CheckKillFlash(MurderInfo info) // IKillFlashSeeable
        => CanSeeKillFlash && !info.IsSuicide && !info.IsAccident && info.AttemptKiller.IsTeammate(Player);
    public bool CanMakeSidekick() => CanCreateSideKick; // ISidekickable
    public bool IsresetAfterKill => false;
    public bool IsPhantomRole => IsOneclick.GetBool() && CanTarget() && !SelfVote.GetBool();
    public bool UseOneclickButton => IsOneclick.GetBool() && !SelfVote.GetBool();
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = CoolDown.GetFloat() >= 1f;

        var target = Player.GetKillTarget(true);
        if (target == null) return;
        if (!CanTarget() || target.IsTeammate(Player) || !IsOneclick.GetBool() || (TargetMode)OptionTargetMode.GetValue() == TargetMode.Never || SelfVote.GetBool()) return;

        SetTarget(target.PlayerId);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}のターゲットを{target.GetNameWithRole().RemoveHtmlTags()}に設定", "EvilTrackerTarget");
        Player.MarkDirtySettings();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        var operation = (TargetOperation)reader.ReadByte();

        switch (operation)
        {
            case TargetOperation.ReEnableTargeting: ReEnableTargeting(); break;
            case TargetOperation.RemoveTarget: RemoveTarget(); break;
            case TargetOperation.SetTarget: SetTarget(reader.ReadByte()); break;
            case TargetOperation.ImpostorKill: CreateMurderNotify((SystemTypes)reader.ReadByte()); break;
            default: Logger.Warn($"不明なオペレーション: {operation}", nameof(EvilTracker)); break;
        }
    }
    private void ReEnableTargeting()
    {
        CanSetTarget = true;
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)TargetOperation.ReEnableTargeting);
        }
    }
    private void RemoveTarget()
    {
        TargetId = byte.MaxValue;
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)TargetOperation.RemoveTarget);
        }
    }
    private void SetTarget(byte targetId)
    {
        TargetId = targetId;
        if (CurrentTargetMode != TargetMode.Always)
        {
            CanSetTarget = false;
        }
        TargetArrow.Add(Player.PlayerId, targetId);
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)TargetOperation.SetTarget);
            sender.Writer.Write(targetId);
        }
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = CanTarget() ? CoolDown.GetFloat() : 255f;
        AURoleOptions.ShapeshifterDuration = 1f;
        AURoleOptions.PhantomCooldown = CanTarget() ? CoolDown.GetFloat() : 255f;
    }
    public override string GetAbilityButtonText() => GetString("EvilTrackerChangeButtonText");
    public override bool OverrideAbilityButton(out string text)
    {
        text = "EvilTracker_Ability";
        return true;
    }
    public override bool CanUseAbilityButton() => CanTarget();

    // 値取得の関数
    private bool CanTarget() => Player.IsAlive() && CanSetTarget;
    private bool IsTrackTarget(PlayerControl target)
        => Player.IsAlive() && target.IsAlive() && !Is(target)
        && (target.IsTeammate(Player) || TargetId == target.PlayerId);

    // 各所で呼ばれる処理
    public override bool CheckShapeshift(PlayerControl target, ref bool animate)
    {
        //ターゲット出来ない、もしくはターゲットが味方の場合は処理しない
        //※どちらにしろシェイプシフトは出来ない
        if (!CanTarget() || target.IsTeammate(Player)) return false;

        SetTarget(target.PlayerId);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}のターゲットを{target.GetNameWithRole().RemoveHtmlTags()}に設定", "EvilTrackerTarget");
        Player.MarkDirtySettings();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        return false;
    }
    public override void OnSpawn(bool initialState)
    {
        if (initialState) return;
        if (CurrentTargetMode == TargetMode.EveryMeeting)
        {
            ReEnableTargeting();
            Player.RpcResetAbilityCooldown(Sync: true);
        }
        var target = PlayerCatch.GetPlayerById(TargetId);
        if (!Player.IsAlive() || !target.IsAlive())
        {
            RemoveTarget();
        }
    }

    // 表示系の関数群
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool _ = false)
    {
        seen ??= seer;
        return TargetId == seen.PlayerId ? Utils.ColorString(Palette.ImpostorRed, "◀") : "";
    }
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting)
        {
            var roomName = GetLastRoom(seen);
            // 空のときにタグを付けると，suffixが空ではない判定となりなにもない3行目が表示される
            return roomName.Length == 0 ? "" : $"<size=1.5>{roomName}</size>";
        }
        else
        {
            if (!canSeeMurderRoom || seer != Player || seen != Player || activeNotifies.Count <= 0)
            {
                return GetArrows(seen);
            }
            var roomNames = activeNotifies.Select(notify => DestroyableSingleton<TranslationController>.Instance.GetString(notify.Room));
            return GetArrows(seen) + "\n" + Utils.ColorString(Color.green, $"{GetString("MurderNotify")}: {string.Join(", ", roomNames)}");
        }
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        string Lower = "";
        if (GameStates.CalledMeeting || GameStates.IsMeeting || seer != seen || !Is(seer))
        {
            return "";
            /*if (!OptionCanSeeLastRoomInMeeting.GetBool())
            {
                return "";
            }*/
        }
        if (ImpostorsId != null)
        {
            foreach (var imp in ImpostorsId)
            {
                if (imp == Player.PlayerId) continue;

                var player = PlayerCatch.GetPlayerById(imp);
                var room = player.GetPlainShipRoom().RoomId;

                Lower += $"<#ff1919>{UtilsName.GetPlayerColor(player)}({GetString($"{room}")})</color>\n";
            }
        }
        if (TargetId != byte.MaxValue)
        {
            var target = PlayerCatch.GetPlayerById(TargetId);
            var trgr = target.GetPlainShipRoom().RoomId;
            Lower += $"<#8cffff>{UtilsName.GetPlayerColor(target)}({GetString($"{trgr}")})";
        }
        return Lower;
    }
    private string GetArrows(PlayerControl seen)
    {
        if (!Is(seen)) return "";

        var trackerId = Player.PlayerId;

        ImpostorsId.RemoveWhere(id => PlayerState.GetByPlayerId(id).IsDead);

        var sb = new StringBuilder(80);
        if (ImpostorsId.Count > 0)
        {
            sb.Append($"<color={UtilsRoleText.GetRoleColorCode(CustomRoles.Impostor)}>");
            foreach (var impostorId in ImpostorsId)
            {
                sb.Append(TargetArrow.GetArrows(Player, impostorId));
            }
            sb.Append($"</color>");
        }

        if (TargetId != byte.MaxValue)
        {
            sb.Append(Utils.ColorString(Color.white, TargetArrow.GetArrows(Player, TargetId)));
            if (PlayerCatch.GetPlayerById(TargetId).IsAlive() && CanSeeDistance)
            {
                sb.Append($"<color=#ffffff><size=60%>({disting})</color></size>");
            }
        }
        return sb.ToString();
    }
    public string GetLastRoom(PlayerControl seen)
    {
        if (!(CanSeeLastRoomInMeeting && IsTrackTarget(seen))) return "";

        string text = Utils.ColorString(Palette.ImpostorRed, TargetArrow.GetArrows(Player, seen.PlayerId));
        var room = seen.GetShipRoomName();

        return text + room;
    }
    /// <summary>相方がキルした部屋を通知する設定がオンなら各プレイヤーに通知を行う</summary>
    private static void HandleMurderRoomNotify(MurderInfo info)
    {
        if (canSeeMurderRoom)
        {
            foreach (var evilTracker in EvilTrackers)
            {
                // 生きてる間に相方のキルでキルフラが鳴った場合に通知を出す
                if (!evilTracker.Player.IsAlive() || !(CanSeeKillFlash && !info.IsSuicide && !info.IsAccident && info.AttemptKiller.Is(CustomRoleTypes.Impostor)) || info.AttemptKiller == evilTracker.Player)
                    return;

                evilTracker.RpcCreateMurderNotify(info.AttemptTarget.GetPlainShipRoom()?.RoomId ?? SystemTypes.Hallway);
            }
        }
    }

    private void RpcCreateMurderNotify(SystemTypes room)
    {
        CreateMurderNotify(room);
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)TargetOperation.ImpostorKill);
            sender.Writer.Write((byte)room);
        }
    }
    /// <summary>
    /// 名前の下にキル発生通知を出す
    /// </summary>
    /// <param name="room">キルが起きた部屋</param>
    private void CreateMurderNotify(SystemTypes room)
    {
        activeNotifies.Add(new()
        {
            CreatedAt = DateTime.Now,
            Room = room,
        });
        if (AmongUsClient.Instance.AmHost)
        {
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        timer += Time.fixedDeltaTime;
        if (timer > 0.5f && PlayerCatch.GetPlayerById(TargetId).IsAlive() && player.IsAlive() && CanSeeDistance)
        {
            timer = 0;
            var oldtext = disting;
            //クライアントの奴と揃える
            var distance = Vector2.Distance(Player.transform.position, PlayerCatch.GetPlayerById(TargetId).transform.position);
            distance = Mathf.Round(distance * 10);//小数第一までは表示させたい
            disting = $"{distance * 0.1f}";
            disting += disting.Contains(".") ? "000" : ".0";//整数の時おかしくなる奴
            disting = disting.Substring(0, 10 <= distance * 0.1f ? 4 : 3);//xx.x / x.x
            if (oldtext != disting) UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
        // 古い通知の削除処理 Mod入りは自分でやる
        if (!AmongUsClient.Instance.AmHost && Player != PlayerControl.LocalPlayer)
        {
            return;
        }
        if (activeNotifies.Count <= 0)
        {
            return;
        }
        // NotifyRolesを実行するかどうかのフラグ
        var doNotifyRoles = false;
        // 古い通知があれば削除
        foreach (var notify in activeNotifies)
        {
            if (DateTime.Now - notify.CreatedAt > TimeSpan.FromSeconds(10))
            {
                activeNotifies.Remove(notify);
                doNotifyRoles = true;
            }
        }
        if (doNotifyRoles && AmongUsClient.Instance.AmHost)
        {
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
    }
    void IKiller.OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (info.AppearanceTarget.PlayerId == TargetId)
            Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[0]);
    }
    public static Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 0);
        achievements.Add(0, n1);
    }
}