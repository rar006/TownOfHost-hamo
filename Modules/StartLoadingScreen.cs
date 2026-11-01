using System;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHost.Roles.Core;
using TMPro;
using UnityEngine;

namespace TownOfHost.Modules;

// ゲーム開始後の読み込み中に出す TOH-hamo のロード画面(自分の画面だけ。通信はしない)。
// TOH-hm オプションの「開始時のロード画面」で、ふつうの読み込みバーと切り替えられる
public static class StartLoadingScreen
{
    private const float MaxShowTime = 40f; // 何かで閉じられなかった時の保険
    // 文字がすぐ切り替わって読めないので、1つの文字は最低これだけ出しておく
    private const float StatusHoldTime = 1.5f;
    private const float TipTime = 8f;
    private static readonly string[] Tips =
    {
        "会議中は /cmd n r I で、入っている役職を確かめられます",
        "Shift+F3 で通信モニターを出せます",
        "役職の説明は、タスクの欄からいつでも見られます",
        "ゲーム設定の「有効なMAP設定」で、今使える設定だけを表示できます",
        "暗転したら、少し待つと戻ることがあります",
        "Shift+F4 で、どの画面からでもバグ報告を開けます",
        "TOH-hm オプションの「ロード画面にゲームを追加する」で、待っている間に遊べます",
        "Shift+F2 で、ロード画面のゲームの続きを遊べます(やることが残っている間は開けません)",
    };
    private static readonly string[] Dots = { ".", "..", "..." };

    private static GameObject root;
    private static SpriteRenderer bg;
    private static Transform top;
    private static SpriteRenderer fill;
    private static TextMeshPro status;
    private static TextMeshPro dots;
    private static TextMeshPro tip;
    private static float shownAt;
    private static float targetPercent;
    private static float shownPercent;
    private static string statusText = "";
    private static string shownStatus;
    private static float statusChangedAt;
    private static int tipIndex;
    private static float tipChangedAt;
    private static int shownDots = -1;
    private static bool active;
    // Shift+F2 で、ロード画面のゲームの続きだけを開いている間
    private static bool playing;
    private static readonly Color BgColor = new(0.035f, 0.04f, 0.07f, 1f);
    public static bool IsShowing => active;
    public static bool Enabled => Main.CustomStartLoading?.Value ?? true;
    static bool GameEnabled => Main.LoadingMiniGame?.Value ?? false;

    public static void Show()
    {
        if (!Enabled || active) return;
        var hud = HudManager.Instance;
        if (hud == null) return;
        try
        {
            Build(hud);
            playing = false;
            bg.color = BgColor;
            top.gameObject.SetActive(true);
            active = true;
            shownAt = Time.unscaledTime;
            targetPercent = shownPercent = 0f;
            // ホストは読み込みの文字が来ないことがあるので、最初の文字を入れておく
            statusText = "ゲームを準備しています";
            shownStatus = null;
            statusChangedAt = -StatusHoldTime;
            shownDots = -1;
            tipIndex = IRandom.Instance.Next(Tips.Length);
            ShowTip();
            var game = GameEnabled;
            // ゲームを出す時は、ロゴなどを上に寄せて下を遊ぶ場所にする
            top.localPosition = new Vector3(0f, game ? 0.85f : 0f, 0f);
            LoadingMiniGame.SetActive(game, false);
            root.SetActive(true);
        }
        catch (Exception e) { Logger.Warn($"ロード画面を出せませんでした: {e.Message}", "StartLoadingScreen"); Hide(); }
    }

    public static void Hide()
    {
        if (active) LoadingMiniGame.SaveBest();
        active = false;
        // Shift+F2 で遊んでいる間は、読み込みバーが閉じる合図で消さない
        if (root != null && !playing) root.SetActive(false);
    }

    public static void SetProgress(float percent, string text)
    {
        targetPercent = Mathf.Clamp01(percent / 100f);
        if (!string.IsNullOrEmpty(text)) statusText = text;
    }

    // Shift+F2: ロード画面で遊んでいたゲームを、止めた所から続ける(自分の画面だけ)
    public static void TogglePlay()
    {
        if (playing) { ClosePlay(); return; }
        if (active || GameStates.IsMeeting) return;
        var hud = HudManager.Instance;
        if (hud == null) return;
        if (HasSomethingToDo())
        {
            Logger.seeingame("まだ君にはやることがある！");
            return;
        }
        try
        {
            Build(hud);
            playing = true;
            top.gameObject.SetActive(false);
            bg.color = new Color(BgColor.r, BgColor.g, BgColor.b, 0.9f);
            LoadingMiniGame.SetActive(true, true);
            root.SetActive(true);
        }
        catch (Exception e) { Logger.Warn($"ミニゲームを開けませんでした: {e.Message}", "StartLoadingScreen"); ClosePlay(); }
    }

