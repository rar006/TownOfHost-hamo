using UnityEngine;

using TownOfHost.Modules;

namespace TownOfHost.Roles.Impostor;

// デストロイヤーがつぶした場所に残す痕跡。ヴァンパイアの血痕と同じく、ダミーの名前欄に印を出す
// (MOD を入れている人には床の血だまりを描いて、ダミーの印は隠す)
public sealed class DestroyerTrace : CustomNetObject
{
    private readonly Vector2 _spawnPos;
    private int _turnsLeft;

    public bool IsAlive { get; private set; } = true;

    public DestroyerTrace(Vector2 spawnPos, int turns)
    {
        _spawnPos = spawnPos;
        _turnsLeft = turns;
        CreateNetObject(_spawnPos);
    }

    protected override void OnCreated()
    {
        SetAppearance(colorId: 6, skinId: "", hatId: "", petId: "", visorId: "");
        SetName("<size=170%><color=#5c0000>✷</color></size>\n<size=60%><color=#8b0000>" + Translator.GetString("DestroyerTraceMark") + "</color></size>");
        SnapToPosition(_spawnPos);
        try { if (PlayerControl != null) ClientEffects.BloodStain(PlayerControl.NetId, _spawnPos); } catch { }
        Logger.Info($"DestroyerTrace: OnCreated position={_spawnPos} turns={_turnsLeft}", "Destroyer");
    }

    // 1 ターン経過。消えたら true
    public bool PassTurn()
    {
        if (!IsAlive) return true;
        if (--_turnsLeft > 0) return false;
        Remove();
        return true;
    }

    public void Remove()
    {
        if (!IsAlive) return;
        IsAlive = false;
        Despawn();
    }
}
