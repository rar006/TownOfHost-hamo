using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hazel;

namespace TownOfHost.Modules;

// ホスト以外が MOD を入れていても、MOD 独自の通信(RPC)は送らない。
// ホストからは MOD の無い参加者と同じに見えるので、コマンドなども MOD の無い人と同じ流れで処理される
// (MOD 入りの参加者の RPC と、ホストの処理がぶつかっておかしくなっていた)
public static class NonHostRpcGuard
{
    private const byte FirstCustomRpc = 80;
    private static readonly HashSet<IntPtr> dummies = new();
    // PlayerId は抜けた人の番号が次の人に使い回されるので、入れた時の接続番号も覚えておき、別の人を MOD 入りと間違えないようにする
    private static readonly Dictionary<byte, int> hiddenModClients = new();

    public static bool Block(byte callId)
        => callId >= FirstCustomRpc && callId != (byte)CustomRPC.HiddenModHello
            // モデレーターが設定画面から変えた値は、ホストに送る
            && callId != (byte)CustomRPC.ModeratorSetOption && callId != (byte)CustomRPC.ModeratorSetVanilla
            && AmongUsClient.Instance != null && !AmongUsClient.Instance.AmHost;

    public static void MarkHidden(PlayerControl pc)
    {
        if (pc == null) return;
        var playerId = pc.PlayerId;
        var clientId = pc.GetClientId();
        var isNew = !hiddenModClients.TryGetValue(playerId, out var old) || old != clientId;
        hiddenModClients[playerId] = clientId;
        if (isNew) Logger.Info($"ID{playerId} はMODを入れていますが、MODの無い人と同じに扱います(設定だけ送ります)", "NonHostRpcGuard");
    }

    // 「MODを入れた参加者をキックする」がONの時、ホスト以外でMODを入れている人をキックする(ホワイトリストの人は除く)
    public static bool KickIfNotAllowed(PlayerControl pc)
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || pc == null || pc.AmOwner) return false;
        if (Options.KickModClient?.GetBool() != true) return false;
        var client = pc.GetClient();
        if (client == null || BanManager.CheckWhiteList(client.FriendCode, client.ProductUserId)) return false;
        AmongUsClient.Instance.KickPlayer(client.Id, false);
        Logger.Info($"{client.PlayerName}({client.FriendCode})はMODを入れているのでキックしました", "Kick");
        Logger.seeingame(string.Format(Translator.GetString("Message.KickedModClient"), client.PlayerName));
        return true;
    }

    public static void ClearHidden() => hiddenModClients.Clear();

    public static bool IsHidden(byte playerId)
    {
        if (!hiddenModClients.TryGetValue(playerId, out var clientId)) return false;
        var pc = PlayerCatch.GetPlayerById(playerId);
        return pc != null && pc.GetClientId() == clientId;
    }
    public static bool AnyHiddenModClient => hiddenModClients.Keys.Any(IsHidden);
    // 送らない RPC 用の空の入れ物。FinishRpcImmediately で捨てる
    public static MessageWriter Dummy(SendOption option)
    {
        var writer = MessageWriter.Get(option);
        dummies.Add(writer.Pointer);
        return writer;
    }

    public static bool TryDiscard(MessageWriter writer)
    {
        if (writer == null || !dummies.Remove(writer.Pointer)) return false;
        writer.Recycle();
        return true;
    }
}

[HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.FinishRpcImmediately))]
internal class FinishRpcImmediatelyGuardPatch
{
    public static bool Prefix([HarmonyArgument(0)] MessageWriter msg) => !NonHostRpcGuard.TryDiscard(msg);
}
