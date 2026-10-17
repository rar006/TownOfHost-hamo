using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Roles.Crewmate;

namespace TownOfHost.Roles.Core;

/// <summary>
/// 2026-10 ユーザー提案の役職で共通に使う処理(チャットコマンド・毎フレームの確認)
/// </summary>
public static class UserRoles2026
{
    /// <summary>
    /// /cmd のコマンド(args[0] は "/mes" のように / 付き)。処理したら true(ホストのみ)
    /// </summary>
    public static bool TryHandleCommand(PlayerControl player, string[] args)
    {
        if (player == null || args == null || args.Length == 0) return false;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        switch (args[0].ToLowerInvariant())
        {
            case "/mm":
                if (player.GetRoleClass() is not TownOfHost.Roles.Madmate.MadMagic magic) return false;
                magic.HandleCommand(args);
                return true;
            case "/mes":
                if (player.GetRoleClass() is not Messenger messenger) return false;
                messenger.HandleCommand(args);
                return true;
            case "/sendmsg":
            {
                // MOD導入者のセンダーから直送された「次のチャット」
                var body = string.Join(" ", args.Skip(1));
                switch (player.GetRoleClass())
                {
                    case Sender s: s.TryIntercept(body); return true;
                    case TownOfHost.Roles.Madmate.MadSender m: m.TryIntercept(body); return true;
                }
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// MOD導入者(ホスト以外)のセンダー: 送信待ちの時、次のチャットを全体に送らず、ホストにだけ直送する。直送したら true
    /// </summary>
    public static bool TryRouteSenderChatToHost(string text)
    {
        if (AmongUsClient.Instance == null || AmongUsClient.Instance.AmHost || !GameStates.IsMeeting) return false;
        if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("/")) return false;
        switch (PlayerControl.LocalPlayer?.GetRoleClass())
        {
            case Sender s when s.HasPending: s.ClearPendingLocal(); break;
            case TownOfHost.Roles.Madmate.MadSender m when m.HasPending: m.ClearPendingLocal(); break;
            default: return false;
        }
        ChatCommands.RequestCommandProcessingFromHost("/cmd sendmsg " + text);
        return true;
    }

    /// <summary>
    /// センダー/マッドセンダー: 相手を選んだ後の次のチャットを横取りして、相手にだけ送る(ホスト)。横取りしたら true
    /// </summary>
    public static bool TryInterceptChat(PlayerControl player, string text)
    {
        if (player == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        return player.GetRoleClass() switch
        {
            Sender s => s.TryIntercept(text),
            TownOfHost.Roles.Madmate.MadSender m => m.TryIntercept(text),
            _ => false,
        };
    }

    /// <summary>
    /// 死亡したプレイヤーを、守護天使ボタンを使う独自のゴースト役職(アルセーヌ・キョンシー)にする(ホスト)
    /// 仕組みは GhostRoleAssingData の配布と同じ
    /// </summary>
    public static void MakeGhost(PlayerControl pc, CustomRoles role)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return;
        var state = PlayerState.GetByPlayerId(pc.PlayerId);
        if (state == null) return;
        if (!Utils.RoleSendList.Contains(pc.PlayerId)) Utils.RoleSendList.Add(pc.PlayerId);
        state.SetGhostRole(role);
        if (PlayerCatch.AnyModClient())
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, Hazel.SendOption.Reliable, -1);
            writer.Write(pc.PlayerId);
            writer.WritePacked((int)role);
            writer.Write(2);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        _ = new LateTask(() =>
        {
            if (GameStates.CalledMeeting || pc == null) return;
            pc.RpcSetRole(AmongUs.GameOptions.RoleTypes.GuardianAngel, true);
            _ = new LateTask(() => pc.RpcResetAbilityCooldown(Sync: true), 0.5f, "UserGhost.ResetCd", true);
        }, 1.4f, "UserGhost.SetRole", true);
        Logger.Info($"ゴースト役職を設定: {pc.Data?.GetLogPlayerName()} = {role}", "UserRoles2026");
    }

    /// <summary>試合開始時に呼ぶ(前の試合の生存者リストなどを持ち越さない)</summary>
    public static void Reset() => TickPatch.Reset();

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class TickPatch
    {
        static float next;
        static int lastDay = -1;
        public static void Reset() { next = 0f; lastDay = -1; alive.Clear(); }
        static readonly System.Collections.Generic.HashSet<byte> alive = new();
        public static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (!GameStates.IsInTask || GameStates.IsMeeting) return;
                if (Time.time < next) return;
                next = Time.time + 0.25f;

                // 会議が終わって日が進んだ: マッドマジックの変身期限の確認
                if (lastDay != UtilsGameLog.day)
                {
                    lastDay = UtilsGameLog.day;
                    TownOfHost.Roles.Madmate.MadMagic.AfterMeeting();
                    TownOfHost.Roles.AddOns.Common.BetrayGuesser.CheckGive();
                    alive.Clear();
                    foreach (var pc in PlayerCatch.AllAlivePlayerControls) alive.Add(pc.PlayerId);
                }
                // 誰かが死亡した: ダオシーの矢印
                var daoshi = TownOfHost.Roles.Neutral.Daoshi.Instance;
                foreach (var id in alive.ToArray())
                {
                    var pc = PlayerCatch.GetPlayerById(id);
                    if (pc != null && pc.IsAlive()) continue;
                    alive.Remove(id);
                    daoshi?.OnSomeoneDied(pc);
                }

                foreach (var role in CustomRoleManager.AllActiveRoles.Values.ToArray())
                {
                    if (role is Fugitive f) f.CheckFleeTargetDead();
                    if (role is TownOfHost.Roles.Impostor.ArchVillain av) av.CheckFollow();
                    if (role is TownOfHost.Roles.Impostor.Clockworker cw) cw.Tick(0.25f);
                    if (role is TownOfHost.Roles.Neutral.Lupin lupin) lupin.Tick();
                    if (role is TownOfHost.Roles.Neutral.Daoshi dao) dao.Tick();
                }
            }
            catch (System.Exception e) { Logger.Warn($"UserRoles2026.Tick: {e.Message}", "UserRoles2026"); }
        }
    }
}
