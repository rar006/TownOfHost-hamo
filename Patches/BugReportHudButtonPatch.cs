using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

using TownOfHost;
using TownOfHost.Modules;

namespace TownOfHost.Patches
{
    // ===== HELPボタンと同じ見た目のバグ報告ボタン =====
    // RoleGuideButtonPatch(HELPボタン)が使っている「バニラのボタン枠を複製する」処理を
    // そのまま流用し、見た目を完全に合わせている。HELPボタンのすぐ右に並べて表示する。
    // 要望により、試合中(IsInGame)は非表示にし、ロビーにいる間だけ表示する。
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Start))]
    public static class BugReportHudButtonPatch
    {
        // HELPの左隣に配置するための間隔。毎フレームHELPの位置を追従する。
        // HELPからの横方向の距離。大きくするとBUGは左へ、0に近づけるとHELPへ近づく。
        // 例: 0.30f = さらに左、0.00f = HELPに近い位置。
        // 前回位置より少し右へ。マイナス値ほどさらに右へ移動する。
        public static float BugButtonHorizontalOffset = -0.6f;
        private static GameObject _buttonObj;
        private static TextMeshPro _unreadBadgeText;

        /// <summary>BUGボタンが現在表示されているか(CredentialsPatchの上部表示位置調整に使う)。</summary>
        public static bool HasBugButton => _buttonObj != null && !_buttonObj.IsDestroyedOrNull() && _buttonObj.activeInHierarchy;

        public static void Postfix(HudManager __instance)
        {
            _ = new LateTask(() => CreateBugReportButton(__instance), 0.6f, "BugReportButton.Create", true);
        }

        private static void CreateBugReportButton(HudManager hud)
        {
            try
            {
                if (hud == null) return;

                var old = hud.transform.Find("BugReportHudButton");
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);

                // HELPボタン(RoleGuideButton)があればその右隣、無ければ設定ボタンの左隣に配置する。
                var settingsButton = hud.SettingsButton;
                var settingsPos = settingsButton != null
                    ? hud.transform.InverseTransformPoint(settingsButton.transform.position)
                    : Vector3.zero;

                var roleGuideButton = hud.transform.Find("RoleGuideButton");
                Vector3 anchorPos;
                if (roleGuideButton != null)
                {
                    anchorPos = hud.transform.InverseTransformPoint(roleGuideButton.position);
                }
                else
                {
                    var chatButton = hud.Chat?.chatButton;
                    anchorPos = chatButton != null && chatButton.gameObject.activeInHierarchy
                        ? hud.transform.InverseTransformPoint(chatButton.transform.position)
                        : settingsPos;
                }

                float spacing = Mathf.Abs(settingsPos.x - anchorPos.x);
                if (spacing < 0.45f || spacing > 1.5f) spacing = 0.78f;

                var btnObj = new GameObject("BugReportHudButton");
                btnObj.transform.SetParent(hud.transform);
                btnObj.layer = 5;
                btnObj.transform.localPosition = new Vector3(
                    roleGuideButton != null ? anchorPos.x - spacing - BugButtonHorizontalOffset : anchorPos.x - spacing,
                    settingsPos.y,
                    settingsPos.z - 0.1f);
                btnObj.transform.localScale = new Vector3(0.28f, 0.28f, 1f); // RoleGuideButtonPatch.GuideButtonScaleと同じ

                RoleGuideButtonPatch.CreateVanillaButtonFrame(hud, btnObj.transform);

                var sr = btnObj.AddComponent<SpriteRenderer>();
                sr.color = Color.white;
                sr.sortingOrder = 10;
                sr.sprite = null;

                var labelText = RoleGuideButtonPatch.MakeText(btnObj, "BugReportText",
                    new Vector3(0f, 0.03f, -0.1f), "BUG", 5f,
                    TextAlignmentOptions.Center, new Vector2(2.35f, 1.4f));
                labelText.enableWordWrapping = false;
                labelText.overflowMode = TextOverflowModes.Overflow;
                labelText.characterSpacing = -8f;
                labelText.color = new Color(0.92f, 0.3f, 0.3f, 1f);

                // 未読の返信があるときに右上に出す、小さな赤丸バッジ(絵文字ではなくフォントに
                // 含まれる「●」を使うことで、このフォントでも確実に表示されるようにしている)。
                _unreadBadgeText = RoleGuideButtonPatch.MakeText(btnObj, "BugReportUnreadBadge",
                    new Vector3(0.85f, 0.85f, -0.2f), "●", 5f,
                    TextAlignmentOptions.Center, new Vector2(1f, 1f));
                _unreadBadgeText.enableWordWrapping = false;
                _unreadBadgeText.color = new Color(0.95f, 0.1f, 0.1f, 1f);
                _unreadBadgeText.gameObject.SetActive(false);

                var col = btnObj.AddComponent<BoxCollider2D>();
                col.size = new Vector2(2.28f, 2.28f);

                var btn = btnObj.AddComponent<PassiveButton>();
                btn.Colliders = new Collider2D[] { col };
                btn.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                btn.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    BugReportWizard.Open();
                }));
                btn.OnMouseOver = new UnityEngine.Events.UnityEvent();
                btn.OnMouseOut = new UnityEngine.Events.UnityEvent();

                _buttonObj = btnObj;

                // このボタンをきっかけにウィザードパネルの親を更新しておく。
                BugReportUIPatch.SetCurrentParent(hud.transform);
            }
            catch (Exception ex)
            {
                Logger.Error($"バグ報告ボタン(HELP風)の生成に失敗しました: {ex}", "BugReportHudButtonPatch");
            }
        }

        // HELPの位置が生成後に確定するため、BUGも毎フレーム隣へ追従させる。
        private static void UpdateBugButtonLayout()
        {
            if (_buttonObj == null || _buttonObj.IsDestroyedOrNull() || !DestroyableSingleton<HudManager>.InstanceExists) return;
            var hud = DestroyableSingleton<HudManager>.Instance;
            var help = hud.transform.Find("RoleGuideButton");
            if (help == null) return;
            var helpPos = hud.transform.InverseTransformPoint(help.position);
            var settings = hud.SettingsButton;
            var settingsPos = settings != null ? hud.transform.InverseTransformPoint(settings.transform.position) : helpPos;
            var spacing = Mathf.Abs(settingsPos.x - helpPos.x);
            if (spacing < 0.45f || spacing > 1.5f) spacing = 0.78f;
            _buttonObj.transform.localPosition = new Vector3(
                helpPos.x - spacing - BugButtonHorizontalOffset,
                helpPos.y,
                helpPos.z - 0.1f);
        }

        // 試合中は非表示、ロビーでは表示する。未読バッジもここで一緒に更新する。
        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
        public static class VisibilityUpdatePatch
        {
            public static void Postfix()
            {
                if (_buttonObj == null || _buttonObj.IsDestroyedOrNull()) return;

                UpdateBugButtonLayout();
                // 試合中・ロビー外に加えて、設定画面(GameSettingMenu)が開いている間も
                // 要望により非表示にする(HELPボタンと同じ判定方法)。
                var shouldShow = GameStates.IsLobby && !GameStates.IsInGame && GameSettingMenu.Instance == null && !(AmongUsClient.Instance != null && AmongUsClient.Instance.NetworkMode == NetworkModes.FreePlay); // フリープレイではBUGを表示しない
                if (_buttonObj.activeSelf != shouldShow)
                    _buttonObj.SetActive(shouldShow);

                if (_unreadBadgeText != null && !_unreadBadgeText.IsDestroyedOrNull())
                {
                    var shouldShowBadge = shouldShow && BugReportSystem.HasAnyUnread;
                    if (_unreadBadgeText.gameObject.activeSelf != shouldShowBadge)
                        _unreadBadgeText.gameObject.SetActive(shouldShowBadge);
                }
            }
        }
    }
}
