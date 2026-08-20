using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TownOfHost.Modules.ClientOptions;

public sealed class ClientOptionItem : ClientActionItem
{
    public ConfigEntry<bool> Config { get; private set; }

    private ClientOptionItem(
        string name,
        ConfigEntry<bool> config,
        OptionsMenuBehaviour optionsMenuBehaviour,
        bool showTooltip = false,
        int? page = null)
    : base(
        name,
        optionsMenuBehaviour,
        showTooltip,
        page)
    {
        Config = config;
        UpdateToggle();
    }

    /// <summary>
    /// Modオプション画面にconfigのトグルを追加します
    /// </summary>
    /// <param name="name">ボタンラベルの翻訳キーとボタンのオブジェクト名</param>
    /// <param name="config">対応するconfig</param>
    /// <param name="optionsMenuBehaviour">OptionsMenuBehaviourのインスタンス</param>
    /// <param name="additionalOnClickAction">クリック時に追加で発火するアクション．configが変更されたあとに呼ばれる</param>
    /// <param name="showTooltip">ツールチップを表示するか</param>
    /// <param name="page">明示的に配置したいページ番号(0始まり)。省略時は生成順から自動計算</param>
    /// <returns>作成したアイテム</returns>
    public static ClientOptionItem Create(
        string name,
        ConfigEntry<bool> config,
        OptionsMenuBehaviour optionsMenuBehaviour,
        Action additionalOnClickAction = null,
        bool showTooltip = false,
        int? page = null)
    {
        var item = new ClientOptionItem(name, config, optionsMenuBehaviour, showTooltip, page);
        item.OnClickAction = () =>
        {
            config.Value = !config.Value;
            item.UpdateToggle();
            additionalOnClickAction?.Invoke();
        };
        return item;
    }

    public void UpdateToggle()
    {
        if (ToggleButton == null)
        {
            return;
        }

        var color = Config.Value ? Color.green : Color.red;
        ToggleButton.Background.color = color;
        if (ToggleButton.Rollover != null)
        {
            ToggleButton.Rollover.ChangeOutColor(color);
        }
    }
}