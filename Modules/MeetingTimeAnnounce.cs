using HarmonyLib;

namespace TownOfHost.Modules;

public static class ExtraFeatureOptions
{
    public static OptionItem SendRuleOnJoin;
    public static OptionItem JoinByCommentOnly;
    public static OptionItem ShowStreamComments;
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
public static class MeetingTimeAnnounce
{
    public static OptionItem Enabled;

    private static readonly int[] Marks = { 30, 10 };
    private static MeetingHud announcedFor;
    private static int nextMarkIndex;

    public static void Postfix(MeetingHud __instance)
    {
        try
        {
            if (Enabled?.GetBool() != true || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (__instance == null || GameStates.ExiledAnimate) return; // 投票が終わって結果を出している間は出さない
            if (announcedFor != __instance) { announcedFor = __instance; nextMarkIndex = 0; }
            var votingTime = Main.NormalOptions.VotingTime;
            if (votingTime <= 0 || nextMarkIndex >= Marks.Length) return;
            // MeetingVoteManager と同じ計算: 話し合いの時間が過ぎてからの経過で残りを出す
            var remaining = votingTime - (__instance.discussionTimer - Main.NormalOptions.DiscussionTime);
            // 最初から30秒より短い時などは、もう過ぎた区切りを飛ばす
            while (nextMarkIndex < Marks.Length && remaining <= Marks[nextMarkIndex] - 1) nextMarkIndex++;
            if (nextMarkIndex >= Marks.Length || remaining > Marks[nextMarkIndex]) return;
            Utils.SendMessage($"投票の残り時間はあと{Marks[nextMarkIndex]}秒です。", byte.MaxValue, "<#ffd36e>【残り時間】</color>");
            nextMarkIndex++;
        }
        catch (System.Exception e)
        {
            Logger.Warn($"残り時間のお知らせに失敗: {e.Message}", "MeetingTimeAnnounce");
        }
    }
}
