using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TownOfHost.Attributes;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

public static class DeadBodyEraser
{
    private static readonly HashSet<byte> noBodyTargets = new();
    private static readonly HashSet<byte> erasedBodies = new();
    private static readonly Dictionary<byte, TextDummy> covers = new();

    private const int CoverColumns = 4;
    private const int CoverRows = 3;
    private const int CoverSize = 125;
    private const string CoverPrefix = "<line-height=1em><mspace=1em><alpha=#00>";

    [GameModuleInitializer]
    public static void Init()
    {
        noBodyTargets.Clear();
        erasedBodies.Clear();
        covers.Clear();
    }

    public static bool KillWithoutBody(PlayerControl killer, PlayerControl target, CustomDeathReason reason, bool force = true)
    {
        if (!AmongUsClient.Instance.AmHost || killer == null || target == null || !target.IsAlive()) return false;
        noBodyTargets.Add(target.PlayerId);
        try
        {
            CustomRoleManager.OnCheckMurder(killer, target, target, target, force, deathReason: reason);
        }
        finally
        {
            noBodyTargets.Remove(target.PlayerId);
        }
        return !target.IsAlive();
    }

    public static bool TakeNoBody(PlayerControl target)
        => target != null && noBodyTargets.Remove(target.PlayerId);

    public static void ExileInsteadOfMurder(PlayerControl appearanceKiller, PlayerControl target)
    {
        target.Exiled();
        var writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.Exiled, Hazel.SendOption.Reliable, -1);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
        CustomRoleManager.OnMurderPlayer(appearanceKiller, target);
        RemovePet(target);
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} を死体を残さずにキルしました", "DeadBodyEraser");
    }

    public static bool IsErased(byte playerId) => erasedBodies.Contains(playerId);

    public static DeadBody FindNearestBody(Vector2 from, float range)
    {
        DeadBody best = null;
        var bestDist = range;
        foreach (var body in Object.FindObjectsOfType<DeadBody>())
        {
            if (body == null || erasedBodies.Contains(body.ParentId)) continue;
            var d = Vector2.Distance(from, body.TruePosition);
            if (d > bestDist) continue;
            bestDist = d;
            best = body;
        }
        return best;
    }

    public static void Erase(byte playerId)
    {
        var body = Object.FindObjectsOfType<DeadBody>().FirstOrDefault(b => b != null && b.ParentId == playerId);
        Vector2? bodyPos = body != null ? BodyCenter(body) : null;
        erasedBodies.Add(playerId);
        HideLocal(playerId);
        if (bodyPos.HasValue) Cover(playerId, bodyPos.Value);
        RemovePet(PlayerCatch.GetPlayerById(playerId));
        Logger.Info($"{playerId} の死体を消しました", "DeadBodyEraser");
    }

    public static void HideLocal(byte playerId)
    {
        erasedBodies.Add(playerId);
        foreach (var body in Object.FindObjectsOfType<DeadBody>().Where(b => b != null && b.ParentId == playerId).ToArray())
            body.gameObject.SetActive(false);
    }

    private static Vector2 BodyCenter(DeadBody body)
    {
        Vector2 pos = body.transform.position;
        try
        {
            var renderers = body.bodyRenderers;
            if (renderers == null) return pos;
            Bounds? bounds = null;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (bounds == null) bounds = r.bounds;
                else { var b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; }
            }
            if (bounds.HasValue) pos = bounds.Value.center;
        }
        catch { }
        return pos;
    }

    private static void Cover(byte playerId, Vector2 pos)
    {
        if (!AmongUsClient.Instance.AmHost || covers.ContainsKey(playerId)) return;
        if (!(Options.ErasedBodyCover?.GetBool() ?? true)) return;
        var color = FloorColorSampler.Sample(pos);
        var dummy = new TextDummy { HiddenForHost = true };
        dummy.Show(CoverArt(ColorUtility.ToHtmlStringRGB(color)));
        dummy.MoveTo(pos);
        covers[playerId] = dummy;
    }

    private static string CoverArt(string hex)
    {
        var row = string.Concat(Enumerable.Repeat("■", CoverColumns));
        var sb = new System.Text.StringBuilder($"<size={CoverSize}%>{CoverPrefix}");
        for (var r = 0; r < CoverRows; r++)
        {
            sb.Append($"<alpha=#FF><mark=#{hex}FF><alpha=#00>").Append(row).Append("</mark>");
            if (r < CoverRows - 1) sb.Append('\n');
        }
        return sb.Append("</size>").ToString();
    }

    public static void OnReportDeadBody()
    {
        foreach (var dummy in covers.Values) dummy?.Remove();
        covers.Clear();
    }

    private static void RemovePet(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return;
        if (!(Options.ErasedBodyRemovePet?.GetBool() ?? true)) return;
        PetsHelper.RemovePet(pc);
    }
}
