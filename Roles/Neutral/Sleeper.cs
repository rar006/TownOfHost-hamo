using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== スリーパー (Sleeper) =====
// From: TownOfHost_hamo
// イントロ：zzz.....
// 陣営：ニュートラル / 置き換え：ファントム(透明化枠を流用し、ネイティブのクールダウンゲージで眠気を表現)
//
// 睡魔ゲージ(0〜100%)が時間経過で溜まっていき、100%に達すると寝落ちてしまう(永眠)。
// ベントに入ることで一時的に「眠り」、睡魔ゲージを設定%分だけ軽減できる(0%未満にはならない)。
// 生存していれば単独勝利(オプションで追加勝利化も可能)。
//
// 実装メモ：
// 睡魔ゲージは「設定スピード(秒) ごとに10%ずつ溜まる」という仕様。
// ネイティブのファントムクールダウン(AURoleOptions.PhantomCooldown)は減少方向の値のため、
// 「ゲージが100%に達するまでの残り時間」に変換してそのまま流用し、
// クールダウンゲージが0になった瞬間(=ゲージ100%)に永眠させることで、
// 見た目のクールダウン表示と睡魔ゲージの進行を一致させている。
//
// 経過時間の計算・ゲージの増減・永眠判定はホストのみが行い、RPCで全員に配信する
// (ホストが唯一の権威。非ホストは受信した値をそのまま採用する)。
//
// 注意: BaseRoleTypeをPhantomにしたことで、Vent.CanUseパッチの判定
// (couldUse = CanUseImpostorVentButton() || Role == Engineer) に引っかからず
// ベントボタン自体が消えてしまう不具合があった。IKillerを実装し
// CanUseImpostorVentButton()をtrueにすることでベントの使用条件を満たしつつ、
// CanKill/IsKillerはfalseにしてキル能力自体は持たせないようにしている。
public sealed class Sleeper : RoleBase, IAdditionalWinner, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Sleeper),
            player => new Sleeper(player),
            CustomRoles.Sleeper,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            56100,
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
        GaugeSpeedSeconds = OptionGaugeSpeed.GetInt();
        VentDecreaseAmount = OptionVentDecrease.GetInt();
        PlayVentAnimation = OptionPlayVentAnimation.GetBool();
        AddWinOnly = OptionAddWinOnly.GetBool();

        SleepinessGauge = 0f;
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

    // 睡魔ゲージが10%溜まるのにかかる秒数。値が小さいほど早く眠くなる。
    private readonly int GaugeSpeedSeconds;
    // ベントで眠った時に軽減される睡魔ゲージの量(%)。
    private readonly int VentDecreaseAmount;
    private readonly bool PlayVentAnimation;
    private readonly bool AddWinOnly;

    // 睡魔ゲージ(0〜100)。100に達すると永眠。ホストのみがこの値を計算し、RPCで全員に同期する。
    public float SleepinessGauge;
    public bool IsAsleepForever;

    // 睡魔ゲージが100%に達するまでの残り秒数を返す(ネイティブのクールダウン表示用)。
    // GaugeSpeedSeconds秒ごとに10%溜まる = 100%溜まるのに GaugeSpeedSeconds*10秒かかる計算。
    private float GetRemainingSecondsUntilFull()
    {
        float totalSecondsToFill = GaugeSpeedSeconds * 10f;
        float progressRatio = SleepinessGauge / 100f;
        float remaining = totalSecondsToFill * (1f - progressRatio);
        return remaining;
    }

    // IKiller実装: キル能力は持たないが、ベントボタンだけは使えるようにする。
    public bool CanKill => false;
    public bool IsKiller => false;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => true;

    private static void SetupOptionItem()
    {
        OptionGaugeSpeed = IntegerOptionItem.Create(RoleInfo, 10, OptionName.SleeperGaugeSpeed, new(1, 100, 1), 5, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVentDecrease = IntegerOptionItem.Create(RoleInfo, 11, OptionName.SleeperVentDecrease, new(10, 100, 10), 70, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionPlayVentAnimation = BooleanOptionItem.Create(RoleInfo, 12, OptionName.SleeperPlayVentAnimation, true, false);
        OptionAddWinOnly = BooleanOptionItem.Create(RoleInfo, 13, OptionName.SleeperAddWinOnly, false, false);
    }

    public override void OnSpawn(bool initialState = false)
    {
        SleepinessGauge = 0f;
        if (AmongUsClient.Instance.AmHost) SendRpc();
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // ネイティブのクールダウンゲージ表示に、睡魔ゲージが満タンになるまでの残り時間を反映する。
        // 0だとゲージ計算上都合が悪いことがあるため下限は少しだけ余裕を持たせる。
        float remaining = GetRemainingSecondsUntilFull();
        AURoleOptions.PhantomCooldown = remaining > 0.1f ? remaining : 0.1f;
    }

    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(SleepinessGauge);
        sender.Writer.Write(IsAsleepForever);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        SleepinessGauge = reader.ReadSingle();
        IsAsleepForever = reader.ReadBoolean();
    }

    // 経過時間の計算・永眠判定はホストのみが行う。非ホストはRPCで受け取った値をそのまま使う。
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;
        if (!Player.IsAlive()) return;
        if (IsAsleepForever) return;

        float prev = SleepinessGauge;

        // GaugeSpeedSeconds秒で10%溜まる = 1秒あたり (10/GaugeSpeedSeconds)% 増加する
        float gaugePerSecond = 10f / UnityEngine.Mathf.Max(GaugeSpeedSeconds, 1);
        SleepinessGauge += gaugePerSecond * UnityEngine.Time.fixedDeltaTime;
        if (SleepinessGauge > 100f) SleepinessGauge = 100f;

        // ネイティブのクールダウンゲージ表示(ApplyGameOptions経由)と、永眠判定に使う
        // 内部値とのズレを防ぐため、毎フレームDirty化してApplyGameOptionsを反映させる。
        Player.MarkDirtySettings();

        // RPC同期(他クライアントのGetLowerTextやログ表示用)は整数%が変わった時だけで十分。
        if (UnityEngine.Mathf.FloorToInt(prev) != UnityEngine.Mathf.FloorToInt(SleepinessGauge))
        {
            SendRpc();
        }

        if (SleepinessGauge >= 100f)
            FallAsleepForever();
    }

    // ベントに入る = 眠る。睡魔ゲージを設定%分だけ軽減する(0%未満にはならない)。
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!AmongUsClient.Instance.AmHost) return PlayVentAnimation;
        if (!Player.IsAlive() || IsAsleepForever) return PlayVentAnimation;

        SleepinessGauge -= VentDecreaseAmount;
        if (SleepinessGauge < 0f) SleepinessGauge = 0f;

        Player.MarkDirtySettings();
        SendRpc();
        // AURoleOptions.PhantomCooldownの値を書き換えただけでは、既に表示中の
        // ネイティブのクールダウンゲージ(見た目のカウントダウン)には反映されない。
        // RpcResetAbilityCooldownを呼ぶことで実際にゲージ表示もリセットする。
        Player.RpcResetAbilityCooldown(log: false, Sync: true);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が眠った (睡魔ゲージ: {SleepinessGauge:F0}%)", "Sleeper");

        // falseを返すとベントから追い出されアニメーションも見せない = 「アニメーションを再生しない」設定に対応
        return PlayVentAnimation;
    }

    // 睡魔ゲージが100%に達した = 永眠(死亡)
    private void FallAsleepForever()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (IsAsleepForever) return;

        IsAsleepForever = true;
        SendRpc();

        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        state.DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayer(Player, true);

        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} は永眠した", "Sleeper");
    }

    // 生存していれば勝利 (単独勝利 or 追加勝利)
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;
        if (IsAsleepForever) return false;

        winnerRole = CustomRoles.Sleeper;

        if (!AddWinOnly)
        {
            // 単独勝利: 既に他陣営の勝利が確定していても、必ず上書きしてスリーパーの単独勝利にする。
            // (WinnerTeamが既にSleeper以外の値だと上書きしない、という誤った条件が付いていたため、
            //  他陣営の勝利とスリーパーの勝利が共存してしまうバグの原因になっていた)
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Sleeper, Player.PlayerId, true);
        }

        return true;
    }
}

