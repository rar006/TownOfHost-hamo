using System.Collections.Generic;
using HarmonyLib;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Impostor;
using TownOfHost.Roles.Neutral;
using UnityEngine;

namespace TownOfHost.Modules;

// ペンギン・アライグマにつかまった人を、MOD を入れている人の画面では「引きずられている」ように見せる。
// 動かすのは体の見た目(cosmetics)だけで、プレイヤー本体の位置や通信には触らない。
// ホストは 3 フレームに 1 回しか位置を合わせないので、そのままだとガクガク重なって見えていた
public static class DragVisual
{
    sealed class Pull
    {
        public Vector3 pos, baseLocal;
        public float side = 1f;
        public bool started;
    }

    static readonly Dictionary<byte, Pull> pulls = new();
    static readonly Dictionary<byte, PlayerControl> nowDragged = new();
    static readonly List<byte> ended = new();

    static void Collect()
    {
        nowDragged.Clear();
        if (!GameStates.IsInTask || GameStates.IsMeeting) return;
        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
        {
            PlayerControl dragger = null, victim = null;
            if (role is Penguin penguin && penguin.DragVictim != null)
            {
                dragger = penguin.Player;
                victim = penguin.DragVictim;
            }
            else if (role is RaccoonParent raccoon && raccoon.GrabbedTargetId != byte.MaxValue)
            {
                dragger = raccoon.Player;
                victim = PlayerCatch.GetPlayerById(raccoon.GrabbedTargetId);
            }
            if (dragger == null || victim == null || !dragger.IsAlive() || !victim.IsAlive()) continue;
            nowDragged[victim.PlayerId] = dragger;
        }
    }

    public static void Update()
    {
        Collect();
        foreach (var (id, dragger) in nowDragged)
        {
            var victim = PlayerCatch.GetPlayerById(id);
            var cos = victim?.cosmetics;
            if (cos == null || cos.transform == victim.transform) continue;
            if (victim.MyPhysics.Animations.IsPlayingAnyLadderAnimation() || dragger.inVent || victim.inVent)
            {
                Reset(victim);
                continue;
            }
            if (!pulls.TryGetValue(id, out var p)) pulls[id] = p = new Pull();

            // 引っぱる人の背中側。向きを変えた時は、ゆっくり反対側へ回り込む
            var facing = dragger.cosmetics.FlipX ? -1f : 1f;
            p.side = Mathf.MoveTowards(p.side, -facing, Time.deltaTime * 4f);
            var from = dragger.transform.position;
            var goal = new Vector3(from.x + p.side * 0.5f, from.y - 0.12f, victim.transform.position.z);
            if (!p.started)
            {
                p.pos = cos.transform.position;
                p.baseLocal = cos.transform.localPosition;
                p.started = true;
            }
            // 少し遅れて付いてくる
            p.pos = Vector3.Lerp(p.pos, goal, 1f - Mathf.Exp(-Time.deltaTime * 9f));
            cos.transform.position = new Vector3(p.pos.x, p.pos.y, cos.transform.position.z);

            // 足を持たれて寝かされた向きに傾け、動いている間は小さく揺らす
            var moving = dragger.MyPhysics?.body != null && dragger.MyPhysics.body.velocity.sqrMagnitude > 0.01f;
            var wobble = moving ? Mathf.Sin(Time.time * 16f) * 6f : 0f;
            cos.transform.localRotation = Quaternion.Euler(0f, 0f, (p.side >= 0f ? 70f : -70f) + wobble);
        }

        // つかまるのが終わった人を元に戻す
        ended.Clear();
        foreach (var id in pulls.Keys) if (!nowDragged.ContainsKey(id)) ended.Add(id);
        foreach (var id in ended)
        {
            Reset(PlayerCatch.GetPlayerById(id));
            pulls.Remove(id);
        }
    }

    static void Reset(PlayerControl pc)
    {
        if (pc == null || pc.cosmetics == null || pc.cosmetics.transform == pc.transform) return;
        if (!pulls.TryGetValue(pc.PlayerId, out var p) || !p.started) return;
        pc.cosmetics.transform.localPosition = p.baseLocal;
        pc.cosmetics.transform.localRotation = Quaternion.identity;
        p.started = false;
    }
}

[HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
static class DragVisualPatch
{
    static void Postfix()
    {
        try { DragVisual.Update(); } catch { }
    }
}
