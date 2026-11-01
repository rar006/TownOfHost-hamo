using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TownOfHost.Modules.AiPlayer;

// 壁を避けて歩くための道探し。マップを細かいマス目に分けて、壁にぶつからないマスだけを通る道を A* で探す。
public static class AiPath
{
    private const float Step = 0.3f;
    private const int MaxExpand = 60000;

    // 壁の判定は、プレイヤーが実際にぶつかる層(レイヤー)と体の大きさで行う。
    // (決まった層だけで見ていると、そこに入っていない壁を「通れる」と思い、壁へまっすぐ歩き続けていた)
    private static int mask = 0;
    private static float radius = 0.3f;

    private static readonly Dictionary<(int, int), bool> walkableCache = new();
    // 歩いてみて引っかかった所(見落とした壁)。次からは通らない
    private static readonly HashSet<(int, int)> blocked = new();

    // はしご: 歩いてはつながっていない場所どうしをつなぐ(エアシップは、はしごでしか行けない所が多く、道が見つからなかった)
    private sealed class Link
    {
        public (int, int) From, To;
        public Vector2 FromPos, ToPos;
        public Ladder Ladder;
    }
    private static readonly Dictionary<(int, int), List<Link>> links = new();
    private static bool linksBuilt;

    public static void Reset()
    {
        walkableCache.Clear();
        blocked.Clear();
        links.Clear();
        linksBuilt = false;
        mask = 0;
    }

    private static void BuildLinks()
    {
        if (linksBuilt) return;
        linksBuilt = true;
        try
        {
            foreach (var ladder in Object.FindObjectsOfType<Ladder>())
            {
                if (ladder == null || ladder.Destination == null) continue;
                var from = NearestWalkable(ToCell(ladder.transform.position));
                var to = NearestWalkable(ToCell(ladder.Destination.transform.position));
                if (from == to) continue;
                var link = new Link { From = from, To = to, FromPos = ToWorld(from), ToPos = ToWorld(to), Ladder = ladder };
                if (!links.TryGetValue(from, out var list)) links[from] = list = new();
                list.Add(link);
            }
            if (links.Count > 0) Logger.Info($"AIが使えるはしご: {links.Values.Sum(l => l.Count)}本", "AiPlayers");
        }
        catch (System.Exception e) { Logger.Warn($"はしごを調べられませんでした: {e.Message}", "AiPlayers"); }
    }

    // 道の a の点から b の点へ、はしごを使うか
    public static Ladder LadderBetween(Vector2 a, Vector2 b)
    {
        if (!links.TryGetValue(ToCell(a), out var list)) return null;
        foreach (var l in list)
            if ((l.ToPos - b).sqrMagnitude < 0.0001f && (l.FromPos - a).sqrMagnitude < 0.0001f) return l.Ladder;
        return null;
    }

    public static void Setup(PlayerControl pc)
    {
        if (mask != 0 || pc == null) return;
        try
        {
            var layer = pc.gameObject.layer;
            var m = Physics2D.GetLayerCollisionMask(layer) & ~(1 << layer);
            foreach (var name in new[] { "Players", "Ghost", "UI" })
            {
                var l = LayerMask.NameToLayer(name);
                if (l >= 0) m &= ~(1 << l);
            }
            mask = m | Constants.ShipAndObjectsMask;
            if (pc.Collider is CircleCollider2D circle)
                radius = Mathf.Clamp(circle.radius * Mathf.Abs(pc.transform.lossyScale.x) + 0.08f, 0.2f, 0.45f);
        }
        catch (System.Exception e)
        {
            mask = Constants.ShipAndObjectsMask;
            Logger.Warn($"AIの壁の判定を作れませんでした(いつもの判定を使います): {e.Message}", "AiPlayers");
        }
        Logger.Info($"AIの壁の判定: mask={mask} radius={radius:0.00}", "AiPlayers");
    }

    private static int Mask => mask != 0 ? mask : Constants.ShipAndObjectsMask;

