using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

// ===== レジェンドスター (LegendStar) =====
// From: TownOfHost_hamo
// イントロ：それは伝説へと変わっていく
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// 死亡すると、自分がレジェンドスターだったことと、その時点までに死亡した全プレイヤーの役職が
// 全員に公開される。
public sealed class LegendStar : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(LegendStar),
            player => new LegendStar(player),
            CustomRoles.LegendStar,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            38500,
            SetupOptionItem,
            "lgs",
            "#ffd700",
            (1, 10),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public LegendStar(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Revealed = false;
    }

    private static OverrideTasksData Tasks;

    private static void SetupOptionItem()
    {
        Tasks = OverrideTasksData.Create(RoleInfo, 20, tasks: (true, 4, 3, 4));
    }

    public override void ApplyGameOptions(IGameOptions opt) { }

    private bool Revealed;

    private enum RPC_type
    {
        Reveal
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        reader.ReadByte(); // RPC_type (現状Revealのみ)
        Revealed = true;
    }

    // 自分の死亡を検知して公開処理を行う (通常killフロー/追放/その他の死因すべてに対応するためポーリング)
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Revealed) return;
        if (Player?.Data == null || !Player.Data.IsDead) return;

        Reveal();
    }

    private void Reveal()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Revealed) return;
        Revealed = true;

        using var sender = CreateSender();
        sender.Writer.Write((byte)RPC_type.Reveal);

        var sb = new StringBuilder();
        sb.Append(Utils.ColorString(RoleInfo.RoleColor, GetString("LegendStarRevealHeader")));

        foreach (var kvp in PlayerState.AllPlayerStates.Where(kvp => kvp.Value.IsDead))
        {
            var pc = PlayerCatch.GetPlayerById(kvp.Key);
            if (pc == null) continue;
            sb.Append($"\n{pc.GetRealName().RemoveHtmlTags()} : {Utils.ColorString(UtilsRoleText.GetRoleColor(kvp.Value.MainRole), UtilsRoleText.GetRoleName(kvp.Value.MainRole))}");
        }

        Utils.SendMessage(sb.ToString());
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} (LegendStar) died: revealed all dead players' roles", "LegendStar");
    }

    // 自分の名前欄にレジェンドスターだったと分かる表示 (死亡後のみ)
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seen)) return "";
        if (!Revealed) return "";

        return Utils.ColorString(RoleInfo.RoleColor, GetString("LegendStar"));
    }
}
