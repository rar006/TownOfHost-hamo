using AmongUs.GameOptions;
using UnityEngine;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Patches;

namespace TownOfHost.Roles.Madmate;

// ===== マッドスクリーム (MadScream) =====
// カラーコード: #FF1919
// イントロ：【魂の叫び】
// 陣営：マッドメイト / 置き換え：インポスター
//
// ペットを撫でた地点にマーカーを設置する(再設置はできない)。
// 自身がキルされた時、マーカーにワープしてから死亡しノイズを鳴らす。
// マーカーを設置する前に死亡した時は、ワープせずにノイズを鳴らす。
//
// 【設定変更について】以前はファントムベースで実装しており、透明化ボタンや
// そのクールタイム管理と絡んでマーカー設置が正しく機能しない不具合があった。
// 要望により置き換えをインポスターに変更し、そのぶんシンプルな構成にした。
// キルボタンは使用させない(マーカー設置は引き続きペットのみで行う)。
public sealed class MadScream : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadScream),
            player => new MadScream(player),
            CustomRoles.MadScream,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Madmate,
            96300,
            SetupOptionItem,
            "Msc",
            "#FF1919",
            (5, 3),
            from: From.TownOfHost_hamo
        );

    public MadScream(PlayerControl player) : base(RoleInfo, player)
    {
        hasMarker = false;
        markerPosition = Vector2.zero;

        // 【重要・バグ修正】以前はコンストラクタでのみPetActionManager.Registerを
        // 呼んでいたが、何らかの理由で登録が反映されない(=ペットを押してもマーカーが
        // 設置されない)ケースに備え、OnSpawnでも再登録して確実性を高める。
        PetActionManager.Register(player.PlayerId, OnPetUsed);
    }

    static OptionItem OptionCanUseVent;

    enum OptionName
    {
    }

    // マーカーが設置済みかどうか(再設置は不可)
    private bool hasMarker;
    private Vector2 markerPosition;

    static void SetupOptionItem()
    {
        OptionCanUseVent = BooleanOptionItem.Create(RoleInfo, 10, GeneralOption.CanVent, true, false);
    }

    public override bool CanClickUseVentButton => OptionCanUseVent.GetBool();

    // インポスターだが、このロールはキルボタンを使わせない(マーカー設置はペットのみ)。
    public bool CanUseKillButton() => false;

    public override void OnSpawn(bool initialState = false)
    {
        // コンストラクタでの登録が何らかの理由で失われていても復旧できるよう、
        // スポーン時にも再登録する。
        PetActionManager.Register(Player.PlayerId, OnPetUsed);
    }

    private void OnPetUsed()
    {
        Logger.Info($"{Player.Data?.GetLogPlayerName()}(MadScream)のOnPetUsedが呼ばれました " +
            $"IsAlive={Player.IsAlive()} hasMarker={hasMarker}", "MadScream");

        if (!Player.IsAlive() || hasMarker) return;

        hasMarker = true;
        markerPosition = Player.GetTruePosition();

        Utils.SendMessage(GetString("MadScream.MarkerSet"), Player.PlayerId);

        UtilsGameLog.AddGameLog("MadScream",
            $"{UtilsName.GetPlayerColor(Player)}がマーカーを設置した");
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (!Is(info.AttemptTarget)) return true;

        // マーカーが設置済みであれば、死亡確定前にそこへワープさせておく
        // (ワープ後にバニラの死亡処理へ進み、そのままそこで息絶える形になる)。
        if (hasMarker)
        {
            Player.RpcSnapToForced(markerPosition);
        }

        // 要望により、死体をNoisemakerとして全員に同期する。
        // 過去はOnMurderPlayerAsTarget(RpcMurderPlayer実行後のフック)でRpcSetRoleしていたが、
        // バニラのNoisemakerアラート演出はRpcMurderPlayerが飛ぶ時点で役職がNoisemakerに
        // なっているかどうかで判定されるため、死亡RPC後に切り替えても間に合わず
        // 演出が発生しなかった。OnCheckMurderAsTarget(RpcMurderPlayer実行前のフック)に
        // 移すことで、死亡確定前に役職をNoisemakerへ切り替え、正しくアラートを鳴らす。
        Player.RpcSetRole(RoleTypes.Noisemaker, false);
        Player.SyncSettings();

        return true;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || !Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        var prefix = isForHud ? "" : "<size=60%>";
        var c = RoleInfo.RoleColorCode;
        var stateText = hasMarker ? "マーカー設置済み" : "ペットでマーカーを設置できます";
        return $"{prefix}<color={c}>{stateText}</color>";
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
