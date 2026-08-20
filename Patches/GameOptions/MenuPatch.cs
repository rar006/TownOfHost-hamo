using System;

using System.Collections.Generic;

using System.Linq;

using Il2CppSystem.Linq;

using HarmonyLib;

using UnityEngine;



using Object = UnityEngine.Object;

using TownOfHost.Modules;

using TownOfHost.Roles.Core;

using static TownOfHost.Translator;

using static TownOfHost.GameSettingMenuStartPatch;



namespace TownOfHost

{

    /// <summary>

    /// 役職一覧の各行にある「?」「役職強制付与」等のボタンに、連打防止のための

    /// クールダウンを共通でかけるためのヘルパー。

    /// 連打するとRPC等が短時間に大量発行され、ホストが重くなったり落ちたりする

    /// おそれがあるため、ボタンごとに個別のクールダウン(既定3秒)を設ける。

    /// </summary>

    public static class RoleRowButtonCooldown

    {

        private static readonly Dictionary<string, float> _lastClickedAt = new();



        /// <summary>

        /// 指定したキー(ボタンごとに一意な名前)がクールダウン中かどうかを確認し、

        /// クールダウン中でなければ「今クリックした」ことを記録してtrueを返す。

        /// クールダウン中ならfalseを返し、呼び出し元は処理を実行しない。

        /// </summary>

        public static bool TryClick(string key, float cooldownSeconds = 3f)

