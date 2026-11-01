using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using TownOfHost.Roles.Core;
using Object = UnityEngine.Object;
using static TownOfHost.Translator;

namespace TownOfHost.Modules;

// ゲッサーの画面: MOD を入れているゲッサーは、会議中に投票の札の照準ボタンから役職を選んで撃てる
// 撃つ処理はチャットの /cmd bt と同じ所(ホスト)で行うので、判定や回数の制限は今まで通り
// Ported from SuperNewRoles (GPL-3.0): 会議画面の部品(PhoneUI・投票の札)を複製してボタンを作るやり方
public static class GuesserUi
{
    const int Columns = 5;
    const int PerPage = 40;
    static readonly CustomRoleTypes[] Teams = { CustomRoleTypes.Crewmate, CustomRoleTypes.Impostor, CustomRoleTypes.Madmate, CustomRoleTypes.Neutral };

    static readonly List<CustomRoles> roles = new();
    static readonly Dictionary<byte, GameObject> targetButtons = new();
    static bool enabled;
    static GameObject panel;
    static Sprite icon;

    // 押してから撃つまでの待ち(MOD を入れている人だけ有利にならないように)。その間は相手の札に照準と残り秒数が出る
    static byte aimTarget = byte.MaxValue;
    static CustomRoles aimRole;
    static float aimStart, aimUntil;
    static readonly List<GameObject> aimParts = new();
    static SpriteRenderer aimSight;
    static TextMeshPro aimText;
    static bool Aiming => aimTarget != byte.MaxValue;
    static float nextCheck;
    public static void Enable(List<CustomRoles> list)
    {
        roles.Clear();
        roles.AddRange(list);
        enabled = roles.Count > 0;
        Logger.Info($"ゲッサーの画面を使えるように({roles.Count}役職)", "GuesserUi");
    }

    public static void Disable()
    {
        enabled = false;
        ClearAim();
        Close();
        foreach (var go in targetButtons.Values) if (go != null) Object.Destroy(go);
        targetButtons.Clear();
    }

    static bool Usable(MeetingHud hud)
    {
        var me = PlayerControl.LocalPlayer;
        return me != null && me.Data != null && !me.Data.IsDead
            && hud.state is MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;
    }

