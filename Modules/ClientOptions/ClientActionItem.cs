using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.ClientOptions;

public class ClientActionItem
{
    public ToggleButtonBehaviour ToggleButton { get; private set; }
    public Action OnClickAction { get; protected set; }

    public static SpriteRenderer CustomBackground { get; private set; }
    public static ToggleButtonBehaviour ModOptionsButton { get; private set; }

    /// <summary>1ページあたりのボタン数(4行 x 2列)</summary>
    private const int ItemsPerPage = 8;

    private static readonly List<Transform> PageContainers = new();
    private static readonly List<int> PageItemCounts = new();
    private static int currentPage = 0;
    private static int numItems = 0;

    private static ToggleButtonBehaviour prevPageButton;
    private static ToggleButtonBehaviour nextPageButton;

    protected ClientActionItem(
        string name,
        OptionsMenuBehaviour optionsMenuBehaviour,
        bool showTooltip = false,
        int? page = null)
    {
        try
        {
            var mouseMoveToggle = optionsMenuBehaviour.DisableMouseMovement;

            // 1つ目のボタンの生成時に背景・共通UI(閉じる/矢印)も生成
            if (CustomBackground == null)
            {
                numItems = 0;
                currentPage = 0;
                PageContainers.Clear();
                PageItemCounts.Clear();

                CustomBackground = Object.Instantiate(optionsMenuBehaviour.Background, optionsMenuBehaviour.transform);
                CustomBackground.name = "CustomBackground";
                CustomBackground.transform.localScale = new(0.9f, 0.9f, 1f);
                CustomBackground.transform.localPosition += Vector3.back * 8;
                CustomBackground.gameObject.SetActive(false);

                // 閉じるボタン(中央下固定)
                var closeButton = Object.Instantiate(mouseMoveToggle, CustomBackground.transform);
                closeButton.transform.localPosition = new(0f, -2.3f, -6f);
                closeButton.name = "Close";
                closeButton.Text.text = Translator.GetString("Close");
                closeButton.Background.color = Palette.DisabledGrey;
                var closePassiveButton = closeButton.GetComponent<PassiveButton>();
                closePassiveButton.OnClick = new();
                closePassiveButton.OnClick.AddListener(new Action(() =>
                {
                    CustomBackground.gameObject.SetActive(false);
                }));

                // 前ページ矢印ボタン(閉じるボタンの真上・左側)
                prevPageButton = Object.Instantiate(mouseMoveToggle, CustomBackground.transform);
                prevPageButton.transform.localPosition = new(-0.65f, -1.8f, -6f);
                prevPageButton.transform.localScale = new(0.5f, 0.5f, 1f);
                prevPageButton.name = "PrevPage";
                prevPageButton.Text.text = "◀";
                prevPageButton.Background.color = Palette.DisabledGrey;
                var prevPassiveButton = prevPageButton.GetComponent<PassiveButton>();
                prevPassiveButton.OnClick = new();
                prevPassiveButton.OnClick.AddListener(new Action(() => ShowPage(currentPage - 1)));

                // 次ページ矢印ボタン(閉じるボタンの真上・右側)
                nextPageButton = Object.Instantiate(mouseMoveToggle, CustomBackground.transform);
                nextPageButton.transform.localPosition = new(0.65f, -1.8f, -6f);
                nextPageButton.transform.localScale = new(0.5f, 0.5f, 1f);
                nextPageButton.name = "NextPage";
                nextPageButton.Text.text = "▶";
                nextPageButton.Background.color = Palette.DisabledGrey;
                var nextPassiveButton = nextPageButton.GetComponent<PassiveButton>();
                nextPassiveButton.OnClick = new();
                nextPassiveButton.OnClick.AddListener(new Action(() => ShowPage(currentPage + 1)));

                UiElement[] selectableButtons = optionsMenuBehaviour.ControllerSelectable.ToArray();
                PassiveButton leaveButton = null;
                PassiveButton returnButton = null;
                for (int i = 0; i < selectableButtons.Length; i++)
                {
                    var button = selectableButtons[i];
                    if (button == null)
                    {
                        continue;
                    }

                    if (button.name == "LeaveGameButton")
                    {
                        leaveButton = button.GetComponent<PassiveButton>();
                    }
                    else if (button.name == "ReturnToGameButton")
                    {
                        returnButton = button.GetComponent<PassiveButton>();
                    }
                }
                var generalTab = mouseMoveToggle.transform.parent.parent.parent;

                ModOptionsButton = Object.Instantiate(mouseMoveToggle, generalTab);
                ModOptionsButton.transform.localPosition = leaveButton?.transform?.localPosition ?? new(0f, -2.4f, 1f);
                ModOptionsButton.name = "TOH-hmOptions";
                ModOptionsButton.Text.text = Translator.GetString("TOH-hmOptions");
                if (ColorUtility.TryParseHtmlString(Main.ModColor, out var modColor))
                {
                    ModOptionsButton.Background.color = modColor;
                }
                var modOptionsPassiveButton = ModOptionsButton.GetComponent<PassiveButton>();
                modOptionsPassiveButton.OnClick = new();
                modOptionsPassiveButton.OnClick.AddListener(new Action(() =>
                {
                    CustomBackground.gameObject.SetActive(true);
                    ShowPage(0);
                }));

                if (leaveButton != null)
                {
                    leaveButton.transform.localPosition = new(-1.35f, -2.411f, -1f);
                }
                if (returnButton != null)
                {
                    returnButton.transform.localPosition = new(1.35f, -2.411f, -1f);
                }
            }

            var pageIndex = page ?? (numItems / ItemsPerPage);
            var container = GetOrCreatePageContainer(pageIndex);
            while (PageItemCounts.Count <= pageIndex)
            {
                PageItemCounts.Add(0);
            }
            var indexInPage = PageItemCounts[pageIndex];
            PageItemCounts[pageIndex] = indexInPage + 1;

            // ボタン生成(各ページコンテナの子として配置)
            ToggleButton = Object.Instantiate(mouseMoveToggle, container);
            ToggleButton.transform.localPosition = new Vector3(
                // ページ内のボタン数を基に位置を計算
                indexInPage % 2 == 0 ? -1.3f : 1.3f,
                2.2f - (0.5f * (indexInPage / 2)),
                -6f);
            ToggleButton.name = name;
            ToggleButton.Text.text = Translator.GetString(name);
            var passiveButton = ToggleButton.GetComponent<PassiveButton>();
            passiveButton.OnClick = new();
            passiveButton.OnClick.AddListener((Action)OnClick);
            if (showTooltip)
            {
                passiveButton.OnMouseOver.AddListener((Action)(() => ToolTip.Show(passiveButton, Translator.GetString($"{name}Info"), null)));
                passiveButton.OnMouseOut.AddListener((Action)ToolTip.Hide);
            }
        }
        finally
        {
            numItems++;
        }
    }

