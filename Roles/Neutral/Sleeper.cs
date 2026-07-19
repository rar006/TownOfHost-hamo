using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== スリーパー (Sleeper) =====
// From: TownOfHost_hamo
// イントロ：zzz.....
// 陣営：ニュートラル / 置き換え：ファントム(透明化枠を流用し、ネイティブのクールダウンゲージで眠気を表現)
// 時間経過でネイティブのクールダウンゲージ(=起きていられる残り時間)が減っていき、0になると眠ってしまう(永眠)。
// ベントに入ることで「眠る」ことができ、クールダウンを最大値までリセットできる(=一時的にスッキリする)。
// 生存していれば単独勝利(オプションで追加勝利化も可能)。
//
// 実装メモ：
// 以前は独自RPCで「睡魔ゲージ(0〜100)」を各クライアントがローカル計算しつつ、
// ホストからのRPCで上書きする方式だったが、非ホストが本人の場合にゲージが
// 進んでいるように見えない不具合があった。
// 今回、ネイティブのファントム役職のクールダウン(AURoleOptions.PhantomCooldown)を
// そのまま「起きていられる残り時間」として流用する方式に変更。
// 経過時間の計算・減少・0判定はホストのみが行い、その結果をRPCで全員に配信、
// 非ホストは受信した値をそのまま採用する(ホストが唯一の権威)。
// これによりローカル計算とRPCの競合が起きなくなる。
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
        AwakeTimeLimitSeconds = OptionGaugeSpeed.GetInt();
        VentRecoverAmount = OptionVentDecrease.GetInt();
        PlayVentAnimation = OptionPlayVentAnimation.GetBool();
        AddWinOnly = OptionAddWinOnly.GetBool();

        RemainingAwakeTime = AwakeTimeLimitSeconds;
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

    // 「起きていられる時間」の最大値(秒)。以前の"ゲージが溜まる速さ"に相当する設定を、
    // そのまま「最大時間」として読み替えている。
    private static int AwakeTimeLimitSeconds;
    // 互換用に残しているが、現行仕様ではベント使用時は満タンにリセットするため未使用。
    private static int VentRecoverAmount;
    private static bool PlayVentAnimation;
    private static bool AddWinOnly;

    // 起きていられる残り時間(秒)。ネイティブのクールダウンゲージにそのまま流用する。
    // 0になると永眠。ホストのみがこの値を計算し、RPCで全員に同期する。
    public float RemainingAwakeTime;
    public bool IsAsleepForever;

    // IKiller実装: キル能力は持たないが、ベントボタンだけは使えるようにする。
    public bool CanKill => false;
    public bool IsKiller => false;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => true;

    private static void SetupOptionItem()
    {
        OptionGaugeSpeed = IntegerOptionItem.Create(RoleInfo, 10, OptionName.SleeperGaugeSpeed, new(10, 300, 5), 100, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVentDecrease = IntegerOptionItem.Create(RoleInfo, 11, OptionName.SleeperVentDecrease, new(10, 100, 10), 70, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionPlayVentAnimation = BooleanOptionItem.Create(RoleInfo, 12, OptionName.SleeperPlayVentAnimation, true, false);
        OptionAddWinOnly = BooleanOptionItem.Create(RoleInfo, 13, OptionName.SleeperAddWinOnly, false, false);
    }

    public override void Add()
    {
        RemainingAwakeTime = AwakeTimeLimitSeconds;
        IsAsleepForever = false;
    }

    public override void OnSpawn(bool initialState = false)
    {
        RemainingAwakeTime = AwakeTimeLimitSeconds;
        if (AmongUsClient.Instance.AmHost) SendRpc();
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // ネイティブのクールダウンゲージ表示に、起きていられる残り時間をそのまま反映する。
        // 0だとゲージ計算上都合が悪いことがあるため下限は少しだけ余裕を持たせる。
        AURoleOptions.PhantomCooldown = RemainingAwakeTime > 0.1f ? RemainingAwakeTime : 0.1f;
    }

    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(RemainingAwakeTime);
        sender.Writer.Write(IsAsleepForever);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        RemainingAwakeTime = reader.ReadSingle();
        IsAsleepForever = reader.ReadBoolean();
    }

    // 経過時間の計算・永眠判定はホストのみが行う。非ホストはRPCで受け取った値をそのまま使う。
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;
        if (!Player.IsAlive()) return;
        if (IsAsleepForever) return;

        float prev = RemainingAwakeTime;
        RemainingAwakeTime -= UnityEngine.Time.fixedDeltaTime;
        if (RemainingAwakeTime < 0f) RemainingAwakeTime = 0f;

        // ネイティブのクールダウンゲージ表示(ApplyGameOptions経由)と、永眠判定に使う
        // 内部値とのズレを防ぐため、毎フレームDirty化してApplyGameOptionsを反映させる。
        // (これを間引くと、ゲージ表示上0になってから実際に死亡するまでに遅延が生じる)
        Player.MarkDirtySettings();

        // RPC同期(他クライアントのGetLowerTextやログ表示用)は秒の桁が変わった時だけで十分。
        if (UnityEngine.Mathf.CeilToInt(prev) != UnityEngine.Mathf.CeilToInt(RemainingAwakeTime))
        {
            SendRpc();
        }

        if (RemainingAwakeTime <= 0f)
            FallAsleepForever();
    }

    // ベントに入る = 眠る。起きていられる時間を満タンまで回復する。
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!AmongUsClient.Instance.AmHost) return PlayVentAnimation;
        if (!Player.IsAlive() || IsAsleepForever) return PlayVentAnimation;

        RemainingAwakeTime = AwakeTimeLimitSeconds;
        Player.MarkDirtySettings();
        SendRpc();
        // AURoleOptions.PhantomCooldownの値を書き換えただけでは、既に表示中の
        // ネイティブのクールダウンゲージ(見た目のカウントダウン)には反映されない。
        // RpcResetAbilityCooldownを呼ぶことで実際にゲージ表示もリセットする。
        Player.RpcResetAbilityCooldown(log: false, Sync: true);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が眠った (起床時間リセット)", "Sleeper");

        // falseを返すとベントから追い出されアニメーションも見せない = 「アニメーションを再生しない」設定に対応
        return PlayVentAnimation;
    }

    // 起きていられる時間が0になった = 永眠(死亡)
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

        if (!AddWinOnly && CustomWinnerHolder.WinnerTeam != CustomWinner.Sleeper)
        {
            // 単独勝利: 他の勝者を上書きする
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Sleeper, Player.PlayerId, true);
        }

        return true;
    }
}
