using HarmonyLib;
using UnityEngine;

using TownOfHost;
using TownOfHost.Modules;

namespace TownOfHost.Patches
{
    // ===== 役職ガイド(？アイコン)ボタンを、HELPボタンの見た目・配置には
    //       一切手を触れずに非表示にするための独立パッチ =====
    //
    // 過去に何度か「RoleGuideButtonPatch.cs自体を直接書き換えて非表示にする」
    // 対応を試みたが、そのたびにHELPボタンの配置計算(役職ガイドボタンの存在を
    // 前提にしている)が壊れてしまっていた。
    // そのため、RoleGuideButtonPatch.cs側は一切変更せず、そちらの生成処理
    // (0.5秒後にCreateGuideButtonを実行)が完全に終わったあとに、
    // 生成された"RoleGuideButton"という名前のGameObjectを外部から探して
    // 非表示にするだけ、というシンプルな方法に変更する。
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Start))]
    public static class RoleGuideButtonHidePatch
    {
        public static void Postfix(HudManager __instance)
        {
            // RoleGuideButtonPatch側のLateTask(0.5秒後)より確実に後になるよう、
            // 少し長めの0.8秒後に実行する。
            _ = new LateTask(() =>
            {
                if (__instance == null) return;
                var btn = __instance.transform.Find("RoleGuideButton");
                if (btn != null && btn.gameObject.activeSelf)
                {
                    btn.gameObject.SetActive(false);
                }
            }, 0.8f, "RoleGuideButton.Hide", true);
        }
    }

    // ===== 設定画面を開いて閉じると、タスク中でもチャットボタンが
    //       表示されたままになってしまう不具合の対策 =====
    //
    // 原因: チャットボタンを「表示すべき時にON」にする処理(RoleGuideButtonPatch内)は
    // あるが、「表示すべきでない時にOFFに戻す」処理がどこにもない。
    // 特定のタイミング(設定を開閉した時)だけ直す形だと再現条件を見誤りやすいため、
    // 毎フレーム確実に本来の状態へ補正するHudManager.Updateへの独立パッチとして実装する。
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class ChatButtonAutoHidePatch
    {
        public static void Postfix(HudManager __instance)
        {
            if (__instance == null) return;
            // 参加者専用MODでは、チャットボタンはゲームとホストに任せる(自分の画面の設定はホストの設定と違うので、勝手に消していた)
            if (Main.IsNonHostClient) return;
            var chatButton = __instance.Chat?.chatButton;
            if (chatButton == null) return;

            // 死んでいる時(ゴースト)はタスク中でも幽霊チャットが使えるので消さない
            // (以前はロビー・会議以外で必ず消していたため、ゴースト中のホストがチャットを開けなかった)
            // 「タスク中のチャット」(通常チャット・近接チャットなど)がONの時は、タスク中でもチャットを使うので消さない
            // (以前は消していたため、近接チャットなどでチャットボタンが出なかった)
            var taskChat = IsTaskChatEnabled();
            var shouldShow = GameStates.IsLobby || GameStates.IsMeeting || IsLocalGhost() || taskChat;
            if (!shouldShow && chatButton.gameObject.activeSelf)
            {
                chatButton.gameObject.SetActive(false);
            }
            else if (shouldShow && (IsLocalGhost() || taskChat) && !chatButton.gameObject.activeSelf)
            {
                // ゴースト中・タスク中のチャットがONの時は、ほかの処理で消されたチャットボタンを出し直す
                chatButton.gameObject.SetActive(true);
                __instance.Chat.SetVisible(true);
            }
        }

        public static bool IsTaskChatEnabled()
            => GameStates.IsInGame && !GameStates.IsLobby && !GameStates.IsMeeting
               && PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.IsAlive()
               && (Options.OptionGameChatSetting?.GetBool() ?? false);

        public static bool IsLocalGhost()
        {
            var me = PlayerControl.LocalPlayer;
            return me != null && me.Data != null && me.Data.IsDead && GameStates.IsInGame && !GameStates.IsLobby;
        }
    }

    // ===== マッチ情報ボタン(バニラのHudManager.MatchInfoButton)を非表示にする =====
    //
    // 要望のあった「マッチ情報ガイド」は、TOH独自の「役職ガイド」とは別の、
    // バニラAmong Us自体が持っている機能(HudManager.MatchInfoButton)だったことが
    // TownOfHost-Pkoの実装(Patches/HudPatch.cs)から判明した。
    // Pko側もこれをゲーム開始後に単純にSetActive(false)で消しているだけなので、
    // 同じ方法をここでも使う。
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class MatchInfoButtonHidePatch
    {
        public static void Postfix(HudManager __instance)
        {
            if (__instance == null) return;
            if (__instance.MatchInfoButton != null && __instance.MatchInfoButton.gameObject.active)
            {
                __instance.MatchInfoButton.gameObject.SetActive(false);
            }
        }
    }
}