    private static Transform GetOrCreatePageContainer(int pageIndex)
    {
        while (PageContainers.Count <= pageIndex)
        {
            var containerObj = new GameObject($"Page{PageContainers.Count}");
            containerObj.transform.SetParent(CustomBackground.transform, false);
            containerObj.SetActive(PageContainers.Count == currentPage);
            PageContainers.Add(containerObj.transform);
        }
        return PageContainers[pageIndex];
    }

    /// <summary>
    /// 指定したページを表示する(範囲外は無視、矢印ボタンの活性状態も更新する)
    /// </summary>
    public static void ShowPage(int pageIndex)
    {
        if (PageContainers.Count == 0) return;
        pageIndex = Mathf.Clamp(pageIndex, 0, PageContainers.Count - 1);

        for (int i = 0; i < PageContainers.Count; i++)
        {
            PageContainers[i].gameObject.SetActive(i == pageIndex);
        }
        currentPage = pageIndex;

        if (prevPageButton != null)
        {
            prevPageButton.gameObject.SetActive(currentPage > 0);
        }
        if (nextPageButton != null)
        {
            nextPageButton.gameObject.SetActive(currentPage < PageContainers.Count - 1);
        }
    }

    /// <summary>
    /// Modオプション画面に何かアクションを起こすボタンを追加します
    /// </summary>
    /// <param name="name">ボタンラベルの翻訳キーとボタンのオブジェクト名</param>
    /// <param name="onClickAction">クリック時に発火するアクション</param>
    /// <param name="optionsMenuBehaviour">OptionsMenuBehaviourのインスタンス</param>
    /// <param name="page">明示的に配置したいページ番号(0始まり)。省略時は生成順から自動計算</param>
    /// <returns>作成したアイテム</returns>
    public static ClientActionItem Create(
        string name,
        Action onClickAction,
        OptionsMenuBehaviour optionsMenuBehaviour,
        bool showTooltip = false,
        int? page = null)
    {
        return new(name, optionsMenuBehaviour, showTooltip, page)
        {
            OnClickAction = onClickAction
        };
    }

    public void OnClick()
    {
        OnClickAction?.Invoke();
    }
}
