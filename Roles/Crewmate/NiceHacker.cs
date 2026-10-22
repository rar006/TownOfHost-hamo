using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

// ナイスハッカー: ペットを撫でるとタスクモードとハックモードを切り替える。
// ハックモードの間はシェイプシフトの画面(名前の下)にアドミンの情報が出る。シェイプシフトはできない。
// ハックモードのまま会議になると、設定した秒数前のアドミンがチャットに出る(回数制限あり)。会議の後はタスクモードに戻る。
public sealed class NiceHacker : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(NiceHacker),
            player => new NiceHacker(player),
            CustomRoles.NiceHacker,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            992600,
            SetupOptionItem,
            "nhk",
            "#C2FF00",
            OptionSort: (6, 10),
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public NiceHacker(PlayerControl player)
    : base(RoleInfo, player)
    {
        canSeeDeadBody = OptionCanSeeDeadBody.GetBool();
        maxCount = OptionMaxCount.GetInt();
        secondsBefore = OptionSecondsBefore.GetFloat();
    }

    private static OptionItem OptionCanSeeDeadBody;
    private static OptionItem OptionMaxCount;
    private static OptionItem OptionSecondsBefore;
    enum OptionName
    {
        NiceHackerCanSeeDeadBody,
        NiceHackerMaxCount,
        NiceHackerSecondsBefore,
    }
    private static bool canSeeDeadBody;
    private static int maxCount;
    private static float secondsBefore;

    private bool hackMode;
    private int remaining;
    // シェイプシフト画面で、その人の名前の下に出すアドミンの行
    private readonly Dictionary<byte, string> nameAdmin = new();
    private string lastText = "";
    // 過去のアドミン(時刻, 文)。会議の時に「何秒前」のものを出すために取っておく
    private readonly Queue<(float time, string text)> history = new();
    private float nextRecordAt;
    private const float RecordInterval = 0.5f;

    private static void SetupOptionItem()
    {
        OptionCanSeeDeadBody = BooleanOptionItem.Create(RoleInfo, 10, OptionName.NiceHackerCanSeeDeadBody, false, false);
        OptionMaxCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.NiceHackerMaxCount, new(1, 99, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionSecondsBefore = FloatOptionItem.Create(RoleInfo, 12, OptionName.NiceHackerSecondsBefore, new(0f, 30f, 0.5f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void Add()
    {
        hackMode = false;
        remaining = maxCount;
        history.Clear();
        nameAdmin.Clear();
        PetActionManager.Register(Player.PlayerId, OnPet);
    }

    public override void OnDestroy() => PetActionManager.Unregister(Player.PlayerId);

    private void OnPet()
    {
        if (!AmongUsClient.Instance.AmHost || Player == null || !Player.IsAlive() || GameStates.IsMeeting) return;
        SetMode(!hackMode);
    }

    private void SetMode(bool hack)
    {
        hackMode = hack;
        if (!AmongUsClient.Instance.AmHost || Player == null) return;
        var clientId = Player.GetClientId();
        if (Player.IsAlive() && (clientId != -1 || Player == PlayerControl.LocalPlayer))
        {
            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                var r = pc.GetCustomRole();
                // ハックモードの間はシェイプシフター(インポスター側)になるので、インポスターの仲間表示が出ないよう科学者に見せる
                if (r.IsImpostor()) SetRoleForClient(pc, hack ? RoleTypes.Scientist : r.GetRoleTypes(), clientId);
                if (pc == Player) SetRoleForClient(pc, hack ? RoleTypes.Shapeshifter : RoleTypes.Crewmate, clientId);
            }
        }
        if (Player == PlayerControl.LocalPlayer && HudManager.InstanceExists)
        {
            var hud = HudManager.Instance;
            hud.KillButton.ToggleVisible(false);
            hud.ImpostorVentButton.ToggleVisible(false);
            hud.SabotageButton.ToggleVisible(false);
        }
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} : {(hack ? "ハックモード" : "タスクモード")}", "NiceHacker");
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    private void SetRoleForClient(PlayerControl target, RoleTypes rt, int clientId)
    {
        if (Player == PlayerControl.LocalPlayer)
        {
            if (target == PlayerControl.LocalPlayer) RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, rt);
            return;
        }
        target.RpcSetRoleDesync(rt, clientId);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = 1f;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    public override bool CheckShapeshift(PlayerControl target, ref bool shouldAnimate)
    {
        shouldAnimate = false;
        return false;
    }

    public override bool NotifyRolesCheckOtherName => true;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player != Player || !Player.IsAlive()) return;
        if (GameStates.Intro || GameStates.CalledMeeting || !GameStates.IsInTask) return;
        if (Time.time < nextRecordAt) return;
        nextRecordAt = Time.time + RecordInterval;

        var text = BuildAdmin(out var perPlayer);
        history.Enqueue((Time.time, text));
        while (history.Count > 0 && Time.time - history.Peek().time > 31f) history.Dequeue();

        if (!hackMode) return;
        nameAdmin.Clear();
        foreach (var (id, line) in perPlayer) nameAdmin[id] = line;
        if (text != lastText)
        {
            lastText = text;
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
    }

    // 今のアドミンを文にする。perPlayer には、シェイプシフト画面で名前の下に出す行を入れる
    private static string BuildAdmin(out List<(byte, string)> perPlayer)
    {
        perPlayer = new();
        var admins = AdminProvider.CalculateAdmin();
        var builder = new StringBuilder(256);
        var message = new StringBuilder(512);
        var list = PlayerCatch.AllAlivePlayerControls.OrderBy(x => x.PlayerId)
            .Concat(PlayerCatch.AllPlayerControls.Where(x => !x.IsAlive()).OrderBy(x => x.PlayerId)).ToArray();
        var index = 0;
        foreach (var entry in admins.Values)
        {
            if (entry.TotalPlayers <= 0) continue;
            builder.Append(DestroyableSingleton<TranslationController>.Instance.GetString(entry.Room));
            builder.Append(": ").Append(entry.TotalPlayers);
            if (canSeeDeadBody && entry.NumDeadBodies > 0)
                builder.Append('(').Append(Translator.GetString("Deadbody")).Append('×').Append(entry.NumDeadBodies).Append(')');
            message.Append(builder).Append('\n');
            if (index < list.Length) perPlayer.Add((list[index].PlayerId, builder.ToString()));
            builder.Clear();
            index++;
        }
        return message.ToString();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        nameAdmin.Clear();
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || !hackMode || remaining <= 0) return;
        if (AddOns.Common.Amnesia.CheckAbilityreturn(Player)) return;
        remaining--;
        // 設定した秒数前に一番近い記録を使う(記録がなければ今のアドミン)
        var want = Time.time - secondsBefore;
        string text = null;
        foreach (var (time, t) in history)
        {
            if (time > want) break;
            text = t;
        }
        text ??= history.Count > 0 ? history.Peek().text : BuildAdmin(out _);
        if (string.IsNullOrEmpty(text)) text = Translator.GetString("NiceHackerNoOne");
        var title = Utils.ColorString(RoleInfo.RoleColor, string.Format(Translator.GetString("NiceHackerAdminTitle"), secondsBefore));
        var playerId = Player.PlayerId;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsInGame) return;
            Utils.SendMessage(text, playerId, title);
            MeetingHudPatch.StartPatch.meetingsends.Add((playerId, text, title));
        }, 4f, "NiceHacker Admin Message");
    }

    public override void AfterMeetingTasks()
    {
        history.Clear();
        if (hackMode && Player.IsAlive()) SetMode(false);
        else hackMode = false;
    }

    public override RoleTypes? AfterMeetingRole => RoleTypes.Crewmate;

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer != Player || !hackMode || isForMeeting) return "";
        // ホストがナイスハッカーの時は、シェイプシフトの画面を開いている間だけ出す
        // (いつも出すと、マップの上のみんなの名前の下にずっと部屋が出ていた)
        if (Player == PlayerControl.LocalPlayer && Minigame.Instance?.TryCast<ShapeshifterMinigame>() == null) return "";
        if (!nameAdmin.TryGetValue(seen.PlayerId, out var admin)) return "";
        return "<color=#C2FF00><size=1.5>" + admin + "</color></size>";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        var mode = hackMode ? Translator.GetString("NiceHackerHackMode") : Translator.GetString("NiceHackerTaskMode");
        return Utils.ColorString(remaining > 0 ? RoleInfo.RoleColor : Color.gray, $" [{remaining}] {mode}");
    }

    public override string GetAbilityButtonText() => Translator.GetString("NiceHackerAbilityButtonText");
}
