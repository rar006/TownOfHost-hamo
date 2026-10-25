using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Hazel;
using UnityEngine;


public static class VentLock
{
    // 通れなくするベントの組み合わせ (両方向)
    static readonly (int, int)[] BlockedPairs =
    {
        (6, 7),   // 管理室 ⇔ 温室
        (6, 5),   // 管理室 ⇔ オフィス
        (5, 4),   // オフィス ⇔ 研究室
        (4, 3),   // 研究室 ⇔ 原子炉
        (3, 9),   // 原子炉 ⇔ 除染室
        (9, 10),  // 除染室 ⇔ ロッカールーム
        (10, 11), // ロッカールーム ⇔ 発射台
        (10, 8),  // ロッカールーム ⇔ 医務室
        (1, 2),   // バルコニー ⇔ 廊下
    };

    // 最後にその人へ送った「掃除中に見せたベント」
    static readonly Dictionary<byte, string> LastSent = new();
    static float nextRefresh;
    public static bool ResendAll;

    // ミラHQ (マップ番号 1) か
    public static bool IsMira() =>
        GameOptionsManager.Instance != null && GameOptionsManager.Instance.CurrentGameOptions.MapId == 1;

    public static bool IsBlocked(int a, int b)
    {
        foreach (var (x, y) in BlockedPairs)
            if ((x == a && y == b) || (x == b && y == a)) return true;
        return false;
    }

    public static void Reset()
    {
        LastSent.Clear();
        nextRefresh = 0f;
    }

    // 毎フレーム: その人が今いるベントから行けないベントを、その人にだけ「掃除中」と送る
    public static void Update()
    {
        if (!ShipStatus.Instance.Systems.TryGetValue(SystemTypes.Ventilation, out ISystemType sys)) return;
        VentilationSystem vs = sys.TryCast<VentilationSystem>();
        if (vs == null) return;

        bool refresh = ResendAll || Time.time >= nextRefresh;
        if (Time.time >= nextRefresh) nextRefresh = Time.time + 1f;
        ResendAll = false;

        foreach (PlayerControl pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;

            List<byte> fake = new();
            if (vs.PlayersInsideVents.TryGetValue(pc.PlayerId, out byte inVent))
                foreach (Vent v in ShipStatus.Instance.AllVents)
                    if (v.Id == inVent)
                        foreach (Vent next in v.NearbyVents)
                            if (next != null && IsBlocked(inVent, next.Id)) fake.Add((byte)next.Id);

            string now = string.Join(",", fake);
            LastSent.TryGetValue(pc.PlayerId, out string last);
            if (now == (last ?? "") && !(refresh && fake.Count > 0)) continue;

            Send(vs, pc, fake);
            LastSent[pc.PlayerId] = now;
        }
    }

    // その人にだけ、ベントの情報 (掃除中のベント + ベントの中にいる人) を送る
    static void Send(VentilationSystem vs, PlayerControl target, List<byte> fake)
    {
        Dictionary<byte, byte> cleaning = new();
        foreach (var kv in vs.PlayersCleaningVents) cleaning[kv.Key] = kv.Value;

        // 「掃除している人」の番号は、空いている番号を借りる (本人以外)
        byte id = 250;
        foreach (byte vent in fake)
        {
            while (cleaning.ContainsKey(id) || id == target.PlayerId) id--;
            cleaning[id] = vent;
        }

        MessageWriter w = MessageWriter.Get(SendOption.Reliable);
        w.StartMessage(6); // 特定の1人にだけ送る
        w.Write(AmongUsClient.Instance.GameId);
        w.WritePacked(target.OwnerId);
        w.StartMessage(1);
        w.WritePacked(ShipStatus.Instance.NetId);
        w.StartMessage((byte)SystemTypes.Ventilation);
        w.WritePacked(cleaning.Count);
        foreach (var kv in cleaning) { w.Write(kv.Key); w.Write(kv.Value); }
        w.WritePacked(vs.PlayersInsideVents.Count);
        foreach (var kv in vs.PlayersInsideVents) { w.Write(kv.Key); w.Write(kv.Value); }
        w.EndMessage();
        w.EndMessage();
        w.EndMessage();
        AmongUsClient.Instance.SendOrDisconnect(w);
        w.Recycle();
    }
}

// 試合が始まったらリセット
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
public static class ResetPatch
{
    public static void Postfix() => VentLock.Reset();
}

// 本当のベントの情報が全員に送られたら、送り直す
[HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.Serialize))]
public static class SerializePatch
{
    public static void Postfix() => VentLock.ResendAll = true;
}

// 毎フレーム (ホストだけ・ミラHQだけ)
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class UpdatePatch
{
    public static BepInEx.Logging.ManualLogSource Logger;
    public static void Postfix(PlayerControl __instance)
    {
        if (__instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost || !VentLock.IsMira()) return;
        try { VentLock.Update(); }
        catch (System.Exception e) { Logger.LogError(e); }
    }
}

// 自分の画面: 行けない矢印に掃除中マークを付ける
[HarmonyPatch(typeof(Vent), nameof(Vent.UpdateArrows))]
public static class ArrowPatch
{
    public static void Postfix(Vent __instance)
    {
        if (!VentLock.IsMira()) return;
        var nearby = __instance.NearbyVents;
        var marks = __instance.CleaningIndicators;
        for (int i = 0; i < nearby.Length && i < marks.Length; i++)
            if (nearby[i] != null && marks[i] != null && VentLock.IsBlocked(__instance.Id, nearby[i].Id))
                marks[i].SetActive(true);
    }
}

// 自分の画面: 行けない矢印を押しても移動しない
[HarmonyPatch(typeof(Vent), nameof(Vent.TryMoveToVent))]
public static class MovePatch
{
    public static bool Prefix(Vent __instance, Vent otherVent, ref bool __result)
    {
        if (!VentLock.IsMira() || otherVent == null || !VentLock.IsBlocked(__instance.Id, otherVent.Id)) return true;
        __result = false;
        return false;
    }
}