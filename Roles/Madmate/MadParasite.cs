using AmongUs.GameOptions;

using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Madmate;

// ===== マッドパラサイト (MadParasite) =====
// イントロ：菌は伝染していく...
// 判定：クルーメイト / 陣営：マッドメイト
//
// 自身をキルしたプレイヤーの役職をマッドパラサイトに変化させる。
// 変化後のマッドパラサイトには変化能力はない(連鎖を1回に限定する)。
//
// 【設定表示について】要望により、この役職自体の設定項目はマッドメイトのタブには
// 個別に表示せず、インフェクトの設定の中にまとめて表示する(Infectedと同様の扱い)。
public sealed class MadParasite : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadParasite),
            player => new MadParasite(player),
            CustomRoles.MadParasite,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            96700,
            SetupOptionItem,
            "Mps",
            "#7A9E2E",
            (5, 7),
            from: From.TownOfHost_hamo
        );

    public MadParasite(PlayerControl player) : base(RoleInfo, player)
    {
        includeImpostor = OptionIncludeImpostor.GetBool();
        canSeeImpostor = OptionCanSeeImpostor.GetBool();
        seenByImpostor = OptionSeenByImpostor.GetBool();
        transformedSeenByImpostor = OptionTransformedSeenByImpostor.GetBool();

        // 変化によって生まれたマッドパラサイトは感染を広げられない(連鎖を1回に限定する)。
        // 【重要・不具合修正】以前はコンストラクタ内でAmongUsClient.Instance.GameId
        // (部屋に紐づくID、同じロビーでの再戦では変わらない)を見て自前でクリア判定を
        // していたが、それだと前回のゲームでマッドパラサイトに変化した経験のある
        // プレイヤーが次のゲームでも記録に残ったままになり、再びマッドパラサイトを
        // 引いたときに感染が一切発動しなくなっていた。
        // 今はCustomRoleManager.Initialize()(ゲーム開始のたびに必ず呼ばれる)から
        // ResetTransformHistory()を呼んで確実にクリアするようにしている。
        canInfect = !alreadyTransformed.Contains(player.PlayerId);
    }

    /// <summary>
    /// ゲーム開始のたびにCustomRoleManager.Initialize()から呼ばれる。
    /// 「変化によって生まれた個体は感染を広げられない」の判定記録をクリアする。
    /// </summary>
    public static void ResetTransformHistory()
    {
        alreadyTransformed.Clear();
    }

    static OptionItem OptionIncludeImpostor;
    static OptionItem OptionCanSeeImpostor;
    static OptionItem OptionSeenByImpostor;
    static OptionItem OptionTransformedSeenByImpostor;

    enum OptionName
    {
        MadParasiteIncludeImpostor,
        MadParasiteCanSeeImpostor,
        MadParasiteSeenByImpostor,
        MadParasiteTransformedSeenByImpostor,
    }

    static void SetupOptionItem()
    {
        // 要望により、マッドパラサイト単独の設定項目は表示せず、
        // インフェクトの設定の中にまとめて表示する(感染系の役職としてまとめる)。
        var parent = TownOfHost.Roles.Impostor.Infect.RoleInfo.RoleOption;

        // 「インポスターも能力対象に含めるか」: ONの場合、インポスターがマッドパラサイトを
        // キルすると、そのインポスターもマッドパラサイト(マッドメイト)に変化する。
        OptionIncludeImpostor = BooleanOptionItem.Create(RoleInfo, 10, OptionName.MadParasiteIncludeImpostor, true, false, parent);
        // 「インポスターを視認できるか」: ONの場合、マッドパラサイトからインポスターが見える。
        OptionCanSeeImpostor = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MadParasiteCanSeeImpostor, true, false, parent);
        // 「インポスターから視認されるか」: ONの場合、インポスター側からオリジナルの
        // マッドパラサイトが見える(マッドメイトとして視認可能)。
        OptionSeenByImpostor = BooleanOptionItem.Create(RoleInfo, 12, OptionName.MadParasiteSeenByImpostor, false, false, parent);
        // 「変化後のマッドパラサイトがインポスターから視認されるか」: 感染によって
        // 生まれたマッドパラサイト(変化能力を持たない個体)についての、上記と同種の設定。
        OptionTransformedSeenByImpostor = BooleanOptionItem.Create(RoleInfo, 13, OptionName.MadParasiteTransformedSeenByImpostor, false, false, parent);
    }

    // インポスターも変化対象に含めるかどうか
    private readonly bool includeImpostor;
    // マッドパラサイトからインポスターを視認できるかどうか
    private readonly bool canSeeImpostor;
    // オリジナルのマッドパラサイトがインポスターから視認されるかどうか
    private readonly bool seenByImpostor;
    // 変化後のマッドパラサイトがインポスターから視認されるかどうか
    private readonly bool transformedSeenByImpostor;

    // このインスタンスが「感染を広げられる」オリジナルのマッドパラサイトかどうか。
    // 変化後に生まれたマッドパラサイトはfalseになり、連鎖が1回に限定される。
    private bool canInfect;

    // マッドパラサイトへの変化によってこの役職になったプレイヤーのID一覧
    // (ゲームをまたいで残らないよう、GameIdが変わったタイミングでクリアする)
    private static readonly System.Collections.Generic.HashSet<byte> alreadyTransformed = new();

    // 既に変化処理を実行済みかどうか(OnMurderPlayerAsTargetと保険用のOnFixedUpdate検知の
    // 両方から呼ばれても二重発動しないようにするためのフラグ)
    private bool transformProcessed;

    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;

        Logger.Info($"MadParasite.OnMurderPlayerAsTarget呼び出し " +
            $"target={target?.name} self={Player.name} killer={killer?.name} " +
            $"canInfect={canInfect} includeImpostor={includeImpostor}", "MadParasite");

        if (!Is(target)) return;

        TryTransform(killer);
    }

    /// <summary>
    /// 【重要・不具合対策】OnMurderPlayerAsTargetはDontRoleAbility等の条件次第で
    /// 呼ばれないケースがあるため、保険としてOnFixedUpdateでも自分の死亡と
    /// キラー情報を監視し、変化処理が未実行であればここで行う。
    /// </summary>
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || transformProcessed || Player.IsAlive()) return;

        var killerId = MyState.GetRealKiller();
        if (killerId == byte.MaxValue) return;

        var killer = PlayerCatch.GetPlayerById(killerId);
        if (killer == null) return;

        Logger.Info($"MadParasite: OnFixedUpdate経由でキラー({killer.name})を検知しました(保険処理)", "MadParasite");
        TryTransform(killer);
    }

    private void TryTransform(PlayerControl killer)
    {
        if (transformProcessed || !canInfect) return;
        if (killer == null || killer.PlayerId == Player.PlayerId) return;

        // オプションがOFFの場合、インポスターは変化の対象外とする(意図しない陣営破壊を避けるため)。
        if (!includeImpostor && killer.GetCustomRole().IsImpostor())
        {
            Logger.Info($"MadParasite: includeImpostorがOFFのためインポスター({killer.name})への変化をスキップしました", "MadParasite");
            transformProcessed = true;
            return;
        }
        // 最後の1人のインポスターは変化させない(インポスターが0人になってその場で試合が終わり、MOD なしの参加者が暗転していた)
        if (killer.GetCustomRole().IsImpostor()
            && System.Linq.Enumerable.Count(PlayerCatch.AllAlivePlayerControls, pc => pc != killer && pc.GetCustomRole().IsImpostor()) == 0)
        {
            Logger.Info($"MadParasite: 最後のインポスター({killer.name})なので変化をスキップしました", "MadParasite");
            transformProcessed = true;
            return;
        }

        transformProcessed = true;
        alreadyTransformed.Add(killer.PlayerId);
        killer.RpcSetCustomRole(CustomRoles.MadParasite);

        Logger.Info($"MadParasite: {killer.name}をマッドパラサイトに変化させました " +
            $"変化後の役職={killer.GetCustomRole()}", "MadParasite");

        Utils.SendMessage(GetString("MadParasite.Infected"), killer.PlayerId);

        UtilsGameLog.AddGameLog("MadParasite",
            $"{UtilsName.GetPlayerColor(killer)}が{UtilsName.GetPlayerColor(Player)}をキルしてマッドパラサイトに変化した");
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;

        // 自分(マッドパラサイト)がインポスターを見る場合の視認マーク。
        if (Is(seer) && canSeeImpostor)
        {
            var seenRole = seen.GetCustomRole();
            if (seenRole.GetCustomRoleTypes() is CustomRoleTypes.Impostor)
                return $"<{RoleInfo.RoleColorCode}>★</color>";
        }

        // インポスターが自分(マッドパラサイト)を見る場合の視認マーク。
        // オリジナル個体と変化後個体とで、別々のオプションで制御する。
        if (Is(seen) && seer.GetCustomRole().GetCustomRoleTypes() is CustomRoleTypes.Impostor)
        {
            var visible = canInfect ? seenByImpostor : transformedSeenByImpostor;
            if (visible)
                return $"<{RoleInfo.RoleColorCode}>★</color>";
        }

        return "";
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
