using System.Collections.Generic;

using AmongUs.GameOptions;



using TownOfHost.Modules;

using TownOfHost.Roles.Core;

using TownOfHost.Roles.Core.Interfaces;



namespace TownOfHost.Roles.Neutral;



// ===== アンチヒーロー (AntiHero) =====

// イントロ：自分なりの正義を貫こう

// 陣営：第三陣営(インポスター判定でキル可能)

//

// キルした対象の陣営によって勝利条件が変わり、最後にキルした陣営の勝利を乗っ取る。

// どの陣営をキルしたかは(設定次第で)本人にも分からない。

// キルされた対象には全員から見える印がつく。

// キルせず試合が終了した場合はクルーメイト勝利に追加勝利する。

public sealed class AntiHero : RoleBase, IAdditionalWinner, IKiller

{

    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(

            typeof(AntiHero),

            player => new AntiHero(player),

            CustomRoles.AntiHero,

            () => RoleTypes.Impostor,

            CustomRoleTypes.Neutral,

            85500,

            SetupOptionItem,

            "ah",

            "#c23616",

            (4, 22),

            true,

            from: From.TownOfHost_hamo

        );



    public AntiHero(PlayerControl player) : base(RoleInfo, player)

    {

        remainingKillCount = OptionKillUseCount.GetInt();

        lastKilledWinner = null;

        turnsSinceLastKill = int.MaxValue;

        hasKilledEver = false;

    }



    // 死んだ後に印だけ独立して残す/消すため、対象IDと「つけた本人のID」を静的辞書で管理する。

    // (役職再割り当て等でインスタンスが複数生成されても安全なように、

    //  マーク描画自体はプレイヤーIDベースの静的なリストを見るstaticメソッドで行う)

    private static readonly Dictionary<byte, byte> MarkedTargetOwners = new();



    // CustomRoleManager.MarkOthersはゲーム開始のたびにClear()されるため、

    // ゲームごとに毎回登録し直す必要がある(GameModuleInitializerはゲーム開始のたびに呼ばれる)。

    [Attributes.GameModuleInitializer]

    public static void Init()

    {

        MarkedTargetOwners.Clear();

        CustomRoleManager.MarkOthers.Add(GetMarkOthers);

    }



    static OptionItem OptionKillCooldown;

    static OptionItem OptionKillUseCount;

    static OptionItem OptionMarkVisible;

    static OptionItem OptionMarkClearOnDeath;



    enum OptionName

    {

        AntiHeroKillCooldown,

        AntiHeroKillUseCount,

        AntiHeroMarkVisible,

        AntiHeroMarkClearOnDeath,

    }



    static void SetupOptionItem()

    {

        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.AntiHeroKillCooldown,

            new(0f, 180f, 1f), 40f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionKillUseCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.AntiHeroKillUseCount,

            new(0, 99, 1), 3, false)

            .SetValueFormat(OptionFormat.Times);

        // アンチヒーローの単独勝利優先度(専用ヘルパーで登録。既定値35)

        SoloWinOption.Create(RoleInfo, 12, defo: 35);

        OptionMarkVisible = BooleanOptionItem.Create(RoleInfo, 13, OptionName.AntiHeroMarkVisible, true, false);