    private static Vector2 ToWorld((int x, int y) c) => new(c.x * Step, c.y * Step);
    private static (int, int) ToCell(Vector2 p) => (Mathf.RoundToInt(p.x / Step), Mathf.RoundToInt(p.y / Step));

    private static bool Walkable((int, int) c)
    {
        if (blocked.Contains(c)) return false;
        if (walkableCache.TryGetValue(c, out var ok)) return ok;
        // 部屋の範囲などの見えない当たり判定(トリガー)は壁として数えない
        var old = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = false;
        try { ok = Physics2D.OverlapCircle(ToWorld(c), radius, Mask) == null; }
        finally { Physics2D.queriesHitTriggers = old; }
        walkableCache[c] = ok;
        return ok;
    }

    // 引っかかった所の先を、通れない所として覚える
    public static void MarkBlocked(Vector2 pos, Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        var ahead = pos + dir.normalized * (radius + Step * 0.5f);
        var c = ToCell(ahead);
        if (c != ToCell(pos)) blocked.Add(c);
    }

    // 2点の間を、体の幅で通れるか
    public static bool Clear(Vector2 a, Vector2 b)
    {
        var d = b - a;
        var dist = d.magnitude;
        if (dist < 0.01f) return true;
        var old = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = false;
        try
        {
            var hit = Physics2D.CircleCast(a, radius * 0.9f, d / dist, dist, Mask);
            if (hit.collider != null) return false;
        }
        finally { Physics2D.queriesHitTriggers = old; }
        // 覚えた「引っかかった所」をまたがないか
        if (blocked.Count > 0)
        {
            var n = Mathf.CeilToInt(dist / Step);
            for (var i = 1; i <= n; i++)
                if (blocked.Contains(ToCell(a + d * (i / (float)n)))) return false;
        }
        return true;
    }

    // 目の中で見通しがきく(壁にさえぎられない)場所
    public static bool LineOfSight(Vector2 a, Vector2 b)
        => !PhysicsHelpers.AnyNonTriggersBetween(a, (b - a).normalized, (b - a).magnitude, Constants.ShipAndObjectsMask);

    // 歩いて行ける、goal にいちばん近い場所(台の上などは歩けないので、その手前)
    public static Vector2 Reachable(Vector2 goal) => ToWorld(NearestWalkable(ToCell(goal)));

