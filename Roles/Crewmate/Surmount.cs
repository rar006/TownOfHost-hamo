using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Impostor;

namespace TownOfHost.Roles.Crewmate;

public sealed class Surmount : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Surmount),
            player => new Surmount(player),
            CustomRoles.Surmount,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            87100,
            null,
            "smt",
            "#7fc8d6",
            (3, 5),
            false,
            tab: TabGroup.Combinations,
            combination: CombinationRoles.TraumaandSurmount,
            isNewRole: true
        );

    public static Surmount Instance { get; private set; }

    public Surmount(PlayerControl player) : base(RoleInfo, player)
    {
        Instance = this;
        givenCount = 0;
    }
    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    int givenCount;

    Trauma FindTrauma() => CustomRoleManager.AllActiveRoles.Values.OfType<Trauma>().FirstOrDefault();

    public override bool OnCompleteTask(uint taskid)
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return true;
        var trauma = FindTrauma();
        if (trauma == null) return true;
        var ts = MyTaskState;
        var type = Trauma.DebuffForTask(ts.CompletedTasksCount, ts.AllTasksCount);
        trauma.ReceiveDebuff(type);
        givenCount++;
        SendRPC();
        Player.MarkDirtySettings(); // 自分の視界が戻る
        return true;
    }

    int RemainingVisionDebuffs
    {
        get
        {
            var ts = MyTaskState;
            var all = ts?.AllTasksCount ?? 0;
            var count = 0;
            for (int i = givenCount + 1; i <= all; i++)
                if (Trauma.DebuffForTask(i, all) == Trauma.DebuffType.Vision) count++;
            return count;
        }
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        var steps = RemainingVisionDebuffs;
        if (steps <= 0) return;
        var normal = Main.DefaultCrewmateVision;
        var v = normal;
        for (int i = 0; i < steps; i++) v *= 0.85f; // 自分が持っているデバフは少しだけ(トラウマに与える分ほど強くはしない)
        opt.SetFloat(FloatOptionNames.CrewLightMod, Mathf.Max(v, normal * 0.3f));
    }

    // ===== 死亡: トラウマに知らせる =====
    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        var trauma = FindTrauma();
        if (trauma == null) return;
        var killer = info.AttemptKiller;
        trauma.OnSurmountDead(killedByMe: killer != null && killer.PlayerId == trauma.Player.PlayerId, exiled: false);
    }

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (exiled == null || exiled.PlayerId != Player.PlayerId) return;
        FindTrauma()?.OnSurmountDead(killedByMe: false, exiled: true);
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        var steps = RemainingVisionDebuffs;
        return steps > 0 ? Utils.ColorString(RoleInfo.RoleColor, $"[{steps}]") : "";
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(givenCount);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        givenCount = reader.ReadInt32();
    }
}
