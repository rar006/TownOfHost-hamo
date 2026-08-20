using HarmonyLib;
using TownOfHost.Roles.Core;

namespace TownOfHost.Patches;

/// <summary>
/// AirShipでGMの場合のみ、湧き場所選択画面(SpawnInMinigame。
/// 「宿舎前通路/エンジンルーム/貨物室」等を選ぶあの画面)を自動で閉じる。
///
/// あくまで見た目だけの処理(クライアント側のみ)。
/// 実際の湧き位置は RandomSpawnPatch.AirshipSpawn 内の
/// 「player.Is(CustomRoles.GM) → new AirshipSpawnMap().FirstTeleport(player)」
/// が別途・独立に処理しているので、ここでUIを閉じても湧き位置には影響しない。
///
/// 過去に AirshipStatus.PrespawnStep をPrefixで潰す方式を試したが、
/// GMの画面が暗転しやすくなる不具合が出たため無効化されていた
/// (Patches/AirshipStatus.cs参照)。
/// 今回はBegin完了直後にCloseする方式にすることで、
/// Begin内部の初期化(カメラ制御等)を素通りさせず、UIだけを畳む。
/// </summary>
[HarmonyPatch(typeof(SpawnInMinigame), nameof(SpawnInMinigame.Begin))]
public static class GMSkipAirshipSpawnMinigamePatch
{
    public static void Postfix(SpawnInMinigame __instance)
    {
        var local = PlayerControl.LocalPlayer;
        if (local == null) return;
        if (!local.Is(CustomRoles.GM)) return;
        if (!GMAutoPossessTiming.IsAirship()) return;

        __instance.Close();
    }
}
