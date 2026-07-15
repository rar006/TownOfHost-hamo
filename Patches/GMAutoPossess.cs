using System.Linq;
using HarmonyLib;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using UnityEngine;
using TMPro;

namespace TownOfHost.Patches;

[HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
public static class GMAutoOpenHauntMenuPatch
{
    public static void Postfix()
    {
        var local = PlayerControl.LocalPlayer;
        if (local == null) return;
        if (!Options.OptionGMAutoPossess.GetBool()) return;
        if (local.GetCustomRole() != CustomRoles.GM) return;
        if (local.IsAlive()) return;

        _ = new LateTask(() =>
        {
            try
            {
<<<<<<< HEAD
                ClickHauntButtonIfFound();
            }
            catch { }
        }, 1.2f, "GMAutoOpenHauntMenu", true);

        // Airshipは死亡直後に「場所を選ぶ」追加の画面が挟まることがあり、
        // 上記の1回きりのクリックだけでは自動化が止まってしまうことがあるため、
        // Airshipの場合のみ数秒間、追加で出てくるボタンを継続的に探してクリックする。
        // ※ 実機未検証のため、対応しきれない場合は具体的なボタン名/画面名を教えてください。
        if ((MapNames)Main.NormalOptions.MapId == MapNames.Airship)
        {
            for (float delay = 1.8f; delay <= 6f; delay += 0.6f)
            {
                _ = new LateTask(() =>
                {
                    try { ClickHauntButtonIfFound(); ClickLikelyLocationSelectButtonIfFound(); }
                    catch { }
                }, delay, "GMAutoOpenHauntMenu.Airship", true);
            }
        }
    }

    private static void ClickHauntButtonIfFound()
    {
        var buttons = UnityEngine.Object.FindObjectsOfType<PassiveButton>();
        foreach (var btn in buttons)
        {
            if (!btn.gameObject.activeInHierarchy) continue;

            var tmpro = btn.GetComponentInChildren<TextMeshPro>();
            string label = tmpro != null ? tmpro.text : "";
            string objName = btn.gameObject.name.ToLower();

            if (objName.Contains("haunt") || label.Contains("憑依") || label.ToLower().Contains("haunt"))
            {
                btn.OnClick?.Invoke();
                return;
            }
        }
    }

    // Airship特有の「場所選択」的な画面に出てきそうなボタンを名前から推測してクリックする。
    // 憑依先を決める前段階として現れる可能性のある、確定/選択系ボタンを広めに拾う。
    private static void ClickLikelyLocationSelectButtonIfFound()
    {
        var buttons = UnityEngine.Object.FindObjectsOfType<PassiveButton>();
        foreach (var btn in buttons)
        {
            if (!btn.gameObject.activeInHierarchy) continue;

            var tmpro = btn.GetComponentInChildren<TextMeshPro>();
            string label = (tmpro != null ? tmpro.text : "").ToLower();
            string objName = btn.gameObject.name.ToLower();

            bool looksLikeSelect =
                objName.Contains("spawn") || objName.Contains("location") || objName.Contains("select") ||
                objName.Contains("confirm") || objName.Contains("record") ||
                label.Contains("spawn") || label.Contains("select") || label.Contains("confirm") ||
                label.Contains("決定") || label.Contains("選択") || label.Contains("確定");

            if (looksLikeSelect)
            {
                btn.OnClick?.Invoke();
                return;
            }
        }
=======
                var buttons = UnityEngine.Object.FindObjectsOfType<PassiveButton>();
                foreach (var btn in buttons)
                {
                    if (!btn.gameObject.activeInHierarchy) continue;

                    var tmpro = btn.GetComponentInChildren<TextMeshPro>();
                    string label = tmpro != null ? tmpro.text : "";
                    string objName = btn.gameObject.name.ToLower();

                    if (objName.Contains("haunt") || label.Contains("憑依") || label.ToLower().Contains("haunt"))
                    {
                        btn.OnClick?.Invoke();
                        break;
                    }
                }
            }
            catch { }
        }, 1.2f, "GMAutoOpenHauntMenu", true);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class GMAutoPossessPatch
{
    static PlayerControl currentTarget = null;
    static PlayerControl lastTarget = null;
    static float searchTimer = 0f;

    public static void Postfix(PlayerControl __instance)
    {
        if (__instance != PlayerControl.LocalPlayer) return;
        if (!Options.OptionGMAutoPossess.GetBool()) return;
        if (__instance.IsAlive() || MeetingHud.Instance != null) return;
        if (__instance.GetCustomRole() != CustomRoles.GM) return;

        if (currentTarget == null || !currentTarget.IsAlive())
        {
            searchTimer += Time.fixedDeltaTime;
            if (searchTimer > 1f)
            {
                searchTimer = 0f;
                var targets = PlayerCatch.AllAlivePlayerControls
                    .Where(p => p.PlayerId != __instance.PlayerId)
                    .ToList();
                if (targets.Count > 0)
                    currentTarget = targets[UnityEngine.Random.Range(0, targets.Count)];
            }
        }

        var hauntMenu = UnityEngine.Object.FindObjectOfType<HauntMenuMinigame>();
        if (hauntMenu != null && hauntMenu.gameObject.activeInHierarchy
            && currentTarget != null && currentTarget.IsAlive())
        {
            if (currentTarget != lastTarget)
            {
                hauntMenu.SetHauntTarget(currentTarget);
                lastTarget = currentTarget;
            }
        }
    }
}