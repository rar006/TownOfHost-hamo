using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using InnerNet;

namespace TownOfHost.Modules;

// 役職の名前表示(SetName を見る人ごとに送る)を、送り先ごとにまとめて送る。
// 以前は「見られる人 × 見る人」の組み合わせごとに1つずつパケットを送っていたため、
// 10人いると名前の更新1回で100通近くになり、参加者(特にSwitch・スマホ)の回線が詰まってpingが不安定になったり、
// 切断されたりしていた。1フレームの間に溜めて、送り先1人につき1つ(大きければ数個)のパケットにする。
// 同じフレームに同じ名前を何度も更新した時は、最後のものだけ送る。
public static class NameRpcBatch
{
    private const int MaxBytesPerPacket = 900;

    private sealed class Entry
    {
        public uint NetId;
        public uint DataNetId;
        public string Name;
        public bool DontShowOnModdedClient;
    }
    // 送り先 → (名前を変える人のNetId → 中身)。並び順は最初に足した順
    private static readonly Dictionary<int, List<Entry>> queue = new();

    public static void Add(int clientId, uint netId, uint dataNetId, string name, bool dontShowOnModdedClient)
    {
        if (!queue.TryGetValue(clientId, out var list)) queue[clientId] = list = new List<Entry>();
        foreach (var e in list)
        {
            if (e.NetId != netId) continue;
            e.Name = name;
            e.DontShowOnModdedClient = dontShowOnModdedClient;
            e.DataNetId = dataNetId;
            return;
        }
        list.Add(new Entry { NetId = netId, DataNetId = dataNetId, Name = name, DontShowOnModdedClient = dontShowOnModdedClient });
    }

    private static bool flushing;

    public static void Flush()
    {
        if (queue.Count == 0 || flushing) return;
        flushing = true;
        try { FlushCore(); }
        finally { flushing = false; }
    }

    private static void FlushCore()
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || !client.AmConnected)
        {
            queue.Clear();
            return;
        }
        foreach (var (clientId, list) in queue)
        {
            MessageWriter w = null;
            void Begin()
            {
                w = MessageWriter.Get(SendOption.None);
                w.StartMessage(6); // GameDataTo
                w.Write(client.GameId);
                w.WritePacked(clientId);
            }
            void Send()
            {
                if (w == null) return;
                w.EndMessage();
                client.SendOrDisconnect(w);
                w.Recycle();
                w = null;
            }
            foreach (var e in list)
            {
                if (w != null && w.Length + e.Name.Length * 3 + 16 > MaxBytesPerPacket) Send();
                if (w == null) Begin();
                w.StartMessage(2); // RPC
                w.WritePacked(e.NetId);
                w.Write((byte)RpcCalls.SetName);
                w.Write(e.DataNetId);
                w.Write(e.Name);
                w.Write(e.DontShowOnModdedClient);
                w.EndMessage();
            }
            Send();
        }
        queue.Clear();
    }

    // ほかの通信を送る直前に、溜まっている名前を先に送る(会議の開始などより名前が後に届かないように、送る順番を守る)
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect)), HarmonyPriority(1000)]
    private static class KeepOrderPatch
    {
        public static void Prefix()
        {
            if (flushing || queue.Count == 0) return;
            try { Flush(); }
            catch (System.Exception e)
            {
                queue.Clear();
                Logger.Warn($"名前のまとめ送信に失敗: {e.Message}", "NameRpcBatch");
            }
        }
    }

    // 毎フレームの最後の方でまとめて送る(ゲームの通信処理と同じタイミング)
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.FixedUpdate))]
    private static class FlushPatch
    {
        public static void Postfix()
        {
            try { Flush(); }
            catch (System.Exception e)
            {
                queue.Clear();
                Logger.Warn($"名前のまとめ送信に失敗: {e.Message}", "NameRpcBatch");
            }
        }
    }
}
