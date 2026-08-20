using System.Collections.Generic;

using AmongUs.GameOptions;

using Hazel;

using HarmonyLib;



using TownOfHost.Roles.Core;

using TownOfHost.Roles.Core.Interfaces;



namespace TownOfHost.Roles.Crewmate;



// ===== タイプライター (Typewriter) =====

// From: TownOfHost_hamo

// イントロ：カタカタカタ...

// 陣営：クルーメイト / 置き換え：クルーメイト

//

// 2回目以降の会議が始まるたびに、「前回の会議でタイプライターが何回チャットしたか」を公開する。

// 設定により、生存して一定ターン数が経過すると単独勝利できる。

// また、会議にて設定票数だけ投票されると、投票結果に関わらず強制追放されてしまう。

public sealed class Typewriter : RoleBase, IAdditionalWinner

{

    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(

            typeof(Typewriter),

            player => new Typewriter(player),

            CustomRoles.Typewriter,

            () => RoleTypes.Crewmate,

            CustomRoleTypes.Crewmate,

            39500,

            SetupOptionItem,

            "twr",

            "#00FFE4",

            (1, 15),

            from: From.TownOfHost_hamo

        );



    public Typewriter(PlayerControl player) : base(RoleInfo, player)

    {

        needTurnsToWin = OptionWinNeedsTurns.GetBool();

        turnsNeeded = OptionTurnsNeeded.GetInt();

        forceExileVotes = OptionForceExileVotes.GetInt();

    }



    static OptionItem OptionWinNeedsTurns;

    static OptionItem OptionTurnsNeeded;

    static OptionItem OptionForceExileVotes;

    private static OverrideTasksData Tasks;



    enum OptionName

    {

        TypewriterWinNeedsTurns,

        TypewriterTurnsNeeded,

        TypewriterForceExileVotes,

    }



    private readonly bool needTurnsToWin;

    private readonly int turnsNeeded;

    private readonly int forceExileVotes;



    // 現在の会議でのチャット回数(まだ会議は終わっていない=未確定分)

    private int currentMeetingChatCount;

    // 直前に終了した会議でのチャット回数(次の会議開始時に公開する値)

    private int lastMeetingChatCount;

    // 経過した会議の回数(単独勝利判定に使用)

    private int meetingsPassed;



    static void SetupOptionItem()

    {

        OptionWinNeedsTurns = BooleanOptionItem.Create(RoleInfo, 10, OptionName.TypewriterWinNeedsTurns, true, false);

        OptionTurnsNeeded = IntegerOptionItem.Create(RoleInfo, 11, OptionName.TypewriterTurnsNeeded,

            new(1, 99, 1), 4, false)

            .SetValueFormat(OptionFormat.Times)

            .SetParent(OptionWinNeedsTurns);

        OptionForceExileVotes = IntegerOptionItem.Create(RoleInfo, 12, OptionName.TypewriterForceExileVotes,

            new(1, 15, 1), 4, false)

            .SetValueFormat(OptionFormat.Votes);

        // 単独勝利の優先度(専用ヘルパーで登録)

        SoloWinOption.Create(RoleInfo, 13, defo: 40);

        // タスク置き換え(レジェンドスターと同じ0/99/1形式: オン/オフ, 通常, ロング, ショート)

        Tasks = OverrideTasksData.Create(RoleInfo, 14, tasks: (false, 4, 3, 4));

    }



    /// <summary>チャット送信を検知した時に呼ぶ(TypewriterChatPatchから)</summary>

    public void OnChatSent()

    {

        currentMeetingChatCount++;

    }



    // 会議開始時、2回目以降の会議であれば前回の会議のチャット回数を公開する

    public override void OnStartMeeting()

    {

        if (!AmongUsClient.Instance.AmHost) return;



        meetingsPassed++;



        if (lastMeetingChatCount >= 0 && meetingsPassed > 1)

        {

            // タイプライター本人の名前は絶対に含めない(公開すると本人特定に繋がるため)。

            // システムメッセージとして回数のみを匿名で公開する。

            Utils.SendMessage(

                string.Format(GetString("Typewriter.ChatCountAnnounce"), lastMeetingChatCount));

        }

    }



    // 会議終了時、今回の会議でのチャット回数を「前回分」として確定し、次回開示用にストックする。

    // 強制追放の判定もここで行う(設定票数以上の投票を集めていた場合、結果に関わらず追放)。

    public override void AfterMeetingTasks()

    {

        if (!AmongUsClient.Instance.AmHost) return;



        lastMeetingChatCount = currentMeetingChatCount;

        currentMeetingChatCount = 0;



        UtilsGameLog.AddGameLog("Typewriter",

            $"{UtilsName.GetPlayerColor(Player)}が今回の会議でチャットした回数: {lastMeetingChatCount}");

    }



    /// <summary>

    /// 投票集計の結果を上書きするフック。

    /// 自分への投票数が設定値以上集まっていれば、天秤(同数)中であっても

    /// 結果に関わらず自分自身を追放対象にする(強制追放)。

    /// </summary>

    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)

    {

        if (!Player.IsAlive()) return false;

        if (!vote.TryGetValue(Player.PlayerId, out var votesAgainstMe)) return false;

        if (votesAgainstMe < forceExileVotes) return false;



        Exiled = GameData.Instance.GetPlayerById(Player.PlayerId);

        IsTie = false;



        UtilsGameLog.AddGameLog("Typewriter",

            $"{UtilsName.GetPlayerColor(Player)}が{votesAgainstMe}票を集めたため、結果に関わらず強制追放された");



        return true;

    }



    public bool CheckWin(ref CustomRoles winnerRole)

    {

        if (Player?.IsAlive() != true) return false;

        if (!needTurnsToWin) return false;

        if (meetingsPassed < turnsNeeded) return false;



        winnerRole = CustomRoles.Typewriter;

        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate)

            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Typewriter, Player.PlayerId, true);



        return true;

    }

}



// ===== チャット送信の検知パッチ =====

// PlayerControl.HandleRpc を経由するすべてのチャット送信RPCを監視し、

// 送信者がタイプライターであれば OnChatSent() を呼ぶ。

// (自分自身の送信もホストの受信ハンドラを通るため、これで全プレイヤー分を正確に検知できる)

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]

public static class TypewriterChatPatch

{

    public static void Postfix(PlayerControl __instance, byte callId, MessageReader reader)

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (__instance == null) return;

        if (callId != (byte)RpcCalls.SendChat) return;

        if (!__instance.IsAlive()) return;

        if (!__instance.Is(CustomRoles.Typewriter)) return;



        if (__instance.GetRoleClass() is Typewriter typewriter)

            typewriter.OnChatSent();

    }

}

