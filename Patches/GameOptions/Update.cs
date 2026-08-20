using System.Linq;

using HarmonyLib;

using TownOfHost.Roles.Core;

using UnityEngine;

using static TownOfHost.GameSettingMenuStartPatch;

using static TownOfHost.Translator;



namespace TownOfHost

{

    [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.Update))]

    public class GameOptionsMenuUpdatePatch

    {

        private static float _timer = 1f;

        public static void Postfix(GameOptionsMenu __instance)

        {

            NumericOptionInput.Tick();

            _timer += Time.deltaTime;

            if (_timer < 0.1f) return;

            _timer = 0f;



            try

            {

                if (priset)

                {

                    // ===== 位置・サイズをMenuPatch.cs側の設定と一致させる =====

                    // 以前はここで別の座標・サイズ(-2.05,3.3 / 0.6倍)に毎フレーム上書きしていたため、

                    // MenuPatch.cs側でどう調整しても0.1秒ごとに元に戻されてしまい、

                    // 「有効なMAP設定」ボタンが意図せず大きく・ズレた位置に表示される原因になっていた。

                    search.transform.localScale = priset.transform.localScale = new Vector3(0.4f, 0.4f, 0f);

                    activeonly.transform.localPosition = new Vector3(-2.0f, 3.25f, -400f);

                    activeonly.transform.localScale = new Vector3(0.4f, 0.4f, 0f);



                    searchtext.enabled = search.textArea.text == "";

                    prisettext.enabled = priset.textArea.text == "";



                    // ===== 表示条件を修正 =====

                    // 元々はTOWNOFHOST-HAMO(ModSettingsButton)が選択されている時だけ

                    // 表示する条件になっていたため、インポスター等の役職タブ(サイドバー)を

                    // 選んでいる間はこのUpdateループが0.1秒ごとに強制的に非表示へ戻してしまい、

                    // 「一瞬表示されてもすぐ消える(チカチカする)」不具合の原因になっていた。

                    // 役職タブ側のサイドバーボタンのいずれかが選択されている場合も表示対象に含める。

                    //

                    // ===== 追加修正 =====

                    // ゲーム内のポーズメニュー(「一般/操作/音声」等が並ぶバニラの設定画面)が

                    // 開いている間は、こちらの検索欄等を表示したままにするとポーズメニューの

                    // 上に重なって表示されてしまっていた。

                    // 以前はGameSettingMenu.GameSettingsTab(役職/ゲームルール設定パネル)を

                    // 見ていたが、実際にこの「一般/操作/音声」画面の正体は別クラスの

                    // OptionsMenuBehaviourだったため、判定が効いていなかった。

                    // こちらの実際のGameObjectのactive状態を見るように修正する。

                    var pauseMenuOpen = OptionsMenuBehaviourStartPatch.Instance != null

                        && OptionsMenuBehaviourStartPatch.Instance.gameObject != null

                        && OptionsMenuBehaviourStartPatch.Instance.gameObject.activeInHierarchy;

                    var active = !pauseMenuOpen && ((ModSettingsButton?.selected ?? false) || (sideTabButtons?.Any(sb => sb != null && sb.selected) ?? false));



                    searchtext.gameObject.SetActive(active);

                    prisettext.gameObject.SetActive(active);

                    search.gameObject.SetActive(active);

                    priset.gameObject.SetActive(active);



                    // ===== ゲーム・プリセット/有効なMAP設定は、チャットやタスク画面が

                    // 開いている間はさらに非表示にする =====

                    // チャット全画面表示やタスク一覧の上に重なって表示されてしまっていたため、

                    // それらが開いている間はこの2つのボタンだけ追加で隠すようにする。

                    var chatOpen = HudManager.Instance?.Chat?.IsOpenOrOpening ?? false;

                    var taskPanelOpen = HudManager.Instance?.TaskPanel?.open ?? false;

                    var activeExceptChatTask = active && !chatOpen && !taskPanelOpen;



                    activeonly.gameObject.SetActive(activeExceptChatTask);

                    if (GamePresetButton) GamePresetButton.gameObject.SetActive(activeExceptChatTask);

                }

                if (timer > 0)

                {

                    timer -= Time.fixedDeltaTime;

                }

                else if (timer > -10)

                {

                    timer = -100;

                    // ユーザー要望によりヒントボックスの文言表示を廃止(常に空文字にする)。

                    GameSettingMenuChangeTabPatch.meg = "";

                }



                var isOnline = AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame;

                var localString = DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.LocalButton);



                if (InfoTimer) InfoTimer.text = isOnline ? GameStartManagerPatch.GetTimerString() : localString;

                if (InfoCount) InfoCount.text = GameStartManager._instance.PlayerCounter.text;

            }

            catch { }



            try

            {

                if (__instance?.transform?.name == "GAME SETTINGS TAB") return;



            if (ShowFilter.IsShowFilter)

            {

                ShowFilter.FixUpdate();

            }



            if (NowRoleTab is not CustomRoles.NotAssigned)

            {

                float numItems = __instance.Children.Count;

                var offset = 2.7f;

                var y = 0.713f;

                foreach (var option in OptionItem.AllOptions)

                {

                    if (option.CustomRole != NowRoleTab && option.ParentRole != NowRoleTab) continue;

                    if (option?.OptionBehaviour == null || option.OptionBehaviour.gameObject == null)

                    {

                        if (option?.OptionHedder is not null)

                        {

                            SetHedder(option);

                        }

                        continue;

                    }

                    var p = option;

                    var parentrole = option.ParentRole;

                    while (p.Parent != null)

                    {

                        p = p.Parent;

                    }

                    if (option.ParentRole == NowRoleTab)

                    {

                        if (!crOptions[NowRoleTab].Contains(p.OptionBehaviour))

                        {

                            p.OptionBehaviour.transform.parent = crlist[option.ParentRole].transform;

                            crOptions[NowRoleTab].Add(p.OptionBehaviour);

                            scOptions[p.Tab].Remove(p.OptionBehaviour);

                        }

                    }

                    if (!crOptions[NowRoleTab].Contains(option.OptionBehaviour) && option.Name == "Maximum")

                    {

                        option.OptionBehaviour.transform.parent = crlist[parentrole].transform;

                        crOptions[NowRoleTab].Add(option.OptionBehaviour);

                        scOptions[option.Tab].Remove(option.OptionBehaviour);

                    }



                    var enabled = true;

                    var parent = option.Parent;

                    var childOption = option;

                    var opt = option.OptionBehaviour.LabelBackground;

                    var isroleoption = option.CustomRole is not CustomRoles.NotAssigned;

                    /*if (isroleoption && rolebutton.TryGetValue(option.CustomRole, out var button))

                    {

                        button.gameObject.SetActive(false);

                    }*/

                    if (roleInfobutton.TryGetValue(option.CustomRole, out var infobutton))

                    {

                        if (!infobutton.isActiveAndEnabled)

                        {

                            infobutton.gameObject.SetActive(true);

                        }

                    }

                    enabled = AmongUsClient.Instance.AmHost && !option.IsHiddenOn(Options.CurrentGameMode);

                    Color color = new Color32(200, 200, 200, 255);

                    Vector2 size = new(5.0f, 0.68f);



                    if (option.Tab is TabGroup.MainSettings or TabGroup.Combinations && (option.NameColor != Color.white || option.NameColorCode != "#ffffff"))

                    {

                        color = option.NameColor == Color.white ? StringHelper.CodeColor(option.NameColorCode) : option.NameColor;



                        color = color.ShadeColor(-6);

                    }

                    if (isroleoption)

                    {

                        color = option.NameColor.ShadeColor(-5);

                    }



                    Transform titleText = option.OptionBehaviour.transform.Find("Title Text");

                    RectTransform titleTextRect = titleText.GetComponent<RectTransform>();



                    var i = 0;

                    while (parent != null && enabled)

                    {

                        i++;

                        enabled = parent.CustomRole is not CustomRoles.NotAssigned || childOption.IsParentValueEnabledForDisplay() || (parent.CustomRole.IsAddOn() && option.Name is not "%roleTypes%Maximum" and not "Maximum" and not "FixedRole");

                        childOption = parent;

                        parent = parent.Parent;

                    }

                    if (i > 0 && option.Name != "Maximum")

                    {

                        i--;

                        if (option.NameColor == Color.white || option.NameColorCode == "#ffffff")

                        {

                            color = UtilsRoleText.GetRoleColor(NowRoleTab, true).ShadeColor(2f).ShadeColor(-0.25f);

                        }

                    }

                    switch (i)

                    {

                        case 0:

                            break;

                        case 1:

                            color = new Color32(40, 50, 80, 255);

                            size = new(4.6f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.8566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.4f, 0.6f);

                            break;

                        case 2:

                            color = new Color32(20, 60, 40, 255);

                            size = new(4.4f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.7566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.35f, 0.6f);

                            break;

                        case 3:

                            color = new Color32(60, 20, 40, 255);

                            size = new(4.2f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.6566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.3f, 0.6f);

                            break;

                        case 4:

                            color = new Color32(60, 40, 10, 255);

                            size = new(4.0f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.6566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.25f, 0.6f);

                            break;

                    }



                    option.OptionBehaviour.gameObject.SetActive(enabled);

                    if (enabled)

                    {

                        opt.color = color;

                        opt.size = size;



                        offset -= option.IsHeader ? 0.68f : 0.45f;

                        option.OptionBehaviour.transform.localPosition = new Vector3(

                            option.OptionBehaviour.transform.localPosition.x,//0.952f,

                            offset - 1.5f,//y,

                            option.OptionBehaviour.transform.localPosition.z);//-120f);

                        y -= option.IsHeader ? 0.68f : 0.45f;



                        if (option.IsHeader)

                        {

                            numItems += 0.5f;

                        }

                    }

                    if (option.Name == "Maximum")

                    {

                        numItems += 0.5f;

                        offset -= 0.23f;

                        y -= 0.23f;

                        var optionrp = OptionItem.AllOptions.FirstOrDefault(op => op.Name == "RoleOption");

                        if (optionrp?.OptionHedder is not null)

                        {

                            optionrp.OptionHedder.transform.parent = option.OptionBehaviour.transform.parent;

                            SetHedder(optionrp);

                        }

                    }

                    else

                    {

                        numItems--;

                    }

                }

                void SetHedder(OptionItem option)

                {

                    var hi = 0;

                    var henabled = AmongUsClient.Instance.AmHost && !option.IsHiddenOn(Options.CurrentGameMode);

                    var heparent = option.Parent;

                    var headerChildOption = option;

                    while (heparent != null && henabled)

                    {

                        hi++;

                        henabled = option.Name == "RoleOption" || heparent.CustomRole is not CustomRoles.NotAssigned || headerChildOption.IsParentValueEnabledForDisplay() || (heparent.CustomRole.IsAddOn() && option.Name is not "%roleTypes%Maximum" and not "Maximum" and not "FixedRole");

                        headerChildOption = heparent;

                        heparent = heparent.Parent;

                    }

                    if (option.Name == "RoleOption")

                    {

                        var chm = option.OptionHedder.GetComponent<CategoryHeaderMasked>();

                        chm.Background.color = UtilsRoleText.GetRoleColor(NowRoleTab);

                        chm.Title.text = NowRoleTab.IsBuffAddon() || NowRoleTab.IsDebuffAddon() || NowRoleTab.IsLovers() ||

                        NowRoleTab is CustomRoles.Amanojaku or CustomRoles.Twins or CustomRoles.Triplets ? "<b>Assign Setting</b>" : "<b>Role Setting</b>";

                        if (NowRoleTab.IsCombinationRole()) chm.Title.text = $"<b>{NowRoleTab} Setting</b>";

                        if (henabled) offset -= -0.23f;

                    }

                    else

                    {

                        var chm = option.OptionHedder.GetComponent<CategoryHeaderMasked>();

                        chm.Background.color = UtilsRoleText.GetRoleColor(NowRoleTab);

                    }

                    option.OptionHedder.gameObject.SetActive(henabled);

                    if (henabled)

                    {

                        offset -= option.IsHeader ? 0.68f : 0.45f;

                        option.OptionHedder.transform.localPosition = new Vector3(

                            option.OptionHedder.transform.localPosition.x,//0.952f,

                            offset - 1.5f,//y,

                            option.OptionHedder.transform.localPosition.z);//-120f);

                        y -= option.IsHeader ? 0.68f : 0.45f;

                        offset -= 0.23f;



                        if (option.IsHeader)

                        {

                            numItems += 0.5f;

                        }

                    }

                    else

                    {

                        numItems--;

                    }

                }

                __instance.GetComponentInParent<Scroller>().ContentYBounds.max = -offset + 0.75f;

                return;

            }



            #region  Tab

            foreach (var tab in EnumHelper.GetAllValues<TabGroup>())

            {

                if (__instance.gameObject.name != tab + "-Stg") continue;



                float numItems = __instance.Children.Count;

                var offset = 2.7f;

                var y = 0.713f;



                foreach (var option in OptionItem.AllOptions)

                {

                    if ((TabGroup)tab != option.Tab) continue;

                    var isroleoption = option.CustomRole is not CustomRoles.NotAssigned;

                    if (option?.OptionBehaviour == null || option.OptionBehaviour.gameObject == null)

                    {

                        if (!isroleoption && option.ParentRole is not CustomRoles.NotAssigned && option.Name is not "Maximum")

                        {

                            option.OptionBehaviour?.gameObject?.SetActive(false);

                            continue;

                        }

                        if (option?.OptionHedder is not null)

                        {

                            var hi = 0;

                            var henabled = AmongUsClient.Instance.AmHost && !option.IsHiddenOn(Options.CurrentGameMode);

                            var heparent = option.Parent;

                            var headerChildOption = option;

                            while (heparent != null && henabled)

                            {

                                hi++;

                                henabled = headerChildOption.IsParentValueEnabledForDisplay();

                                headerChildOption = heparent;

                                heparent = heparent.Parent;

                            }

                            option.OptionHedder.gameObject.SetActive(henabled);

                            if (henabled)

                            {

                                offset -= option.IsHeader ? 0.68f : 0.45f;

                                option.OptionHedder.transform.localPosition = new Vector3(

                                    option.OptionHedder.transform.localPosition.x,//0.952f,

                                    offset - 1.5f,//y,

                                    option.OptionHedder.transform.localPosition.z);//-120f);

                                y -= option.IsHeader ? 0.68f : 0.45f;



                                if (option.IsHeader)

                                {

                                    numItems += 0.5f;

                                }

                            }

                            else

                            {

                                numItems--;

                            }

                        }

                        continue;

                    }

                    var enabled = true;

                    var parent = option.Parent;



                    enabled = AmongUsClient.Instance.AmHost && !option.IsHiddenOn(Options.CurrentGameMode);





                    if (option.CustomRole is not CustomRoles.NotAssigned)

                    {

                        var p = option;

                        while (p.Parent != null)

                        {

                            p = p.Parent;

                        }

                        if (!scOptions[option.Tab].Contains(p.OptionBehaviour))

                        {

                            p.OptionBehaviour.transform.parent = list[option.Tab].transform;

                            scOptions[option.Tab].Add(p.OptionBehaviour);

                            crOptions[option.CustomRole].Remove(p.OptionBehaviour);

                        }

                    }

                    else

                    {

                        if (!scOptions[option.Tab].Contains(option.OptionBehaviour) && option.Name == "Maximum")

                        {

                            option.OptionBehaviour.transform.parent = list[option.Tab].transform;

                            scOptions[option.Tab].Add(option.OptionBehaviour);

                            crOptions[option.ParentRole].Remove(option.OptionBehaviour);

                        }

                    }

                    if (!isroleoption && option.ParentRole is not CustomRoles.NotAssigned && option.Name is not "Maximum")

                    {

                        option.OptionBehaviour?.gameObject?.SetActive(false);

                        continue;

                    }



                    //起動時以外で表示/非表示を切り替える際に使う

                    if (enabled)

                    {

                        if (ActiveOnlyMode)

                        {

                            if (isroleoption)

                            {

                                enabled = option.GetBool();

                            }

                            if (OptionShower.Checkenabled(option) is false or null)

                            {

                                var v = OptionShower.Checkenabled(option);

                                enabled = v is not null && option.GetBool();

                            }

                            if (option.Parent is not null)

                                if (OptionShower.Checkenabled(option.Parent) is false or null)

                                {

                                    var v = OptionShower.Checkenabled(option.Parent);

                                    enabled = v is not null && option.IsParentValueEnabledForDisplay();

                                }

                        }

                        if (!Event.CheckRole(option.CustomRole))

                        {

                            enabled = false;

                        }

                    }

                    var opt = option.OptionBehaviour.LabelBackground;

                    if (isroleoption && rolebutton.TryGetValue(option.CustomRole, out var button))

                    {

                        button.gameObject.SetActive(!(2f < (opt.transform.position.y - roletaby) ||

                        (opt.transform.position.y - roletaby) <= -2));



                        if (roleInfobutton.TryGetValue(option.CustomRole, out var infobutton))

                        {

                            if (!infobutton.isActiveAndEnabled) infobutton.gameObject.SetActive(true);

                        }

                    }



                    Color color = new Color32(200, 200, 200, 255);

                    Vector2 size = new(5.0f, 0.68f);



                    if (option.Tab is TabGroup.MainSettings or TabGroup.Combinations && (option.NameColor != Color.white || option.NameColorCode != "#ffffff"))

                    {

                        color = option.NameColor == Color.white ? StringHelper.CodeColor(option.NameColorCode) : option.NameColor;



                        color = color.ShadeColor(-6);

                    }

                    if (isroleoption)

                    {

                        color = option.NameColor.ShadeColor(-5);

                    }



                    Transform titleText = option.OptionBehaviour.transform.Find("Title Text");

                    RectTransform titleTextRect = titleText.GetComponent<RectTransform>();



                    var i = 0;

                    var childOption = option;

                    while (parent != null && enabled)

                    {

                        i++;

                        enabled = childOption.IsParentValueEnabledForDisplay();

                        childOption = parent;

                        parent = parent.Parent;

                    }



                    switch (i)

                    {

                        case 0:

                            break;

                        case 1:

                            color = new Color32(40, 50, 80, 255);

                            size = new(4.6f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.8566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.4f, 0.6f);

                            break;

                        case 2:

                            color = new Color32(20, 60, 40, 255);

                            size = new(4.4f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.7566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.35f, 0.6f);

                            break;

                        case 3:

                            color = new Color32(60, 20, 40, 255);

                            size = new(4.2f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.6566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.3f, 0.6f);

                            break;

                        case 4:

                            color = new Color32(60, 40, 10, 255);

                            size = new(4.0f, 0.68f);

                            titleText.transform.localPosition = new Vector3(-1.6566f, 0f);

                            titleTextRect.sizeDelta = new Vector2(6.25f, 0.6f);

                            break;

                    }



                    option.OptionBehaviour.gameObject.SetActive(enabled);

                    if (enabled)

                    {

                        opt.color = color;

                        opt.size = size;

                        offset -= option.IsHeader ? 0.68f : 0.45f;

                        option.OptionBehaviour.transform.localPosition = new Vector3(

                            option.OptionBehaviour.transform.localPosition.x,//0.952f,

                            offset - 1.5f,//y,

                            option.OptionBehaviour.transform.localPosition.z);//-120f);

                        y -= option.IsHeader ? 0.68f : 0.45f;



                        if (option.IsHeader)

                        {

                            numItems += 0.5f;

                        }

                    }

                    else

                    {

                        numItems--;

                    }

                }

                __instance.GetComponentInParent<Scroller>().ContentYBounds.max = -offset + 0.75f;

            }

            #endregion

            }

            catch { }

        }

    }

    [HarmonyPatch(typeof(GameOptionButton), nameof(GameOptionButton.SetInteractable))]

    class GameOptionButtonSetInteractablePatch

    {

        public static bool Prefix(GameOptionButton __instance)

        {

            __instance.isInteractable = true;

            return false;

        }

    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Update))]

    class GameSettingMenuUpdataPatch

    {

        public static void Postfix(GameSettingMenu __instance)

        {

            {

                // meg経由のテキストクリアだけだと、ヴァニラ側のホバー説明などが

                // 別タイミングで書き込んでしまうことがあるため、箱自体を非表示にする。

                // (テキストのオブジェクトだけでなく、枠や吹き出しキャラ等を含む

                //  親のオブジェクトごと非表示にしないと、枠だけ残ってしまう)

                if (ModSettingsButton?.selected ?? false)

                    __instance.MenuDescriptionText.text = GameSettingMenuChangeTabPatch.meg;



                var descParent = __instance.MenuDescriptionText.transform.parent;

                if (descParent != null) descParent.gameObject.SetActive(false);

                else __instance.MenuDescriptionText.gameObject.SetActive(false);

            }

            var settingsButton = __instance.GameSettingsTab?.gameObject;

            var allButton = ModSettingsTab?.AllButton?.gameObject;

            if (settingsButton?.active == true && (ModSettingsButton?.selected ?? false) && __instance?.GameSettingsTab is not null)

            {

                __instance.GameSettingsTab?.gameObject?.SetActive(false);

            }

            if (allButton?.active == true)

                allButton?.SetActive(false);

        }

    }

    [HarmonyPatch(typeof(FreeChatInputField), nameof(FreeChatInputField.UpdateCharCount))]

    class BlockUpdateCharCount

    {

        public static bool Prefix(FreeChatInputField __instance)

        {

            if (__instance.transform.name is "SearchSet" or "PresetSet")

            {

                __instance.textArea.characterLimit = 20;

                __instance.charCountText.text = $"{__instance.textArea.text.Length}/20";

                return false;

            }

            return true;

        }

    }

}

