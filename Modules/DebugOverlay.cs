using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost.Modules;

// デバッグ版だけの枠(YouTubeのコメント枠と同じ操作)。
// 帯をつかんで移動、右下の「◢」をつかんで大きさを変える、帯の「－」「＋」で最小化。
// 位置・大きさ・最小化は次に起動した時も同じになる。
public sealed class DebugWindow
{
    private const float HeaderHeight = 0.32f;
    private const float ButtonSize = 0.34f;
    private const float GripSize = 0.3f;
    private const float MinimizedWidth = 2.4f;
    private const float PadX = 0.1f, PadY = 0.08f;
    private const float MinWidth = 2.4f, MaxWidth = 10f;
    private const float MinHeight = 0.9f, MaxHeight = 6.5f;
    private const int BaseOrder = 600;

    // 重なっている時に、いちばん手前の枠だけがクリックを受け取るための印
    public static bool ClickTaken;
    private static int focusCounter;

    public readonly string Key;
    public readonly string Title;
    private readonly float defX, defY, defW, defH;
    private readonly List<(string id, string label)> extraButtons = new();
    public Func<string, bool> IsButtonOn = _ => false;
    public Action<string> OnButton = _ => { };
    public int Focus { get; private set; }

    private GameObject root; // 枠の左下(背景の左下)を原点にした入れ物。文字の枠(RectTransform)の子にはしない
    private TextMeshPro text;
    private TextMeshPro header;
    private TextMeshPro grip;
    private readonly List<(string id, TextMeshPro t, float x)> buttonTexts = new();
    private SpriteRenderer background;
    private SpriteRenderer headerBar;
    private AspectPosition aspect;
    private enum Mode { None, Drag, Resize }
    private Mode mode;
    private string pressedButton;
    private Vector3 lastMouse;
    private float posX, posY, width, height;
    private bool minimized;
    private bool loaded;
    private int appliedFocus = -1;

    public DebugWindow(string key, string title, float x, float y, float w, float h, params (string id, string label)[] buttons)
    {
        Key = key;
        Title = title;
        defX = x; defY = y; defW = w; defH = h;
        extraButtons.AddRange(buttons);
    }

