using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

public sealed class DarkSheriff : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(DarkSheriff),
            player => new DarkSheriff(player),
            CustomRoles.DarkSheriff,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            77150,
            SetupOptionItem,
            "dsh",
            "#f8cd46",
            OptionSort: (7, 1),
            from: From.TownOfHost_hamo
        );

    public DarkSheriff(PlayerControl player) : base(RoleInfo, player)
    {
        Awakened = false;
        CurrentKillCooldown = OptionKillCooldownBefore.GetFloat();
    }

    static OptionItem OptionKillCooldownBefore;
    static OptionItem OptionKillCooldownAfter;
    static OptionItem OptionAwakenKillCount;
    static OptionItem OptionCanVentAfterAwaken;

    enum OptionName
    {
        DarkSheriffAwakenKillCount,
        DarkSheriffKillCooldownAfter,
        DarkSheriffCanVentAfterAwaken,
    }

    static void SetupOptionItem()
    {
        OptionKillCooldownBefore = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAwakenKillCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.DarkSheriffAwakenKillCount, new(1, 15, 1), 3, false);
        OptionKillCooldownAfter = FloatOptionItem.Create(RoleInfo, 12, OptionName.DarkSheriffKillCooldownAfter, new(0f, 180f, 0.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanVentAfterAwaken = BooleanOptionItem.Create(RoleInfo, 13, OptionName.DarkSheriffCanVentAfterAwaken, true, false);
    }

    public bool Awakened;
    float CurrentKillCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        AURoleOptions.KillCooldown = CurrentKillCooldown;
    }

    public float CalculateKillCooldown() => CurrentKillCooldown;

    /// <summary>
    /// 本人には常に通常のシェリフとして役職名を表示する。
    /// ダークシェリフという内部名はゲーム進行中の自己認識・イントロには出さない。
    /// </summary>
    public override void OverrideTrueRoleName(ref UnityEngine.Color roleColor, ref string roleText)
    {
        roleColor = UtilsRoleText.GetRoleColor(CustomRoles.Sheriff);
        roleText = GetString(nameof(CustomRoles.Sheriff));
    }

    // HELP／マイロール表示でも自分をシェリフとして認識する。
    public override CustomRoles Misidentify() => CustomRoles.Sheriff;

    public bool CanUseSabotageButton() => false;

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (info.DoKill && info.AttemptKiller != info.AttemptTarget)
        {
            var kills = Player.GetPlayerState().GetKillCount();
            if (!Awakened && kills >= OptionAwakenKillCount.GetInt())
            {
                Awaken();
            }
            SendRpc();
        }
    }

    void Awaken()
    {
        Awakened = true;
        CurrentKillCooldown = OptionKillCooldownAfter.GetFloat();
        AURoleOptions.KillCooldown = CurrentKillCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);
        Player.MarkDirtySettings();
    }

    public override bool CanClickUseVentButton => !Awakened || OptionCanVentAfterAwaken.GetBool();

    /// <summary>
    /// 自覚後、生存者が残り2人(1人)になったらダークシェリフの単独勝利。
    /// 他の陣営の勝利判定には一切干渉しない独立チェック。
    /// </summary>
    public static bool CheckWin(ref GameOverReason reason)
    {
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc.GetRoleClass() is not DarkSheriff darkSheriff) continue;
            if (!darkSheriff.Awakened) continue;
            if (!pc.IsAlive()) continue;

            var aliveCount = PlayerCatch.AllAlivePlayerControls.Count();
            if (aliveCount > 2) continue;

            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.DarkSheriff, pc.PlayerId))
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(pc.PlayerId);
                reason = GameOverReason.ImpostorsByKill;
                return true;
            }
        }
        return false;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        // 本人へ「私は正義のシェリフだ…」などの自己紹介文は表示しない。
        return "";
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(Awakened);
        sender.Writer.Write(CurrentKillCooldown);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        Awakened = reader.ReadBoolean();
        CurrentKillCooldown = reader.ReadSingle();
    }
}
