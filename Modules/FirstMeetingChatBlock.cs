using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Patches;

namespace TownOfHost.Modules;

public static class FirstMeetingChatBlock
{
    public const int UntilEnd = 1000;

    static bool active;
    static float startedAt;
    static bool hiding;

    static int Seconds => Options.FirstTurnMeetingChatBlock?.GetInt() ?? 0;

    public static bool IsBlocking
        => active && GameStates.IsMeeting && (Seconds >= UntilEnd || Time.time - startedAt < Seconds);

    public static bool ShouldBlock(string text)
        => IsBlocking && !string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith("/");

    static string Notice => Seconds >= UntilEnd
        ? Translator.GetString("FirstMeetingChatBlockedUntilEnd")
        : string.Format(Translator.GetString("FirstMeetingChatBlockedSeconds"), Seconds);

    public static void ShowLocalNotice()
    {
        try { HudManager.Instance?.Chat?.AddChat(PlayerControl.LocalPlayer, Notice); } catch { }
    }

    static bool CanUseDeadTrick => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost
        && !AntiBlackout.IsSet && !AntiBlackout.IsCached && !Roles.Impostor.Assassin.NowUse;

    public static void HideOthersForVanilla()
    {
        if (!CanUseDeadTrick) return;
        var state = new Dictionary<byte, bool>();
        foreach (var p in PlayerCatch.AllPlayerControls) if (p?.Data != null) state[p.PlayerId] = p.Data.IsDead;
        GameDataSerializePatch.SerializeMessageCount++;
        try
        {
            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId || pc.IsModClient()) continue;
                foreach (var tg in PlayerCatch.AllAlivePlayerControls)
                {
                    if (tg.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue; // ホストの発言(お知らせ)は届くように
                    tg.Data.IsDead = true;
                }
                pc.Data.IsDead = false;
                RPC.RpcSyncAllNetworkedPlayer(pc.GetClientId());
            }
        }
        finally
        {
            GameDataSerializePatch.SerializeMessageCount--;
            foreach (var p in PlayerCatch.AllPlayerControls)
                if (p?.Data != null && state.TryGetValue(p.PlayerId, out var dead)) p.Data.IsDead = dead;
        }
        hiding = true;
        ResendMeetingNames(0.3f);
        Logger.Info("初手会議のチャット無効化: MOD未導入者の画面で他の全員を死亡扱いにしました", "FirstMeetingChatBlock");
    }

    static void RestoreForVanilla()
    {
        if (!hiding) return;
        hiding = false;
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsMeeting) return;
        // 「試合中チャットコマンドを非表示にする」が効いている時は、その状態のままにする
        if (Options.ExHideChatCommand.GetBool() && !Utils.IsRestriction()) { ChatManager.ChatManager.StratMeetingSetDead(); ResendMeetingNames(1.0f); return; }
        if (AntiBlackout.IsSet || AntiBlackout.IsCached) return;
        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId || pc.IsModClient()) continue;
            RPC.RpcSyncAllNetworkedPlayer(pc.GetClientId());
        }
        ResendMeetingNames(0.3f);
        Utils.SendMessage(Translator.GetString("FirstMeetingChatUnblocked"));
        Logger.Info("初手会議のチャット無効化: 時間が来たので元に戻しました", "FirstMeetingChatBlock");
    }

    public static void ResendMeetingNames(float delay)
        => _ = new LateTask(() =>
        {
            if (GameStates.IsMeeting && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
                NameColorManager.RpcMeetingColorName();
        }, delay, "FirstMeetingChatBlock.ResendNames", true);

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class StartPatch
    {
        public static void Postfix()
        {
            active = Options.firstturnmeeting && MeetingStates.First && Seconds > 0;
            startedAt = Time.time;
            hiding = false;
            if (!active || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            // 投票欄が作られた後に行う(会議開始の演出と重ならないように)
            _ = new LateTask(() => { if (IsBlocking) HideOthersForVanilla(); }, 0.6f, "FirstMeetingChatBlock.Hide", true);
            _ = new LateTask(() => { if (GameStates.IsMeeting) Utils.SendMessage(Notice); }, 1.5f, "FirstMeetingChatBlock.Notice", true);
            if (Seconds < UntilEnd)
                _ = new LateTask(RestoreForVanilla, Seconds, "FirstMeetingChatBlock.Restore", true);
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    static class EndPatch
    {
        public static void Postfix() { active = false; hiding = false; }
    }

    // チャットを止めている間は「〇〇が投票しました」も出さない(投票した順番から推理できてしまうため)。
    // ホストが全員に送る通知(RpcSendChatNote)と、自分の画面に出す通知(AddChatNote)の両方を止める。
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChatNote))]
    static class RpcSendChatNotePatch
    {
        public static bool Prefix([HarmonyArgument(1)] ChatNoteTypes noteType)
            => !(noteType == ChatNoteTypes.DidVote && IsBlocking);
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChatNote))]
    static class AddChatNotePatch
    {
        public static bool Prefix([HarmonyArgument(1)] ChatNoteTypes noteType)
            => !(noteType == ChatNoteTypes.DidVote && IsBlocking);
    }
}

public sealed class ChatBlockSecondsOptionItem : IntegerOptionItem
{
    public ChatBlockSecondsOptionItem(int id, string name, int defaultValue, TabGroup tab)
        : base(id, name, defaultValue, tab, false, new(0, FirstMeetingChatBlock.UntilEnd, 1)) { }

    string Label(string normal)
        => GetInt() >= FirstMeetingChatBlock.UntilEnd ? Translator.GetString("FirstMeetingChatBlockUntilEndValue") : normal;

    public override string GetString() => Label(base.GetString());
    public override string GetValueString(bool coloroff) => Label(base.GetValueString(coloroff));
}