    public bool IsShown => root != null && root.activeSelf;
    private float W => minimized ? MinimizedWidth + extraButtons.Count * ButtonSize : Mathf.Clamp(width, MinWidth, MaxWidth);
    private float H => minimized ? HeaderHeight + 0.04f : Mathf.Clamp(height, MinHeight, MaxHeight);

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        mode = Mode.None;
        pressedButton = null;
    }

    // 表示する(作っていなければ作る)。マウスの処理は HandleMouse で別に行う
    public void Show(HudManager hud)
    {
        if (!loaded) Load();
        if (root == null) Build(hud);
        if (root != null && !root.activeSelf) root.SetActive(true);
    }

    public void SetBody(string body)
    {
        if (text == null) return;
        RefreshButtons();
        ApplyFocusOrder();
        var shown = minimized ? "" : body;
        if (text.text != shown) text.text = shown;
    }

    private TextMeshPro CreateText(string name, HudManager hud, float size, TextAlignmentOptions align)
    {
        var t = Templates.TMPTemplate.Create(name, "", Color.white, size, align, true, root.transform);
        var font = hud.TaskPanel?.taskText?.font;
        if (font != null) t.font = font;
        t.rectTransform.pivot = new Vector2(0f, 0f);
        t.SetOutlineColor(Color.black);
        t.SetOutlineThickness(0.15f);
        t.enableWordWrapping = false;
        return t;
    }

    private void Build(HudManager hud)
    {
        root = new GameObject("DebugWindow_" + Key);
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(hud.transform, false);
        aspect = root.AddComponent<AspectPosition>();
        aspect.Alignment = AspectPosition.EdgeAlignments.LeftBottom;
        aspect.DistanceFromEdge = new Vector3(posX, posY, -25f);
        aspect.AdjustPosition();

        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0f), 1f);
        background = MakeRect("Bg", sprite, new Color(0.03f, 0.03f, 0.07f, 0.72f), 0.1f);
        headerBar = MakeRect("Bar", sprite, new Color(0.55f, 0.2f, 0.42f, 0.9f), 0.05f);

        text = CreateText("DebugWindowBody_" + Key, hud, 1.15f, TextAlignmentOptions.TopLeft);
        text.overflowMode = TextOverflowModes.Truncate;
        header = CreateText("DebugWindowHeader_" + Key, hud, 1.25f, TextAlignmentOptions.MidlineLeft);
        foreach (var (id, _) in extraButtons)
            buttonTexts.Add((id, CreateText("DebugWindowBtn_" + Key + id, hud, 1.35f, TextAlignmentOptions.Center), 0f));
        buttonTexts.Add(("min", CreateText("DebugWindowMin_" + Key, hud, 1.8f, TextAlignmentOptions.Center), 0f));
        grip = CreateText("DebugWindowGrip_" + Key, hud, 1.3f, TextAlignmentOptions.BottomRight);
        grip.text = "<color=#bbbbbb>◢</color>";
        Layout();
        ApplyFocusOrder();
    }

    private SpriteRenderer MakeRect(string name, Sprite sprite, Color color, float z)
    {
        var go = new GameObject("DebugWindow" + name + "_" + Key);
        go.layer = root.layer;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        return sr;
    }

    // 最後に触った枠ほど手前に描く
    private void ApplyFocusOrder()
    {
        if (root == null || appliedFocus == Focus) return;
        appliedFocus = Focus;
        var baseOrder = BaseOrder + Focus * 10;
        background.sortingOrder = baseOrder;
        headerBar.sortingOrder = baseOrder + 1;
        foreach (var r in root.GetComponentsInChildren<TextMeshPro>(true)) r.sortingOrder = baseOrder + 2;
        var p = aspect.DistanceFromEdge;
        aspect.DistanceFromEdge = new Vector3(p.x, p.y, -25f - Mathf.Min(Focus, 50) * 0.2f);
        aspect.AdjustPosition();
    }

    private void Layout()
    {
        if (root == null) return;
        var w = W;
        var h = H;
        background.transform.localPosition = new Vector3(0f, 0f, 0.1f);
        background.transform.localScale = new Vector3(w, h, 1f);
        headerBar.transform.localPosition = new Vector3(0f, h - HeaderHeight, 0.05f);
        headerBar.transform.localScale = new Vector3(w, HeaderHeight, 1f);
        // 本文は帯の下の左上から
        text.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.1f, w - PadX * 2), Mathf.Max(0.1f, h - HeaderHeight - PadY * 2));
        text.transform.localPosition = new Vector3(PadX, PadY, -0.01f);
        header.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.1f, w - ButtonSize * buttonTexts.Count - PadX * 2), HeaderHeight);
        header.transform.localPosition = new Vector3(PadX, h - HeaderHeight, -0.01f);
        header.text = $"<b>{Title}</b>";
        // 帯の右側に、右から「－」→ ほかのボタンの順で並べる
        for (var i = 0; i < buttonTexts.Count; i++)
        {
            var (id, t, _) = buttonTexts[i];
            var x = w - PadX * 0.5f - ButtonSize * (buttonTexts.Count - i);
            t.rectTransform.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            t.transform.localPosition = new Vector3(x, h - ButtonSize, -0.01f);
            buttonTexts[i] = (id, t, x);
        }
        grip.gameObject.SetActive(!minimized);
        grip.rectTransform.sizeDelta = new Vector2(GripSize, GripSize);
        grip.transform.localPosition = new Vector3(w - GripSize, 0f, -0.01f);
    }

    private void RefreshButtons()
    {
        foreach (var (id, t, _) in buttonTexts)
        {
            if (id == "min") { t.text = minimized ? "<b>＋</b>" : "<b>－</b>"; continue; }
            var label = extraButtons.FirstOrDefault(b => b.id == id).label;
            t.text = IsButtonOn(id) ? $"<b><color=#ffd36e>{label}</color></b>" : $"<b><color=#cccccc>{label}</color></b>";
        }
    }

    private Vector2 ToFrame(Vector3 world)
    {
        var p = root.transform.InverseTransformPoint(world);
        return new Vector2(p.x, p.y);
    }

    private static bool InRect(Vector2 p, float x, float y, float w, float h) => p.x >= x && p.x <= x + w && p.y >= y && p.y <= y + h;

    private string ButtonAt(Vector2 p, float h)
    {
        foreach (var (id, _, x) in buttonTexts)
            if (InRect(p, x - 0.02f, h - ButtonSize - 0.02f, ButtonSize + 0.04f, ButtonSize + 0.04f)) return id;
        return null;
    }

    // 手前の枠から順に呼ぶ。この枠がクリックを受け取ったら ClickTaken を立てて、後ろの枠には渡さない
    public void HandleMouse()
    {
        var cam = Camera.main;
        if (cam == null || root == null || !root.activeSelf || aspect == null) return;
        var mouse = cam.ScreenToWorldPoint(Input.mousePosition);
        var w = W;
        var h = H;
        if (mode == Mode.None && pressedButton == null)
        {
            if (!Input.GetMouseButtonDown(0) || ClickTaken) return;
            var p = ToFrame(mouse);
            if (!InRect(p, 0f, 0f, w, h)) return;
            ClickTaken = true;
            Focus = ++focusCounter;
            ApplyFocusOrder();
            lastMouse = mouse;
            var b = ButtonAt(p, h);
            if (b != null) { pressedButton = b; return; }
            mode = !minimized && InRect(p, w - GripSize - 0.05f, 0f, GripSize + 0.05f, GripSize + 0.05f) ? Mode.Resize : Mode.Drag;
            return;
        }
        if (pressedButton != null)
        {
            if (Input.GetMouseButton(0)) return;
            var b = pressedButton;
            pressedButton = null;
            // 押したままボタンの外で離した時は何もしない
            if (ButtonAt(ToFrame(mouse), h) != b) return;
            if (b == "min") ToggleMinimize();
            else OnButton(b);
            return;
        }
        var screenH = cam.orthographicSize * 2f;
        var screenW = screenH * cam.aspect;
        if (Input.GetMouseButton(0))
        {
            var d = mouse - lastMouse;
            lastMouse = mouse;
            var pos = aspect.DistanceFromEdge;
            if (mode == Mode.Drag)
            {
                pos += new Vector3(d.x, d.y, 0f);
            }
            else
            {
                // 右下をつかんでいるので、右に動かすと横に広がり、下に動かすと縦に広がる(上の端はそのまま)
                var newW = Mathf.Clamp(w + d.x, MinWidth, Mathf.Min(MaxWidth, screenW - pos.x));
                var newH = Mathf.Clamp(h - d.y, MinHeight, Mathf.Min(MaxHeight, pos.y + h));
                pos.y -= newH - h;
                width = newW;
                height = newH;
                Layout();
                w = newW;
                h = newH;
            }
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, screenW - w));
            pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, screenH - h));
            aspect.DistanceFromEdge = pos;
            aspect.AdjustPosition();
            return;
        }
        mode = Mode.None;
        posX = aspect.DistanceFromEdge.x;
        posY = aspect.DistanceFromEdge.y;
        Save();
    }

    private void ToggleMinimize()
    {
        var oldH = H;
        minimized = !minimized;
        var pos = aspect.DistanceFromEdge;
        pos.y = Mathf.Max(0f, pos.y + oldH - H);
        aspect.DistanceFromEdge = pos;
        aspect.AdjustPosition();
        posY = pos.y;
        Layout();
        Save();
    }

    private void Load()
    {
        loaded = true;
        posX = defX; posY = defY; width = defW; height = defH; minimized = false;
        try
        {
            posX = PlayerPrefs.GetFloat($"TOH_DW_{Key}_X", defX);
            posY = PlayerPrefs.GetFloat($"TOH_DW_{Key}_Y", defY);
            width = PlayerPrefs.GetFloat($"TOH_DW_{Key}_W", defW);
            height = PlayerPrefs.GetFloat($"TOH_DW_{Key}_H", defH);
            minimized = PlayerPrefs.GetInt($"TOH_DW_{Key}_Min", 0) == 1;
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            PlayerPrefs.SetFloat($"TOH_DW_{Key}_X", posX);
            PlayerPrefs.SetFloat($"TOH_DW_{Key}_Y", posY);
            PlayerPrefs.SetFloat($"TOH_DW_{Key}_W", width);
            PlayerPrefs.SetFloat($"TOH_DW_{Key}_H", height);
            PlayerPrefs.SetInt($"TOH_DW_{Key}_Min", minimized ? 1 : 0);
            PlayerPrefs.Save();
        }
        catch { }
    }
}

