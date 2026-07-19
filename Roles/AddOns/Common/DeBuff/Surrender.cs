using System.Collections.Generic;
using UnityEngine;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Attributes;
using static TownOfHost.Options;

namespace TownOfHost.Roles.AddOns.Common;

// ===== サレンダー (Surrender) =====
// From: TownOfHost_hamo
// イントロ：【全部吐くから、殺さないで！】
// 陣営：属性
//
// キルされた時、キル者にだけ自身の役職を暴露してしまう(次の会議で、キル者の視点にのみ
// 本来の役職タグが名前の上に表示される。かけだし占い師と同じ見せ方)。
// 設定次第で、キルをガードした場合でも役職を暴露する。
//
// 【実装メモ】「ガードされた場合の暴露」は、既存のOthersフック群だけでは
// (誰が誰をガードで防いだか)を汎用的に検知する仕組みが見当たらなかったため、
// CheckMurderInfos(直近のキル未遂情報)を毎Tickポーリングして、
// 「対象がサレンダー保持者かつ生存中(＝キルが成立しなかった)」を近似的に検知している。
// 実機での挙動は未検証なので、意図通りに動かない場合は教えてください。
public static class Surrender
{
    private static readonly int Id = 74200;
    private static Color RoleColor = UtilsRoleText.GetRoleColor(CustomRoles.Surrender);
    public static string SubRoleMark = Utils.ColorString(RoleColor, "s");
    private static List<byte> playerIdList = new();

    // 暴露済み: 被害者PlayerId -> (キル者/未遂キル者PlayerId)
    // RPC.cs (SyncSurrenderReveal) から非ホストクライアントでも書き込むためinternal。
    internal static Dictionary<byte, byte> RevealedTo = new();
    // ガード検知の二重発火防止用
    private static HashSet<byte> GuardCheckedKillers = new();

    public static OptionItem RevealOnGuard;

    // ホストが暴露を確定させた際に、対象のキル者クライアントだけへ同期する。
    // (ホストの内部辞書に書くだけでは、キル者が非ホストの場合その本人には伝わらないため)
    private static void SyncRevealToKiller(byte targetId, byte killerId)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var killerPc = PlayerCatch.GetPlayerById(killerId);
        if (killerPc == null) return;

        var sender = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncModSystem, SendOption.Reliable, killerPc.GetClientId());
        sender.Write((int)RPC.ModSystem.SyncSurrenderReveal);
        sender.Write(targetId);
        sender.Write(killerId);
        AmongUsClient.Instance.FinishRpcImmediately(sender);
    }

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Surrender, fromtext: UtilsOption.GetFrom(From.TownOfHost_hamo));
        AddOnsAssignData.Create(Id + 10, CustomRoles.Surrender, true, true, true, true);

        ObjectOptionitem.Create(Id + 50, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Surrender);
        RevealOnGuard = BooleanOptionItem.Create(Id + 51, "SurrenderRevealOnGuard", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Surrender);
    }

    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        RevealedTo = new();
        GuardCheckedKillers = new();

        CustomRoleManager.OnMurderPlayerOthers.Add(OnMurderPlayerOthers);
        CustomRoleManager.SuffixOthers.Add(GetSuffix);
        CustomRoleManager.OnFixedUpdateOthers.Add(OnFixedUpdateOthers);
    }

    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId);
    }

    // 通常通りキルが成立した場合: キル者に暴露
    private static void OnMurderPlayerOthers(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var target = info.AttemptTarget;
        if (!playerIdList.Contains(target.PlayerId)) return;

        var (killer, _) = info.AppearanceTuple;
        if (killer == null) return;

        RevealedTo[target.PlayerId] = killer.PlayerId;
        SyncRevealToKiller(target.PlayerId, killer.PlayerId);
    }

    // ガードされて死ななかった場合の近似検知(未検証)
    private static void OnFixedUpdateOthers(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!RevealOnGuard.GetBool()) return;
        if (playerIdList.Count == 0) return;

        foreach (var kvp in CustomRoleManager.CheckMurderInfos)
        {
            var killerId = kvp.Key;
            var info = kvp.Value;
            if (info?.AttemptTarget == null) continue;
            var targetId = info.AttemptTarget.PlayerId;

            if (!playerIdList.Contains(targetId)) continue;
            if (RevealedTo.ContainsKey(targetId)) continue; // 既に暴露済み
            if (GuardCheckedKillers.Contains(killerId)) continue;

            var targetPc = PlayerCatch.GetPlayerById(targetId);
            if (targetPc == null || !targetPc.IsAlive()) continue; // 生存している=ガードで防いだ可能性が高い

            GuardCheckedKillers.Add(killerId);
            RevealedTo[targetId] = killerId;
            SyncRevealToKiller(targetId, killerId);
        }
    }

    // 会議中、暴露対象になったキル者(未遂含む)にだけ本来の役職を見せる(かけだし占い師と同じ見せ方)
    public static string GetSuffix(PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        if (!isForMeeting) return "";
        if (seer == null || seen == null) return "";
        if (!RevealedTo.TryGetValue(seen.PlayerId, out var killerId)) return "";
        if (seer.PlayerId != killerId) return "";

        var role = seen.GetCustomRole();
        return "\n" + Utils.ColorString(UtilsRoleText.GetRoleColor(role), UtilsRoleText.GetRoleName(role));
    }
}
