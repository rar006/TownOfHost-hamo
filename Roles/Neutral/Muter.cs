using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== ミューター (Muter) =====
// イントロ：【...】
// 陣営：ニュートラル / 置き換え：クルーメイト
//
// 勝利条件：試合終了時に生存していれば単独勝利。
// 1回の会議で設定回数を超えてチャットを行うと死亡する。
// 上限を超えてチャットすると、チャッターやゲッサーと同様の死亡ログが流れる(死因は「自殺」)。
public sealed class Muter : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Muter),
            player => new Muter(player),
            CustomRoles.Muter,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            96000,
            SetupOptionItem,
            "Mut",
            "#A77AFF",
            (5, 0),
            from: From.TownOfHost_hamo
        );

    public Muter(PlayerControl player) : base(RoleInfo, player)
    {
        chatLimit = OptionChatLimit.GetInt();
        countSecretChat = OptionCountSecretChat.GetBool();
        chatCountThisMeeting = 0;
        hasDiedThisMeeting = false;
    }

    static OptionItem OptionChatLimit;
    static OptionItem OptionCountSecretChat;

    enum OptionName
    {
        MuterChatLimit,
        MuterCountSecretChat,
    }

    private readonly int chatLimit;
    private readonly bool countSecretChat;

    // 現在の会議でのチャット回数
    private int chatCountThisMeeting;
    // 上限超過による死亡処理を、1会議中に二重で行わないためのフラグ
    private bool hasDiedThisMeeting;

    static void SetupOptionItem()
    {
        OptionChatLimit = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MuterChatLimit,
            new(1, 250, 1), 12, false)
            .SetValueFormat(OptionFormat.Times);
        OptionCountSecretChat = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MuterCountSecretChat, true, false);

        // 単独勝利の優先度(専用ヘルパーで登録)
        SoloWinOption.Create(RoleInfo, 12, defo: 40);
    }

    /// <summary>チャット送信を検知した時に呼ぶ(MuterChatPatchから)</summary>
    /// <param name="isSecretChat">秘匿チャット(インポスター間チャットなど)かどうか</param>
    public void OnChatSent(bool isSecretChat)
    {
        if (!Player.IsAlive() || hasDiedThisMeeting) return;
        if (isSecretChat && !countSecretChat) return;

        chatCountThisMeeting++;

        if (chatCountThisMeeting > chatLimit)
        {
            hasDiedThisMeeting = true;
            MyState.DeathReason = CustomDeathReason.Suicide;
            Player.SetRealKiller(Player);
            Player.RpcMurderPlayer(Player);

            UtilsGameLog.AddGameLog("Muter",
                $"{UtilsName.GetPlayerColor(Player)}がチャット上限({chatLimit}回)を超えて死亡した");
        }
    }

    public override void OnStartMeeting()
    {
        chatCountThisMeeting = 0;
        hasDiedThisMeeting = false;
    }

    /// <summary>「/cmd mc」コマンドで、自分の残りチャット回数を確認できるようにする。
    /// (「/cm」は既存の「/callmeeting」と衝突するため「/mc」を使う)</summary>
    public static void HandleRemainingChatCommand(PlayerControl sender, string[] args)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (sender == null) return;

        if (sender.GetRoleClass() is not Muter muter)
        {
            Utils.SendMessage(GetString("Muter.NotMuter"), sender.PlayerId);
            return;
        }

        var remaining = System.Math.Max(0, muter.chatLimit - muter.chatCountThisMeeting);
        Utils.SendMessage(string.Format(GetString("Muter.RemainingChatCount"), remaining), sender.PlayerId);
    }

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;

        winnerRole = CustomRoles.Muter;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate)
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Muter, Player.PlayerId, true);

        return true;
    }

    // 要望により、名前の上に自動でチャット残り回数を表示する機能は廃止した。
    // 「/cmd mc」コマンドで自分から確認する方式に一本化する。

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}

// ===== チャット送信の検知パッチ =====
// PlayerControl.HandleRpc を経由するすべてのチャット送信RPCを監視し、
// 送信者がミューターであれば OnChatSent() を呼ぶ。
[HarmonyLib.HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
public static class MuterChatPatch
{
    public static void Postfix(PlayerControl __instance, byte callId, MessageReader reader)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (__instance == null) return;
        if (__instance.GetCustomRole() != CustomRoles.Muter) return;

        var isSecretChat = callId == (byte)RpcCalls.SendChatNote;
        if (callId != (byte)RpcCalls.SendChat && !isSecretChat) return;
        if (!__instance.IsAlive()) return;

        if (__instance.GetRoleClass() is Muter muter)
            muter.OnChatSent(isSecretChat);
    }
}