    static void ClosePlay()
    {
        if (playing) LoadingMiniGame.SaveBest();
        playing = false;
        LoadingMiniGame.SetActive(false, false);
        if (root != null && !active) root.SetActive(false);
    }

    // インポスター陣営・タスクが残っているクルー・能力のある幽霊・生きているクルー以外の人は、まだ遊ばせない
    static bool HasSomethingToDo()
    {
        var pc = PlayerControl.LocalPlayer;
        if (pc == null || pc.Data == null || !GameStates.IsInGame || GameStates.IsLobby) return false;
        var role = pc.GetCustomRole();
        var team = role.GetCustomRoleTypes();
        if (team is CustomRoleTypes.Impostor) return true;
        if (PlayerState.GetByPlayerId(pc.PlayerId)?.GhostRole is { } ghost && ghost is not CustomRoles.NotAssigned) return true;
        if (pc.Data.Role != null && pc.Data.Role.Role is RoleTypes.GuardianAngel) return true;
        var task = pc.GetPlayerTaskState();
        if (task != null && task.hasTasks && !task.IsTaskFinished) return true;
        return pc.IsAlive() && team is not CustomRoleTypes.Crewmate;
    }

    public static void Tick()
    {
        if (playing)
        {
            // 会議・試合の終わり・Esc で閉じる(遊んでいた所はそのまま残る)
            if (Input.GetKeyDown(KeyCode.Escape) || GameStates.IsMeeting || (GameStates.IsInGame && GameStates.IsEnded)) ClosePlay();
            else LoadingMiniGame.Tick();
            return;
        }
        if (!active || root == null) return;
        // イントロが始まったり、イントロが終わったりしたら閉じる。
        // 「タスク中」の印はゲーム開始の直後(ホストでは読み込み中)から立つので、閉じる合図には使わない
        if (IntroCutscene.Instance != null || GameStates.introDestroyed || GameStates.IsMeeting || Time.unscaledTime - shownAt > MaxShowTime)
        {
            Hide();
            return;
        }
        var now = Time.unscaledTime;
        shownPercent = Mathf.MoveTowards(shownPercent, Mathf.Max(targetPercent, 0.05f), Time.unscaledDeltaTime * 0.8f);
        fill.transform.localScale = new Vector3(BarWidth * shownPercent, BarHeight, 1f);
        fill.transform.localPosition = new Vector3(-BarWidth / 2f + BarWidth * shownPercent / 2f, BarY, -0.2f);
        if (statusText != shownStatus && now - statusChangedAt >= StatusHoldTime)
        {
            shownStatus = statusText;
            statusChangedAt = now;
            status.text = statusText;
        }
        var d = (int)(now * 3f) % 3;
        if (d != shownDots)
        {
            shownDots = d;
            dots.text = Dots[d];
        }
        if (now - tipChangedAt >= TipTime)
        {
            tipIndex = (tipIndex + 1) % Tips.Length;
            ShowTip();
        }
        // 切り替わった直後は少しずつ出す
        tip.alpha = Mathf.Clamp01((now - tipChangedAt) / 0.4f);
        LoadingMiniGame.Tick();
    }

    private static void ShowTip()
    {
        tipChangedAt = Time.unscaledTime;
        tip.text = $"<color={UiSkin.DimHex}>TIPS</color>  {Tips[tipIndex]}";
    }

    private const float BarWidth = 4.2f, BarHeight = 0.12f, BarY = -0.9f;

