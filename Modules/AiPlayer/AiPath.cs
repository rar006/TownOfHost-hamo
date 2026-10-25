using System.Collections.Generic;
using UnityEngine;

namespace TownOfHost.Modules.AiPlayer;

// 壁を避けて歩くための道探し。マップを細かいマス目に分けて、壁にぶつからないマスだけを通る道を A* で探す。
public static class AiPath
{
    private const float Step = 0.35f;
    // 壁との間を少しあける(小さいと、壁ぎりぎりを通ろうとしてぶつかっていた)
    private const float Radius = 0.32f;
    private const int MaxExpand = 30000;

    private static readonly Dictionary<(int, int), bool> walkableCache = new();

    public static void Reset() => walkableCache.Clear();

    private static Vector2 ToWorld((int x, int y) c) => new(c.x * Step, c.y * Step);
    private static (int, int) ToCell(Vector2 p) => (Mathf.RoundToInt(p.x / Step), Mathf.RoundToInt(p.y / Step));

    private static bool Walkable((int, int) c)
    {
        if (walkableCache.TryGetValue(c, out var ok)) return ok;
        // 部屋の範囲などの見えない当たり判定(トリガー)は壁として数えない。
        // (数えると部屋の中のマスが全部通れない扱いになり、道が見つからず、壁へまっすぐ歩いていた)
        var old = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = false;
        try { ok = Physics2D.OverlapCircle(ToWorld(c), Radius, Constants.ShipAndObjectsMask) == null; }
        finally { Physics2D.queriesHitTriggers = old; }
        walkableCache[c] = ok;
        return ok;
    }

    // 2点の間に壁が無いか
    public static bool Clear(Vector2 a, Vector2 b)
    {
        var d = b - a;
        if (d.magnitude < 0.01f) return true;
        if (PhysicsHelpers.AnyNonTriggersBetween(a, d.normalized, d.magnitude, Constants.ShipAndObjectsMask)) return false;
        // 体の幅のぶん、左右にずらした線でも壁に当たらないか見る(真ん中だけ見ると、角をこすってぶつかっていた)
        var side = new Vector2(-d.y, d.x).normalized * (Radius * 0.8f);
        return !PhysicsHelpers.AnyNonTriggersBetween(a + side, d.normalized, d.magnitude, Constants.ShipAndObjectsMask)
            && !PhysicsHelpers.AnyNonTriggersBetween(a - side, d.normalized, d.magnitude, Constants.ShipAndObjectsMask);
    }

    // start から goal までの道(通る点のリスト)。見つからない時は null
    public static List<Vector2> Find(Vector2 start, Vector2 goal)
    {
        if (Clear(start, goal)) return new List<Vector2> { goal };
        var s = ToCell(start);
        var g = NearestWalkable(ToCell(goal));
        if (!Walkable(s)) s = NearestWalkable(s);
        var open = new SortedSet<(float f, int id)>();
        var cells = new List<(int, int)>();
        var index = new Dictionary<(int, int), int>();
        var gScore = new List<float>();
        var parent = new List<int>();
        int Add((int, int) c, float gs, int from)
        {
            var id = cells.Count;
            cells.Add(c); gScore.Add(gs); parent.Add(from); index[c] = id;
            return id;
        }
        float H((int x, int y) c) => Vector2.Distance(ToWorld(c), ToWorld(g));
        var startId = Add(s, 0, -1);
        open.Add((H(s), startId));
        var closed = new HashSet<int>();
        var expanded = 0;
        while (open.Count > 0 && expanded++ < MaxExpand)
        {
            var cur = open.Min;
            open.Remove(cur);
            var id = cur.id;
            if (!closed.Add(id)) continue;
            var c = cells[id];
            if (c == g) return Smooth(start, Rebuild(id, cells, parent), goal);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = (c.Item1 + dx, c.Item2 + dy);
                if (!Walkable(n)) continue;
                // 斜めに進む時は、角をすり抜けないよう横と縦も空いているか見る
                if (dx != 0 && dy != 0 && (!Walkable((c.Item1 + dx, c.Item2)) || !Walkable((c.Item1, c.Item2 + dy)))) continue;
                var cost = gScore[id] + (dx != 0 && dy != 0 ? 1.414f : 1f) * Step;
                if (index.TryGetValue(n, out var nid))
                {
                    if (closed.Contains(nid) || cost >= gScore[nid]) continue;
                    gScore[nid] = cost; parent[nid] = id;
                }
                else nid = Add(n, cost, id);
                open.Add((cost + H(n), nid));
            }
        }
        return null;
    }

    private static (int, int) NearestWalkable((int x, int y) c)
    {
        if (Walkable(c)) return c;
        for (var r = 1; r <= 4; r++)
            for (var dx = -r; dx <= r; dx++)
                for (var dy = -r; dy <= r; dy++)
                {
                    var n = (c.x + dx, c.y + dy);
                    if (Walkable(n)) return n;
                }
        return c;
    }

    private static List<Vector2> Rebuild(int id, List<(int, int)> cells, List<int> parent)
    {
        var path = new List<Vector2>();
        for (var i = id; i >= 0; i = parent[i]) path.Add(ToWorld(cells[i]));
        path.Reverse();
        return path;
    }

    // 見通しのよい所は点を飛ばして、まっすぐ歩けるようにする
    private static List<Vector2> Smooth(Vector2 start, List<Vector2> path, Vector2 goal)
    {
        path.Add(goal);
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
}