        {

            float now = Time.realtimeSinceStartup;

            if (_lastClickedAt.TryGetValue(key, out var last) && now - last < cooldownSeconds)

            {

                return false;

            }

            _lastClickedAt[key] = now;

            return true;

        }

    }



    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]

    public class RpcSyncSettingsPatch

    {

        public static void Postfix()

        {

            OptionShower.Update = true;

            OptionItem.SyncAllOptions();

        }

    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Close))]

    class GameSettingMenuClosePatch

    {

        public static bool Prefix()

        {

            if (ShowFilter.CallEsc(true)) return false;

            if (ShowRandomSpawnOption.CallEsc(true)) return false;

            return true;

        }

        public static void Postfix()

        {

            NumericOptionInput.Close(false);

            if (ShowFilter.CallEsc()) return;

            if (ShowRandomSpawnOption.CallEsc()) return;



            // ===== 重要 =====

            // list/crlist で生成した GameOptionsMenu(タブ・役職ごとの設定入れ物)や、

            // 各オプションが持つ OptionBehaviour(実際のUI部品)は、ここで参照を

            // null にするだけでは実体のGameObjectが破棄されない。

            // 破棄しないまま次回設定を開くと、

            //   ・同名("{role}-Stg"等)のGameObjectが二重三重に増え続け動作がおかしくなる

            //     (2回目以降設定が開けなくなる原因)

            //   ・役職数分のGameObject/コンポーネントが開くたびに積み重なり、

            //     ホストの処理が重くなって参加者側の同期にも影響が出る原因

            // になっていたため、ここで確実にDestroyしてから参照をクリアする。

            DestroyGeneratedMenus();



            ModSettingsButton = null;

            ModSettingsTab = null;

            activeonly = null;

            ActiveOnlyMode = false;

            priset = null;

            prisettext = null;

            search = null;

            searchtext = null;

            list = null;

            scOptions = null;

            crlist = null;

            crOptions = null;

            roleopts = new();

            rolebutton = new();

            roleInfobutton = new();

            IsClick = false;

            ModoruTabu = (TabGroup.MainSettings, 0);

            timer = -100;

            tabGenerated = null;

            ShowFilter.CloseOptionMenu();

            ShowRandomSpawnOption.CloseOptionMenu();

            VanillaOptionHolder.SetOptinItem();

            StringOptionStartPatch.all?.Clear();

            NumberOptionStartPatch.all?.Clear();

            ToggleOptionStartPatch.all?.Clear();

        }



        /// <summary>

        /// 設定を閉じる際、タブ/役職ごとに生成したメニューのGameObjectと、

        /// 各オプションが保持しているUI部品(OptionBehaviour)の参照を確実に破棄する。

        /// これにより「閉じて再度開く」を繰り返してもGameObjectが積み重ならない。

        /// </summary>

        private static void DestroyGeneratedMenus()

        {

            try

            {

                if (list != null)

                {

                    foreach (var kv in list)

                    {

                        if (kv.Value != null && kv.Value.gameObject != null)

                            Object.DestroyImmediate(kv.Value.gameObject);

                    }

                }

                if (crlist != null)

                {

                    foreach (var kv in crlist)

                    {

                        if (kv.Value != null && kv.Value.gameObject != null)

                            Object.DestroyImmediate(kv.Value.gameObject);

                    }

                }



                // タブボタン(list/crlistとは別に生成している)も同様に破棄する

                if (tabButtons != null)

                {

                    foreach (var btn in tabButtons)

                    {

                        if (btn != null && btn.gameObject != null)

                            Object.DestroyImmediate(btn.gameObject);

                    }

                }



                // 各オプションが持つUI部品(OptionBehaviour)への参照をクリアする。

                // 破棄済みのGameObjectへの参照が残ったままだと、次回生成時の

                // 「既に生成済みかどうか」の判定(option.OptionBehaviour == null)が

                // 正しく働かなくなるおそれがあるため、ここで確実にnullへ戻す。

                if (OptionItem.AllOptions != null)

                {

                    foreach (var option in OptionItem.AllOptions)

                    {

                        option.OptionBehaviour = null;

                    }

                }

            }

            catch (Exception e)

            {

                Logger.Error($"設定メニューのGameObject破棄中にエラー: {e.Message}", "GameSettingMenuClosePatch");

            }

        }

    }



    [HarmonyPatch(typeof(GameOptionsMenu))]

    class GameOptionsMenuInitializePatch

    {        public static bool CheckModMenu(GameOptionsMenu __instance) => __instance.name.IndexOf("-Stg".AsSpan().ToString(), StringComparison.Ordinal) > 0;

        [HarmonyPrefix]

        [HarmonyPatch(nameof(GameOptionsMenu.Awake))]

        [HarmonyPatch(nameof(GameOptionsMenu.CloseMenu))]

        public static bool CheckPrefix(GameOptionsMenu __instance)

            => !CheckModMenu(__instance);



        [HarmonyPatch(nameof(GameOptionsMenu.Initialize)), HarmonyPrefix]

        public static bool InitializePrefix(GameOptionsMenu __instance)

        {

            if (!CheckModMenu(__instance)) return true;

            __instance.cachedData = GameOptionsManager.Instance.CurrentGameOptions;

            return false;

        }

    }



    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]

    class GameSettingMenuStartPatch

    {

        public static bool ShowModSetting;

        public static PassiveButton ModSettingsButton;

        public static RolesSettingsMenu ModSettingsTab;

        public static PassiveButton activeonly;

        public static bool ActiveOnlyMode;

        // 「ゲーム・プリセット」ボタン。元はPostfix内のローカル変数だったが、

        // Update.cs側からも表示状態を制御する必要があるためstaticフィールドに昇格。

        public static PassiveButton GamePresetButton;

        public static FreeChatInputField priset;

        public static TMPro.TextMeshPro prisettext;

        public static FreeChatInputField search;

        public static TMPro.TextMeshPro searchtext;

        public static Il2CppSystem.Collections.Generic.List<PassiveButton> tabButtons;

        // サイドバーの役職タブボタン(インポスター〜ゴースト)。選択色の更新に使う。

        public static List<PassiveButton> sideTabButtons = new();

        public static CustomRoles NowRoleTab;

        public static CustomRoles Nowinfo;

        public static Dictionary<TabGroup, GameOptionsMenu> list = new();

        public static Dictionary<TabGroup, Il2CppSystem.Collections.Generic.List<OptionBehaviour>> scOptions = new();

        public static Dictionary<CustomRoles, GameOptionsMenu> crlist = new();

        public static Dictionary<CustomRoles, Il2CppSystem.Collections.Generic.List<OptionBehaviour>> crOptions = new();

        public static List<OptionItem> roleopts = new();

        public static Dictionary<CustomRoles, PassiveButton> rolebutton = new();

        public static Dictionary<CustomRoles, PassiveButton> roleInfobutton = new();

        public static (TabGroup, float) ModoruTabu;

        public static float timer;

        public static bool IsClick = false;

        public static TMPro.TextMeshPro InfoTimer;

        public static TMPro.TextMeshPro InfoCount;

        public static StringOption BaseOption;

        public static float roletaby;

        public static HashSet<TabGroup> tabGenerated;



        /// <summary>

        /// 指定した親Transform配下に、指定した名前の子オブジェクトが残っていないかを

        /// 実際のシーン階層から検索し、あれば確実に破棄する。

        /// C#側のstatic参照(ModSettingsButton等)がnullかどうかに関わらず、

        /// 実体のGameObjectが残っていないかを直接チェックすることで、

        /// Close処理がどの経路を通って終了したかに依存しない、より確実な重複防止を行う。

        /// </summary>

        private static void DestroyStaleChildByName(Transform parent, string childName)

        {

            if (parent == null) return;

            try

            {

                // 同名の子が複数残っている可能性もゼロではないため、見つかった分すべて破棄する

                for (int i = parent.childCount - 1; i >= 0; i--)

                {

                    var child = parent.GetChild(i);

                    if (child != null && child.name == childName)

                    {

                        Object.DestroyImmediate(child.gameObject);

                    }

                }

            }

            catch (Exception e)

            {

                Logger.Error($"残留オブジェクトの掃除中にエラー({childName}): {e.Message}", "GameSettingMenuStartPatch");

            }

        }



        /// <summary>

        /// 検索欄/プリセット名編集/有効なMAP設定・役職のみ表示するボタン/ゲーム・プリセットボタンは、

        /// 役職タブ(インポスター等)の一覧パネルより後ろのsortingOrderで描画されていたため、

        /// 役職タブを開くと一覧パネルの陰に隠れて見えなくなることがあった。

        /// これらの子要素すべてのSpriteRenderer/TextMeshProに強制的に高いsortingOrderを設定し、

        /// 常に最前面に描画されるようにする。

        /// ===== 注意 =====

        /// 全部を同じ値にすると、ボタン自体の背景(SpriteRenderer)がその上に乗っている

        /// 文字(TextMeshPro)より前に来てしまい、文字が背景の裏に隠れて消えて見える

        /// (実際に一度これで発生した)。背景は基準値、文字はさらにその上、という順で

        /// ずらして設定する。

        /// </summary>

        private static void ForceRenderInFront(Component root, int sortingOrder = 6)

        {

            if (root == null) return;

            try

            {

                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))

                {

                    sr.sortingOrder = sortingOrder;

                    // sortingLayerIDが親と食い違っていると、sortingOrderをいくら上げても

                    // 別レイヤーとして扱われ前面化が効かないことがあるため、既定(0)に揃える。

                    sr.sortingLayerID = 0;

                }

                foreach (var tmp in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))

                {

                    tmp.sortingOrder = sortingOrder + 10;

                }

            }

            catch (Exception e)

            {

                Logger.Error($"最前面表示化中にエラー: {e.Message}", "GameSettingMenuStartPatch");

            }

        }



        public static void Postfix(GameSettingMenu __instance)

        {

            var ErrorNumber = 0;

            PassiveButton settingsButton = null;



            try

            {

                // 万一前回のCloseで破棄しきれず残っていた場合に備え、開く前にも念のため

                // 同名の残留GameObject(古いModSettingsButton等)を掃除しておく。

                // これにより「開けないことがある」不具合の再現条件を減らす。

                if (ModSettingsButton != null && ModSettingsButton.gameObject != null)

                {

                    Object.DestroyImmediate(ModSettingsButton.gameObject);

                    ModSettingsButton = null;

                }

                if (activeonly != null && activeonly.gameObject != null)

                {

                    Object.DestroyImmediate(activeonly.gameObject);

                    activeonly = null;

                }



                var size = __instance.transform.localScale;

                __instance.transform.localScale = new(1, 1, 1);

                timer = -100;

                ModoruTabu = (TabGroup.MainSettings, 0);

                roleopts = new();

                rolebutton = new();

                roleInfobutton = new();

                NowRoleTab = CustomRoles.NotAssigned;

                tabGenerated = new();

                if (HudManager.Instance?.TaskPanel?.open is true)

                {

                    HudManager.Instance.TaskPanel.ToggleOpen();

                }

                else if (HudManager.Instance?.TaskPanel?.open is null)

                {

                    Logger.Error("HudManagerがnull!", "OptionMenu");

                }

                ActiveOnlyMode = false;

                GamePresetButton = __instance.GamePresetsButton;

                var GameSettingsButton = __instance.GameSettingsButton;

                var RoleSettingsButton = __instance.RoleSettingsButton;



                // ===== 重複生成防止(堅牢化) =====

                // Close処理の経路(フィルター/スポーン選択画面が開いていた場合の

                // 早期リターン等)によっては、前回のGameObjectが破棄されないまま

                // 残ってしまうケースがある。C#側の参照(ModSettingsButton等)だけに

                // 頼らず、実際の親Transform配下を名前で検索し、同名の残留オブジェクトが

                // あれば確実に削除してから新規生成する。

                DestroyStaleChildByName(RoleSettingsButton.transform.parent, "TownOfHostSetting");

                DestroyStaleChildByName(__instance.RoleSettingsTab.transform.parent, "ActiveOnly");



                ModSettingsButton = Object.Instantiate(RoleSettingsButton, RoleSettingsButton.transform.parent);

                activeonly = Object.Instantiate(GamePresetButton, __instance.RoleSettingsTab.transform.parent);



                ErrorNumber = 1;

                if (activeonly)

                {

                    activeonly.buttonText.text = $"{GetString("ActiveOptionOnly")} <size=5>(OFF)</size>";

                    activeonly.gameObject.name = "ActiveOnly";



                    activeonly.inactiveSprites.GetComponent<SpriteRenderer>().color =

                    activeonly.activeSprites.GetComponent<SpriteRenderer>().color =

                    activeonly.selectedSprites.GetComponent<SpriteRenderer>().color = ModColors.bluegreen;

                    activeonly.buttonText.DestroyTranslator();



                    // ===== 位置を明示する =====

                    // 「有効なMAP設定/役職のみ表示する」ボタン自体の位置。

                    // 上のTOH HAMO/GAME/OTHERアイコン行と被らない高さにする。

                    activeonly.transform.localPosition = new Vector3(-2.0f, 3.25f, -400f);

                    activeonly.transform.localScale = new Vector3(0.4f, 0.4f, 0f);

                }



                ForceRenderInFront(activeonly?.transform);

                activeonly?.transform?.SetAsFirstSibling();



                // ===== 「ゲーム・プリセット」を「有効なMAP設定/役職のみ表示する」の真上に配置する =====

                // GamePresetButtonは元々別の親(バニラのUI階層)にいるため、activeonly/priset/searchと

                // 同じ座標系(RoleSettingsTabの親)に付け替えてから位置を合わせる。

                GamePresetButton.transform.SetParent(__instance.RoleSettingsTab.transform.parent, false);

                GamePresetButton.transform.localScale = new(0.3f, 0.3f);

                GamePresetButton.transform.localPosition = new Vector3(-1.9f, 3.6f, -400f);

                ForceRenderInFront(GamePresetButton?.transform);

                GamePresetButton.transform.SetAsFirstSibling();



                GamePresetButton.OnClick = new();

                GamePresetButton.OnClick.AddListener((Action)(() =>

                {

                    IsClick = true;

                    __instance.ChangeTab(0, false);

                }));



                // hamo側の独自役職設定UIを使用するため、標準の役職設定ボタンは非表示にする。
                RoleSettingsButton.gameObject.SetActive(false);



                // ===== 左サイドバーの「ゲーム設定」「TOWNOFHOST-HAMO」を小さく上に詰める =====

                // デフォルトのままだと大きく、画面下の役職一覧まではみ出て見えなくなるため、

                // 一回り小さくして上に寄せる。上に寄せすぎると見出しの「ゲーム設定」の文字と

                // 被ってしまうため、上げ幅は控えめにする。

                const float sidebarTopScale = 0.56f;

                GameSettingsButton.transform.localScale *= sidebarTopScale;

                GameSettingsButton.transform.localPosition += new Vector3(0f, 2.0f, 0f);



                ModSettingsButton.gameObject.name = "TownOfHostSetting";

                ModSettingsButton.buttonText.text = "TownOfHost-hamo";

                var activeSprite = ModSettingsButton.activeSprites.GetComponent<SpriteRenderer>();

                var selectedSprite = ModSettingsButton.selectedSprites.GetComponent<SpriteRenderer>();

                activeSprite.color = StringHelper.CodeColor(Main.ModColor);

                selectedSprite.color = StringHelper.CodeColor(Main.ModColor).ShadeColor(-0.2f);

                ModSettingsButton.buttonText.DestroyTranslator();//翻訳破壊☆



                // 「ゲーム設定」と同じ縮尺にして、その直下に配置する。

                ModSettingsButton.transform.localScale = GameSettingsButton.transform.localScale;

                ModSettingsButton.transform.localPosition = GameSettingsButton.transform.localPosition + new Vector3(0f, -0.5f, 0f);



                ModSettingsButton.OnMouseOver.AddListener((Action)(() => { if (Controller.currentTouchType == Controller.TouchType.Joystick) __instance.ChangeTab(3, true); }));

                ControllerManager.Instance.CurrentUiState.SelectableUiElements.Add(ModSettingsButton);



                ErrorNumber = 2;

                activeonly.OnClick = new();

                activeonly.OnClick.AddListener((Action)(() =>

                {

                    // ===== 有効判定を修正 =====

                    // 元々はTOWNOFHOST-HAMO(ModSettingsButton)選択時のみ動作する条件だったため、

                    // インポスター等の役職タブでこのボタンを押しても反応しなかった。

                    // 役職タブ側のサイドバーボタンが選択されている場合も対象に含める。

                    if (ModSettingsButton.selected || (sideTabButtons?.Any(sb => sb != null && sb.selected) ?? false))

                    {

                        ActiveOnlyMode = !ActiveOnlyMode;

                        activeonly.inactiveSprites.GetComponent<SpriteRenderer>().color =

                        activeonly.activeSprites.GetComponent<SpriteRenderer>().color =

                        activeonly.selectedSprites.GetComponent<SpriteRenderer>().color = ActiveOnlyMode ? ModColors.GhostRoleColor : ModColors.bluegreen;

                        var now = ActiveOnlyMode ? "ON" : "OFF";

                        activeonly.buttonText.text = $"{GetString("ActiveOptionOnly")} <size=5>({now})</size>";

                        activeonly.selected = false;

                        ModSettingsTab.scrollBar.velocity = Vector2.zero;

                        ModSettingsTab.scrollBar.Inner.localPosition = new Vector3(ModSettingsTab.scrollBar.Inner.localPosition.x, 0, ModSettingsTab.scrollBar.Inner.localPosition.z);

                        ModSettingsTab.scrollBar.ScrollRelative(Vector2.zero);

                    }

                }));

                activeonly.gameObject.SetActive(false);



                // ModSettingsTab配下に list/crlist で生成された全タブ・役職メニューが

                // ぶら下がっている。ここで確実に古いModSettingTabごと破棄しておけば、

                // 中身のGameObjectも道連れで一掃されるため、二重生成を防げる。

                DestroyStaleChildByName(__instance.RoleSettingsTab.transform.parent, "ModSettingTab");



                ModSettingsTab = Object.Instantiate(__instance.RoleSettingsTab, __instance.RoleSettingsTab.transform.parent);

                ModSettingsTab.name = "ModSettingTab";

                var backButton = ModSettingsTab.BackButton.Cast<PassiveButton>();

                backButton.OnClick = new();

                backButton.OnClick.AddListener((Action)(() => { ModSettingsTab.CloseMenu(); __instance.ChangeTab(3, true); }));

                roletaby = ModSettingsTab.transform.position.y;



                if (priset == null)

                {

                    try

                    {

                        priset = Object.Instantiate(HudManager.Instance.Chat.freeChatField, __instance.RoleSettingsTab.transform.parent);

                        search = Object.Instantiate(HudManager.Instance.Chat.freeChatField, __instance.RoleSettingsTab.transform.parent);



                        // ===== AspectPositionの除去 =====

                        // freeChatField(元はチャット入力欄)にはAspectPositionが付いており、

                        // 毎フレーム画面端に強制的に位置を合わせ直してしまう。

                        // このせいでlocalPositionを設定しても効かず、検索欄が画面右上に

                        // ズレたまま表示される不具合の原因になっていたため、複製後に破棄する。

                        var prisetAspect = priset.GetComponent<AspectPosition>();

                        if (prisetAspect != null) Object.DestroyImmediate(prisetAspect);

                        var searchAspect = search.GetComponent<AspectPosition>();

                        if (searchAspect != null) Object.DestroyImmediate(searchAspect);



                        prisettext = Object.Instantiate(HudManager.Instance.TaskPanel.taskText, priset.transform);

                        prisettext.text = $"<size=120%><#cccccc><b>{GetString("SetPresetName")}</b></color></size>";

                        prisettext.transform.localPosition = new Vector3(-2f, -1.1f);

                        // 「検索」ラベルの親はsearch(検索欄本体)にする(以前priset側に付いていて位置がズレていた)

                        searchtext = Object.Instantiate(HudManager.Instance.TaskPanel.taskText, search.transform);

                        searchtext.text = $"<size=120%><#ffa826><b>{GetString("Search")}</b></color></size>";

                        // ===== 「検索」ラベルを白い入力欄の中に表示する =====

                        // 以前は入力欄の上に浮いた見た目になっていたため、少し下に下げて

                        // 実際の入力ボックス内(プリセット名編集の上の白いBOX)に重なるようにする。

                        searchtext.transform.localPosition = new Vector3(-2f, -1.0f);

                        priset.name = "PresetSet";

                        search.name = "SearchSet";

                    }

                    catch (Exception ex)

                    {

                        Logger.Exception(ex, "OptionsManager");

                    }

                }



                ErrorNumber = 3;

                if (priset)

                {

                    priset.transform.localPosition = new Vector3(0.3f, 3.2f);

                    priset.transform.localScale = new Vector3(0.4f, 0.4f, 0f);

                    priset?.gameObject?.SetActive(true);

                    ForceRenderInFront(priset);

                    priset.submitButton.OnPressed = (Action)(() =>

                    {

                        if (priset.textArea.text != "")

                        {

                            var pr = OptionItem.AllOptions.Where(op => op.Id == 0).FirstOrDefault();

                            switch (pr.CurrentValue)

                            {

                                case 0: Main.Preset1.Value = priset.textArea.text; break;

                                case 1: Main.Preset2.Value = priset.textArea.text; break;

                                case 2: Main.Preset3.Value = priset.textArea.text; break;

                                case 3: Main.Preset4.Value = priset.textArea.text; break;

                                case 4: Main.Preset5.Value = priset.textArea.text; break;

                                case 5: Main.Preset6.Value = priset.textArea.text; break;

                                case 6: Main.Preset7.Value = priset.textArea.text; break;

                                case 7: Main.Preset8.Value = priset.textArea.text; break;

                                case 8: Main.Preset9.Value = priset.textArea.text; break;

                                case 9: Main.Preset10.Value = priset.textArea.text; break;

                                case 10: Main.Preset11.Value = priset.textArea.text; break;

                                case 11: Main.Preset12.Value = priset.textArea.text; break;

                                case 12: Main.Preset13.Value = priset.textArea.text; break;

                                case 13: Main.Preset14.Value = priset.textArea.text; break;

                                case 14: Main.Preset15.Value = priset.textArea.text; break;

                                case 15: Main.Preset16.Value = priset.textArea.text; break;



                            }

                            priset.textArea.Clear();

                        }

                    });

                }

                else { Logger.Error("prisetでError!", "MenuPatch"); }

                Dictionary<TabGroup, GameObject> menus = new();

                Dictionary<CustomRoles, GameObject> crmenus = new();



                __instance?.GameSettingsTab?.gameObject?.SetActive(true);

                GameObject.Find("Main Camera/PlayerOptionsMenu(Clone)/MainArea/ModSettingTab/Gradient")?.SetActive(false);



                BaseOption = GameObject.Find("Main Camera/PlayerOptionsMenu(Clone)/MainArea/GAME SETTINGS TAB/Scroller/SliderInner/GameOption_String(Clone)")?.GetComponent<StringOption>();



                ErrorNumber = 4;



                list = new();

                scOptions = new();

                crlist = new();

                crOptions = new();



                var TabLength = EnumHelper.GetAllValues<TabGroup>().Length;



                ErrorNumber = 5;

                for (var i = 0; i < TabLength; i++)

                {

                    var tab = (TabGroup)i;

                    var optionsMenu = new GameObject($"{tab}-Stg").AddComponent<GameOptionsMenu>();

                    var transform = optionsMenu.transform;

                    transform.SetParent(ModSettingsTab.AdvancedRolesSettings.transform.parent);

                    transform.localPosition = new Vector3(0.7789f, -0.5101f);

                    list.Add(tab, optionsMenu);

                    scOptions[tab] = new();

                }

                foreach (var role in EnumHelper.GetAllValues<CustomRoles>())

                {

                    var optionsMenu = new GameObject($"{role}-Stg").AddComponent<GameOptionsMenu>();

                    var transform = optionsMenu.transform;

                    transform.SetParent(ModSettingsTab.AdvancedRolesSettings.transform.parent);

                    transform.localPosition = new Vector3(0.7789f, -0.5101f);

                    crlist.Add(role, optionsMenu);

                    crOptions[role] = new();

                }



                ErrorNumber = 6;



                ErrorNumber = 7;

                var templateTabButton = ModSettingsTab.AllButton;

                {

                    Object.Destroy(templateTabButton.buttonText.gameObject);

                }



                ModSettingsTab.roleTabs = new();

                tabButtons = new();

                // ボタン本体のactive/inactive/selectedSpritesはAmong Us標準の背景として維持する。
                // アイコンは別オブジェクトで表示し、選択時にボタン背景が消える問題を回避する。
                var topTabIconRenderers = new Dictionary<TabGroup, SpriteRenderer>();
                Sprite LoadTopTabIcon(TabGroup targetTab, bool selected)
                {
                    var prefix = selected ? "TabIcon_S_" : "TabIcon_";
                    var icon = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Tab.{prefix}{targetTab}.png", selected ? 120 : 60);

                    // OTHER／OTHER2のS画像がまだない場合に、TOH HAMO（MainSettings）へ
                    // 差し替わらないよう同タブの通常画像を使う。S画像を置けばそちらを優先する。
                    if (icon == null && selected)
                        icon = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Tab.TabIcon_{targetTab}.png", 120);

                    return icon ?? UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Tab.{prefix}MainSettings.png", selected ? 120 : 60);
                }

                // ===== 横一列に出すのはゲーム設定(TOWNOFHOST-HAMO)/GAME/OTHER/OTHER2の4つだけにする =====

                // インポスター〜ゴーストの役職タブは横一列から外し、縦のサイドバー側に移す。

                int visibleTabCount = 0;

                for (var i = 0; i < TabLength; i++)

                {

                    var tab = (TabGroup)i;

                    var tabs = list[tab];

                    Il2CppSystem.Collections.Generic.List<OptionBehaviour> options = new();

                    tabs.Children = scOptions[tab];

                    tabs.gameObject.SetActive(false);

                    tabs.enabled = true;

                    menus.Add(tab, tabs.gameObject);



                    bool showInTopRow = tab is TabGroup.MainSettings or TabGroup.Game or TabGroup.Other or TabGroup.Other2;



                    var tabButton = Object.Instantiate(templateTabButton, templateTabButton.transform.parent);

                    tabButton.name = tab.ToString();

                    if (showInTopRow)
                    {
                        // 元のhamoボタンの3つの状態スプライトをそのまま利用する。
                        // 別オブジェクトを重ねないため、アイコンの表示サイズは従来どおりになる。
                        var normalIcon = LoadTopTabIcon(tab, false);
                        var selectedIcon = LoadTopTabIcon(tab, true);
                        tabButton.inactiveSprites.GetComponent<SpriteRenderer>().sprite = normalIcon;
                        tabButton.activeSprites.GetComponent<SpriteRenderer>().sprite = selectedIcon;
                        tabButton.selectedSprites.GetComponent<SpriteRenderer>().sprite = selectedIcon;

                    }

                    if (showInTopRow)

                    {

                        tabButton.transform.position = templateTabButton.transform.position + new Vector3((0.762f * visibleTabCount * 0.8f) + (0.762f * visibleTabCount * 0.2f), 0, -300f);

                        visibleTabCount++;

                    }

                    else

                    {

                        // 役職タブは横一列には表示しない(サイドバー側のボタンから開く)

                        tabButton.gameObject.SetActive(false);

                    }

                    if (false && showInTopRow)
                    {
                        // 旧・専用アイコン複製処理（サイズ復元のため使用しない）。
                        // これによりOTHERをクリックしても、ボタン本体の背景スプライトは消えない。
                        var sourceIconTransform = tabButton.activeSprites.transform;
                        var iconObject = Object.Instantiate(tabButton.activeSprites, tabButton.transform);
                        iconObject.name = $"TopTabIcon_{tab}";
                        iconObject.SetActive(true);
                        foreach (var collider in iconObject.GetComponents<Collider2D>())
                            Object.DestroyImmediate(collider);

                        var iconRenderer = iconObject.GetComponent<SpriteRenderer>();
                        if (iconRenderer != null)
                        {
                            iconRenderer.sprite = LoadTopTabIcon(tab, false);
                            iconRenderer.color = Color.white;
                            iconRenderer.enabled = true;
                            var backgroundRenderer = tabButton.inactiveSprites.GetComponent<SpriteRenderer>();
                            iconRenderer.sortingLayerID = backgroundRenderer.sortingLayerID;
                            iconRenderer.sortingOrder = backgroundRenderer.sortingOrder + 5;
                            topTabIconRenderers[tab] = iconRenderer;
                        }
                        // 複製元の既存transformをそのまま継承する。Vector3.oneへ上書きしないため、
                        // 以前のhamoタブと同じアイコンサイズ・配置になる。
                        iconObject.transform.localPosition = sourceIconTransform.localPosition;
                        iconObject.transform.localRotation = sourceIconTransform.localRotation;
                        iconObject.transform.localScale = sourceIconTransform.localScale;
                    }



                    // ===== アイコンの後ろに四角い枠(背景)を追加する =====

                    // 画像だけだと枠が無く浮いて見えるため、単色の正方形を1枚背面に敷いて

                    // ボタンらしい見た目にする。

                    if (false && showInTopRow)

                    {

                        var frameObj = new GameObject($"TabFrame_{tab}");

                        frameObj.transform.SetParent(tabButton.transform, false);

                        frameObj.transform.localPosition = new Vector3(0f, 0f, 1f);

                        var frameRenderer = frameObj.AddComponent<SpriteRenderer>();

                        var frameTexture = new Texture2D(1, 1);

                        frameTexture.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f, 1f));

                        frameTexture.Apply();

                        frameRenderer.sprite = Sprite.Create(frameTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

                        frameRenderer.color = new Color(0.15f, 0.15f, 0.15f, 1f);

                        var baseRenderer = tabButton.inactiveSprites.GetComponent<SpriteRenderer>();

                        frameRenderer.sortingLayerID = baseRenderer.sortingLayerID;

                        frameRenderer.sortingOrder = baseRenderer.sortingOrder - 1;

                        frameObj.transform.localScale = new Vector3(0.78f, 0.78f, 1f);

                    }



                                        // 上部タブは元の描画順を維持する。

                    // 役職説明・タスク説明はTaskPanel側で最前面化する。

                    tabButtons.Add(tabButton);

                    tabButton.transform.SetAsFirstSibling();



                }

                ErrorNumber = 8;



                foreach (var role in EnumHelper.GetAllValues<CustomRoles>())

                {

                    var tabs = crlist[role];

                    Il2CppSystem.Collections.Generic.List<OptionBehaviour> options = new();

                    tabs.Children = crOptions[role];

                    tabs.gameObject.SetActive(false);

                    tabs.enabled = true;

                    crmenus.Add(role, tabs.gameObject);

                }



                ErrorNumber = 9;

                //一旦全部作ってから

                for (var i = 0; i < TabLength; i++)

                {

                    var tab = (TabGroup)i;

                    var tabButton = tabButtons[i];

                    if (tabButton == null) continue;



                    tabButton.OnClick = new();

                    tabButton.OnClick.AddListener((Action)(() =>

                    {

                        for (var i = 0; i < TabLength; i++)

                        {

                            var n = (TabGroup)i;

                            var tabButton = tabButtons[i];

                            if (tab != n) menus[n].SetActive(false);

                            tabButton.SelectButton(false);

                            if (topTabIconRenderers.TryGetValue(n, out var inactiveIconRenderer))
                                inactiveIconRenderer.sprite = LoadTopTabIcon(n, false);

                        }

                        crmenus[NowRoleTab].SetActive(false);

                        NowRoleTab = CustomRoles.NotAssigned;

                        tabButton.SelectButton(true);

                        if (topTabIconRenderers.TryGetValue(tab, out var selectedIconRenderer))
                            selectedIconRenderer.sprite = LoadTopTabIcon(tab, true);

                        menus[tab].SetActive(true);

                        var tabTitle = ModSettingsTab.quotaHeader;

                        CategoryHeaderEditRole[] tabSubTitle = tabTitle.transform.parent.GetComponentsInChildren<CategoryHeaderEditRole>();

                        tabTitle.Title.DestroyTranslator();

                        tabTitle.Title.text = tab switch

                        {

                            TabGroup.Game => "GAME",

                            TabGroup.Other => "OTHER",

                            TabGroup.Other2 => "OTHER2",

                            _ => GetString("TabGroup." + tab),

                        };



                        tabTitle.Background.color = ModColors.Gray;

                        tabTitle.Title.color = Color.white;



                        ModSettingsTab.scrollBar.velocity = Vector2.zero;

                        ModSettingsTab.scrollBar.Inner.localPosition = new Vector3(ModSettingsTab.scrollBar.Inner.localPosition.x, 0, ModSettingsTab.scrollBar.Inner.localPosition.z);

                        ModSettingsTab.scrollBar.ScrollRelative(Vector2.zero);

                        foreach (var sub in tabSubTitle)

                        {

                            Object.Destroy(sub.gameObject);

                        }



                        CreateOptions(tab, menus, crmenus, __instance.RoleSettingsTab.transform.parent);



                        // ===== 検索/プリセット名編集/有効なMAP設定・役職のみ表示するボタンを常に表示する =====

                        // 役職タブ(インポスター等)を開いた際、役職一覧パネルの陰に隠れて

                        // 見えなくなってしまうことがあるため、表示状態を明示しつつ、

                        // 兄弟階層の最後尾に移動して手前に描画されるようにする。

                        search?.gameObject?.SetActive(true);

                        search?.transform?.SetAsFirstSibling();

                        priset?.gameObject?.SetActive(true);

                        priset?.transform?.SetAsFirstSibling();

                        activeonly?.gameObject?.SetActive(true);

                        activeonly?.transform?.SetAsFirstSibling();

                        GamePresetButton?.transform?.SetAsFirstSibling();



                        // ===== 役職タブの一覧パネルの陰に隠れないよう、描画順を強制的に最前面にする =====

                        ForceRenderInFront(search);

                        ForceRenderInFront(priset);

                        ForceRenderInFront(activeonly?.transform);

                        ForceRenderInFront(GamePresetButton?.transform);



                        // ===== 少し遅れて再度表示を確定させる(横タブ経由の場合) =====

                        void ReassertVisibilityFromTopTab()

                        {

                            search?.gameObject?.SetActive(true);

                            priset?.gameObject?.SetActive(true);

                            activeonly?.gameObject?.SetActive(true);

                            ForceRenderInFront(search);

                            ForceRenderInFront(priset);

                            ForceRenderInFront(activeonly?.transform);

                            ForceRenderInFront(GamePresetButton?.transform);

                            search?.transform?.SetAsFirstSibling();

                            priset?.transform?.SetAsFirstSibling();

                            activeonly?.transform?.SetAsFirstSibling();

                            GamePresetButton?.transform?.SetAsFirstSibling();

                        }

                        _ = new LateTask(ReassertVisibilityFromTopTab, 0.05f, "", true);

                        _ = new LateTask(ReassertVisibilityFromTopTab, 0.15f, "", true);

                        _ = new LateTask(ReassertVisibilityFromTopTab, 0.35f, "", true);

                        _ = new LateTask(ReassertVisibilityFromTopTab, 0.6f, "", true);



                        // ===== サイドバー/TOWNOFHOST-HAMOの選択色を横タブ切り替え時にも同期する =====

                        // GAME/OTHER/OTHER2/TOWNOFHOST-HAMOの各タブを直接押した場合、

                        // 以前は役職タブ(インポスター等)を選んだままの選択色が残ってしまい、

                        // 実際に表示されている内容とサイドバーの見た目が食い違って見えるバグがあった。

                        // ===== 重要 =====

                        // このOnClickハンドラは横一列の4タブだけでなく、サイドバー経由で

                        // 呼ばれる役職タブ(インポスター等)でも共有して使われている。

                        // そのため、ここを無条件に実行するとTOWNOFHOST-HAMOと役職タブの

                        // 選択色が同時に点灯してしまう(実際に発生したバグ)。

                        // 横一列の4タブ(MainSettings/Game/Other/Other2)の場合のみ、

                        // ここでTOWNOFHOST-HAMO側の選択色を更新する。役職タブ経由の場合は

                        // sideButton自身のOnClick側で選択色を管理する。

                        if (tab is TabGroup.MainSettings or TabGroup.Game or TabGroup.Other or TabGroup.Other2)

                        {

                            ModSettingsButton.SelectButton(true);

                            foreach (var sb in sideTabButtons)

                                sb?.SelectButton(false);

                        }

                    }));



                    ModSettingsTab.roleTabs.Add(tabButton);

                }



                // ===== 左サイドバーに縦並びの役職タブボタンを追加 =====

                // インポスター〜ゴーストは横一列には出さず、TOWNOFHOST-HAMOボタンを複製した

                // 縦並びのボタンから開く。クリック時は対応する横タブのOnClickをそのまま

                // 呼び出すことで、ロジックを二重に持たず挙動を完全に一致させる。

                if (ModSettingsButton.transform.parent != null)

                {

                    var parentT = ModSettingsButton.transform.parent;

                    for (int ci = parentT.childCount - 1; ci >= 0; ci--)

                    {

                        var child = parentT.GetChild(ci);

                        if (child != null && child.name.StartsWith("SideTabButton_"))

                            Object.DestroyImmediate(child.gameObject);

                    }

                }



                sideTabButtons.Clear();



                for (var i = 0; i < TabLength; i++)

                {

                    var tab = (TabGroup)i;

                    // 横一列に出す4つ(ゲーム設定/GAME/OTHER/OTHER2)は対象外。役職タブのみ縦に並べる。

                    if (tab is TabGroup.MainSettings or TabGroup.Game or TabGroup.Other or TabGroup.Other2) continue;



                    var sideButton = Object.Instantiate(ModSettingsButton, ModSettingsButton.transform.parent);

                    sideButton.gameObject.name = $"SideTabButton_{tab}";



                    // ===== 間隔は完全に一定にする =====

                    const float sideButtonSpacing = 0.36f; // 画面下(ゴースト)がはみ出さないよう、さらに詰める

                    int roleSideIndex = tab switch

                    {

                        TabGroup.ImpostorRoles => 1,

                        TabGroup.MadmateRoles => 2,

                        TabGroup.CrewmateRoles => 3,

                        TabGroup.NeutralRoles => 4,

                        TabGroup.Combinations => 5,

                        TabGroup.Addons => 6,

                        TabGroup.GhostRoles => 7,

                        _ => 0,

                    };

                    sideButton.transform.localPosition = ModSettingsButton.transform.localPosition + new Vector3(0f, -sideButtonSpacing * roleSideIndex, 0f);

                    sideButton.transform.localScale = ModSettingsButton.transform.localScale;



                    sideButton.buttonText.text = tab switch

                    {

                        TabGroup.ImpostorRoles => "インポスター",

                        TabGroup.MadmateRoles => "マッドメイト",

                        TabGroup.CrewmateRoles => "クルーメイト",

                        TabGroup.NeutralRoles => "ニュートラル",

                        TabGroup.Combinations => "コンビネーション",

                        TabGroup.Addons => "属性",

                        TabGroup.GhostRoles => "ゴースト",

                        _ => tab.ToString(),

                    };

                    sideButton.buttonText.DestroyTranslator();

                    // ===== テキストがはみ出さないようにする =====

                    sideButton.buttonText.enableAutoSizing = true;

                    sideButton.buttonText.fontSizeMin = 0.6f;

                    sideButton.buttonText.fontSizeMax = sideButton.buttonText.fontSize;

                    // ===== 左にアイコン、右にテキストの配置にする =====

                    // 中央揃えのままだとアイコンと文字が重なってしまうため、

                    // 右寄せにしてアイコン側(左)の余白を空ける。

                    // sizeDeltaだけでは、テキストボックス自体がボタン中央にあるままなので

                    // 実際にはあまり右に寄って見えなかったため、ボックス自体の位置も

                    // 右へ動かす。

                    sideButton.buttonText.alignment = TMPro.TextAlignmentOptions.MidlineRight;

                    var sideTextRect = sideButton.buttonText.GetComponent<RectTransform>();

                    if (sideTextRect != null)

                    {

                        sideTextRect.sizeDelta = new Vector2(1.7f, 0.6f);

                    }

                    sideButton.buttonText.transform.localPosition += new Vector3(0.55f, 0f, 0f);

                    // ===== アイコンを、活きているactiveSpritesを複製した別オブジェクトに表示する =====

                    // 前回、activeSprites自体を直接アイコンとして流用したところ、

                    // マウスオーバー時にactiveSprites/inactiveSpritesを入れ替えるボタン本来の

                    // 挙動と衝突し、ホバー中はボタンの背景(inactiveSprites)が消えて

                    // アイコンと文字だけが残る不具合が起きた。

                    // そこで、activeSprites自体はホバー用として無傷のまま残し、

                    // それを複製した「別オブジェクト」を常時表示のアイコンとして使う。

                    // (このactiveSpritesは実際に画面へ描画できることが確認済みのため、

                    //  複製元として利用する)

                    var sideIconObj = Object.Instantiate(sideButton.activeSprites, sideButton.transform);

                    sideIconObj.name = $"SideTabIcon_{tab}";

                    sideIconObj.SetActive(true);

                    foreach (var col in sideIconObj.GetComponents<Collider2D>())

                        Object.DestroyImmediate(col);



                    var sideIconRenderer = sideIconObj.GetComponent<SpriteRenderer>();

                    if (sideIconRenderer != null)

                    {

                        sideIconRenderer.sprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Tab.TabIcon_{tab}.png", 60);

                        sideIconRenderer.color = Color.white;

                        sideIconRenderer.enabled = true;

                        // ===== ボタンの裏に隠れないよう、描画順を明示的に前面へ =====

                        // ニュートラル/コンビネーションのアイコンだけ、ボタン背景の裏に

                        // 隠れて見えなくなる現象が起きていたため、複製元(activeSprites)の

                        // sortingOrderより確実に高い値を明示的に指定する。

                        var baseRenderer = sideButton.inactiveSprites.GetComponent<SpriteRenderer>();

                        sideIconRenderer.sortingLayerID = baseRenderer.sortingLayerID;

                        sideIconRenderer.sortingOrder = baseRenderer.sortingOrder + 5;

                    }

                    // ===== 正方形にする =====

                    // 計算での自動補正を何度か試したが、狙い通りにならなかったため、

                    // 単純に横方向だけ狭める固定値を直接指定する方式にする。

                    sideIconObj.transform.localPosition = new Vector3(-1.45f, -0.05f, -1f);

                    sideIconObj.transform.localRotation = Quaternion.identity;

                    sideIconObj.transform.localScale = new Vector3(0.2f, 0.7f, 1f);



                    var capturedIndex = i;

                    sideButton.OnClick = new();

                    sideButton.OnClick.AddListener((Action)(() =>

                    {

                        IsClick = true;

                        // 1. まずTownOfHost-hamoの設定ページ自体を開く

                        __instance.ChangeTab(3, false);

                        // 2. その上で、対応するタブに切り替える(横タブと同じ処理をそのまま流用)

                        tabButtons[capturedIndex]?.OnClick?.Invoke();

                        // 3. 選択色を更新する。TOWNOFHOST-HAMOと他の役職ボタンは非選択に、

                        //    今押したボタンだけ選択状態(色が変わる)にする。

                        ModSettingsButton.SelectButton(false);

                        foreach (var sb in sideTabButtons)

                            sb?.SelectButton(false);

                        sideButton.SelectButton(true);



                        // ===== 念のためここでも明示的に表示・最前面化する =====

                        // タスク/検索/プリセット名編集/有効なMAP設定は

                        // tabButtons[].OnClick側でも表示処理をしているが、

                        // サイドバーの役職ボタンから開いた場合にも確実に効くよう、

                        // ここでも重ねて呼んでおく。

                        search?.gameObject?.SetActive(true);

                        priset?.gameObject?.SetActive(true);

                        activeonly?.gameObject?.SetActive(true);

                        ForceRenderInFront(search);

                        ForceRenderInFront(priset);

                        ForceRenderInFront(activeonly?.transform);

                        ForceRenderInFront(GamePresetButton?.transform);

                        search?.transform?.SetAsFirstSibling();

                        priset?.transform?.SetAsFirstSibling();

                        activeonly?.transform?.SetAsFirstSibling();

                        GamePresetButton?.transform?.SetAsFirstSibling();



                        // ===== 少し遅れて再度表示を確定させる =====

                        // __instance.ChangeTab(3, false)はバニラ側の処理のため、

                        // その内部で数フレーム遅れて他のUIのActive状態が変更されている

                        // 可能性がある。念のため複数のタイミングで繰り返し確定させる。

                        void ReassertVisibility()

                        {

                            search?.gameObject?.SetActive(true);

                            priset?.gameObject?.SetActive(true);

                            activeonly?.gameObject?.SetActive(true);

                            ForceRenderInFront(search);

                            ForceRenderInFront(priset);

                            ForceRenderInFront(activeonly?.transform);

                            ForceRenderInFront(GamePresetButton?.transform);

                            search?.transform?.SetAsFirstSibling();

                            priset?.transform?.SetAsFirstSibling();

                            activeonly?.transform?.SetAsFirstSibling();

                            GamePresetButton?.transform?.SetAsFirstSibling();

                        }

                        _ = new LateTask(ReassertVisibility, 0.05f, "", true);

                        _ = new LateTask(ReassertVisibility, 0.15f, "", true);

                        _ = new LateTask(ReassertVisibility, 0.35f, "", true);

                        _ = new LateTask(ReassertVisibility, 0.6f, "", true);

                    }));



                    sideTabButtons.Add(sideButton);

                    // ===== サイドバー役職ボタンも最後列(背面)に配置する =====

                    sideButton.transform.SetAsFirstSibling();

                    ControllerManager.Instance.CurrentUiState.SelectableUiElements.Add(sideButton);

                }



                ErrorNumber = 10;

                if (search)

                {

                    search.transform.localPosition = new Vector3(0.3f, 3.5f);

                    search.transform.localScale = new Vector3(0.4f, 0.4f, 0f);

                    search?.gameObject?.SetActive(true);

                    ForceRenderInFront(search);

                    search.submitButton.OnPressed = (Action)(() =>

                    {

                        bool ch = false;

                        List<OptionItem> subopt = new();

                        foreach (var op in OptionItem.AllOptions.Where(o => (o as ObjectOptionitem)?.IsHedderObject is not true))

                        {

                            var name = op.GetName().RemoveHtmlTags();



                            if (name == search.textArea.text)

                            {

                                scroll(op);

                                ch = true;

                                break;

                            }



                            if (name.Contains(search.textArea.text))

                            {

                                subopt.Add(op);

                            }

                        }



                        //不必要なループをなくしてみる

                        if (!ch)

                        {

                            foreach (var op in subopt)

                            {

                                scroll(op);

                                break;

                            }

                        }

                        search.textArea.Clear();



                        //スクロール処理

                        void scroll(OptionItem op)

                        {

                            var opt = op;

                            while (opt.Parent != null && (!opt.GetBool() || roleopts.Contains(opt)))

                            {

                                opt = opt.Parent;

                            }



                            int tabIndex = (int)opt.Tab;



                            if (tabIndex >= 0 && tabIndex < tabButtons.Count && tabButtons[tabIndex] != null)

                            {

                                tabButtons[tabIndex].OnClick.Invoke();



                                // ===== サイドバー/TOWNOFHOST-HAMOの選択色を検索ジャンプ時にも同期する =====

                                // 検索結果からのジャンプはtabButtons[].OnClickを直接呼ぶだけで、

                                // サイドバーボタンのSelectButton状態が更新されていなかったため、

                                // 横のタブ表示が前に選んでいた役職タブのまま(選択色が残ったまま)に

                                // 見えてしまうバグがあった。ジャンプ先に応じて選択状態を明示的に揃える。

                                var jumpTab = (TabGroup)tabIndex;

                                if (jumpTab is TabGroup.MainSettings or TabGroup.Game or TabGroup.Other or TabGroup.Other2)

                                {

                                    ModSettingsButton.SelectButton(true);

                                    foreach (var sb in sideTabButtons)

                                        sb?.SelectButton(false);

                                }

                                else

                                {

                                    ModSettingsButton.SelectButton(false);

                                    foreach (var sb in sideTabButtons)

                                        sb?.SelectButton(sb.gameObject.name == $"SideTabButton_{jumpTab}");

                                }

                            }



                            _ = new LateTask(() =>

                            {

                                if (!(ModSettingsTab?.gameObject?.active ?? false)) return;

                                ModSettingsTab.scrollBar.velocity = Vector2.zero;

                                var relativePosition = ModSettingsTab.scrollBar.transform.InverseTransformPoint(opt.OptionBehaviour.transform.FindChild("Title Text").transform.position);// Scrollerのローカル空間における座標に変換

                                var scrollAmount = 1 - relativePosition.y;

                                ModSettingsTab.scrollBar.Inner.localPosition = ModSettingsTab.scrollBar.Inner.localPosition + Vector3.up * scrollAmount;  // 強制スクロール

                                ModSettingsTab.scrollBar.ScrollRelative(Vector2.zero);

                            }, 0.1f, "", true);

                        }

                    });

                }

                ErrorNumber = 11;



                ModSettingsButton.OnClick = new();

                ModSettingsButton.OnClick.AddListener((Action)(() =>

                {

                    __instance.ChangeTab(3, false);



                    // ===== TOWNOFHOST-HAMOを押したら必ずMainSettingsタブに戻す =====

                    // 以前はShowModSetting(初回だけ立つフラグ)の時だけ切り替えていたため、

                    // インポスター等の役職タブを見ている状態でTOWNOFHOST-HAMOを押しても

                    // その役職タブの中身が表示されたままになってしまうバグがあった。

                    // 常にMainSettingsタブへ切り替えるようにする。

                    if (tabButtons[0] != null)

                        tabButtons[0].OnClick.Invoke();

                    ShowModSetting = false;



                    // ===== サイドバーの選択色を更新する =====

                    ModSettingsButton.SelectButton(true);

                    foreach (var sb in sideTabButtons)

                        sb?.SelectButton(false);

                }));



                ErrorNumber = 12;

                __instance.GameSettingsTab.gameObject.SetActive(false);



                settingsButton = HudManager.Instance.SettingsButton.GetComponent<PassiveButton>();



                // ボタン生成

                CreateButton("OptionReset", Color.red, -4.55f, new Action(() =>

                {

                    OptionItem.AllOptions.ToArray().Where(x => x.Id > 0 && x.Id is not 2 and not 3 && 1_000_000 > x.Id && x.CurrentValue != x.DefaultValue).Do(x => x.SetValue(x.DefaultValue, false, false));

                    var pr = OptionItem.AllOptions.Where(op => op.Id == 0).FirstOrDefault();

                    switch (pr.CurrentValue)

                    {

                        case 0: Main.Preset1.Value = GetString("Preset_1"); break;

                        case 1: Main.Preset2.Value = GetString("Preset_2"); break;

                        case 2: Main.Preset3.Value = GetString("Preset_3"); break;

                        case 3: Main.Preset4.Value = GetString("Preset_4"); break;

                        case 4: Main.Preset5.Value = GetString("Preset_5"); break;

                        case 5: Main.Preset6.Value = GetString("Preset_6"); break;

                        case 6: Main.Preset7.Value = GetString("Preset_7"); break;

                        case 7: Main.Preset8.Value = GetString("Preset_8"); break;

                        case 8: Main.Preset9.Value = GetString("Preset_9"); break;

                        case 9: Main.Preset10.Value = GetString("Preset_10"); break;

                        case 10: Main.Preset11.Value = GetString("Preset_11"); break;

                        case 11: Main.Preset12.Value = GetString("Preset_12"); break;

                        case 12: Main.Preset13.Value = GetString("Preset_13"); break;

                        case 13: Main.Preset14.Value = GetString("Preset_14"); break;

                        case 14: Main.Preset15.Value = GetString("Preset_15"); break;

                        case 15: Main.Preset16.Value = GetString("Preset_16"); break;



                    }

                    GameSettingMenuChangeTabPatch.meg = GetString("OptionResetMeg");

                    timer = 3;

                    VanillaOptionHolder.ResetVanilla();

                    OptionItem.SyncAllOptions();

                    OptionSaver.Save();

                }), UtilsSprite.LoadSprite("TownOfHost.Resources.TOHhm.RESET-STG.png", 150f));

                CreateButton("OptionCopy", Color.green, -3.9f, new Action(() =>

                {

                    OptionSerializer.SaveToClipboard();

                    GameSettingMenuChangeTabPatch.meg = GetString("OptionCopyMeg");

                    timer = 3;

                }), UtilsSprite.LoadSprite("TownOfHost.Resources.TOHhm.COPY-STG.png", 180f));

                CreateButton("OptionLoad", Color.green, -3.25f, new Action(() =>

                {

                    OptionSerializer.LoadFromClipboard();

                    GameSettingMenuChangeTabPatch.meg = GetString("OptionLoadMeg");

                    timer = 3;

                }), UtilsSprite.LoadSprite("TownOfHost.Resources.TOHhm.LOAD-STG.png", 180f));

                ErrorNumber = 13;



                CreateLobbyInfo();



                ErrorNumber = 14;

                __instance.transform.localScale = size;

            }

            catch (Exception Error)

            {

                Logger.Error($"Error:{ErrorNumber}\n{Error.ToString()}", "OptionMenu");

            }



            void CreateButton(string text, Color color, float xPos, Action action, Sprite sprite = null)

            {

                settingsButton ??= HudManager.Instance.SettingsButton.GetComponent<PassiveButton>();

                var ToggleButton = Object.Instantiate(settingsButton, __instance.transform);



                ToggleButton.transform.localScale -= new Vector3(0.25f, 0.25f);

                ToggleButton.name = text;

                if (sprite != null)

                {

                    ToggleButton.inactiveSprites.GetComponent<SpriteRenderer>().sprite = sprite;

                    ToggleButton.activeSprites.GetComponent<SpriteRenderer>().sprite = sprite;

                    ToggleButton.selectedSprites.GetComponent<SpriteRenderer>().sprite = sprite;

                }



                ToggleButton.OnClick = new();

                ToggleButton.OnClick.AddListener(action);



                ToggleButton.OnMouseOut.AddListener((Action)ToolTip.Hide);

                ToggleButton.OnMouseOver.AddListener((Action)(() => ToolTip.Show(ToggleButton, GetString($"{text}Info"), new Vector3(-4f, ToolTip.GetMoucePos().y - 0.35f, -255))));



                var aspectPosition = ToggleButton.GetComponent<AspectPosition>();

                aspectPosition.DistanceFromEdge = new Vector3(xPos, 2.49f, -200f);

                aspectPosition.Alignment = AspectPosition.EdgeAlignments.Center;



                /*var textTMP = new GameObject("Text_TMP").AddComponent<TMPro.TextMeshPro>();

                textTMP.text = Utils.ColorString(color, GetString(text));

                textTMP.transform.SetParent(ToggleButton.transform);

                textTMP.transform.localPosition = new Vector3(0.8f, 0.8f);

                textTMP.transform.localScale = new Vector3(0, -0.5f);

                textTMP.alignment = TMPro.TextAlignmentOptions.Top;

                textTMP.fontSize = 10f;*/

            }



            void CreateLobbyInfo()

            {

                var baseTimer = GameStartManager._instance.RulesPresetText.transform.parent;

                var baseCount = GameStartManager._instance.PlayerCounter.transform.parent;



                var timer = GameObject.Instantiate(baseTimer, __instance.transform);

                var count = GameObject.Instantiate(baseCount, __instance.transform);



                timer.transform.localPosition = new(-1.7905f, 3.9055f, -200f);

                count.transform.localPosition = new(-2.35f, 2.6545f, -200f);



                timer.transform.localScale = new(0.45f, 0.45f, 1);

                count.transform.localScale = new(0.45f, 0.45f, 1);



                InfoTimer = timer.transform.GetComponentInChildren<TMPro.TextMeshPro>();

                InfoCount = count.transform.GetComponentInChildren<TMPro.TextMeshPro>();

            }

        }

        public static void CreateOptions(TabGroup tab, Dictionary<TabGroup, GameObject> menus, Dictionary<CustomRoles, GameObject> crmenus, Transform tabtransfrom, bool forceAllTabs = false)

        {

            if (!forceAllTabs && tabGenerated.Contains(tab)) return;

            var template = GetTeamplate();

            if (template == null) return;



            // ===== ラベル背景画像の表示サイズ修正 =====

            // LoadSpriteはデフォルトでpixelsPerUnit=1のため、画像のピクセルサイズが

            // そのままUnity上のワールドサイズになってしまい、本来のラベルの大きさと

            // 合わず巨大化・テキストとの重なりが発生していた。

            //

            // 重要: SpriteRendererがSliced/Tiledモードの場合、実際の表示サイズは

            // sprite.boundsではなく SpriteRenderer.size プロパティで決まる

            // (9-sliceで元画像とは独立して引き伸ばされているため)。

            // Simpleモードの場合のみ sprite.bounds が実際の表示サイズになる。

            float labelPixelsPerUnit = 100f; // 基準の元Spriteが取得できない場合のフォールバック

            Vector4 labelBorder = Vector4.zero; // 元Spriteの9-slice境界情報(あれば引き継ぐ)

            {

                var labelRenderer = template.LabelBackground;

                var baseSprite = labelRenderer?.sprite;

                if (baseSprite != null && baseSprite.rect.width > 0 && labelRenderer != null)

                {

                    labelBorder = baseSprite.border;



                    float baseWorldWidth;

                    if (labelRenderer.drawMode is SpriteDrawMode.Sliced or SpriteDrawMode.Tiled)

                    {

                        // Sliced/Tiledの場合、SpriteRenderer.sizeが実際の表示サイズ(ローカル単位)。

                        baseWorldWidth = labelRenderer.size.x;

                    }

                    else

                    {

                        // Simpleモードなら sprite.bounds がそのまま実際の表示サイズ。

                        baseWorldWidth = baseSprite.bounds.size.x;

                    }



                    if (baseWorldWidth > 0)

                    {

                        // 新しい画像(196px幅)が、差し替え前と同じローカル表示幅になるようppuを逆算する。

                        const float newImageWidthPx = 196f;

                        float computed = newImageWidthPx / baseWorldWidth;



                        // 異常値(計算誤りで極端に大小になった場合)の安全弁。

                        // 通常のUI用PPUは十数〜数百の範囲に収まるはずなので、

                        // 明らかにおかしい値ならフォールバック(100)のままにする。

                        if (computed is > 1f and < 2000f)

                        {

                            labelPixelsPerUnit = computed;

                        }

                    }

                }

            }



            var LabelBackgroundSprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Label.LabelBackground.png", labelPixelsPerUnit, Vector4.zero);

            var LabelBackgroundToolSprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Label.LabelBackgroundTool.png", labelPixelsPerUnit, Vector4.zero);

            var ShowOptionSprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.ShowOption.png");



            foreach (var option in OptionItem.AllOptions)

            {

                if (!forceAllTabs && option.Tab != tab) continue;

                if (option.OptionBehaviour == null)

                {

                    var parentrole = option.ParentRole;

                    //タブの場合

                    if ((option as ObjectOptionitem)?.IsHedderObject is true)

                    {

                        var optionsMenu = parentrole is not CustomRoles.NotAssigned && option.CustomRole is CustomRoles.NotAssigned ?

                        crlist[parentrole] : list[option.Tab];

                        var defotabtitle = ModSettingsTab.transform.FindChild("Scroller/SliderInner/ChancesTab");

                        var tabtitle = Object.Instantiate(defotabtitle, optionsMenu.transform);

                        var chm = tabtitle.transform.FindChild("CategoryHeaderMasked").GetComponent<CategoryHeaderMasked>();

                        CategoryHeaderEditRole[] tabsubtitle = chm.transform.parent.GetComponentsInChildren<CategoryHeaderEditRole>();

                        chm.Title.DestroyTranslator();

                        chm.Title.text = $"<b>{option.GetName(false)}</b>";

                        option.OptionHedder = chm;

                        tabtitle.name = option.Name;

                        tabtitle.transform.localPosition = new Vector3(-0.7789f, -0.15f, -10);



                        if (parentrole is not CustomRoles.NotAssigned && option.CustomRole is CustomRoles.NotAssigned)

                        {

                            roleopts.Add(option);

                        }

                        continue;

                    }

                    //役職設定の場合

                    if (parentrole is not CustomRoles.NotAssigned && option.CustomRole is CustomRoles.NotAssigned)

                    {

                        var optionsMenu = crlist[parentrole];

                        var stringOption = Object.Instantiate(template, optionsMenu.transform);

                        crOptions[parentrole].Add(stringOption);

                        roleopts.Add(option);

                        stringOption.TitleText.text = $"<b>{option.GetName()}</b>";

                        stringOption.Value = stringOption.oldValue = option.CurrentValue;

                        stringOption.ValueText.text = "読み込み中..";

                        stringOption.name = option.Name;

                        var __prevLabelSize = stringOption.LabelBackground.size;

                        var __prevLabelDrawMode = stringOption.LabelBackground.drawMode;

                        stringOption.LabelBackground.sprite = option.Tooltip.Invoke() == "" ? LabelBackgroundSprite : LabelBackgroundToolSprite;

                        // Sliced/Tiledモードの場合、sprite差し替え後もSpriteRendererの

                        // size/drawModeが変わらないことを明示的に保証しておく(念のための保険)。

                        stringOption.LabelBackground.drawMode = __prevLabelDrawMode;

                        if (__prevLabelDrawMode is SpriteDrawMode.Sliced or SpriteDrawMode.Tiled)

                            stringOption.LabelBackground.size = __prevLabelSize;

                        if (option.HideValue)

                        {

                            stringOption.PlusBtn.transform.localPosition = new Vector3(100, 100, 100);

                            stringOption.MinusBtn.transform.localPosition = new Vector3(100, 100, 100);

                        }

                        // フィルターオプション、属性設定なら

                        if (option is FilterOptionItem or AssignOptionItem)

                        {

                            stringOption.MinusBtn.OnClick = new();

                            stringOption.MinusBtn.OnClick.AddListener((System.Action)(() =>

                            {

                                if (option is FilterOptionItem filterOptionItem) filterOptionItem.SetRoleValue(parentrole);

                                if (option is AssignOptionItem assignoptionitem) assignoptionitem.SetRoleValue(new());

                            }));

                            stringOption.MinusBtn.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "<size=80%>←";

                            stringOption.PlusBtn.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "<rotate=-20>ρ";

                            stringOption.PlusBtn.OnClick = new();

                            stringOption.PlusBtn.OnClick.AddListener((System.Action)(() =>

                            {

                                CustomRoles[] notAssign = [];

                                var (imp, mad, crew, neu, addon) = (true, true, true, true, true);

                                if (option is FilterOptionItem filterOptionItem)

                                {

                                    notAssign = filterOptionItem.NotAssin?.Invoke() ?? [];

                                    (imp, mad, crew, neu, addon) = filterOptionItem.roles;

                                    ShowFilter.NowOption = option;

                                    ShowFilter.CreateFilterOptionMenu(tabtransfrom, null, notAssign, (imp, mad, crew, neu, addon));

                                    return;

                                }

                                if (option is AssignOptionItem assignoptionitem)

                                {

                                    notAssign = assignoptionitem.NotAssin?.Invoke() ?? [];

                                    (imp, mad, crew, neu, addon) = assignoptionitem.roles;



                                    ShowFilter.NowOption = option;

                                    ShowFilter.CreateFilterOptionMenu(tabtransfrom, assignoptionitem.RoleValues[AssignOptionItem.Getpresetid()], notAssign, (imp, mad, crew, neu, addon));

                                    return;

                                }

                            }));

                        }

                        if ((option as ObjectOptionitem)?.ClickActionkey is not null)

                        {

                            stringOption.MinusBtn.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "<rotate=-20>ρ";

                            stringOption.PlusBtn.transform.localPosition = new Vector3(100, 100, 100);

                            stringOption.MinusBtn.OnClick = new();

                            stringOption.MinusBtn.OnClick.AddListener((Action)(() =>

                            {

                                SetAction((option as ObjectOptionitem)?.ClickActionkey, tabtransfrom);

                            }));

                        }

                        if (option.Tooltip.Invoke() is not "")//一旦こういう実装にしているが、？マークをどこかに設置してでもいいかも?

                        {

                            stringOption.LabelBackground.gameObject.AddComponent<PassiveButton>();

                            stringOption.LabelBackground.gameObject.AddComponent<BoxCollider2D>().autoTiling = true;

                            var passive = stringOption.LabelBackground.gameObject.GetComponent<PassiveButton>();

                            passive.OnMouseOut = new();

                            passive.OnMouseOver = new();

                            passive.OnClick = new();

                            passive.OnMouseOut.AddListener((Action)(() => ToolTip.Hide()));

                            passive.OnMouseOver.AddListener((Action)(() => ToolTip.Show(passive, option.Tooltip.Invoke(), null)));

                        }



                        var transform = stringOption.ValueText.transform;

                        var pos = transform.localPosition;

                        transform.localPosition = new Vector3(pos.x + 0.7322f, pos.y, pos.z);

                        NumericOptionInput.Attach(option, stringOption);

                        stringOption.SetClickMask(optionsMenu.ButtonClickMask);

                        option.OptionBehaviour = stringOption;

                    }

                    else

                    {

                        var optionsMenu = list[option.Tab];

                        var stringOption = Object.Instantiate(template, optionsMenu.transform);

                        scOptions[option.Tab].Add(stringOption);

                        stringOption.TitleText.text = $"<b>{option.GetName()}</b>";

                        stringOption.Value = stringOption.oldValue = option.CurrentValue;

                        stringOption.ValueText.text = "読み込み中..";

                        stringOption.name = option.Name;



                        var __prevLabelSize = stringOption.LabelBackground.size;

                        var __prevLabelDrawMode = stringOption.LabelBackground.drawMode;

                        stringOption.LabelBackground.sprite = option.Tooltip.Invoke() == "" ? LabelBackgroundSprite : LabelBackgroundToolSprite;

                        // Sliced/Tiledモードの場合、sprite差し替え後もSpriteRendererの

                        // size/drawModeが変わらないことを明示的に保証しておく(念のための保険)。

                        stringOption.LabelBackground.drawMode = __prevLabelDrawMode;

                        if (__prevLabelDrawMode is SpriteDrawMode.Sliced or SpriteDrawMode.Tiled)

                            stringOption.LabelBackground.size = __prevLabelSize;



                        if (option.IsHeader)

                        {

                            var marksprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Label.{option.Name}.png");

                            if (UtilsRoleInfo.GetRoleByInputName(GetString(option.Name), out var role, true) && role.IsVanilla())

                            {

                                var roleb = RoleManager.Instance.AllRoles.ToArray().Where(x => x.Role == role.GetRoleTypes()).FirstOrDefault();



                                if (roleb is not null)

                                {

                                    marksprite = roleb.RoleIconSolid;

                                }

                            }

                            if (marksprite is not null)

                            {

                                var mark = Object.Instantiate(stringOption.LabelBackground, stringOption.transform);

                                mark.sprite = marksprite;

                                mark.transform.localPosition = new Vector3(0.53f, -0.062f, -10.1f);

                                mark.transform.localScale = new Vector3(0.26f, 0.85f, 1);

                            }

                        }

                        if (option.HideValue)

                        {

                            stringOption.PlusBtn.transform.localPosition = new Vector3(100, 100, 100);

                            stringOption.MinusBtn.transform.localPosition = new Vector3(100, 100, 100);

                        }

                        // フィルターオプション、属性設定なら

                        if (option is FilterOptionItem or AssignOptionItem)

                        {

                            stringOption.MinusBtn.OnClick = new();

                            stringOption.MinusBtn.OnClick.AddListener((System.Action)(() =>

                            {

                                if (option is FilterOptionItem filterOptionItem) filterOptionItem.SetRoleValue(parentrole);

                                if (option is AssignOptionItem assignoptionitem) assignoptionitem.SetRoleValue(new());

                            }));

                            stringOption.MinusBtn.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "<size=80%>←";

                            stringOption.PlusBtn.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "<rotate=-20>ρ";

                            stringOption.PlusBtn.OnClick = new();

                            stringOption.PlusBtn.OnClick.AddListener((System.Action)(() =>

                            {

                                CustomRoles[] notAssign = [];

                                var (imp, mad, crew, neu, addon) = (true, true, true, true, true);

                                if (option is FilterOptionItem filterOptionItem)

                                {

                                    notAssign = filterOptionItem.NotAssin?.Invoke() ?? [];

                                    (imp, mad, crew, neu, addon) = filterOptionItem.roles;

                                }

                                if (option is AssignOptionItem assignoptionitem)

                                {

                                    notAssign = assignoptionitem.NotAssin?.Invoke() ?? [];

                                    (imp, mad, crew, neu, addon) = assignoptionitem.roles;



                                    ShowFilter.NowOption = option;

                                    ShowFilter.CreateFilterOptionMenu(tabtransfrom, assignoptionitem.RoleValues[AssignOptionItem.Getpresetid()], notAssign, (imp, mad, crew, neu, addon));

                                    return;

                                }

                                ShowFilter.NowOption = option;

                                ShowFilter.CreateFilterOptionMenu(tabtransfrom, null, notAssign, (imp, mad, crew, neu, addon));

                            }));

                        }

                        if (option.CustomRole is not CustomRoles.NotAssigned and not CustomRoles.GM)

                        {

                            var button = Object.Instantiate(GameSettingMenu.Instance.GameSettingsButton, stringOption.transform);

                            button.inactiveSprites.GetComponent<SpriteRenderer>().sprite =

                            button.selectedSprites.GetComponent<SpriteRenderer>().sprite = null;



                            button.OnClick = new();

                            button.buttonText.DestroyTranslator();

                            button.buttonText.text = " ";

                            button.gameObject.name = $"{option.Name}OptionButton";

                            button.transform.localPosition = new Vector3(-2.46f, 0.0446f, -2);

                            button.transform.localScale = new Vector3(1.44f, 1.14f, 1f);

                            {

                                var __activeRenderer = button.activeSprites.GetComponent<SpriteRenderer>();

                                var __prevSize = __activeRenderer.size;

                                var __prevDrawMode = __activeRenderer.drawMode;

                                __activeRenderer.sprite = option.Tooltip.Invoke() == "" ? LabelBackgroundSprite : LabelBackgroundToolSprite;

                                __activeRenderer.drawMode = __prevDrawMode;

                                if (__prevDrawMode is SpriteDrawMode.Sliced or SpriteDrawMode.Tiled)

                                    __activeRenderer.size = __prevSize;

                            }

                            button.activeSprites.GetComponent<SpriteRenderer>().color = UtilsRoleText.GetRoleColor(option.CustomRole).ShadeColor(0.2f).SetAlpha(0.35f);



                            button.OnClick.AddListener((System.Action)(() =>

                            {

                                if (NowRoleTab is not CustomRoles.NotAssigned)

                                {

                                    var atabtitle = ModSettingsTab.transform.FindChild("Scroller/SliderInner/ChancesTab/CategoryHeaderMasked").GetComponent<CategoryHeaderMasked>();

                                    CategoryHeaderEditRole[] stabsubtitle = atabtitle.transform.parent.GetComponentsInChildren<CategoryHeaderEditRole>();

                                    atabtitle.Title.DestroyTranslator();

                                    atabtitle.Title.text = GetString("TabGroup." + ModoruTabu.Item1);



                                    atabtitle.Background.color = ModColors.Gray;

                                    atabtitle.Title.color = Color.white;

                                    NowRoleTab = CustomRoles.NotAssigned;

                                    menus[ModoruTabu.Item1].SetActive(true);

                                    crmenus[option.CustomRole].SetActive(false);

                                    foreach (var sub in stabsubtitle)

                                    {

                                        Object.Destroy(sub.gameObject);

                                    }

                                    ModSettingsTab.scrollBar.velocity = Vector2.zero;

                                    ModSettingsTab.scrollBar.ScrollRelative(Vector2.zero);

                                    ModSettingsTab.scrollBar.Inner.localPosition = new Vector3(ModSettingsTab.scrollBar.Inner.localPosition.x, ModoruTabu.Item2, ModSettingsTab.scrollBar.Inner.localPosition.z);

                                    return;

                                }

                                button.selected = false;

                                NowRoleTab = option.CustomRole;

                                ModoruTabu = (option.Tab, ModSettingsTab.scrollBar.Inner.localPosition.y);



                                menus[option.Tab].SetActive(false);

                                crmenus[option.CustomRole].SetActive(true);

                                var tabtitle = ModSettingsTab.transform.FindChild("Scroller/SliderInner/ChancesTab/CategoryHeaderMasked").GetComponent<CategoryHeaderMasked>();

                                CategoryHeaderEditRole[] tabsubtitle = tabtitle.transform.parent.GetComponentsInChildren<CategoryHeaderEditRole>();

                                tabtitle.Title.DestroyTranslator();

                                Color.RGBToHSV(UtilsRoleText.GetRoleColor(option.CustomRole, true), out var h, out var s, out var v);

                                if (v < 0.6f)

                                {

                                    v = 0.6f;

                                }

                                var rolecolor = Color.HSVToRGB(h, s, v);

                                tabtitle.Title.text = Utils.ColorString(rolecolor, GetString(option.CustomRole.ToString()));

                                tabtitle.Title.color = Color.white;

                                var type = option.CustomRole.GetCustomRoleTypes();

                                Color color = ModColors.CrewMateBlue;



                                switch (type)

                                {

                                    case CustomRoleTypes.Impostor: color = ModColors.ImpostorRed; break;

                                    case CustomRoleTypes.Madmate: color = ModColors.MadMateOrenge; break;

                                    case CustomRoleTypes.Neutral: color = ModColors.NeutralGray; break;

                                    case CustomRoleTypes.Crewmate:

                                        color = ModColors.CrewMateBlue;

                                        if (option.CustomRole.IsAddOn()) color = ModColors.AddonsColor;

                                        if (option.CustomRole.IsGhostRole()) color = ModColors.GhostRoleColor;

                                        if (option.CustomRole.IsLovers()) color = UtilsRoleText.GetRoleColor(option.CustomRole);

                                        break;

                                }



                                tabtitle.Background.color = color.ShadeColor(0.7f);



                                ModSettingsTab.scrollBar.velocity = Vector2.zero;

                                ModSettingsTab.scrollBar.Inner.localPosition = new Vector3(ModSettingsTab.scrollBar.Inner.localPosition.x, 0, ModSettingsTab.scrollBar.Inner.localPosition.z);

                                ModSettingsTab.scrollBar.ScrollRelative(Vector2.zero);

                                foreach (var sub in tabsubtitle)

                                {

                                    Object.Destroy(sub.gameObject);

                                }

                            }));



                            rolebutton.Add(option.CustomRole, button);



                            {

                                var infobutton = Object.Instantiate(stringOption.MinusBtn, stringOption.transform);

                                {

                                    infobutton.gameObject.name = $"{option.Name}-InfoButton";

                                    infobutton.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "?";



                                    infobutton.OnClick = new();

                                    infobutton.OnClick.AddListener((System.Action)(() =>

                                    {

                                        // 連打防止のクールダウン(3秒)。連打するとRPC等が

                                        // 大量発行されホストが重くなる/落ちるおそれがあるため。

                                        if (!RoleRowButtonCooldown.TryClick($"{option.Name}-InfoButton")) return;



                                        var oldinfo = Nowinfo;

                                        Nowinfo = option.CustomRole;

                                        if (HudManager.Instance.TaskPanel.open && oldinfo.IsCombinationRole() && Nowinfo.IsCombinationRole())

                                        {

                                            switch (oldinfo)

                                            {

                                                case CustomRoles.Assassin: if (Nowinfo is CustomRoles.Assassin) Nowinfo = CustomRoles.Merlin; break;

                                                case CustomRoles.Merlin: if (Nowinfo is CustomRoles.Assassin) Nowinfo = CustomRoles.Assassin; break;

                                                case CustomRoles.Driver: if (Nowinfo is CustomRoles.Driver) Nowinfo = CustomRoles.Braid; break;

                                                case CustomRoles.Braid: if (Nowinfo is CustomRoles.Driver) Nowinfo = CustomRoles.Driver; break;

                                                case CustomRoles.Vega: if (Nowinfo is CustomRoles.Vega) Nowinfo = CustomRoles.Altair; break;

                                                case CustomRoles.Altair: if (Nowinfo is CustomRoles.Vega) Nowinfo = CustomRoles.Vega; break;

                                                case CustomRoles.Fool: if (Nowinfo is CustomRoles.Nue) Nowinfo = CustomRoles.Nue; break;

                                                case CustomRoles.Nue: if (Nowinfo is CustomRoles.Nue) Nowinfo = CustomRoles.Fool; break;

                                            }

                                            return;

                                        }

                                        if (HudManager.Instance.TaskPanel.open is false || Nowinfo == oldinfo)

                                            HudManager.Instance.TaskPanel.ToggleOpen();

                                    }));

                                    infobutton.gameObject.transform.SetLocalX(-0.1f);

                                    infobutton.gameObject.transform.SetLocalZ(-50);



                                    roleInfobutton.Add(option.CustomRole, infobutton);

                                }

                            }

                            {

                                var Showoptionbutton = Object.Instantiate(stringOption.MinusBtn, stringOption.transform);

                                {

                                    Showoptionbutton.gameObject.name = $"{option.Name}-SetOption";

                                    Showoptionbutton.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "";

                                    Showoptionbutton.buttonSprite.sprite = ShowOptionSprite;



                                    Showoptionbutton.OnClick = new();

                                    Showoptionbutton.OnClick.AddListener((System.Action)(() =>

                                    {

                                        // 連打防止のクールダウン(3秒)

                                        if (!RoleRowButtonCooldown.TryClick($"{option.Name}-SetOption")) return;

                                        button.OnClick.Invoke();

                                    }));

                                    Showoptionbutton.gameObject.transform.SetLocalX(-0.6f);

                                    Showoptionbutton.gameObject.transform.SetLocalZ(-50);

                                }

                            }

                            {

                                // ===== 役職を強制付与するチャットコマンドを実行するボタン =====

                                // 「?」ボタンの隣に追加。押すと /cmd h r <役職コマンド名> を

                                // 実行したのと同じ結果になる(UtilsRoleInfo.GetRolesInfoを直接呼ぶ)。

                                // 実際にチャット欄へ文字列を打ち込ませる遠回りな方式ではなく、

                                // コマンドの処理本体を直接呼ぶことで確実に実行する。

                                var forceRoleInfoButton = Object.Instantiate(stringOption.MinusBtn, stringOption.transform);

                                {

                                    forceRoleInfoButton.gameObject.name = $"{option.Name}-ForceRoleInfo";

                                    forceRoleInfoButton.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "H";



                                    forceRoleInfoButton.OnClick = new();

                                    forceRoleInfoButton.OnClick.AddListener((System.Action)(() =>

                                    {

                                        // 連打防止のクールダウン(3秒)。連打で役職ヘルプRPCが

                                        // 大量発行されるとホストが重くなる/落ちるおそれがあるため。

                                        if (!RoleRowButtonCooldown.TryClick($"{option.Name}-ForceRoleInfo")) return;



                                        // roleCommands辞書はGetRolesInfoの初回呼び出し時に遅延初期化されるため、

                                        // まだ未初期化(null)の場合に備え、空引数で一度呼んで初期化だけ済ませておく。

                                        if (ChatCommands.roleCommands == null)

                                        {

                                            UtilsRoleInfo.GetRolesInfo("", byte.MaxValue);

                                        }



                                        var chatCommand = ChatCommands.roleCommands != null && ChatCommands.roleCommands.TryGetValue(option.CustomRole, out var cmd)

                                            ? cmd

                                            : option.CustomRole.GetRoleInfo()?.ChatCommand;

                                        if (string.IsNullOrEmpty(chatCommand)) return;



                                        // "/cmd h r <役職コマンド名>" と同じ処理だが、

                                        // 送信先をホスト自身だけでなく全員(byte.MaxValue)にする。

                                        // SendMessage内部で「ホストでなければ何もしない」処理が

                                        // 既にあるため、実質的に「ホストが押した時だけ全員に送られる」形になる。

                                        UtilsRoleInfo.GetRolesInfo(chatCommand, byte.MaxValue);

                                    }));

                                    forceRoleInfoButton.gameObject.transform.SetLocalX(-1.1f);

                                    forceRoleInfoButton.gameObject.transform.SetLocalZ(-50);

                                }

                            }

                        }

                        if ((option as ObjectOptionitem)?.ClickActionkey is not null)

                        {

                            stringOption.MinusBtn.transform.FindChild("Text_TMP").GetComponent<TMPro.TextMeshPro>().text = "<rotate=-20>ρ";

                            stringOption.PlusBtn.transform.localPosition = new Vector3(100, 100, 100);

                            stringOption.MinusBtn.OnClick = new();

                            stringOption.MinusBtn.OnClick.AddListener((Action)(() =>

                            {

                                SetAction((option as ObjectOptionitem)?.ClickActionkey, tabtransfrom);

                            }));

                        }

                        if (option.Tooltip.Invoke() is not "")

                        {

                            stringOption.LabelBackground.gameObject.AddComponent<PassiveButton>();

                            stringOption.LabelBackground.gameObject.AddComponent<BoxCollider2D>().autoTiling = true;

                            var passive = stringOption.LabelBackground.gameObject.GetComponent<PassiveButton>();

                            passive.OnMouseOut = new();

                            passive.OnMouseOver = new();

                            passive.OnClick = new();

                            passive.OnMouseOut.AddListener((Action)(() => ToolTip.Hide()));

                            passive.OnMouseOver.AddListener((Action)(() => ToolTip.Show(passive, option.Tooltip.Invoke(), null)));

                        }



                        var transform = stringOption.ValueText.transform;

                        var pos = transform.localPosition;

                        transform.localPosition = new Vector3((pos.x + 0.7322f), pos.y, pos.z);

                        NumericOptionInput.Attach(option, stringOption);

                        stringOption.SetClickMask(optionsMenu.ButtonClickMask);

                        option.OptionBehaviour = stringOption;

                    }

                }

                option.OptionBehaviour.gameObject.active = true;

            }



            tabGenerated.Add(tab);

            Object.Destroy(template.gameObject);

        }



        public static StringOption GetTeamplate()

        {

            var template = Object.Instantiate(BaseOption);

            Vector3 pos = new();

            Vector3 scale = new();



            template.stringOptionName = AmongUs.GameOptions.Int32OptionNames.TaskBarMode;

            //Background

            var label = template.LabelBackground.transform;

            {

                label.localScale = new Vector3(1.3f, 1.14f, 1f);

                label.SetLocalX(-2.2695f);

            }

            //プラスボタン

            var plusButton = template.PlusBtn.transform;

            {

                pos = plusButton.localPosition;

                scale = plusButton.localScale;

                plusButton.localScale = new Vector3(scale.x, scale.y);

                plusButton.localPosition = new Vector3((pos.x + 1.1434f), pos.y, pos.z);

            }

            //マイナスボタン

            var minusButton = template.MinusBtn.transform;

            {

                pos = minusButton.localPosition;

                scale = minusButton.localScale;

                minusButton.localPosition = new Vector3((pos.x + 0.3463f), (pos.y), pos.z);

                minusButton.localScale = new Vector3(scale.x, scale.y);

            }

            //値を表示するテキスト

            var valueTMP = template.ValueText.transform;

            {

                pos = valueTMP.localPosition;

                valueTMP.localPosition = new Vector3((pos.x + 2.5f), pos.y, pos.z);

                scale = valueTMP.localScale;

                valueTMP.localScale = new Vector3(scale.x, scale.y, scale.z);

            }

            //上のテキストを囲む箱(ﾀﾌﾞﾝ)

            var valueBox = template.transform.FindChild("ValueBox");

            {

                pos = valueBox.localPosition;

                valueBox.localPosition = new Vector3((pos.x + 0.7322f), pos.y, pos.z);

                scale = valueBox.localScale;

                valueBox.localScale = new Vector3((scale.x + 0.2f), scale.y, scale.z);

            }

            //タイトル(設定名)

            var titleText = template.TitleText;

            {

                var transform = titleText.transform;

                pos = transform.localPosition;

                transform.localPosition = new Vector3((pos.x + -1.096f), pos.y, pos.z);

                scale = transform.localScale;

                transform.localScale = new Vector3(scale.x, scale.y, scale.z);

                titleText.rectTransform.sizeDelta = new Vector2(6.5f, 0.37f);

                titleText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

                titleText.SetOutlineColor(Color.black);

                titleText.SetOutlineThickness(0.125f);

            }

            template.OnValueChanged = new System.Action<OptionBehaviour>((o) => { });

            return template;

        }

        static void SetAction(string actionkey, Transform parent)

        {

            switch (actionkey)

            {

                case "ShowSleld": ShowRandomSpawnOption.CreateSpawanOptionMenu(parent, MapNames.Skeld); break;

                case "ShowMira": ShowRandomSpawnOption.CreateSpawanOptionMenu(parent, MapNames.MiraHQ); break;

                case "ShowPolus": ShowRandomSpawnOption.CreateSpawanOptionMenu(parent, MapNames.Polus); break;

                case "ShowAirship": ShowRandomSpawnOption.CreateSpawanOptionMenu(parent, MapNames.Airship); break;

                case "ShowFungle": ShowRandomSpawnOption.CreateSpawanOptionMenu(parent, MapNames.Fungle); break;

            }

        }

    }



    [HarmonyPatch(typeof(StringOption), nameof(StringOption.FixedUpdate))]

    class PrisetNamechengePatch

    {

        public static void Postfix(StringOption __instance)

        {

            if (ModSettingsTab == null) return;



            var option = PresetOptionItem.Preset;

            if (option == null) return;

            if (option.OptionBehaviour != __instance) return;



            __instance.ValueText.text = option.GetString();

        }

    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]

    class Prisetkesu

    {

        public static void Postfix(GameSettingMenu __instance)

        {

            __instance.ChangeTab(1, false);

            GameSettingMenuChangeTabPatch.ClickCount = 0;

        }

    }



    [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.Update))]

    class ModSettingsMenuUpdatePatch

    {

        public static bool Prefix(RolesSettingsMenu __instance)

        {

            if (__instance != ModSettingsTab) return true;



            if (!(ControllerManager.Instance.CurrentUiState.MenuName == __instance.name))

                return false;

            Rewired.Player player = Rewired.ReInput.players.GetPlayer(0);

            bool flag = false;

            if (__instance.selectedRoleTab > 0 && player.GetButtonDown(35))

            {

                --__instance.selectedRoleTab;

                flag = true;

            }

            if (__instance.selectedRoleTab < __instance.roleTabs.Count - 1 && player.GetButtonDown(34))

            {

                ++__instance.selectedRoleTab;

                flag = true;

            }

            if (flag)

            {

                __instance.roleTabs[__instance.selectedRoleTab].OnClick.Invoke();

            }

            __instance.glyphL.color = __instance.selectedRoleTab <= 0 ? __instance.glyphUnavailableColor : Color.white;

            if (__instance.selectedRoleTab < __instance.roleTabs.Count - 1)

                __instance.glyphR.color = Color.white;

            else

                __instance.glyphR.color = __instance.glyphUnavailableColor;

            return false;

        }

    }

}

