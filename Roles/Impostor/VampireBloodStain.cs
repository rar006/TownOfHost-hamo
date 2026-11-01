using UnityEngine;

using TownOfHost.Modules;

namespace TownOfHost.Roles.Impostor;

/// <summary>
/// ヴァンパイヤが噛んだ場所に残す血痕エフェクト。
/// AstelStar(星召喚)と同じ仕組みを流用し、新規画像アセットを追加せず、
/// ダミープレイヤーの名前欄に小さい赤い●を表示するだけの軽量な実装にしている。
/// </summary>
public sealed class VampireBloodStain : CustomNetObject
{
    private readonly Vector2 _spawnPos;
    private float _lifeTime;

    public bool IsAlive { get; private set; } = true;

    public VampireBloodStain(Vector2 spawnPos, float lifeTimeSeconds)
    {
        _spawnPos = spawnPos;
        _lifeTime = lifeTimeSeconds;
        CreateNetObject(_spawnPos);
    }

    protected override void OnCreated()
    {
        // 星召喚(800%)よりだいぶ小さいサイズで、血痕らしく足元にポツンと出す。
        SetAppearance(colorId: 6, skinId: "", hatId: "", petId: "", visorId: "");
        SetName("<size=150%><color=#8b0000>●</color></size>");
        SnapToPosition(_spawnPos);
        // MOD の人には床の血だまりを描いて、ダミーの●は隠す
        try { if (PlayerControl != null) ClientEffects.BloodStain(PlayerControl.NetId, _spawnPos); } catch { }
        Logger.Info($"VampireBloodStain: OnCreated完了 position={_spawnPos}", "Vampire.Bloodstain");
    }

    /// <summary>毎フレーム呼び出す。設定秒数が経過したら消滅させる。</summary>
    public void Tick(float deltaTime)
    {
        if (!IsAlive) return;

        _lifeTime -= deltaTime;
        if (_lifeTime <= 0f)
        {
            Remove();
        }
    }

    public void Remove()
    {
        if (!IsAlive) return;
        IsAlive = false;
        Despawn();
    }
}
