using AmongUs.GameOptions;

using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Madmate;

// ===== ブラックマッドメイト (BlackMadMate) =====
// From: TownOfHost_hamo
// イントロ：色が無くなった
// 陣営：インポスター
// 置き換え：ベントON時はエンジニア／OFF時はクルーメイト
//
// ブラックビジョナーにサイドキックされて生まれるマッドメイト。
// ブラックビジョナー同様、他プレイヤーが黒でしか見えなくなる。ただしブラックビジョナー(相方)だけは真の色が見える。
// ブラックビジョナーが死亡すると後追いで死亡する(後追い処理自体はBlackVisioner側で実行)。
public sealed class BlackMadMate : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(BlackMadMate),
            player => new BlackMadMate(player),
            CustomRoles.BlackMadMate,
            () => Impostor.BlackVisioner.OptionMadmateCanUseVent != null && Impostor.BlackVisioner.OptionMadmateCanUseVent.GetBool()
                ? RoleTypes.Engineer
                : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            23000,
            SetupOptionItem,
            "bm",
            "#111111",
            (8, 5),
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_hamo
        );

    public BlackMadMate(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    private static void SetupOptionItem() { }

    public override void ApplyGameOptions(IGameOptions opt) { }

    // 生成された(サイドキックされた)瞬間に、現在生存しているブラックビジョナーを相方として登録する
    public override void ChengeRoleAdd()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var visioner = Impostor.BlackVisioner.VisionerPlayer?.GetRoleClass() as Impostor.BlackVisioner;
        visioner?.RegisterPartner(Player.PlayerId);

        ChangeColor();
    }

    // すべてのクルーが黒く見える。ただしブラックビジョナー(相方)だけは真の色が見える。
    public override void ChangeColor()
    {
        if (!Player.IsAlive()) return;
        var visionerPlayer = Impostor.BlackVisioner.VisionerPlayer;

        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc == Player) continue;
            if (visionerPlayer != null && pc.PlayerId == visionerPlayer.PlayerId) continue;

            pc.RpcChColor(Player, 15, true); // 15 = 黒
        }
    }
}
