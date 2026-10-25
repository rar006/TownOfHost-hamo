using System;
using System.Collections.Generic;
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

    public static bool Block(byte callId)
        => callId >= FirstCustomRpc && AmongUsClient.Instance != null && !AmongUsClient.Instance.AmHost;

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