// デバッグ版だけの枠をまとめて動かす。
// F3 で全部の枠の表示/非表示、Ctrl+F3 でデバッグモードの ON/OFF(ホストのロビーだけ)。
// 「デバッグ」枠の帯のボタン: [P] プレイヤー一覧の枠 / [D] 全員の位置の枠 / [L] ログをゲーム内にも出す
public static class DebugOverlay
{
    private static readonly DebugWindow main = new("Main", "☆デバッグ☆", 0.35f, 3.0f, 4.6f, 2.1f, ("P", "P"), ("D", "D"), ("L", "L"));
    private static readonly DebugWindow players = new("Players", "プレイヤー", 0.35f, 5.3f, 7.2f, 3.4f);
    private static readonly DebugWindow positions = new("Pos", "全員の位置", 3.2f, 0.25f, 6.4f, 1.8f);

    private static bool visible = true;
    private static bool loaded;
    private static float nextUpdate;
    private static float smoothDelta = 0.016f;
    private static string mainBody = "", playersBody = "", positionsBody = "";

    public static bool Active => DebugModeManager.AmDebugger;

    static DebugOverlay()
    {
        main.IsButtonOn = id => id switch
        {
            "P" => Main.DebugTours.Value,
            "D" => Main.ShowDistance.Value,
            "L" => Logger.isAlsoInGame,
            _ => false,
        };
        main.OnButton = id =>
        {
            switch (id)
            {
                case "P": Main.DebugTours.Value = !Main.DebugTours.Value; break;
                case "D": Main.ShowDistance.Value = !Main.ShowDistance.Value; break;
                case "L":
                    Logger.isAlsoInGame = !Logger.isAlsoInGame;
                    Logger.seeingame($"ログのゲーム内出力: {Logger.isAlsoInGame}");
                    break;
            }
            nextUpdate = 0f;
        };
    }

