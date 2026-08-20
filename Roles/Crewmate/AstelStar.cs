using TownOfHost.Modules;
using UnityEngine;

namespace TownOfHost.Roles.Crewmate;

public sealed class AstelStar : CustomNetObject
{
    private readonly Vector2 _spawnPos;
    private float _lifeTime;
    private readonly byte _ownerId;

    public bool IsAlive { get; private set; } = true;

    public AstelStar(PlayerControl owner, float lifeTimeSeconds)
    {
        _spawnPos = owner.transform.position;
        _lifeTime = lifeTimeSeconds;
        _ownerId = owner.PlayerId;
        CreateNetObject(_spawnPos);
    }

    protected override void OnCreated()
    {
        // 波動砲の星と同じ 800% サイズで表示する。
        SetAppearance(colorId: 12, skinId: "", hatId: "", petId: "", visorId: "");
        SetName("<size=800%><color=#ffe066>★</color></size>");
        SnapToPosition(_spawnPos);
    }

    /// <summary>
    /// 毎フレーム呼び出す。設定秒数が経過したら消滅させる。
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsAlive) return;

        var owner = _ownerId.GetPlayerControl();
        if (owner == null)
        {
            Remove();
            return;
        }

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