    // start から goal までの道(通る点のリスト)。見つからない時は null
    public static List<Vector2> Find(Vector2 start, Vector2 goal)
    {
        BuildLinks();
        var g = NearestWalkable(ToCell(goal));
        var goalPos = Walkable(ToCell(goal)) ? goal : ToWorld(g);
        if (Clear(start, goalPos)) return new List<Vector2> { goalPos };
        var s = ToCell(start);
        if (!Walkable(s)) s = NearestWalkable(s);
        var open = new SortedSet<(float f, int id)>();
        var cells = new List<(int, int)>();
        var index = new Dictionary<(int, int), int>();
        var gScore = new List<float>();
        var parent = new List<int>();
        var viaLadder = new List<bool>();
        var closed = new HashSet<int>();
        int Add((int, int) c, float gs, int from, bool ladder = false)
        {
            var id = cells.Count;
            cells.Add(c); gScore.Add(gs); parent.Add(from); viaLadder.Add(ladder); index[c] = id;
            return id;
        }
        void Relax(int id, (int, int) n, float cost, bool ladder)
        {
            if (index.TryGetValue(n, out var nid))
            {
                if (closed.Contains(nid) || cost >= gScore[nid]) return;
                gScore[nid] = cost; parent[nid] = id; viaLadder[nid] = ladder;
            }
            else nid = Add(n, cost, id, ladder);
            open.Add((cost + H(n), nid));
        }
        float H((int x, int y) c) => Vector2.Distance(ToWorld(c), ToWorld(g));
        var startId = Add(s, 0, -1);
        open.Add((H(s), startId));
        var expanded = 0;
        while (open.Count > 0 && expanded++ < MaxExpand)
        {
            var cur = open.Min;
            open.Remove(cur);
            var id = cur.id;
            if (!closed.Add(id)) continue;
            var c = cells[id];
            if (c == g) return Build(start, id, cells, parent, viaLadder, goalPos);
            // はしごの上り下りは、少し遠回りくらいの手間として数える
            if (links.TryGetValue(c, out var ls))
                foreach (var l in ls) Relax(id, l.To, gScore[id] + 3f, true);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = (c.Item1 + dx, c.Item2 + dy);
                if (!Walkable(n)) continue;
                // 斜めに進む時は、角をすり抜けないよう横と縦も空いているか見る
                if (dx != 0 && dy != 0 && (!Walkable((c.Item1 + dx, c.Item2)) || !Walkable((c.Item1, c.Item2 + dy)))) continue;
                // 壁ぎわのマスは少し遠回り扱いにして、壁から離れた真ん中を歩く
                Relax(id, n, gScore[id] + (dx != 0 && dy != 0 ? 1.414f : 1f) * Step * (NearWall(n) ? 2.5f : 1f), false);
            }
        }
        return null;
    }

    private static bool NearWall((int x, int y) c)
        => !Walkable((c.x + 1, c.y)) || !Walkable((c.x - 1, c.y)) || !Walkable((c.x, c.y + 1)) || !Walkable((c.x, c.y - 1));

    private static (int, int) NearestWalkable((int x, int y) c)
    {
        if (Walkable(c)) return c;
        for (var r = 1; r <= 6; r++)
            for (var dx = -r; dx <= r; dx++)
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var n = (c.x + dx, c.y + dy);
                    if (Walkable(n)) return n;
                }
        return c;
    }

    // 道を組み立てる。はしごの所で区切り、区切りの中だけ点を飛ばす(はしごの上り口と下り口は必ず通る)
    private static List<Vector2> Build(Vector2 start, int id, List<(int, int)> cells, List<int> parent, List<bool> viaLadder, Vector2 goal)
    {
        var nodes = new List<int>();
        for (var i = id; i >= 0; i = parent[i]) nodes.Add(i);
        nodes.Reverse();
        var result = new List<Vector2>();
        var segment = new List<Vector2>();
        var from = start;
        foreach (var n in nodes)
        {
            if (viaLadder[n] && segment.Count > 0)
            {
                result.AddRange(Smooth(from, segment));
                from = segment[^1];
                segment.Clear();
            }
            segment.Add(ToWorld(cells[n]));
        }
        segment.Add(goal);
        result.AddRange(Smooth(from, segment));
        return result;
    }

    // 見通しのよい所は点を飛ばして、まっすぐ歩けるようにする(最後の点は必ず残す)
    private static List<Vector2> Smooth(Vector2 start, List<Vector2> path)
    {
        var result = new List<Vector2>();
        var from = start;
        var i = 0;
        while (i < path.Count)
        {
            var far = i;
            for (var j = path.Count - 1; j > i; j--)
                if (Clear(from, path[j])) { far = j; break; }
            result.Add(path[far]);
            from = path[far];
            i = far + 1;
        }
        return result;
    }

    // 見通しのきく方向から、歩いて行ける遠めの場所を選ぶ(道が見つからない時に、壁へ歩き続けないように)
    public static Vector2? OpenSpot(Vector2 from, System.Random rand)
    {
        Vector2? best = null;
        var bestDist = 0.8f;
        var offset = rand.Next(0, 16);
        for (var k = 0; k < 16; k++)
        {
            var ang = (k + offset) * Mathf.PI * 2f / 16f;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            for (var dist = 4f; dist >= 1f; dist -= 1f)
            {
                var p = from + dir * dist;
                if (!Walkable(ToCell(p)) || !Clear(from, p)) continue;
                if (dist > bestDist) { bestDist = dist; best = p; }
                break;
            }
        }
        return best;
    }
}