    internal static void Update(MeetingHud hud)
    {
        if (!enabled) return;
        if (!Usable(hud))
        {
            // 会議の始まりの演出中は、まだ札を押せないだけなので待つ。
            // (ここで止めていたので、一覧が届く 1 秒後には演出中で、照準ボタンが一度も出ていなかった)
            var me = PlayerControl.LocalPlayer;
            if (hud.state == MeetingHud.MeetingStates.Animating && me != null && me.Data != null && !me.Data.IsDead) return;
            Disable();
            return;
        }
        if (Aiming) StepAim(hud);
        // 札のボタンの出し入れは 1 秒に 5 回確かめれば十分
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        foreach (var pva in hud.playerStates)
        {
            if (pva == null || pva.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
            targetButtons.TryGetValue(pva.PlayerId, out var go);
            if (pva.AmDead || Aiming)
            {
                if (go != null && go.activeSelf) go.SetActive(false);
                continue;
            }
            if (go == null) targetButtons[pva.PlayerId] = MakeTargetButton(hud, pva);
            else if (!go.activeSelf) go.SetActive(true);
        }
    }

    static PlayerVoteArea AreaOf(MeetingHud hud, byte id)
    {
        foreach (var pva in hud.playerStates) if (pva != null && pva.PlayerId == id) return pva;
        return null;
    }

    static void StartAim(MeetingHud hud, byte targetId, CustomRoles role)
    {
        var delay = Options.ClientEffectsGuessUiDelay?.GetFloat() ?? 0f;
        var area = AreaOf(hud, targetId);
        if (delay <= 0f || area == null)
        {
            Shoot(targetId, role);
            return;
        }
        ClearAim();
        aimTarget = targetId;
        aimRole = role;
        aimStart = Time.time;
        aimUntil = Time.time + delay;
        nextCheck = 0f;
        var go = new GameObject("GuessAim");
        go.transform.SetParent(area.transform, false);
        go.layer = area.gameObject.layer;
        go.transform.localPosition = new Vector3(0f, 0f, -6f);
        aimSight = go.AddComponent<SpriteRenderer>();
        aimSight.sprite = Icon;
        aimParts.Add(go);
        aimText = Object.Instantiate(area.NameText, area.transform);
        aimText.name = "GuessAimText";
        aimText.alignment = TextAlignmentOptions.Center;
        aimText.enableWordWrapping = false;
        aimText.transform.localPosition = new Vector3(0.9f, 0f, -6.1f);
        aimText.color = new Color(1f, 0.3f, 0.3f);
        aimParts.Add(aimText.gameObject);
    }

    static void StepAim(MeetingHud hud)
    {
        var area = AreaOf(hud, aimTarget);
        if (area == null || area.AmDead || aimSight == null || aimText == null) { ClearAim(); return; }
        var t = Time.time - aimStart;
        var left = aimUntil - Time.time;
        if (left <= 0f)
        {
            var target = aimTarget;
            var role = aimRole;
            ClearAim();
            Shoot(target, role);
            return;
        }
        // 外から絞り込みながら、ゆっくり回る。最後の 1 秒は速く点滅する
        var close = Mathf.Clamp01(t / Mathf.Max(0.3f, aimUntil - aimStart));
        aimSight.transform.localScale = Vector3.one * Mathf.Lerp(2.2f, 1.2f, 1f - (1f - close) * (1f - close));
        aimSight.transform.localRotation = Quaternion.Euler(0f, 0f, t * 30f);
        aimSight.color = new Color(1f, 1f, 1f, left < 1f ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 14f)) : 0.9f);
        var text = Mathf.CeilToInt(left).ToString();
        if (aimText.text != text) aimText.text = text;
    }

    static void ClearAim()
    {
        aimTarget = byte.MaxValue;
        foreach (var go in aimParts) if (go != null) Object.Destroy(go);
        aimParts.Clear();
        aimSight = null;
        aimText = null;
    }

    static Sprite Icon
    {
        get
        {
            if (icon != null) return icon;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size * 2f - 1f;
                    var v = (y + 0.5f) / size * 2f - 1f;
                    var d = Mathf.Sqrt(u * u + v * v);
                    // 暗い札の上でも見えるように、白い丸の上に太い赤のスコープ(丸と十字)を描き、外側を黒でふちどる
                    var disc = 1f - Mathf.SmoothStep(0.9f, 0.98f, d);
                    var edge = Mathf.SmoothStep(0.8f, 0.86f, d);
                    var ring = 1f - Mathf.Clamp01((Mathf.Abs(d - 0.56f) - 0.07f) / 0.03f);
                    var line = Mathf.Min(Mathf.Abs(u), Mathf.Abs(v));
                    var cross = d < 0.78f && d > 0.14f ? 1f - Mathf.Clamp01((line - 0.06f) / 0.03f) : 0f;
                    var dot = 1f - Mathf.Clamp01((d - 0.07f) / 0.03f);
                    var mark = Mathf.Max(ring, Mathf.Max(cross, dot));
                    var c = Color.Lerp(Color.Lerp(Color.white, new Color(0.08f, 0.08f, 0.1f), edge), new Color(0.95f, 0.08f, 0.1f), mark * (1f - edge));
                    pixels[y * size + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(disc * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            tex.hideFlags |= HideFlags.HideAndDontSave;
            icon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / 0.62f);
            icon.hideFlags |= HideFlags.HideAndDontSave;
            return icon;
        }
    }

    static GameObject MakeTargetButton(MeetingHud hud, PlayerVoteArea pva)
    {
        // 取り消しボタンを複製すると、元の見た目や色を変える部品が残って照準が見えなかったので、新しく作る
        var go = new GameObject("GuessButton") { layer = pva.gameObject.layer };
        go.transform.SetParent(pva.transform, false);
        go.transform.localPosition = new Vector3(-0.95f, 0.03f, -1.3f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Icon;
        sr.color = Color.white;
        // 札の背景と同じ描く順番・マスクにそろえて、札より手前に出す
        var back = pva.Background;
        if (back != null)
        {
            sr.sortingLayerID = back.sortingLayerID;
            sr.sortingOrder = back.sortingOrder + 50;
            sr.maskInteraction = back.maskInteraction;
        }
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.62f, 0.62f);
        var id = pva.PlayerId;
        var button = go.AddComponent<PassiveButton>();
        button.OnClick = new();
        button.OnMouseOver = new();
        button.OnMouseOut = new();
        button.OnClick.AddListener((Action)(() => Open(hud, id)));
        button.OnMouseOver.AddListener((Action)(() => sr.color = new Color(1f, 0.75f, 0.75f)));
        button.OnMouseOut.AddListener((Action)(() => sr.color = Color.white));
        return go;
    }

    static void SetVoteAreas(MeetingHud hud, bool active)
    {
        foreach (var pva in hud.playerStates)
            if (pva != null && pva.gameObject.activeSelf != active) pva.gameObject.SetActive(active);
    }

    static void Close()
    {
        if (panel == null) return;
        Object.Destroy(panel);
        panel = null;
        if (MeetingHud.Instance != null) SetVoteAreas(MeetingHud.Instance, true);
    }

    sealed class UiButton
    {
        public Transform root;
        public SpriteRenderer back;
        public TextMeshPro label;
        public PassiveButton passive;
    }

    static UiButton MakeButton(Transform parent, PlayerVoteArea sample, Vector3 pos, Vector2 scale, string text)
    {
        var holder = new GameObject("GuessUiButton").transform;
        holder.SetParent(parent, false);
        holder.localPosition = pos;
        holder.localScale = new Vector3(scale.x, scale.y, 1f);
        var button = Object.Instantiate(sample.transform.Find("votePlayerBase"), holder);
        button.Find("ControllerHighlight")?.gameObject.SetActive(false);
        Object.Instantiate(sample.transform.Find("MaskArea"), holder);
        var label = Object.Instantiate(sample.NameText, button);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.transform.localPosition = new Vector3(0f, 0f, label.transform.localPosition.z);
        var len = text.RemoveHtmlTags().Length;
        label.transform.localScale *= 1.6f * Mathf.Min(1f, 7f / Mathf.Max(7, len));
        var passive = button.GetComponent<PassiveButton>();
        passive.OnClick = new();
        passive.ClickSound = null;
        return new UiButton { root = holder, back = button.GetComponent<SpriteRenderer>(), label = label, passive = passive };
    }

    static string TeamName(CustomRoleTypes team) => GetString(team switch
    {
        CustomRoleTypes.Impostor => "GuessUiImpostor",
        CustomRoleTypes.Madmate => "GuessUiMadmate",
        CustomRoleTypes.Neutral => "GuessUiNeutral",
        _ => "GuessUiCrewmate",
    });

    static Color TeamColor(CustomRoleTypes team) => team switch
    {
        CustomRoleTypes.Impostor => Palette.ImpostorRed,
        CustomRoleTypes.Madmate => new Color(1f, 0.36f, 0f),
        CustomRoleTypes.Neutral => new Color(0.6f, 0.6f, 0.6f),
        _ => new Color(0.55f, 0.85f, 1f),
    };

    static void Open(MeetingHud hud, byte targetId)
    {
        if (!enabled || panel != null || Aiming || !Usable(hud) || hud.playerStates.Length == 0) return;
        var target = PlayerCatch.GetPlayerById(targetId);
        if (target == null || target.Data == null || target.Data.IsDead) return;
        var prototype = hud.transform.Find("MeetingContents/PhoneUI");
        if (prototype == null) return;
        var sample = hud.playerStates[0];
        var container = Object.Instantiate(prototype, hud.transform);
        container.localPosition = new Vector3(0f, 0f, -200f);
        panel = container.gameObject;
        SetVoteAreas(hud, false);

        var byTeam = new Dictionary<CustomRoleTypes, List<UiButton>>();
        foreach (var team in Teams) byTeam[team] = new List<UiButton>();
        var tabs = new Dictionary<CustomRoleTypes, UiButton>();
        var team0 = CustomRoleTypes.Crewmate;
        var page = 0;
        UiButton selected = null;
        CustomRoles selectedRole = CustomRoles.NotAssigned;
        UiButton prev = null, next = null, shoot = null;

        var title = Object.Instantiate(sample.NameText, container);
        title.alignment = TextAlignmentOptions.Center;
        title.enableWordWrapping = false;
        title.transform.localPosition = new Vector3(-0.6f, -2.2f, -200f);
        title.text = string.Format(GetString("GuessUiTarget"), Utils.ColorString(Palette.PlayerColors[target.Data.DefaultOutfit.ColorId], target.Data.PlayerName));

        var exit = MakeButton(container, sample, new Vector3(3.2f, 2.225f, -200f), new Vector2(0.25f, 0.9f), "");
        exit.back.sprite = sample.Buttons.transform.Find("CancelButton").GetComponent<SpriteRenderer>().sprite;
        exit.passive.OnClick.AddListener(new Action(Close));

        for (var i = 0; i < Teams.Length; i++)
        {
            var team = Teams[i];
            var tab = MakeButton(container, sample, new Vector3(-3.2f + i * 1.55f, 2.225f, -200f), new Vector2(0.5f, 0.55f), Utils.ColorString(TeamColor(team), TeamName(team)));
            tab.passive.OnClick.AddListener(new Action(() => { team0 = team; page = 0; Refresh(); }));
            tabs[team] = tab;
        }

        prev = MakeButton(container, sample, new Vector3(-3.47f, -2.2f, -200f), new Vector2(0.55f, 0.55f), GetString("GuessUiPrev"));
        next = MakeButton(container, sample, new Vector3(3.53f, -2.2f, -200f), new Vector2(0.55f, 0.55f), GetString("GuessUiNext"));
        prev.passive.OnClick.AddListener(new Action(() => { if (page > 0) { page--; Refresh(); } }));
        // 役職を選んでから押す「撃つ」ボタン
        shoot = MakeButton(container, sample, new Vector3(1.78f, -2.2f, -200f), new Vector2(0.55f, 0.55f), Utils.ColorString(new Color(1f, 0.25f, 0.25f), GetString("GuessUiShoot")));
        shoot.passive.OnClick.AddListener(new Action(() =>
        {
            if (selectedRole == CustomRoles.NotAssigned) return;
            var chosen = selectedRole;
            Close();
            StartAim(hud, targetId, chosen);
        }));
        next.passive.OnClick.AddListener(new Action(() => { if ((page + 1) * PerPage < byTeam[team0].Count) { page++; Refresh(); } }));

        foreach (var role in roles)
        {
            var team = role.GetCustomRoleTypes();
            if (!byTeam.TryGetValue(team, out var list)) list = byTeam[CustomRoleTypes.Neutral];
            var slot = list.Count % PerPage;
            var b = MakeButton(container, sample, new Vector3(-3.47f + 1.75f * (slot % Columns), 1.5f - 0.45f * (slot / Columns), -200f), new Vector2(0.55f, 0.55f),
                Utils.ColorString(UtilsRoleText.GetRoleColor(role), GetString(role.ToString())));
            var r = role;
            b.passive.OnClick.AddListener(new Action(() =>
            {
                if (selected != b)
                {
                    selected = b;
                    selectedRole = r;
                    title.text = string.Format(GetString("GuessUiTarget"), Utils.ColorString(Palette.PlayerColors[target.Data.DefaultOutfit.ColorId], target.Data.PlayerName))
                        + "  " + GetString("GuessUiConfirm");
                    Refresh();
                    return;
                }
                var chosen = selectedRole;
                Close();
                StartAim(hud, targetId, chosen);
            }));
            list.Add(b);
        }

        void Refresh()
        {
            foreach (var pair in byTeam)
            {
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var b = pair.Value[i];
                    b.root.gameObject.SetActive(pair.Key == team0 && i / PerPage == page);
                    b.back.color = b == selected ? new Color(1f, 0.35f, 0.35f) : Color.white;
                }
                tabs[pair.Key].back.color = pair.Key == team0 ? Color.white : new Color(1f, 1f, 1f, pair.Value.Count == 0 ? 0.15f : 0.4f);
            }
            var count = byTeam[team0].Count;
            shoot.root.gameObject.SetActive(selected != null);
            prev.root.gameObject.SetActive(page > 0);
            next.root.gameObject.SetActive((page + 1) * PerPage < count);
        }

        container.localScale *= 0.75f;
        Refresh();
    }

    static void Shoot(byte targetId, CustomRoles role)
    {
        if (role == CustomRoles.NotAssigned) return;
        var command = $"/cmd /bt {targetId} #{(int)role}";
        if (AmongUsClient.Instance.AmHost) GuessManager.GuesserMsg(PlayerControl.LocalPlayer, command);
        else ChatCommands.RequestCommandProcessingFromHost(command);
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
static class GuesserUiStartPatch
{
    static void Postfix()
    {
        GuesserUi.Disable();
        if (AmongUsClient.Instance.AmHost) _ = new LateTask(ClientEffects.SendGuessUi, 1f, "GuesserUi", true);
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
static class GuesserUiUpdatePatch
{
    static void Postfix(MeetingHud __instance)
    {
        try { GuesserUi.Update(__instance); }
        catch (Exception e) { Logger.Warn(e.Message, "GuesserUi"); }
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
static class GuesserUiDestroyPatch
{
    static void Postfix() => GuesserUi.Disable();
}
