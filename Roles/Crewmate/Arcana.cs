using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

// ===== アルカナ (Arcana) =====
// イントロ：この秘密は、私だけのもの
// 陣営：クルーメイト / 置き換え：クルーメイト / カウント：クルーメイト
//
// タスクが完了すると生存クルーメイトの数を常に知れる(アナライザーと同じ判定)。
// 役職・属性が変更されない。変更されようとした時、設定により反射して殺害する。
// 何らかの手段で正体が知られる時、必ず「クルーメイト」として伝わる。
public sealed class Arcana : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Arcana),
            player => new Arcana(player),
            CustomRoles.Arcana,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            39300,
            SetupOptionItem,
            "arc",
            "#FF33D4",
            (1, 13),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            countType: CountTypes.Crew,
            from: From.TownOfHost_hamo
        );

    public Arcana(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }

    static OptionItem OptionReflectRoleChange;
    static OptionItem OptionReflectAttributeGrant;
    private static OverrideTasksData Tasks;

    enum OptionName
    {
        ArcanaReflectRoleChange,
        ArcanaReflectAttributeGrant,
    }

    static void SetupOptionItem()
    {
        OptionReflectRoleChange = BooleanOptionItem.Create(RoleInfo, 10, OptionName.ArcanaReflectRoleChange, true, false);
        OptionReflectAttributeGrant = BooleanOptionItem.Create(RoleInfo, 11, OptionName.ArcanaReflectAttributeGrant, false, false);
        // タスク置き換え(レジェンドスターと同じ0/99/1形式: オン/オフ, 通常, ロング, ショート)
        Tasks = OverrideTasksData.Create(RoleInfo, 12, tasks: (false, 4, 3, 4));
    }

    /// <summary>役職が変更されようとした時、反射して殺害する設定かどうか</summary>
    public static bool ReflectRoleChangeEnabled => OptionReflectRoleChange.GetBool();
    /// <summary>属性が付与されようとした時、反射して殺害する設定かどうか</summary>
    public static bool ReflectAttributeGrantEnabled => OptionReflectAttributeGrant.GetBool();

    // アナライザーと同じ判定(クルーメイト or マッドメイトを「クルーメイト」としてカウント)で
    // 生存クルーメイト数を数える。
    public static int CountAliveCrewmates()
    {
        var count = 0;
        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            var role = pc.GetTellResults(null);
            if (role.IsCrewmate() || role.IsMadmate()) count++;
        }
        return count;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seen) || !Player.IsAlive()) return "";
        if (MyTaskState.HasCompletedEnoughCountOfTasks(1) is false) return "";

        var count = CountAliveCrewmates();
        var mes = $"<color={RoleInfo.RoleColorCode}>{string.Format(GetString("Arcana.AliveCrewmateCount"), count)}</color>";
        return isForHud ? mes : $"<size=40%>{mes}</size>";
    }

    // 何らかの手段(占い師・霊媒師等)で正体が知られる時、必ず「クルーメイト」として伝わる。
    public override CustomRoles Misidentify() => CustomRoles.Crewmate;
    public override CustomRoles TellResults(PlayerControl player) => CustomRoles.Crewmate;

    // ===== 役職・属性 反射キル =====
    // アルカナは役職・属性が変更されない。変更されようとした時、設定がONなら
    // 「変更を実行しようとした側」を殺害することで反射する。
    // (交換自体はキャンセルされ、アルカナの役職はそのまま維持される)

    /// <summary>対象がアルカナかつ役職反射がONかどうか(モイラ等から実行前に呼ぶ)</summary>
    public static bool ShouldReflectRoleChange(PlayerControl target)
        => target != null && target.Is(CustomRoles.Arcana) && ReflectRoleChangeEnabled;

    /// <summary>対象がアルカナかつ属性反射がONかどうか(マドンナ等から実行前に呼ぶ)</summary>
    public static bool ShouldReflectAttributeGrant(PlayerControl target)
        => target != null && target.Is(CustomRoles.Arcana) && ReflectAttributeGrantEnabled;

    /// <summary>
    /// 役職変更の反射キル(モイラ用)。交換は行わず、会議後にモイラ自身を死亡させる。
    /// </summary>
    public static void ReflectKillRoleChanger(PlayerControl changer, PlayerControl arcanaTarget)
    {
        if (changer == null || arcanaTarget == null) return;
        if (!AmongUsClient.Instance.AmHost) return;

        Utils.SendMessage(string.Format(GetString("Arcana.ReflectRoleChangeAnnounce"),
            UtilsName.GetPlayerColor(arcanaTarget, true),
            UtilsName.GetPlayerColor(changer, true)));

        UtilsGameLog.AddGameLog("Arcana",
            $"{UtilsName.GetPlayerColor(arcanaTarget)}の役職を変更しようとした" +
            $"{UtilsName.GetPlayerColor(changer)}が反射により死亡した");

        _ = new LateTask(() =>
        {
            if (changer != null && changer.IsAlive())
                changer.RpcMurderPlayer(changer);
        }, Main.LagTime, "Arcana.ReflectKillRoleChanger", true);
    }

    /// <summary>
    /// 属性付与の反射キル(マドンナ用)。付与せず、告白してきた相手を即座に死亡させる(ラバーズにはならない)。
    /// </summary>
    public static void ReflectKillAttributeGranter(PlayerControl granter, PlayerControl arcanaTarget)
    {
        if (granter == null || arcanaTarget == null) return;
        if (!AmongUsClient.Instance.AmHost) return;

        Utils.SendMessage(string.Format(GetString("Arcana.ReflectAttributeGrantAnnounce"),
            UtilsName.GetPlayerColor(arcanaTarget, true),
            UtilsName.GetPlayerColor(granter, true)), granter.PlayerId);

        UtilsGameLog.AddGameLog("Arcana",
            $"{UtilsName.GetPlayerColor(arcanaTarget)}に属性を付与しようとした" +
            $"{UtilsName.GetPlayerColor(granter)}が反射により死亡した");

        granter.RpcProtectedMurderPlayer();
    }
}
