using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TownOfHost;

public static partial class TitleSystem
{
    private sealed class HostTrade
    {
        public byte A, B;                 // A=申し込んだ人 / B=相手
        public string FcA = "", FcB = "";
        public string GiveA = "", GiveB;  // A が渡す / B が渡す
        public bool A1, B1, A2, B2;
        public float BothAcceptedAt = -1f;
        public float CreatedAt;
        public bool Executing;
        public bool Has(byte id) => id == A || id == B;
    }

    private static readonly List<HostTrade> hostTrades = new();

    private static HostTrade HostTradeOf(byte id) => hostTrades.FirstOrDefault(t => t.Has(id));

    internal static bool InHostTrade(byte id) => HostTradeOf(id) != null;

    private static void Say(byte id, string msg) => Utils.SendMessage(msg, id);

    internal static void HostTradeCommand(PlayerControl player, string[] args)
    {
        if (player == null) return;
        var me = player.PlayerId;
        if (!IsConfigured) { Say(me, "二つ名サーバー(Bot)のURLが設定されていません。"); return; }
        if (args.Length == 0)
        {
            Say(me, "<b>二つ名の交換</b>(ロビーのみ)\n"
                + "/cmd nit 名前 … 相手のインベントリを見る\n"
                + "/cmd nit 名前 a b … 自分の a 番と相手の b 番の交換を申し込む\n"
                + "/cmd nit y … 承諾(両方が承諾したら、3秒後にもう一度 /cmd nit y で成立)\n"
                + "/cmd nit n … キャンセル/断る");
            return;
        }
        var first = args[0].ToLowerInvariant();
        if (args.Length == 1 && IsYes(first)) { HostTradeAccept(me); return; }
        if (args.Length == 1 && IsNo(first))
        {
            var t = HostTradeOf(me);
            if (t == null) Say(me, "進行中の交換はありません。");
            else HostTradeCancel(t, $"{PlayerLabel(me)} が交換をキャンセルしました。");
            return;
        }

        int mine = 0, theirs = 0;
        bool isPropose = args.Length >= 3
            && int.TryParse(args[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out mine)
            && int.TryParse(args[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out theirs);
        var name = string.Join(" ", isPropose ? args[..^2] : args);
        var target = FindPlayerByName(name, out var error);
        if (target == null) { Say(me, error); return; }
        if (target.PlayerId == me) { Say(me, "自分とは交換できません。"); return; }
        var myFc = FriendCodeOf(player);
        var targetFc = FriendCodeOf(target);
        if (myFc.Length == 0 || targetFc.Length == 0) { Say(me, "フレンドコードを取得できませんでした。"); return; }

        var targetId = target.PlayerId;
        if (!isPropose) { HostShowInventoryAsync(me, targetId, targetFc); return; }
        if (HostTradeOf(me) != null) { Say(me, "すでに交換が進行中です。 /cmd nit n でキャンセルできます。"); return; }
        if (HostTradeOf(targetId) != null) { Say(me, "相手は別の交換の途中です。"); return; }
        HostProposeAsync(me, myFc, targetId, targetFc, mine, theirs);
    }

    private static async void HostShowInventoryAsync(byte to, byte partnerId, string partnerFc)
    {
        try
        {
            var partnerName = PlayerLabel(partnerId);
            await EnsureCatalogAsync().ConfigureAwait(false);
            var inv = InventoryOf(await FetchUserAsync(partnerFc, true).ConfigureAwait(false));
            var sb = new StringBuilder();
            if (inv.Count == 0) sb.Append($"{partnerName} は二つ名を持っていません。");
            else
            {
                sb.Append($"<b>{partnerName} のインベントリ</b>({inv.Count}個)\n");
                for (int i = 0; i < inv.Count; i++) sb.Append(i + 1).Append(". ").Append(Colored(Catalog[inv[i]])).Append('\n');
                sb.Append($"\n/cmd nit {partnerName} (自分の番号) (相手の番号) で交換を申し込めます");
            }
            var text = sb.ToString();
            RunOnMain(() => Say(to, text));
        }
        catch (Exception e) { Logger.Exception(e, Tag); RunOnMain(() => Say(to, "相手のインベントリを取得できませんでした。")); }
    }

    private static async void HostProposeAsync(byte a, string fcA, byte b, string fcB, int mine, int theirs)
    {
        try
        {
            await EnsureCatalogAsync().ConfigureAwait(false);
            var recA = await FetchUserAsync(fcA, true).ConfigureAwait(false);
            var recB = await FetchUserAsync(fcB, true).ConfigureAwait(false);
            var invA = InventoryOf(recA);
            var invB = InventoryOf(recB);
            string err = null;
            if (mine < 1 || mine > invA.Count) err = $"自分の番号は 1〜{invA.Count} で指定してください(/cmd nii で確認)。";
            else if (theirs < 1 || theirs > invB.Count) err = $"相手の番号は 1〜{invB.Count} で指定してください(/cmd nit 名前 で確認)。";
            string give = null, get = null;
            if (err == null)
            {
                give = invA[mine - 1];
                get = invB[theirs - 1];
                if (give == get) err = "同じ二つ名どうしは交換できません。";
                else if (recA.Owned.Contains(get)) err = $"{TitleLabel(get)} はすでに持っています。";
                else if (recB.Owned.Contains(give)) err = $"相手はすでに {TitleLabel(give)} を持っています。";
            }
            RunOnMain(() =>
            {
                if (err != null) { Say(a, err); return; }
                if (!GameStates.IsLobby) return;
                if (HostTradeOf(a) != null || HostTradeOf(b) != null) { Say(a, "どちらかが別の交換の途中です。"); return; }
                if (FindPlayerById(b) == null) { Say(a, "相手が見つかりません。"); return; }
                hostTrades.Add(new HostTrade { A = a, B = b, FcA = fcA, FcB = fcB, GiveA = give, GiveB = get, CreatedAt = Time.time });
                Say(a, $"{PlayerLabel(b)} に交換を申し込みました。\n渡す: {TitleLabel(give)}  ⇔  もらう: {TitleLabel(get)}\n/cmd nit y で承諾 / /cmd nit n でキャンセル");
                Say(b, $"{PlayerLabel(a)} から二つ名の交換の申し込みです。\n渡す: {TitleLabel(get)}  ⇔  もらう: {TitleLabel(give)}\n/cmd nit y で承諾 / /cmd nit n で断る");
            });
        }
        catch (Exception e) { Logger.Exception(e, Tag); RunOnMain(() => Say(a, "交換を申し込めませんでした。")); }
    }

    private static void HostTradeAccept(byte id)
    {
        var t = HostTradeOf(id);
        if (t == null) { Say(id, "進行中の交換はありません。"); return; }
        if (t.Executing) { Say(id, "交換の処理中です。少しお待ちください。"); return; }
        var isA = id == t.A;
        var other = isA ? t.B : t.A;

        if (!(isA ? t.A1 : t.B1))
        {
            if (isA) t.A1 = true; else t.B1 = true;
            if (t.A1 && t.B1)
            {
                t.BothAcceptedAt = Time.time;
                var msg = $"両方が承諾しました。{TradeConfirmDelay:0}秒後にもう一度 /cmd nit y を送ると交換が成立します。";
                Say(t.A, msg + $"\n渡す: {TitleLabel(t.GiveA)}  ⇔  もらう: {TitleLabel(t.GiveB)}");
                Say(t.B, msg + $"\n渡す: {TitleLabel(t.GiveB)}  ⇔  もらう: {TitleLabel(t.GiveA)}");
            }
            else
            {
                Say(id, "承諾しました。相手の承諾を待っています。");
                Say(other, $"{PlayerLabel(id)} が交換を承諾しました。 /cmd nit y で承諾 / /cmd nit n で断る");
            }
            return;
        }
        if (!(t.A1 && t.B1)) { Say(id, "相手の承諾を待っています。"); return; }
        var wait = TradeConfirmDelay - (Time.time - t.BothAcceptedAt);
        if (wait > 0f) { Say(id, $"あと{Mathf.CeilToInt(wait)}秒待ってから、もう一度 /cmd nit y で確定してください。"); return; }
        if (isA ? t.A2 : t.B2) { Say(id, "確定済みです。相手の確定を待っています。"); return; }
        if (isA) t.A2 = true; else t.B2 = true;
        if (t.A2 && t.B2) { t.Executing = true; HostExecuteAsync(t); }
        else Say(id, "確定しました。相手の確定を待っています。");
    }

    private static void HostTradeCancel(HostTrade t, string msg)
    {
        hostTrades.Remove(t);
        Say(t.A, msg);
        Say(t.B, msg);
    }

    private static async void HostExecuteAsync(HostTrade t)
    {
        string error = "";
        bool ok = false;
        UserRecord recA = null, recB = null;
        try
        {
            var (status, doc) = await RequestAsync(System.Net.Http.HttpMethod.Post, $"{BaseUrl}/trade", new
            {
                friendCodeA = t.FcA,
                titleA = t.GiveA,
                friendCodeB = t.FcB,
                titleB = t.GiveB,
            }).ConfigureAwait(false);
            using (doc)
            {
                ok = status == 200 && doc != null && doc.RootElement.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
                if (!ok) error = doc != null ? GetString(doc.RootElement, "error") : "";
            }
            if (ok)
            {
                recA = await FetchUserAsync(t.FcA, true).ConfigureAwait(false);
                recB = await FetchUserAsync(t.FcB, true).ConfigureAwait(false);
            }
        }
        catch (Exception e) { Logger.Exception(e, Tag); error = "network"; }

        RunOnMain(() =>
        {
            hostTrades.Remove(t);
            if (!ok)
            {
                var m = TradeErrorMessage(error);
                Say(t.A, m);
                Say(t.B, m);
                return;
            }
            // 表示を更新(渡した二つ名を付けていたら外れている)
            if (recA != null) SetShown(t.A, t.FcA, recA.Equipped);
            if (recB != null) SetShown(t.B, t.FcB, recB.Equipped);
            SendNotifyFor(t.A);
            SendNotifyFor(t.B);
            Say(t.A, $"交換が成立しました!  {TitleLabel(t.GiveA)} → {TitleLabel(t.GiveB)}");
            Say(t.B, $"交換が成立しました!  {TitleLabel(t.GiveB)} → {TitleLabel(t.GiveA)}");
        });
    }

    private static void HostTradeTick()
    {
        if (hostTrades.Count == 0) return;
        if (!GameStates.IsLobby)
        {
            foreach (var t in hostTrades.ToList()) HostTradeCancel(t, "試合が始まったため、二つ名の交換はキャンセルされました。");
            return;
        }
        foreach (var t in hostTrades.ToList())
        {
            if (t.Executing) continue;
            if (FindPlayerById(t.A) == null || FindPlayerById(t.B) == null) HostTradeCancel(t, "相手がいなくなったため、交換はキャンセルされました。");
            else if (Time.time - t.CreatedAt > TradeTimeout) HostTradeCancel(t, "時間切れのため、交換はキャンセルされました。");
        }
    }
}
