using System.Linq;
using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// ===== ブラックビジョナー (BlackVisioner) =====
// From: TownOfHost_hamo
// イントロ：色はすべて同じだけど
// 陣営：インポスター / 置き換え：インポスター / カウント：インポスター
//
// すべてのクルーが黒色に見えるインポスター。マッドメイト(ブラックマッドメイト)を作成できる。
// ブラックマッドメイトとは互いの真の色を視認できる。ブラックビジョナーが死亡するとブラックマッドメイトも道連れで死亡する。
// 実際の(通常の)インポスター仲間は黒く塗りつぶされて見分けがつかなくなるため、誤ってキルしてしまう可能性がある演出上のリスクがある。
//
// 【実装メモ】サイドキック生成は当初 Shapeshifter+ISidekickable 方式(Egoist方式)で組んだが、
// このコードベースでインポスター陣営がマッドメイトを作る役職(Jackal, EvilMaker)は実際には
// RoleTypes.Phantom + IUsePhantomButton 方式(いわゆる「専用ボタン」)を使っており、
// Shapeshifter方式はインポスター陣営には正しく機能しない。EvilMakerを参考に書き直した。
public sealed class BlackVisioner : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(BlackVisioner),
            player => new BlackVisioner(player),
            CustomRoles.BlackVisioner,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            9500,
            SetupOptionItem,
            "bv",
            "#ff1919",
            (8, 4),
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_hamo
        );

    public BlackVisioner(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        SidekickCooldown = OptionSidekickCooldown.GetFloat();
        CanMadmateUseVent = OptionMadmateCanUseVent.GetBool();

        __blackVisioner = this;
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionSidekickCooldown;
    public static OptionItem OptionMadmateCanUseVent;

    enum OptionName
    {
        BlackVisionerSidekickCooldown,
        BlackVisionerMadmateCanUseVent
    }

    private float KillCooldown;
    private float SidekickCooldown;
    public static bool CanMadmateUseVent;

    // シングルトン参照 (BlackMadMate側が相方を特定するために使う)
    private static BlackVisioner __blackVisioner;
    public static PlayerControl VisionerPlayer => __blackVisioner?.Player;
    // 相方(ブラックマッドメイト)のId。まだ作られていなければ byte.MaxValue
    public byte PartnerId = byte.MaxValue;
    private bool PartnerDead;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSidekickCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.BlackVisionerSidekickCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMadmateCanUseVent = BooleanOptionItem.Create(RoleInfo, 12, OptionName.BlackVisionerMadmateCanUseVent, true, false);
    }

    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = SidekickCooldown;

    public bool CanUseSabotageButton() => true;
    public float CalculateKillCooldown() => KillCooldown;

    // Phantomボタン(専用ボタン)を使ってキルターゲットをブラックマッドメイトに変える
    bool IUsePhantomButton.IsPhantomRole => PartnerId == byte.MaxValue || PartnerDead;
    bool IUsePhantomButton.IsresetAfterKill => false;
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        ResetCooldown = true;
        if (PartnerId != byte.MaxValue && !PartnerDead) return;

        var target = Player.GetKillTarget(true);
        if (target == null) return;
        // 自分の(本来の)インポスター仲間はブラックマッドメイトにできない
        if (target.IsTeammate(Player)) return;

        AdjustKillCooldown = false;
        target.RpcSetCustomRole(CustomRoles.BlackMadMate, log: null);
        if (!Utils.RoleSendList.Contains(target.PlayerId)) Utils.RoleSendList.Add(target.PlayerId);
        target.RpcSetRole(CanMadmateUseVent ? RoleTypes.Engineer : RoleTypes.Crewmate, true);
        target.MarkDirtySettings();

        RegisterPartner(target.PlayerId);
        UtilsNotifyRoles.NotifyRoles();
    }

    private enum RPC_type
    {
        SetPartner,
        SetPartnerDead
    }
    private void SendRPC(RPC_type type, byte targetId = byte.MaxValue)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)type);
        if (type == RPC_type.SetPartner) sender.Writer.Write(targetId);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        var type = (RPC_type)reader.ReadByte();
        switch (type)
        {
            case RPC_type.SetPartner:
                PartnerId = reader.ReadByte();
                PartnerDead = false;
                break;
            case RPC_type.SetPartnerDead:
                PartnerDead = true;
                break;
        }
    }

    // ブラックマッドメイト生成が確定した瞬間に、自分が親であることを記録する
    public void RegisterPartner(byte partnerId)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        PartnerId = partnerId;
        PartnerDead = false;
        SendRPC(RPC_type.SetPartner, partnerId);
        ChangeColor();
    }

    public void NotifyPartnerDead()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (PartnerDead) return;
        PartnerDead = true;
        SendRPC(RPC_type.SetPartnerDead);
    }

    // 自分が死亡したら相方(ブラックマッドメイト)を道連れにする (通常killフロー)
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (PartnerId == byte.MaxValue || PartnerDead) return;
        if (Player?.Data == null || !Player.Data.IsDead) return;

        var partner = PlayerCatch.GetPlayerById(PartnerId);
        if (partner?.IsAlive() != true) return;

        PartnerDead = true;
        SendRPC(RPC_type.SetPartnerDead);
        partner.RpcMurderPlayer(partner, true);
    }

    // 自分が会議で追放されたら相方(ブラックマッドメイト)を道連れにする
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (PartnerId == byte.MaxValue || PartnerDead) return;
        if (exiled.PlayerId != Player.PlayerId) return;

        var partner = PlayerCatch.GetPlayerById(PartnerId);
        if (partner?.IsAlive() != true) return;

        PartnerDead = true;
        SendRPC(RPC_type.SetPartnerDead);
        partner.RpcExileV3();
    }

    // すべてのクルーが黒く見える。ただしブラックマッドメイト(相方)だけは真の色が見える。
    // シェイプ後・イントロ後・タスクターン開始時に自動で呼ばれる。
    public override void ChangeColor()
    {
        if (!Player.IsAlive()) return;

        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc == Player) continue;
            if (PartnerId != byte.MaxValue && !PartnerDead && pc.PlayerId == PartnerId) continue;

            pc.RpcChColor(Player, 15, true); // 15 = 黒
        }
    }
}
