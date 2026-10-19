using AmongUs.GameOptions;

using TownOfHost.Patches;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

// ===== トゥモロー (Tomorrow) =====
// カラーコード: #00FFEA
// イントロ：【明日を呼ぶ者】
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// サボタージュ中ではない時にペットを撫でると、1度だけ特殊な緊急会議を開ける。
// この能力は、自身の残りボタン数や全体のボタン数に影響を及ぼさない。
// 特殊な緊急会議は、トゥモローがボタンを押したものとして扱われる。
// 特殊な緊急会議で死亡した人物が発見された時、設定によりその死体の情報を公開する。
public sealed class Tomorrow : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Tomorrow),
            player => new Tomorrow(player),
            CustomRoles.Tomorrow,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            96500,
            SetupOptionItem,
            "Tmr",
            "#00FFEA",
            (5, 5),
            from: From.TownOfHost_hamo
        );

    public Tomorrow(PlayerControl player) : base(RoleInfo, player)
    {
        cooldown = OptionCooldown.GetFloat();
        revealDeathTime = OptionRevealDeathTime.GetBool();
        revealDeathLocation = OptionRevealDeathLocation.GetBool();
        revealDeathRole = OptionRevealDeathRole.GetBool();

        used = false;
        lastUseTime = float.NegativeInfinity;

        Patches.PetActionManager.Register(player.PlayerId, OnPetUsed);
    }

    static OptionItem OptionCooldown;
    static OptionItem OptionRevealDeathTime;
    static OptionItem OptionRevealDeathLocation;
    static OptionItem OptionRevealDeathRole;

    enum OptionName
    {
        TomorrowCooldown,
        TomorrowRevealDeathTime,
        TomorrowRevealDeathLocation,
        TomorrowRevealDeathRole,
    }

    private readonly float cooldown;
    private readonly bool revealDeathTime;
    private readonly bool revealDeathLocation;
    private readonly bool revealDeathRole;

    // 能力を既に使い切ったか(1度きり)
    private bool used;
    private float lastUseTime;

    static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.TomorrowCooldown,
            new(0.5f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRevealDeathTime = BooleanOptionItem.Create(RoleInfo, 11, OptionName.TomorrowRevealDeathTime, true, false);
        OptionRevealDeathLocation = BooleanOptionItem.Create(RoleInfo, 12, OptionName.TomorrowRevealDeathLocation, false, false);
        OptionRevealDeathRole = BooleanOptionItem.Create(RoleInfo, 13, OptionName.TomorrowRevealDeathRole, false, false);
    }

    public override void OnDestroy()
    {
        Patches.PetActionManager.Unregister(Player.PlayerId);
    }

    private void OnPetUsed()
    {
        if (!Player.IsAlive() || used) return;
        if (GameStates.IsMeeting) return;
        // サボタージュ中は使用不可。
        if (Utils.IsActive(SystemTypes.Reactor)
            || Utils.IsActive(SystemTypes.Electrical)
            || Utils.IsActive(SystemTypes.Laboratory)
            || Utils.IsActive(SystemTypes.Comms)
            || Utils.IsActive(SystemTypes.LifeSupp)
            || Utils.IsActive(SystemTypes.HeliSabotage))
        {
            return;
        }

        var now = UnityEngine.Time.time;
        if (now - lastUseTime < cooldown) return;

        used = true;
        lastUseTime = now;
        revealPending = true;

        // 要望通り「自身の残りボタン数に影響を及ぼさない」ようにするため、
        // 会議を開く直前の残りボタン数を記録しておき、直後の処理で
        // ExReportDeadBody内部が(target=nullの通常の緊急会議として)勝手に
        // 1消費してしまった分を元に戻す。
        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        var buttonsBefore = state?.NumberOfRemainingButtons ?? -1;

        // 全体のボタン数や自身のボタン数を消費せず、トゥモローが押したものとして緊急会議を開く。
        ReportDeadBodyPatch.ExReportDeadBody(Player, null, false);

        if (state != null && buttonsBefore >= 0)
        {
            state.NumberOfRemainingButtons = buttonsBefore;
        }

        UtilsGameLog.AddGameLog("Tomorrow",
            $"{UtilsName.GetPlayerColor(Player)}が特殊な緊急会議を開いた");
    }

    // 直前に開いた特殊会議で、まだ死体情報の公開を行っていない場合はtrue
    private bool revealPending;
    private readonly System.Collections.Generic.HashSet<byte> deadAtTurnStart = new();
    private void RememberDeadAtTurnStart()
    {
        deadAtTurnStart.Clear();
        foreach (var pc in PlayerCatch.AllPlayerControls)
            if (pc?.Data != null && pc.Data.IsDead) deadAtTurnStart.Add(pc.PlayerId);
    }
    public override void Add() => RememberDeadAtTurnStart();
    public override void AfterMeetingTasks() => RememberDeadAtTurnStart();

    public override void OnStartMeeting()
    {
        if (!revealPending) return;
        revealPending = false;

        if (!revealDeathTime && !revealDeathLocation && !revealDeathRole) return;

        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc?.Data == null || !pc.Data.IsDead) continue;
            if (deadAtTurnStart.Contains(pc.PlayerId)) continue; // 前のターンまでに死んでいた人は公開しない
            var parts = new System.Collections.Generic.List<string>();
            if (revealDeathTime)
            {
                var state = PlayerState.GetByPlayerId(pc.PlayerId);
                var deathTime = state?.RealKiller.Item1 ?? System.DateTime.MinValue;
                if (deathTime == System.DateTime.MinValue)
                {
                    parts.Add(GetString("Tomorrow.RevealTimeUnknown"));
                }
                else
                {
                    var secondsAgo = System.Math.Max(0, (int)(System.DateTime.Now - deathTime).TotalSeconds);
                    parts.Add(string.Format(GetString("Tomorrow.RevealTime"), secondsAgo));
                }
            }
            if (revealDeathRole)
                parts.Add(string.Format(GetString("Tomorrow.RevealRole"), UtilsRoleText.GetRoleName(pc.GetCustomRole())));
            if (revealDeathLocation)
                parts.Add(string.Format(GetString("Tomorrow.RevealLocation"), GetString($"{pc.GetPlainShipRoom()?.RoomId}")));

            if (parts.Count == 0) continue;

            Utils.SendMessage(string.Format(GetString("Tomorrow.RevealHeader"), pc.Data.PlayerName) + " " + string.Join(" / ", parts));
        }
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || !Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        var prefix = isForHud ? "" : "<size=60%>";
        var c = RoleInfo.RoleColorCode;
        var stateText = used ? "能力は使用済みです" : "ペットで特殊会議を開けます";
        return $"{prefix}<color={c}>{stateText}</color>";
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