    public static void Update(HudManager hud)
    {
        if (!Active || hud == null) return;
        if (!loaded)
        {
            loaded = true;
            try { visible = PlayerPrefs.GetInt("TOH_DW_Visible", 1) == 1; } catch { }
        }
        smoothDelta += (Time.unscaledDeltaTime - smoothDelta) * 0.1f;
        if (Input.GetKeyDown(KeyCode.F3))
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ToggleDebugMode();
            else
            {
                visible = !visible;
                try { PlayerPrefs.SetInt("TOH_DW_Visible", visible ? 1 : 0); PlayerPrefs.Save(); } catch { }
            }
            nextUpdate = 0f;
        }
        var chatOpen = hud.Chat != null && hud.Chat.IsOpenOrOpening;
        if (!visible || chatOpen)
        {
            main.Hide(); players.Hide(); positions.Hide();
            return;
        }
        if (Time.unscaledTime >= nextUpdate)
        {
            nextUpdate = Time.unscaledTime + 0.25f;
            mainBody = BuildMain();
            playersBody = BuildPlayers();
            positionsBody = BuildPositions();
        }
        var inGame = GameStates.InGame;
        main.Show(hud);
        if (Main.DebugTours.Value && inGame) players.Show(hud); else players.Hide();
        if (Main.ShowDistance.Value && inGame) positions.Show(hud); else positions.Hide();
        // マウスは手前の枠から順に渡す(重なっている所をつかんだ時に、2つ一緒に動かないように)
        DebugWindow.ClickTaken = false;
        foreach (var w in new[] { main, players, positions }.Where(w => w.IsShown).OrderByDescending(w => w.Focus))
            w.HandleMouse();
        main.SetBody(mainBody);
        if (players.IsShown) players.SetBody(playersBody);
        if (positions.IsShown) positions.SetBody(positionsBody);
    }

    private static string BuildMain()
    {
        var sb = new StringBuilder();
        var debugMode = DebugModeManager.IsDebugMode;
        var role = !AmongUsClient.Instance ? "-" : AmongUsClient.Instance.AmHost ? "ホスト" : "参加者";
        sb.Append($"デバッグモード: {(debugMode ? "<color=#7cff7c>ON</color>" : "<color=#ff7c7c>OFF</color>")}　{role}");
        sb.Append($"　FPS: {Mathf.RoundToInt(1f / Mathf.Max(smoothDelta, 0.0001f))}");
        if (AmongUsClient.Instance && GameStates.IsOnlineGame) sb.Append($"　Ping: {AmongUsClient.Instance.Ping}ms");
        sb.Append('\n');
        sb.Append($"場面: {Phase()}");
        if (GameStates.InGame)
            sb.Append($"　{UtilsGameLog.day}日目　生存 {PlayerCatch.AllAlivePlayerControls.Count()}/{PlayerCatch.AllPlayerControls.Count()}");
        sb.Append('\n');
        var me = PlayerControl.LocalPlayer;
        if (me != null)
        {
            var pos = me.GetTruePosition();
            var room = GameStates.InGame ? me.GetShipRoomName() : "";
            sb.Append($"座標: ({pos.x:0.00}, {pos.y:0.00}) {room}\n");
        }
        sb.Append("<color=#ffd36e>[P]</color>プレイヤー一覧　<color=#ffd36e>[D]</color>全員の位置　<color=#ffd36e>[L]</color>ログをゲーム内に\n");
        if (debugMode)
        {
            sb.Append("Shift+I: 座標をログへ　Y: 設定を同期　O+C: 自分のタスク全完了\n");
            sb.Append("Shift+Enter+E: 自分を追放　V: 投票をクリア(ローカル)");
        }
        else sb.Append("<color=#aaaaaa>Ctrl+F3 でデバッグモードをONにすると、強いデバッグ機能も使えます</color>");
        return sb.ToString();
    }

    private static bool CanSeeRoles()
        => AmongUsClient.Instance && (AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame
            || (AmongUsClient.Instance.AmHost && DebugModeManager.IsDebugMode));

    // タスクの欄に出していたプレイヤー一覧(今は専用の枠に出す)
    private static string BuildPlayers()
    {
        if (!GameStates.InGame) return "";
        var sb = new StringBuilder();
        var showRoles = CanSeeRoles();
        foreach (var pc in PlayerCatch.AllPlayerControls.OrderBy(p => p.PlayerId))
        {
            if (pc == null) continue;
            var life = pc.IsAlive() ? "<#3cff63>●</color>" : Utils.GetVitalText(pc.PlayerId, true);
            var roleText = showRoles ? UtilsRoleText.GetTrueRoleName(pc.PlayerId) : "<color=#888888>?</color>";
            var pos = pc.GetTruePosition();
            sb.Append($"{UtilsName.GetPlayerColor(pc)}({life}) : {roleText} ({pos.x:0.00}, {pos.y:0.00}) {(pc.inVent ? "<color=#ff9f43>ベント</color>" : "歩き")} {pc.Data?.RoleType}\n");
        }
        if (!showRoles) sb.Append("<color=#aaaaaa>(役職はホストのデバッグモードの時だけ出ます)</color>");
        return sb.ToString();
    }

    // 下に出していた「全員との距離」(今は専用の枠に出す)
    private static string BuildPositions()
    {
        var me = PlayerControl.LocalPlayer;
        if (!GameStates.InGame || me == null) return "";
        var sb = new StringBuilder();
        var myPos = (Vector2)me.transform.position;
        foreach (var pc in PlayerCatch.AllPlayerControls.Where(p => p != null && p != me).OrderBy(p => Vector2.Distance(myPos, p.transform.position)))
        {
            var dist = Vector2.Distance(myPos, pc.transform.position);
            var pos = pc.GetTruePosition();
            var color = Palette.PlayerColors[Mathf.Clamp(pc.cosmetics.ColorId, 0, Palette.PlayerColors.Length - 1)];
            var name = Utils.ColorString(color, pc.Data?.PlayerName ?? pc.name);
            sb.Append($"{name}{(pc.IsAlive() ? "" : " <color=#888888>(死亡)</color>")}: 距離 {dist:0.00}　({pos.x:0.00}, {pos.y:0.00}) {pc.GetShipRoomName()}\n");
        }
        return sb.ToString();
    }

    private static string Phase()
    {
        if (!AmongUsClient.Instance || GameStates.IsNotJoined) return "メニュー";
        if (GameStates.IsLobby) return "ロビー";
        if (GameStates.Intro) return "イントロ";
        if (GameStates.IsMeeting) return "会議";
        if (GameStates.IsInTask) return "タスク";
        return GameStates.InGame ? "試合中" : "-";
    }

    private static void ToggleDebugMode()
    {
        if (DebugModeManager.EnableDebugMode == null) return;
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby)
        {
            Logger.seeingame("デバッグモードの切り替えは、ホストのロビーでだけできます");
            return;
        }
        var on = !DebugModeManager.EnableDebugMode.GetBool();
        DebugModeManager.EnableDebugMode.SetValue(on ? 1 : 0);
        Logger.seeingame($"デバッグモード: {(on ? "ON" : "OFF")}");
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class DebugOverlayPatch
{
    private static bool errorLogged;
    public static void Postfix(HudManager __instance)
    {
        try { DebugOverlay.Update(__instance); }
        catch (Exception e)
        {
            // 毎フレーム同じエラーを出し続けないよう、1回だけ記録する
            if (!errorLogged) Logger.Warn($"DebugOverlay: {e}", "DebugOverlay");
            errorLogged = true;
        }
    }
}
