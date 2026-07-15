using AmongUs.GameOptions;
using Hazel;
using UnityEngine;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== ヌル (Null) =====
// From: TownOfHost_hamo
// イントロ：1-1=0
// 陣営：ニュートラル / 置き換え：クルーメイト
//
// 対称的な2段階の勝利条件を持つ第三陣営。
// フェーズ1: 誰か他プレイヤーが近くにいる状態が累計で設定秒数(既定100秒)続くと、フェーズ2へ移行。
// フェーズ2: 誰も近くにいない状態が累計で設定秒数(既定100秒)続くと、単独勝利。
public sealed class Null : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Null),
            player => new Null(player),
            CustomRoles.Null,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            56500,
            SetupOptionItem,
            "nul",
            "#ffffff",
            (6, 5),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public Null(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        TogetherRange = OptionTogetherRange.GetFloat();
        AloneRange = OptionAloneRange.GetFloat();
        TogetherTimeNeeded = OptionTogetherTimeNeeded.GetFloat();
        AloneTimeNeeded = OptionAloneTimeNeeded.GetFloat();

        TogetherTimer = 0f;
        AloneTimer = 0f;
        Phase = 1;
    }

    static OptionItem OptionTogetherRange;
    static OptionItem OptionAloneRange;
    static OptionItem OptionTogetherTimeNeeded;
    static OptionItem OptionAloneTimeNeeded;

    enum OptionName
    {
        NullTogetherRange,
        NullAloneRange,
        NullTogetherTimeNeeded,
        NullAloneTimeNeeded
    }

    private float TogetherRange;
    private float AloneRange;
    private float TogetherTimeNeeded;
    private float AloneTimeNeeded;

    // 1 = 「誰かと一緒」フェーズ, 2 = 「誰もいない」フェーズ, 3 = 達成(勝利確定)
    public int Phase;
    private float TogetherTimer;
    private float AloneTimer;

    private static void SetupOptionItem()
    {
        OptionTogetherRange = FloatOptionItem.Create(RoleInfo, 10, OptionName.NullTogetherRange, new(0.25f, 5.0f, 0.25f), 1.0f, false);
        OptionAloneRange = FloatOptionItem.Create(RoleInfo, 11, OptionName.NullAloneRange, new(0.25f, 5.0f, 0.25f), 2.0f, false);
        OptionTogetherTimeNeeded = FloatOptionItem.Create(RoleInfo, 12, OptionName.NullTogetherTimeNeeded, new(5f, 300f, 5f), 100f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAloneTimeNeeded = FloatOptionItem.Create(RoleInfo, 13, OptionName.NullAloneTimeNeeded, new(5f, 300f, 5f), 100f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt) { }

    private enum RPC_type
    {
        SetPhase
    }
    private void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)RPC_type.SetPhase);
        sender.Writer.Write((byte)Phase);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        reader.ReadByte(); // RPC_type (現状SetPhaseのみ)
        Phase = reader.ReadByte();
    }

    // 判定はAndroidのバッテリーと同様、各クライアントでローカルに行う (表示が固まるのを防ぐため)。
    // 実際にフェーズが進む/勝利する副作用のみホスト限定。
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!GameStates.IsInTask) return;
        if (!Player.IsAlive()) return;
        if (Phase == 3) return;

        bool someoneNearby;
        if (Phase == 1)
        {
            someoneNearby = false;
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc == null || pc == Player || !pc.IsAlive()) continue;
                if (Vector2.Distance(Player.transform.position, pc.transform.position) <= TogetherRange)
                {
                    someoneNearby = true;
                    break;
                }
            }

            if (someoneNearby)
            {
                TogetherTimer += Time.fixedDeltaTime;
                if (TogetherTimer >= TogetherTimeNeeded)
                {
                    Phase = 2;
                    if (AmongUsClient.Instance.AmHost) SendRPC();
                }
            }
        }
        else if (Phase == 2)
        {
            someoneNearby = false;
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc == null || pc == Player || !pc.IsAlive()) continue;
                if (Vector2.Distance(Player.transform.position, pc.transform.position) <= AloneRange)
                {
                    someoneNearby = true;
                    break;
                }
            }

            if (!someoneNearby)
            {
                AloneTimer += Time.fixedDeltaTime;
                if (AloneTimer >= AloneTimeNeeded)
                {
                    Phase = 3;
                    if (AmongUsClient.Instance.AmHost) SendRPC();
                }
            }
        }
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || !Is(seen)) return "";
        if (isForMeeting) return "";

        string text = Phase switch
        {
            1 => $"{GetString("NullPhase1Text")}: {(int)TogetherTimer}/{(int)TogetherTimeNeeded}",
            2 => $"{GetString("NullPhase2Text")}: {(int)AloneTimer}/{(int)AloneTimeNeeded}",
            _ => GetString("NullPhase3Text"),
        };
        return Utils.ColorString(RoleInfo.RoleColor, text);
    }

    // フェーズ3(達成)に到達していれば勝利
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;
        if (Phase != 3) return false;

        winnerRole = CustomRoles.Null;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Null)
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Null, Player.PlayerId, true);

        return true;
    }
}
