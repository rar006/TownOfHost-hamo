using System.Collections.Generic;
using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== ファラオ (Pharaoh) =====
// イントロ：奪う者に天罰を、余に勝利を
// 陣営：ニュートラル(置き換え：インポスター)、生存人数にカウントされない
//
// サボタージュを直せない。
// ゲーム開始時に設定カウントを持つ。カウントが0の状態で会議に入ると自殺する。
// 会議中に自分に投票されると、カウントが減少する代わりに投票者にマークがつく。
// マークを持つプレイヤーしかキルできないが、マーク持ちをキルするとカウントが1増加する。
// マークが無いプレイヤーより多い人数から投票され、かつ追放されなかった場合、
// カウントの数だけ投票者からランダムにマークをつける。
//
// 生存していれば追加勝利する(IAdditionalWinner)。
public sealed class Pharaoh : RoleBase, IKiller, ISystemTypeUpdateHook, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Pharaoh),
            player => new Pharaoh(player),
            CustomRoles.Pharaoh,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            85600,
            SetupOptionItem,
            "pha",
            "#A68B2B",
            (4, 23),
            true,
            countType: CountTypes.OutOfGame,
            from: From.TownOfHost_hamo
        );

    public Pharaoh(PlayerControl player) : base(RoleInfo, player)
    {
        count = OptionInitialCount.GetInt();
        Instances[player.PlayerId] = this;
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionInitialCount;
    static OptionItem OptionMarkExpireMeetings;

    enum OptionName
    {
        PharaohKillCooldown,
        PharaohInitialCount,
        PharaohMarkExpireMeetings,
    }

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.PharaohKillCooldown,
            new(0.5f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionInitialCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.PharaohInitialCount,
            new(1, 99, 1), 4, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMarkExpireMeetings = IntegerOptionItem.Create(RoleInfo, 12, OptionName.PharaohMarkExpireMeetings,
            new(1, 99, 1), 2, false)
            .SetValueFormat(OptionFormat.Turns);
    }

    // 現在のカウント
    private int count;
    // マークを持つプレイヤーIDと、消滅までの残り会議数
    private readonly Dictionary<byte, int> markedPlayers = new();

    // 静的にファラオのインスタンスを引けるようにしておく(全プレイヤー総当たりの投票判定から使うため)
    private static readonly Dictionary<byte, Pharaoh> Instances = new();

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        Instances.Clear();
        CustomRoleManager.MarkOthers.Add(GetMarkOthers);
    }

    // ===== キル制限: マークを持つ相手にしかキルできない =====
    bool IKiller.CanKill => true;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseImpostorVentButton() => true;
    public bool CanUseSabotageButton() => false;
    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.AttemptTarget == null) return;
        if (!markedPlayers.ContainsKey(info.AttemptTarget.PlayerId))
        {
            // マークの無い相手はキルできない
            info.CanKill = false;
            return;
        }
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (info.AttemptTarget == null) return;
        if (markedPlayers.Remove(info.AttemptTarget.PlayerId))
        {
            count++;
            UtilsGameLog.AddGameLog("Pharaoh",
                $"{UtilsName.GetPlayerColor(Player)}がマーク持ちの{UtilsName.GetPlayerColor(info.AttemptTarget)}をキルし、カウントが増加した(現在:{count})");
        }
    }

    // ===== サボタージュを直せない =====
    public bool UpdateReactorSystem(ReactorSystemType reactorSystem, byte amount) => !Player.IsAlive();
    public bool UpdateHeliSabotageSystem(HeliSabotageSystem heliSabotageSystem, byte amount) => !Player.IsAlive();
    public bool UpdateLifeSuppSystem(LifeSuppSystemType lifeSuppSystem, byte amount) => !Player.IsAlive();
    public bool UpdateHudOverrideSystem(HudOverrideSystemType hudOverrideSystem, byte amount) => !Player.IsAlive();
    public bool UpdateHqHudSystem(HqHudSystemType hqHudSystemType, byte amount) => !Player.IsAlive();
    public bool UpdateSwitchSystem(SwitchSystem switchSystem, byte amount) => !Player.IsAlive();
    public bool UpdateDoorsSystem(DoorsSystemType doorsSystem, byte amount) => true;

    // ===== 会議開始時: カウント0なら自殺 =====
    public override void OnStartMeeting()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;

        if (count <= 0)
        {
            UtilsGameLog.AddGameLog("Pharaoh",
                $"{UtilsName.GetPlayerColor(Player)}はカウントが尽きて自滅した");
            Player.RpcMurderPlayer(Player);
        }
    }

    /// <summary>
    /// 誰かがファラオに投票した瞬間に呼ばれる。カウントを減らす代わりに、投票者にマークをつける。
    /// MeetingHudPatch.CastVotePatch から、ファラオ自身の役職クラスに対して呼ばれる
    /// (投票先が自分で、かつ投票した本人が自分以外の時だけ処理する)。
    /// </summary>
    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (votedForId != Player.PlayerId) return true;
        if (voter == null || voter.PlayerId == Player.PlayerId) return true;
        if (!Player.IsAlive()) return true;

        if (count > 0)
        {
            count--;
        }

        markedPlayers[voter.PlayerId] = OptionMarkExpireMeetings.GetInt();

        UtilsGameLog.AddGameLog("Pharaoh",
            $"{UtilsName.GetPlayerColor(voter)}がファラオに投票し、マークが付与された(残りカウント:{count})");

        return true; // 投票自体は成立させる
    }

    // ===== 会議終了時: マークの残り会議数を減らす。追放されなかった場合の追加マーク付与もここで判定する =====
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        // マークの残り会議数を減らし、期限が来たものを解除する
        var expired = new List<byte>();
        var keys = new List<byte>(markedPlayers.Keys);
        foreach (var id in keys)
        {
            markedPlayers[id]--;
            if (markedPlayers[id] <= 0)
                expired.Add(id);
        }
        foreach (var id in expired)
            markedPlayers.Remove(id);

        // 自分が追放されずに生き残っていて、規定数より多い人数から投票されていた場合、
        // 投票者からランダムにカウント分だけマークをつける。
        if (!Player.IsAlive()) return;
        if (count <= 0) return;

        var votersForMe = new List<byte>();
        foreach (var kv in Main.LastMeetingVotedFor)
        {
            if (kv.Value == Player.PlayerId && kv.Key != Player.PlayerId)
                votersForMe.Add(kv.Key);
        }
        if (votersForMe.Count <= count) return;

        var pool = new List<byte>(votersForMe);
        var rng = new System.Random();
        int markCount = System.Math.Min(count, pool.Count);
        for (int i = 0; i < markCount; i++)
        {
            int idx = rng.Next(pool.Count);
            var id = pool[idx];
            pool.RemoveAt(idx);
            markedPlayers[id] = OptionMarkExpireMeetings.GetInt();
        }

        UtilsGameLog.AddGameLog("Pharaoh",
            $"{UtilsName.GetPlayerColor(Player)}に規定数以上の投票が集まったが追放されず、ランダムに{markCount}人へマークが付与された");
    }

    /// <summary>
    /// 全プレイヤーの画面で共通表示される印。CustomRoleManager.MarkOthersに登録して使う。
    /// </summary>
    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        foreach (var pharaoh in Instances.Values)
        {
            if (!pharaoh.Player.IsAlive()) continue;
            if (pharaoh.markedPlayers.ContainsKey(seen.PlayerId))
                return Utils.ColorString(RoleInfo.RoleColor, "☥");
        }
        return "";
    }

    // ===== 勝利判定: 生存していれば追加勝利 =====
    // IAdditionalWinnerを実装していなかったため、ファラオが生存していても
    // ゲーム終了時の勝利判定処理(CheckGameEndPatch.cs)から一切呼ばれず、
    // 常に勝利できないバグになっていた。
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;

        winnerRole = CustomRoles.Pharaoh;
        return true;
    }
}
