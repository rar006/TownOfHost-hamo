using HarmonyLib;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;

namespace TownOfHost.Patches;

/// <summary>
/// タスクターン中のチャットを近チャに制限するPatch。
/// OptionGameChatNormalChat = ON のとき有効。
/// OptionGameChatNormalNearChat = ON のとき近チャ制限を適用。
/// 範囲はOptionGameChatNormalNearChatRangeで設定。
/// </summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
public static class NearChatPatch
{
    public static bool Prefix(PlayerControl __instance, string chatText)
    {
        // ★ ゲーム中タスクターンのみ有効
        if (!GameStates.IsInTask) return true;
        if (MeetingHud.Instance != null) return true;

        // ★ 通常チャットオプションOFF → スルー
        if (!Options.OptionGameChatSetting.GetBool()) return true;
        if (!Options.OptionGameChatNormalChat.GetBool()) return true;

        // ★ 近チャOFF → スルー（全員に届く通常チャット）
        if (!Options.OptionGameChatNormalNearChat.GetBool()) return true;

        // ★ ホストのみ処理（RPCを受け取る側）
        if (!AmongUsClient.Instance.AmHost) return true;

        var sender = __instance;
        if (sender == null) return true;

        float range = Options.OptionGameChatNormalNearChatRange?.GetInt() ?? 10;
        var senderPos = sender.GetTruePosition();

        // ★ 送信者自身には届ける
        // ★ 範囲内のプレイヤーにだけ個別送信
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            // GMには全チャット届ける
            bool isGM = pc.Is(CustomRoles.GM);

            // 送信者自身
            bool isSelf = pc.PlayerId == sender.PlayerId;

            // 近チャ範囲チェック
            float dist = Vector2.Distance(senderPos, pc.GetTruePosition());
            bool inRange = dist <= range;

            if (!isSelf && !isGM && !inRange) continue;

            // ★ そのプレイヤーにだけチャットを送信
            if (pc.AmOwner)
            {
                // ローカルプレイヤー（ホスト）には直接表示
                DestroyableSingleton<HudManager>.Instance?.Chat?.AddChat(sender, chatText);
            }
            else
            {
                // 他クライアントへはRPCで個別送信
                var writer = AmongUsClient.Instance.StartRpcImmediately(
                    sender.NetId, (byte)RpcCalls.SendChat,
                    Hazel.SendOption.Reliable, pc.OwnerId);
                writer.Write(chatText);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        // ★ 元のRPC（全員送信）をキャンセル
        return false;
    }
}