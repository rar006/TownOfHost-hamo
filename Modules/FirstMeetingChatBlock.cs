using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Patches;

namespace TownOfHost.Modules;

/// <summary>
/// 「開始後、緊急会議を発動させる」の初手会議で、チャットを一定時間(または会議終了まで)使えなくする。
/// ・MOD導入者(ホスト含む): 自分のMODが送信を止める。届いたMOD未導入者のチャットも表示しない
/// ・MOD未導入者: 「試合中チャットコマンドを非表示にする」と同じ仕組みで、その人の画面でだけ
///   他の全員を死亡扱いにする。バニラは死者のチャットを生存者に見せないので、お互いのチャットが届かなくなる。
///   (チャットを連投して流す方法と違い、公式サーバーでも使える)
/// 「/」から始まるコマンドは止めない。
/// </summary>
public static class FirstMeetingChatBlock
{
    /// <summary>設定の一番上の値。これを選ぶと「会議終了時まで」</summary>
    public const int UntilEnd = 1000;

    static bool active;
    static float startedAt;
    /// <summary>MOD未導入者の画面を「他の全員が死亡」にしているか</summary>
    static bool hiding;

    static int Seconds => Options.FirstTurnMeetingChatBlock?.GetInt() ?? 0;

    /// <summary>今チャットを止めているか</summary>
    public static bool IsBlocking
        => active && GameStates.IsMeeting && (Seconds >= UntilEnd || Time.time - startedAt < Seconds);

    /// <summary>止める対象のメッセージか(コマンドは通す)</summary>
    public static bool ShouldBlock(string text)
        => IsBlocking && !string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith("/");

    static string Notice => Seconds >= UntilEnd
        ? Translator.GetString("FirstMeetingChatBlockedUntilEnd")
        : string.Format(Translator.GetString("FirstMeetingChatBlockedSeconds"), Seconds);

    /// <summary>自分(MOD導入者)が送ろうとして止めた時に、自分にだけ出す</summary>
    public static void ShowLocalNotice()
    {
        try { HudManager.Instance?.Chat?.AddChat(PlayerControl.LocalPlayer, Notice); } catch { }
    }

    static bool CanUseDeadTrick => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost
        && !AntiBlackout.IsSet && !AntiBlackout.IsCached && !Roles.Impostor.Assassin.NowUse;

    /// <summary>MOD未導入者それぞれの画面で、ホスト以外の全員を死亡扱いにする(ホストのみ)</summary>
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

    /// <summary>秒数指定で時間が来た時: 本当の生死に戻す(ホストのみ)</summary>
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

    /// <summary>
    /// 生死を送り直すと、名前も一緒に送られて「会議中の見え方(役職の色を隠した名前)」が上書きされ、
    /// 他の人の役職の色が見えてしまう。送り直した後に、会議中の名前をもう一度送る。
    /// (会議開始直後は、もともと7秒後に名前を送るので、その前なら何もしない)
    /// </summary>
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

/// <summary>0=OFF / 1〜999秒 / 1000=会議終了時まで の設定</summary>
public sealed class ChatBlockSecondsOptionItem : IntegerOptionItem
{
    public ChatBlockSecondsOptionItem(int id, string name, int defaultValue, TabGroup tab)
        : base(id, name, defaultValue, tab, false, new(0, FirstMeetingChatBlock.UntilEnd, 1)) { }

    string Label(string normal)
        => GetInt() >= FirstMeetingChatBlock.UntilEnd ? Translator.GetString("FirstMeetingChatBlockUntilEndValue") : normal;

    public override string GetString() => Label(base.GetString());
    public override string GetValueString(bool coloroff) => Label(base.GetValueString(coloroff));
}