        OptionMarkClearOnDeath = BooleanOptionItem.Create(RoleInfo, 14, OptionName.AntiHeroMarkClearOnDeath, true, false);

    }



    // 残りキル使用回数

    private int remainingKillCount;

    // 最後にキルした対象の勝利エントリ(=乗っ取る対象の勝利条件)

    private CustomWinner? lastKilledWinner;

    // キルが成立してから経過した会議数(1ターン経過するまで勝利条件に反映しない)

    private int turnsSinceLastKill = int.MaxValue;

    // 一度もキルしないまま試合が終わったかどうかの判定用

    private bool hasKilledEver;



    bool IKiller.CanKill => remainingKillCount > 0;

    public bool CanUseKillButton() => Player.IsAlive() && remainingKillCount > 0;

    public bool CanUseImpostorVentButton() => true;

    public bool CanUseSabotageButton() => false;

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();



    public void OnCheckMurderAsKiller(MurderInfo info)

    {

        if (info.AttemptTarget == null) return;

        if (!AmongUsClient.Instance.AmHost) return;



        var targetRole = info.AttemptTarget.GetCustomRole();



        remainingKillCount--;

        hasKilledEver = true;

        turnsSinceLastKill = 0;



        // キルした対象の勝利エントリを記録する(=乗っ取る勝利条件)

        lastKilledWinner = ResolveWinnerFor(targetRole);



        MarkedTargetOwners[info.AttemptTarget.PlayerId] = Player.PlayerId;



        UtilsGameLog.AddGameLog("AntiHero",

            $"{UtilsName.GetPlayerColor(Player)}が{UtilsName.GetPlayerColor(info.AttemptTarget)}をキルした(乗っ取り対象を更新)");

    }



    /// <summary>

    /// 対象の役職から、乗っ取り判定に使う勝利エントリ(CustomWinner)を推定する。

    /// 明確に対応するエントリが無い場合は、陣営全体の代表エントリにフォールバックする。

    /// </summary>

    private static CustomWinner? ResolveWinnerFor(CustomRoles role)

    {

        // 役職名と同名のCustomWinnerエントリがあれば、それを最優先で使う

        if (System.Enum.TryParse<CustomWinner>(role.ToString(), out var exact))

            return exact;



        if (role.IsImpostor() || role.IsMadmate()) return CustomWinner.Impostor;

        if (role.IsCrewmate()) return CustomWinner.Crewmate;



        // それ以外のニュートラル役職で個別エントリが無いものは判定不能として扱う

        return null;

    }



    public override void AfterMeetingTasks()

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (turnsSinceLastKill != int.MaxValue)

            turnsSinceLastKill++;

    }



    public bool CheckWin(ref CustomRoles winnerRole)

    {

        if (!Player.IsAlive()) return false;



        // 一度もキルしていない場合、試合終了時にクルーメイト勝利へ追加勝利する

        if (!hasKilledEver)

        {

            if (CustomWinnerHolder.winners.Contains(CustomWinner.Crewmate))

            {

                winnerRole = CustomRoles.Crewmate;

                return true;

            }

            return false;

        }



        // キルしてから1ターン経過するまでは、まだ勝利条件に反映しない

        if (turnsSinceLastKill < 1) return false;



        if (lastKilledWinner is not { } winner) return false;

        if (!CustomWinnerHolder.winners.Contains(winner)) return false;



        // 最後にキルした陣営が勝利した場合、その勝利を乗っ取る(単独勝利として追加)

        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.AntiHero, Player.PlayerId, hantrole: CustomRoles.AntiHero))

        {

            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);

        }

        return false;

    }



    /// <summary>

    /// 全プレイヤーの画面で共通表示される印。CustomRoleManager.MarkOthersに登録して使う。

    /// (RoleBase.GetMarkはseer自身の役職クラスでしか呼ばれないため、

    ///  「他人から見た他人」への表示にはこの仕組みを使う)

    /// </summary>

    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)

    {

        seen ??= seer;

        if (!OptionMarkVisible.GetBool()) return "";

        if (!MarkedTargetOwners.TryGetValue(seen.PlayerId, out var ownerId)) return "";



        if (OptionMarkClearOnDeath.GetBool())

        {

            var owner = PlayerCatch.GetPlayerById(ownerId);

            if (owner == null || !owner.IsAlive()) return "";

        }



        return Utils.ColorString(RoleInfo.RoleColor, "†");

    }

}



// ===== 手動ドア(Polus/MiraHQ等のドアコンソール)使用禁止 =====

// AntiHeroはベース役職がRoleTypes.Impostorなため、素の状態だとネイティブに

// ドアコンソールを使えてしまう(サボタージュボタンとは別の判定経路のため、

// CanUseSabotageButtonをfalseにするだけでは防げない)。ここで直接ブロックする。

[HarmonyLib.HarmonyPatch(typeof(DoorConsole), nameof(DoorConsole.Use))]

public static class AntiHeroBlockDoorConsolePatch

{

    public static bool Prefix()

    {

        var local = PlayerControl.LocalPlayer;

        if (local != null && local.Is(CustomRoles.AntiHero)) return false;

        return true;

    }

}