    private static Sprite white;
    internal static SpriteRenderer Rect(string name, Transform parent, Color color, Vector3 pos, Vector2 size, int order)
    {
        white ??= Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = white;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    internal static TextMeshPro Text(string name, HudManager hud, Transform parent, float size, Vector3 pos, int order)
    {
        var t = Templates.TMPTemplate.Create(name, "", Color.white, size, TextAlignmentOptions.Center, true, parent);
        var font = hud.TaskPanel?.taskText?.font;
        if (font != null) t.font = font;
        t.rectTransform.sizeDelta = new Vector2(9f, 0.6f);
        t.transform.localPosition = pos;
        t.enableWordWrapping = false;
        t.sortingOrder = order;
        return t;
    }

    private static void Build(HudManager hud)
    {
        if (root != null) return;
        root = new GameObject("TOHhamoStartLoading");
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(hud.transform, false);
        // 画面の真ん中・いちばん手前
        root.transform.localPosition = new Vector3(0f, 0f, -600f);

        const int order = 2000;
        bg = Rect("Bg", root.transform, BgColor, Vector3.zero, new Vector2(40f, 30f), order);
        top = new GameObject("Top").transform;
        top.gameObject.layer = LayerMask.NameToLayer("UI");
        top.SetParent(root.transform, false);
        var logo = UtilsSprite.LoadSprite("TownOfHost.Resources.TownOfHost-hamo-logo.png", 170f);
        if (logo != null)
        {
            var go = new GameObject("Logo");
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(top, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, -0.1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = logo;
            sr.sortingOrder = order + 1;
        }
        Rect("BarBg", top, new Color(1f, 1f, 1f, 0.12f), new Vector3(0f, BarY, -0.1f), new Vector2(BarWidth, BarHeight), order + 1);
        fill = Rect("BarFill", top, new Color(1f, 0.52f, 0.85f, 1f), new Vector3(-BarWidth / 2f, BarY, -0.2f), new Vector2(0f, BarHeight), order + 2);
        status = Text("Status", hud, top, 2.2f, new Vector3(0f, -0.45f, -0.3f), order + 3);
        dots = Text("Dots", hud, top, 2.2f, new Vector3(0f, -1.3f, -0.3f), order + 3);
        tip = Text("Tip", hud, top, 1.6f, new Vector3(0f, -2.1f, -0.3f), order + 3);
        LoadingMiniGame.Build(hud, root.transform, order + 4);
        root.SetActive(false);
    }
}

// ロード画面で遊べる、ジャンプで箱をよけるだけの軽いゲーム(自分の画面だけ。通信はしない)。
// 箱は 3 つを使い回し、文字は点数が変わった時だけ書き換える
static class LoadingMiniGame
{
    private const float GroundY = -2.5f, PlayerX = -3.2f, PlayerSize = 0.3f, BoxWidth = 0.24f;
    private const float Gravity = 26f, JumpSpeed = 6.4f;
    private const int Boxes = 3;

    private static GameObject area;
    private static SpriteRenderer player;
    private static readonly SpriteRenderer[] box = new SpriteRenderer[Boxes];
    private static readonly float[] boxX = new float[Boxes];
    private static readonly float[] boxH = new float[Boxes];
    private static readonly bool[] boxOn = new bool[Boxes];
    private static readonly bool[] boxPassed = new bool[Boxes];
    private static TextMeshPro scoreText;
    private static float y, vy, nextBox, missUntil, halfWidth = 5.3f;
    private static int score, best = -1, shownScore = -1, shownBest = -1, shownMode = -1;
    // 前に遊んだ続きがあるか / 開き直した直後で、ジャンプするまで止めているか / Shift+F2 で開いているか
    private static bool started, waiting, overlay;
    private static readonly Color PlayerColor = new(1f, 0.52f, 0.85f, 1f);
    private static readonly Color MissColor = new(1f, 0.3f, 0.3f, 1f);
    // 自分の色のクルーの体を使えた時の見た目(使えない時はピンクの四角)
    private const float CrewHeight = 0.5f;
    private static bool crew;
    private static Vector2 crewScale = Vector2.one;
    private static float crewOffsetY;

    public static void Build(HudManager hud, Transform parent, int order)
    {
        area = new GameObject("MiniGame");
        area.layer = LayerMask.NameToLayer("UI");
        area.transform.SetParent(parent, false);
        StartLoadingScreen.Rect("Ground", area.transform, new Color(1f, 0.52f, 0.85f, 0.45f), new Vector3(0f, GroundY - 0.02f, -0.3f), new Vector2(40f, 0.04f), order);
        player = StartLoadingScreen.Rect("Player", area.transform, PlayerColor, Vector3.zero, new Vector2(PlayerSize, PlayerSize), order + 1);
        for (var i = 0; i < Boxes; i++)
        {
            box[i] = StartLoadingScreen.Rect("Box", area.transform, new Color(0.4f, 0.85f, 0.85f, 1f), Vector3.zero, Vector2.one, order + 1);
            box[i].gameObject.SetActive(false);
        }
        scoreText = StartLoadingScreen.Text("Score", hud, area.transform, 1.4f, new Vector3(0f, GroundY - 0.35f, -0.3f), order + 2);
        area.SetActive(false);
    }

    // 閉じても箱の位置や点数は残しておき、次に開いた時はジャンプするまで止めておく(ロードが終わった所から続けられる)
    public static void SetActive(bool on, bool asOverlay)
    {
        if (area == null) return;
        area.SetActive(on);
        if (!on) return;
        overlay = asOverlay;
        // 試合のカメラ(Camera.main)はズームや観戦で大きさが変わり、箱が画面のずっと外から出て来て見えなかったので、
        // 画面の部品を映すカメラの大きさ(縦の半分が 3)で測る
        if (Screen.height > 0) halfWidth = 3f * Screen.width / Screen.height;
        if (best < 0) best = Main.LoadingMiniGameBest?.Value ?? 0;
        UseOwnCrew();
        if (!started)
        {
            Restart();
            started = true;
            waiting = false;
        }
        else waiting = true;
        missUntil = 0f;
    }

    // 自分の体の絵と色をそのまま借りる(1回だけ。毎フレームは何もしない)
    private static void UseOwnCrew()
    {
        crew = false;
        defaultSprite ??= player.sprite;
        defaultMaterial ??= player.sharedMaterial;
        try
        {
            var pc = PlayerControl.LocalPlayer;
            var body = pc?.cosmetics?.currentBodySprite?.BodySprite;
            // 読み込み中は体の絵がまだ空のことがあり、そのまま借りるとキャラが見えなかった
            if (body == null || body.sprite == null || body.sprite.texture == null || pc.Data == null || body.sprite.bounds.size.y < 0.1f)
            {
                UseSquare();
                return;
            }
            player.sprite = body.sprite;
            player.material = new Material(body.sharedMaterial);
            PlayerMaterial.SetColors(pc.Data.DefaultOutfit.ColorId, player);
            var size = body.sprite.bounds.size;
            var k = CrewHeight / Mathf.Max(0.01f, size.y);
            crewScale = new Vector2(k, k);
            // 絵の中心が足元からどれだけ上にあるか(足を地面に付けるため)
            crewOffsetY = -body.sprite.bounds.min.y * k;
            crew = true;
        }
        catch { UseSquare(); }
    }

    private static Sprite defaultSprite;
    private static Material defaultMaterial;

    // 体の絵を借りられない時は、前に借りた絵や色を残さず、ピンクの四角に戻す
    private static void UseSquare()
    {
        crew = false;
        if (player == null) return;
        if (defaultSprite != null) player.sprite = defaultSprite;
        if (defaultMaterial != null) player.sharedMaterial = defaultMaterial;
    }

    public static void SaveBest()
    {
        if (best < 0 || Main.LoadingMiniGameBest == null || Main.LoadingMiniGameBest.Value >= best) return;
        Main.LoadingMiniGameBest.Value = best;
    }

    private static void Restart()
    {
        score = 0;
        y = vy = 0f;
        nextBox = 1f;
        for (var i = 0; i < Boxes; i++) { boxOn[i] = false; box[i].gameObject.SetActive(false); }
    }

    public static void Tick()
    {
        if (area == null || !area.activeSelf) return;
        var dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
        var now = Time.unscaledTime;
        var grounded = y <= 0f;
        // 試合中に開いている時は、W と ↑ は歩く操作なので使わない
        var jump = Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)
            || (!overlay && (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)));
        if (waiting)
        {
            if (!jump)
            {
                UpdateScoreText();
                return;
            }
            waiting = false;
        }
        if (grounded && jump)
            vy = JumpSpeed;
        vy -= Gravity * dt;
        y += vy * dt;
        if (y < 0f) { y = 0f; vy = 0f; }

        var speed = 3.6f + Mathf.Min(score * 0.12f, 3f);
        nextBox -= dt;
        if (nextBox <= 0f)
        {
            for (var i = 0; i < Boxes; i++)
            {
                if (boxOn[i]) continue;
                boxOn[i] = true;
                boxPassed[i] = false;
                boxX[i] = halfWidth + 0.4f;
                boxH[i] = UnityEngine.Random.Range(0.22f, 0.46f);
                box[i].transform.localScale = new Vector3(BoxWidth, boxH[i], 1f);
                box[i].gameObject.SetActive(true);
                break;
            }
            nextBox = UnityEngine.Random.Range(0.75f, 1.6f) * 4f / speed;
        }

        var hit = false;
        for (var i = 0; i < Boxes; i++)
        {
            if (!boxOn[i]) continue;
            boxX[i] -= speed * dt;
            box[i].transform.localPosition = new Vector3(boxX[i], GroundY + boxH[i] / 2f, -0.4f);
            if (Mathf.Abs(boxX[i] - PlayerX) < (BoxWidth + PlayerSize) / 2f - 0.04f && y < boxH[i] - 0.03f) hit = true;
            if (!boxPassed[i] && boxX[i] < PlayerX - PlayerSize)
            {
                boxPassed[i] = true;
                score++;
                if (score > best) best = score;
            }
            if (boxX[i] < -halfWidth - 0.5f)
            {
                boxOn[i] = false;
                box[i].gameObject.SetActive(false);
            }
        }
        if (hit)
        {
            missUntil = now + 0.4f;
            SaveBest();
            Restart();
        }

        // 跳んでいる間は少し縦に伸び、着地の直前は丸く見えるように
        var stretch = grounded ? 0f : Mathf.Clamp(vy * 0.015f, -0.08f, 0.12f);
        if (crew)
        {
            // 走っている間は小さく上下にはずむ
            var bob = grounded ? Mathf.Abs(Mathf.Sin(now * 14f)) * 0.03f : 0f;
            player.transform.localScale = new Vector3(crewScale.x * (1f - stretch), crewScale.y * (1f + stretch), 1f);
            player.transform.localPosition = new Vector3(PlayerX, GroundY + y + bob + crewOffsetY * (1f + stretch), -0.5f);
            player.color = now < missUntil ? MissColor : Color.white;
        }
        else
        {
            player.transform.localScale = new Vector3(PlayerSize * (1f - stretch), PlayerSize * (1f + stretch), 1f);
            player.transform.localPosition = new Vector3(PlayerX, GroundY + y + PlayerSize / 2f, -0.5f);
            player.color = now < missUntil ? MissColor : PlayerColor;
        }

        UpdateScoreText();
    }

