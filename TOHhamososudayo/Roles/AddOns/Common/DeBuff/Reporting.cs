using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using TownOfHost.Roles.Core;
using TownOfHost.Attributes;
using static TownOfHost.Options;

namespace TownOfHost.Roles.AddOns.Common;

// ===== レポーティング (Reporting) =====
// From: TownOfHost_hamo
// イントロ：通報しないと
// 陣営：--- / 置き換え：---
//
// 通報ボタンが光る(＝通報可能な死体が範囲内にある)と、必ず通報してしまう属性。
// 設定次第でトランスパレント(スカベンジャー的な、死体を通報させない属性)を貫通して通報できる。
//
// 【実装メモ】「ボタンが光る」の正確な内部フラグは確認できなかったため、
// バニラの通報可能距離(IGameOptions.PlayerReportDistance)を使って
// 「範囲内に未通報の死体があるか」を毎Tick判定する形で近似実装している。実機未検証。
public static class Reporting
{
    private static readonly int Id = 74100;
    private static Color RoleColor = UtilsRoleText.GetRoleColor(CustomRoles.Reporting);
    public static string SubRoleMark = Utils.ColorString(RoleColor, "Ⓡ");
    private static List<byte> playerIdList = new();

    public static OptionItem PenetrateTransparent;
    public static OptionItem DetectRange;

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Reporting, fromtext: UtilsOption.GetFrom(From.TownOfHost_hamo));
        AddOnsAssignData.Create(Id + 10, CustomRoles.Reporting, true, true, true, true);

        ObjectOptionitem.Create(Id + 50, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Reporting);
        PenetrateTransparent = BooleanOptionItem.Create(Id + 51, "ReportingPenetrateTransparent", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Reporting);
        // バニラの「通報ボタンが光る距離(内部フラグ)」を直接参照する確実な方法が確認できなかったため、
        // 独自の検知範囲として設定できるようにしている(既定1.5=バニラの通報距離に近い値)。
        DetectRange = FloatOptionItem.Create(Id + 52, "ReportingDetectRange", new(0.5f, 5f, 0.25f), 1.5f, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Reporting);
    }

    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        CustomRoleManager.OnFixedUpdateOthers.Add(OnFixedUpdateOthers);
    }

    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId);
    }

    public static bool CanPenetrateTransparent(PlayerControl reporter)
        => reporter != null && playerIdList.Contains(reporter.PlayerId) && PenetrateTransparent.GetBool();

    private static void OnFixedUpdateOthers(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;
        if (player == null || !playerIdList.Contains(player.PlayerId)) return;
        if (!player.IsAlive()) return;

        var reportDistance = DetectRange.GetFloat();
        var myPos = player.GetTruePosition();

        foreach (var body in UnityEngine.Object.FindObjectsOfType<DeadBody>())
        {
            if (body == null || (UnityEngine.Object)(object)body.gameObject == null) continue;

            var bodyPos = body.transform.position;
            if (Vector2.Distance(myPos, bodyPos) > reportDistance) continue;

            var targetInfo = GameData.Instance.GetPlayerById(body.ParentId);
            if (targetInfo == null) continue;

            var targetPc = PlayerCatch.GetPlayerById(body.ParentId);
            if (!CanPenetrateTransparent(player) &&
                (targetPc?.Is(CustomRoles.Transparent) == true || Transparent.playerIdList.Contains(body.ParentId)))
                continue; // トランスパレントは貫通設定がなければスルー

            ReportDeadBodyPatch.ExReportDeadBody(player, targetInfo);
            return;
        }
    }
}
