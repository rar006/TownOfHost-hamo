using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== スリーパー (Sleeper) =====
// From: TownOfHost_hamo
// イントロ：zzz.....
// 陣営：ニュートラル / 置き換え：エンジニア
// 時間経過で睡魔ゲージが溜まっていき、100になると眠ってしまう(永眠)。
// ベントに入ることで「眠る」ことができ、睡魔ゲージを一定量減らせる。
// 生存していれば単独勝利(オプションで追加勝利化も可能)。
public sealed class Sleeper : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Sleeper),
            player => new Sleeper(player),
            CustomRoles.Sleeper,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Neutral,
            56000,
            SetupOptionItem,
            "slp",
            "#7a6ff0",
            (6, 4),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public Sleeper(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        GaugeIntervalSeconds = OptionGaugeSpeed.GetInt();
        VentDecreaseAmount = OptionVentDecrease.GetInt();
        PlayVentAnimation = OptionPlayVentAnimation.GetBool();
        AddWinOnly = OptionAddWinOnly.GetBool();

        SleepGauge = 0f;
        elapsed = 0f;
        IsAsleepForever = false;
    }

    public static OptionItem OptionGaugeSpeed;
    public static OptionItem OptionVentDecrease;
    public static OptionItem OptionPlayVentAnimation;
    public static OptionItem OptionAddWinOnly;

    enum OptionName
    {
        SleeperGaugeSpeed,
        SleeperVentDecrease,
        SleeperPlayVentAnimation,
        SleeperAddWinOnly
    }

    private static int GaugeIntervalSeconds;
    private static int VentDecreaseAmount;
    private static bool PlayVentAnimation;
    private static bool AddWinOnly;

    // 睡魔ゲージ (0~100)。100で永眠。
    public float SleepGauge;
    private float elapsed;
    public bool IsAsleepForever;

    private static void SetupOptionItem()
    {
        OptionGaugeSpeed = IntegerOptionItem.Create(RoleInfo, 10, OptionName.SleeperGaugeSpeed, new(1, 100, 1), 5, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVentDecrease = IntegerOptionItem.Create(RoleInfo, 11, OptionName.SleeperVentDecrease, new(10, 100, 10), 70, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionPlayVentAnimation = BooleanOptionItem.Create(RoleInfo, 12, OptionName.SleeperPlayVentAnimation, true, false);
        OptionAddWinOnly = BooleanOptionItem.Create(RoleInfo, 13, OptionName.SleeperAddWinOnly, false, false);
    }

    public override void ApplyGameOptions(IGameOptions opt) { }

    private enum RPC_type
    {
        SetGauge,
        SetAsleepForever
    }
    private void SendRPC(RPC_type type)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)type);
        switch (type)
        {
            case RPC_type.SetGauge:
                sender.Writer.Write(SleepGauge);
                break;
            case RPC_type.SetAsleepForever:
                sender.Writer.Write(IsAsleepForever);
                break;
        }
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        var type = (RPC_type)reader.ReadByte();
        switch (type)
        {
            case RPC_type.SetGauge:
                SleepGauge = reader.ReadSingle();
                break;
            case RPC_type.SetAsleepForever:
                IsAsleepForever = reader.ReadBoolean();
                break;
        }
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        // ゲージ自体の計算はAndroidのバッテリーと同様、各クライアントでローカルに行う
        // (ホスト限定にすると、スリーパー本人が非ホストの場合にRPC同期タイミング次第で
        //  表示が更新されないことがあるため)。実際の死亡処理(副作用)のみホスト限定にする。
        if (!GameStates.IsInTask) return;
        if (!Player.IsAlive()) return;
        if (IsAsleepForever) return;

        elapsed += UnityEngine.Time.fixedDeltaTime;
        if (elapsed < GaugeIntervalSeconds) return;

        elapsed = 0f;
        SleepGauge = System.Math.Min(100f, SleepGauge + 10f);
        if (AmongUsClient.Instance.AmHost) SendRPC(RPC_type.SetGauge);

        if (SleepGauge >= 100f && AmongUsClient.Instance.AmHost)
            FallAsleepForever();
    }

    // ベントに入る = 眠る。ゲージを減らす。
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!AmongUsClient.Instance.AmHost) return PlayVentAnimation;
        if (!Player.IsAlive() || IsAsleepForever) return PlayVentAnimation;

        SleepGauge = System.Math.Max(0f, SleepGauge - VentDecreaseAmount);
        SendRPC(RPC_type.SetGauge);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が眠った (残ゲージ {SleepGauge})", "Sleeper");

        // falseを返すとベントから追い出されアニメーションも見せない = 「アニメーションを再生しない」設定に対応
        return PlayVentAnimation;
    }

    // 睡魔ゲージが100になった = 永眠(死亡)
    private void FallAsleepForever()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (IsAsleepForever) return;

        IsAsleepForever = true;
        SendRPC(RPC_type.SetAsleepForever);

        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        state.DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayer(Player, true);

        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} は永眠した", "Sleeper");
    }

    // オーナーにのみ睡魔ゲージを表示
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || !Is(seen)) return "";
        if (IsAsleepForever) return "";

        return Utils.ColorString(RoleInfo.RoleColor, $"{GetString("SleeperGaugeText")}: {(int)SleepGauge}%");
    }

    // 生存していれば勝利 (単独勝利 or 追加勝利)
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;
        if (IsAsleepForever) return false;

        winnerRole = CustomRoles.Sleeper;

        if (!AddWinOnly && CustomWinnerHolder.WinnerTeam != CustomWinner.Sleeper)
        {
            // 単独勝利: 他の勝者を上書きする
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Sleeper, Player.PlayerId, true);
        }

        return true;
    }
}