    private static void UpdateScoreText()
    {
        var mode = (waiting ? 1 : 0) + (overlay ? 2 : 0);
        if (score == shownScore && best == shownBest && mode == shownMode) return;
        shownScore = score;
        shownBest = best;
        shownMode = mode;
        var help = waiting ? "スペース / クリックで続きから" : "スペース / クリックでジャンプ";
        var close = overlay ? "    Esc / Shift+F2 で閉じる" : "";
        scoreText.text = $"<color={UiSkin.DimHex}>{help}</color>    スコア {score}    ベスト {best}<color={UiSkin.DimHex}>{close}</color>";
    }
}

[HarmonyPatch(typeof(LoadingBarManager))]
static class StartLoadingScreenPatch
{
    // ゲーム開始の読み込みの時だけ、ふつうのバーの代わりに出す(起動時の読み込みは別の画面)
    [HarmonyPatch(nameof(LoadingBarManager.ToggleLoadingBar)), HarmonyPostfix]
    static void Toggle(LoadingBarManager __instance, [HarmonyArgument(0)] bool on)
    {
        try
        {
            if (!on) { StartLoadingScreen.Hide(); return; }
            if (!StartLoadingScreen.Enabled || HudManager.Instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.IsGameStarted) return;
            StartLoadingScreen.Show();
            if (__instance?.loadingBar != null) __instance.loadingBar.gameObject.SetActive(false);
        }
        catch { }
    }

    [HarmonyPatch(nameof(LoadingBarManager.SetLoadingPercent)), HarmonyPostfix]
    static void Percent(LoadingBarManager __instance, [HarmonyArgument(0)] float percent, [HarmonyArgument(1)] StringNames loadText)
    {
        try
        {
            // ゲーム開始の読み込みなので、まだ出ていなければここで出す。
            // ホストは「ほかの人を待っています」ではなく「ゲームを開始しています」から始まるので、両方で出す
            if (loadText is StringNames.LoadingBarGameStartWaitingPlayers or StringNames.LoadingBarGameStart && StartLoadingScreen.Enabled) StartLoadingScreen.Show();
            StartLoadingScreen.SetProgress(percent, __instance?.loadingBar?.loadingText?.text);
            if (StartLoadingScreen.IsShowing && __instance?.loadingBar != null) __instance.loadingBar.gameObject.SetActive(false);
        }
        catch { }
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class StartLoadingScreenTickPatch
{
    static void Postfix()
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.F2) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                StartLoadingScreen.TogglePlay();
            StartLoadingScreen.Tick();
        }
        catch { }
    }
}
