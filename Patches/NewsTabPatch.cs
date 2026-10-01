using System.Collections.Generic;
using Assets.InnerNet;
using HarmonyLib;
using UnityEngine;

using TownOfHost.Templates;

namespace TownOfHost;

/// <summary>
/// メインメニューの「ニュース」画面を、公式ニュースとMODニュースの2ページに分けて見られるようにする。
/// ModNewsHistory側でMOD発のニュースにはNumberとして100000以上の番号が振られているので、それで仕分けする。
///
/// 実装方針(v2):
/// ゲーム側が一覧のパネル(AnnouncementPanel)を全件作り終えた後(CreateAnnouncementListのPostfix)に、
/// 現在のタブに合わないパネルを非表示にして、残りを上から詰め直す。ゲーム側のデータは書き換えない。
/// 動作確認用に、タブ切替のたびに BepInEx のログへ "NewsTab" タグで件数を出している。
///
/// 注意: タブボタンの表示位置(localPosition)は実機で見た目を確認できていないため、
/// 見た目がずれていたり画面外に出てしまっている場合は座標を調整してほしい。
/// </summary>
[HarmonyPatch(typeof(AnnouncementPopUp))]
public static class NewsTabPatch
{
    private enum NewsTab
    {
        Official,
        Mod,
    }

    private const int ModNewsNumberThreshold = 100000;

    private static NewsTab CurrentTab = NewsTab.Official;
    private static SimpleButton officialTabButton;
    private static SimpleButton modTabButton;

    // 一覧のパネルを作り終えた「後」に、現在のタブに合わないパネルを隠して詰め直す方式。
    // (ゲーム側のデータ allAnnouncements は書き換えない)
    private static readonly List<Vector3> panelSlots = new();

    [HarmonyPatch("CreateAnnouncementList")]
    [HarmonyPostfix]
    private static void CreateAnnouncementListPostfix(AnnouncementPopUp __instance)
    {
        EnsureTabButtons(__instance);

        // 作り直された直後の、全件並んでいる状態の位置を「枠」として覚えておく
        panelSlots.Clear();
        var panels = __instance.visibleAnnouncements;
        if (panels != null)
            foreach (var p in panels)
                panelSlots.Add(p == null ? Vector3.zero : p.transform.localPosition);

        ApplyFilter(__instance);
    }

    private static bool IsModNews(AnnouncementPanel panel)
        => panel != null && panel.announcement != null && panel.announcement.Number >= ModNewsNumberThreshold;

    private static void ApplyFilter(AnnouncementPopUp instance)
    {
        UpdateTabButtonLook();

        var panels = instance.visibleAnnouncements;
        if (panels == null) return;

        var slot = 0;
        AnnouncementPanel first = null;
        var total = 0;
        foreach (var p in panels)
        {
            if (p == null) continue;
            total++;
            var show = IsModNews(p) == (CurrentTab == NewsTab.Mod);
            p.gameObject.SetActive(show);
            if (!show) continue;
            if (slot < panelSlots.Count) p.transform.localPosition = panelSlots[slot];
            slot++;
            first ??= p;
        }
        Logger.Info($"tab={CurrentTab} panels={total} shown={slot} slots={panelSlots.Count}", "NewsTab");

        // 右側の本文も、今のタブの先頭のニュースに合わせる
        if (first != null && first.PassiveButton != null)
            first.PassiveButton.OnClick.Invoke();
    }

    private static void EnsureTabButtons(AnnouncementPopUp __instance)
    {
        if (!SimpleButton.IsNullOrDestroyed(officialTabButton) && !SimpleButton.IsNullOrDestroyed(modTabButton))
        {
            // ポップアップが作り直されている場合に備えて親だけ付け直す
            officialTabButton.Button.transform.SetParent(__instance.transform, false);
            modTabButton.Button.transform.SetParent(__instance.transform, false);
            return;
        }

        officialTabButton = new SimpleButton(
            parent: __instance.transform,
            name: "TOHhamo_NewsTab_Official",
            localPosition: new Vector3(-1.3f, 1.9f, -5f),
            normalColor: new Color32(60, 90, 150, 230),
            hoverColor: new Color32(80, 110, 180, 230),
            action: () => SwitchTab(__instance, NewsTab.Official),
            label: Translator.GetString("NewsTabOfficial"));
        officialTabButton.Scale = new Vector2(1.2f, 0.4f);
        officialTabButton.FontSize = 1.3f;

        modTabButton = new SimpleButton(
            parent: __instance.transform,
            name: "TOHhamo_NewsTab_Mod",
            localPosition: new Vector3(0.1f, 1.9f, -5f),
            normalColor: new Color32(150, 60, 130, 230),
            hoverColor: new Color32(180, 80, 160, 230),
            action: () => SwitchTab(__instance, NewsTab.Mod),
            label: Translator.GetString("NewsTabMod"));
        modTabButton.Scale = new Vector2(1.2f, 0.4f);
        modTabButton.FontSize = 1.3f;
    }

    /// <summary>今選んでいるタブが分かるように、非選択側を少し暗くする</summary>
    private static void UpdateTabButtonLook()
    {
        if (SimpleButton.IsNullOrDestroyed(officialTabButton) || SimpleButton.IsNullOrDestroyed(modTabButton)) return;

        officialTabButton.NormalSprite.color = CurrentTab == NewsTab.Official
            ? new Color32(80, 120, 200, 255)
            : new Color32(60, 90, 150, 130);
        modTabButton.NormalSprite.color = CurrentTab == NewsTab.Mod
            ? new Color32(190, 70, 160, 255)
            : new Color32(150, 60, 130, 130);
    }

    private static void SwitchTab(AnnouncementPopUp instance, NewsTab tab)
    {
        if (CurrentTab == tab) return;
        CurrentTab = tab;
        ApplyFilter(instance);
    }
}
