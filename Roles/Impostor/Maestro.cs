using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// ===== マエストロ (Maestro) =====
// カラーコード: #FF1919
// イントロ：【その死に様すら糧にして】
// 陣営：インポスター / 置き換え：インポスター
//
// セルフ通報をする度にキルクールが減少するインポスター。
// キルクール減少は、設定値(下限)に到達するまで累積し続ける。
public sealed class Maestro : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Maestro),
            player => new Maestro(player),
            CustomRoles.Maestro,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            96400,
            SetupOptionItem,
            "Mae",
            "#FF1919",
            (5, 4),
            from: From.TownOfHost_hamo
        );

    public Maestro(PlayerControl player) : base(RoleInfo, player)
    {
        baseKillCooldown = OptionKillCooldown.GetFloat();
        reductionPerSelfReport = OptionReductionPerSelfReport.GetFloat();
        minKillCooldown = OptionMinKillCooldown.GetFloat();

        currentKillCooldown = baseKillCooldown;
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionReductionPerSelfReport;
    static OptionItem OptionMinKillCooldown;

    enum OptionName
    {
        MaestroKillCooldown,
        MaestroReductionPerSelfReport,
        MaestroMinKillCooldown,
    }

    private readonly float baseKillCooldown;
    private readonly float reductionPerSelfReport;
    private readonly float minKillCooldown;

    // 累積された減少分を反映した、現在有効なキルクール秒数
    private float currentKillCooldown;

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.MaestroKillCooldown,
            new(0.5f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionReductionPerSelfReport = FloatOptionItem.Create(RoleInfo, 11, OptionName.MaestroReductionPerSelfReport,
            new(0.5f, 180f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMinKillCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.MaestroMinKillCooldown,
            new(0.5f, 180f, 0.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!Is(reporter) || target is null) return;

        // 自分がキルした死体を、自分自身で通報した場合のみ「セルフ通報」とみなす。
        var realKiller = target.Object?.GetRealKiller();
        if (realKiller == null || realKiller.PlayerId != Player.PlayerId) return;

        if (currentKillCooldown <= minKillCooldown) return;

        currentKillCooldown = System.Math.Max(minKillCooldown, currentKillCooldown - reductionPerSelfReport);
        Player.SetKillCooldown(time: currentKillCooldown, force: true);

        Utils.SendMessage(string.Format(GetString("Maestro.CooldownReduced"), currentKillCooldown), Player.PlayerId);

        UtilsGameLog.AddGameLog("Maestro",
            $"{UtilsName.GetPlayerColor(Player)}がセルフ通報によりキルクールを{currentKillCooldown}秒に短縮した");
    }

    // IKiller.CalculateKillCooldown()のデフォルト実装(Options.DefaultKillCooldownを返すだけ)
    // のままだと、セルフ通報で減らしたcurrentKillCooldownの値がキルボタンの実際の
    // クールダウン計算に反映されない(見た目のメッセージだけ変わって実質何も変わらない)
    // 不具合があったため、明示的にオーバーライドする。
    public float CalculateKillCooldown() => currentKillCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // ゲーム開始時点のキルクールにも、baseKillCooldownの設定値を反映する
        // (未オーバーライドのままだとバニラのデフォルトキルクールで開始されてしまう)。
        AURoleOptions.KillCooldown = currentKillCooldown;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || !Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        var prefix = isForHud ? "" : "<size=60%>";
        var c = RoleInfo.RoleColorCode;
        return $"{prefix}<color={c}>現在のキルクール: {currentKillCooldown:F1}秒</color>";
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
