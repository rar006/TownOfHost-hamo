using System.Collections.Generic;
using UnityEngine;

namespace TownOfHost.Modules;

public sealed class TextDummy : CustomNetObject
{
    private static readonly Vector2 ParkPos = new(50f, 50f);
    private static readonly Dictionary<string, Vector2> offsets = new();
    private static readonly Vector2 DefaultOffset = new(0f, 0.6f);

    private bool created;
    private bool removed;
    private string art = "";
    private string shownName;
    private Vector2 wanted = ParkPos;
    private bool parked = true;
    private Vector2 wantedPoint;

    public bool HiddenForHost { get; set; }

    public bool IsReady => created && !removed;

    public TextDummy() => CreateNetObject(ParkPos);

    [Attributes.GameModuleInitializer]
    public static void Init() => offsets.Clear();

    protected override void OnCreated()
    {
        if (removed) { Despawn(); return; }
        created = true;
        SetAppearance(colorId: 0, skinId: "", hatId: "", petId: "", visorId: "");
        SetName("");
        shownName = "";
        if (!parked) wanted = wantedPoint - OffsetOf(art);
        SnapToPosition(wanted);
        Apply();
        _ = new LateTask(HideBody, 0.3f, "TextDummy.HideBody", true);
    }

    public void Show(string text)
    {
        art = text ?? "";
        if (IsReady) Apply();
    }

    public void MoveTo(Vector2 point)
    {
        parked = false;
        wantedPoint = point;
        wanted = point - OffsetOf(art);
        if (!IsReady) return;
        if (Vector2.Distance(Position, wanted) < 0.02f) return;
        SnapToPosition(wanted);
    }

    public void Park()
    {
        art = "";
        wanted = ParkPos;
        if (!IsReady) return;
        Apply();
        if (parked && Vector2.Distance(Position, ParkPos) < 0.02f) return;
        parked = true;
        SnapToPosition(ParkPos);
    }

    public void Remove()
    {
        if (removed) return;
        removed = true;
        if (created) Despawn();
    }

    public override void OnMeeting() => Park();

    private void Apply()
    {
        if (art == shownName) return;
        shownName = art;
        SetName(art);
        HideBody();
    }

    private Vector2 OffsetOf(string text)
    {
        if (string.IsNullOrEmpty(text)) return DefaultOffset;
        if (offsets.TryGetValue(text, out var o)) return o;
        o = DefaultOffset;
        try
        {
            var name = PlayerControl?.cosmetics?.nameText;
            if (name == null) return DefaultOffset;
            {
                var before = name.text;
                name.text = text;
                name.ForceMeshUpdate(true, true);
                o = (Vector2)name.transform.TransformPoint(name.textBounds.center) - (Vector2)PlayerControl.transform.position;
                name.text = before;
            }
        }
        catch (System.Exception e) { Logger.Warn($"絵の位置を測れませんでした: {e.Message}", "TextDummy"); }
        offsets[text] = o;
        return o;
    }

    private void HideBody()
    {
        if (PlayerControl == null) return;
        try
        {
            foreach (var r in PlayerControl.GetComponentsInChildren<SpriteRenderer>(true)) r.enabled = false;
            PlayerControl.cosmetics.colorBlindText.color = Color.clear;
            if (HiddenForHost) PlayerControl.cosmetics.nameText.gameObject.SetActive(false);
        }
        catch { }
    }
}
