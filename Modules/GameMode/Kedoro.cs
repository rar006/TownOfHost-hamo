using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;

namespace TownOfHost;

/// <summary>
/// ケイドロモード。鬼(インポスター)と逃走者(エンジニア)に分かれる。
/// ・鬼のキルは「捕獲」になり、逃走者は牢屋(カスタムスポーン位置。2つ以上ならランダム)へ送られる
/// ・逃走者は捕まった人の近くでペットを撫でると解放できる(クールダウンあり)
/// ・解放した人/された人には、一定時間スピードアップとシールドが付く
/// ・逃走者はタスクを全部終えるとシールド1枚とスピードアップが付く
/// ・逃走者全体のタスク進捗率に応じて、鬼のキルクールが短くなり、スピードが上がる
/// </summary>
public static class Kedoro
{
    /// <summary>タスク全完了のごほうびのスピード倍率(設定項目なし)</summary>
    private const float TaskCompleteSpeedMultiplier = 1.25f;
    /// <summary>牢屋からこれ以上離れたら連れ戻す距離</summary>
    private const float JailLeashDistance = 0.9f; // 檻の中だけ動ける
    private const float RescueRange = 2.5f; // 牢屋(または捕まっている人)からこの距離以内
    private const float TickInterval = 0.5f;

    private static readonly HashSet<byte> Oni = new();
    private static readonly HashSet<byte> Runners = new();
    /// <summary>捕まっている逃走者 → 入れられている牢屋の位置</summary>
    private static readonly Dictionary<byte, Vector2> Captured = new();
    private static readonly Dictionary<byte, float> LastRescue = new();
    private static readonly List<Vector2> JailPositions = new();
    /// <summary>牢屋の見た目(ジェイラーと同じ檻)</summary>
    private static readonly List<TownOfHost.Roles.Crewmate.PrisonNetObject> JailObjects = new();
    private static bool jailObjectsSpawned;
    /// <summary>時間制限つきのスピード倍率(倍率, 終わる時刻)</summary>
    private static readonly Dictionary<byte, List<(float Mult, float Until)>> SpeedBuffs = new();
    /// <summary>シールド1枚ごとの有効期限(float.MaxValue = 期限なし)</summary>
    private static readonly Dictionary<byte, List<float>> Shields = new();
    private static readonly HashSet<byte> TaskCompleted = new();
    private static readonly Dictionary<byte, float> SentSpeed = new();
    private static float sentOniKillCooldown = -1f;
    private static float tickTimer;
    private static bool SabotageTimerStarted;
    private static float SabotageElapsed;

    public static bool IsActive => Options.IsKedoro;
    public static bool IsOni(PlayerControl pc) => pc != null && Oni.Contains(pc.PlayerId);
    public static bool IsRunner(PlayerControl pc) => pc != null && Runners.Contains(pc.PlayerId);
    public static bool IsCaptured(PlayerControl pc) => pc != null && Captured.ContainsKey(pc.PlayerId);

    private static float BaseSpeed => Main.RealOptionsData?.GetFloat(FloatOptionNames.PlayerSpeedMod) ?? 1f;

    public static void Reset()
    {
        Oni.Clear();
        Runners.Clear();
        Captured.Clear();
        LastRescue.Clear();
        JailPositions.Clear();
        VentUsed.Clear();
        LastVentCount.Clear();
        foreach (var o in JailObjects) { try { o?.Despawn(); } catch { } }
        JailObjects.Clear();
        jailObjectsSpawned = false;
        SpeedBuffs.Clear();
        Shields.Clear();
        TaskCompleted.Clear();
        SentSpeed.Clear();
        sentOniKillCooldown = -1f;
        tickTimer = 0f;
        SabotageTimerStarted = false;
        SabotageElapsed = 0f;
    }

    /// <summary>Vanillaのインポスターを鬼、その他を逃走者として再分類します。</summary>
    public static void AssignTeams()
    {
        Reset();
        if (AmongUsClient.Instance?.AmHost == true)
            Main.NormalOptions.KillCooldown = Options.KedoroKillCooldown.GetFloat();
        var players = PlayerCatch.AllPlayerControls.Where(pc => pc != null && !pc.IsTestBot() && !pc.Is(CustomRoles.GM)).ToList();
        foreach (var pc in players)
        {
            if (pc.Data?.Role?.IsImpostor == true) Oni.Add(pc.PlayerId);
            else Runners.Add(pc.PlayerId);
        }
        // 逃走者をエンジニア置き換えにする。鬼の人数は通常のインポスター設定を尊重する。
        foreach (var pc in players)
        {
            if (IsOni(pc))
            {
                pc.RpcSetCustomRole(CustomRoles.Impostor, false);
                if (pc.Data?.Role?.Role != RoleTypes.Impostor) pc.RpcSetRole(RoleTypes.Impostor, false);
                Main.AllPlayerKillCooldown[pc.PlayerId] = Options.KedoroKillCooldown.GetFloat();
            }
            else
            {
                pc.RpcSetCustomRole(CustomRoles.Engineer, false);
                // 既にバニラでエンジニアなら送り直さない(送り直すとクライアントのボタンが壊れる)
                if (pc.Data?.Role?.Role != RoleTypes.Engineer) pc.RpcSetRole(RoleTypes.Engineer, false);
            }
        }
        BuildJailPositions();
    }

    // =====================================================================
    //  毎フレーム(ホストのみ)
    // =====================================================================
    public static void FixedUpdate()
    {
        if (!IsActive || AmongUsClient.Instance?.AmHost != true) return;
        if (GameStates.Intro || GameStates.IsMeeting) return;

        if (!SabotageTimerStarted)
        {
            SabotageElapsed += Time.fixedDeltaTime;
            if (SabotageElapsed >= Options.KedoroSabotageDelay.GetFloat()) StartSabotageTimer();
        }

        // イントロが終わったら、牢屋の位置にジェイラーと同じ檻を置く(1回だけ)
        if (!jailObjectsSpawned && GameStates.introDestroyed)
        {
            jailObjectsSpawned = true;
            foreach (var pos in JailPositions)
            {
                try { JailObjects.Add(new TownOfHost.Roles.Crewmate.PrisonNetObject(pos, "")); }
                catch (System.Exception e) { Logger.Warn($"Kedoro jail object: {e.Message}", "Kedoro"); }
            }
        }

        tickTimer += Time.fixedDeltaTime;
        if (tickTimer < TickInterval) return;
        tickTimer = 0f;

        KeepCapturedInJail();
        CheckTaskCompletion();
        UpdateOni();
        UpdateRunnerSpeeds();
    }

    /// <summary>サドンデスと同じく、サボタージュのカウントダウンを残り時間として開始します。</summary>
    private static void StartSabotageTimer()
    {
        if (SabotageTimerStarted || AmongUsClient.Instance?.AmHost != true || ShipStatus.Instance == null) return;
        SabotageTimerStarted = true;
        Main.IsActiveSabotage = true;
        ShipStatus.Instance.RpcUpdateSystem(Utils.GetCriticalSabotageSystemType(), 128);
        UtilsNotifyRoles.NotifyRoles(NoCache: true);
    }

    // ===== 牢屋の位置の設定(ランダムスポーンと同じ部屋一覧から選ぶ) =====
    private static readonly Dictionary<MapNames, OptionItem> JailMapOptions = new();
    private static readonly Dictionary<MapNames, List<(OptionItem Option, Vector2 Pos)>> JailRoomOptions = new();
    private static readonly Dictionary<MapNames, List<(OptionItem opt, int posId)>> JailMenuItems = new();

    /// <summary>「牢屋の位置を選ぶ」を押したとき: ランダムスポーンと同じ地図画面で部屋を選ぶ</summary>
    public static void OpenJailMenu(Transform parent, string actionKey)
    {
        var name = actionKey.Substring("KedoroJail".Length);
        if (!System.Enum.TryParse<MapNames>(name, out var map)) return;
        if (!JailMenuItems.TryGetValue(map, out var items) || !JailMapOptions.TryGetValue(map, out var header)) return;
        ShowRandomSpawnOption.CreateSpawanOptionMenu(parent, map, items, header);
    }

    private static RandomSpawn.SpawnMap SpawnMapOf(MapNames map) => map switch
    {
        MapNames.Skeld => new RandomSpawn.SkeldSpawnMap(),
        MapNames.MiraHQ => new RandomSpawn.MiraHQSpawnMap(),
        MapNames.Polus => new RandomSpawn.PolusSpawnMap(),
        MapNames.Airship => new RandomSpawn.AirshipSpawnMap(),
        MapNames.Fungle => new RandomSpawn.FungleSpawnMap(),
        _ => null,
    };

    /// <summary>
    /// マップごとに「牢屋にする部屋」を選べるオプションを作る(ID: 112600〜)。
    /// どれも選んでいないマップでは、部屋の中からランダムに選ぶ。
    /// </summary>
    public static void SetupJailOptions()
    {
        var maps = new (MapNames Map, string Label, int Base)[]
        {
            (MapNames.Skeld, "The Skeld", 112600),
            (MapNames.MiraHQ, "MIRA HQ", 112650),
            (MapNames.Polus, "Polus", 112700),
            (MapNames.Airship, "The Airship", 112750),
            (MapNames.Fungle, "The Fungle", 112800),
        };
        foreach (var (map, label, baseId) in maps)
        {
            var spawnMap = SpawnMapOf(map);
            if (spawnMap == null) continue;
            // 押すと地図の画面が開く(ランダムスポーンと同じ)。値は「どれか選んでいるか」(0/1)
            var mapOpt = ObjectOptionitem.Create(baseId, $"KedoroJail{map}", false, $"KedoroJail{map}", TabGroup.MainSettings)
                .SetTag(CustomOptionTags.Kedoro).SetParent(Options.KedoroKillCooldown)
                .SetOptionName(() => $"牢屋の位置を選ぶ({label})");
            JailMapOptions[map] = mapOpt;
            var list = new List<(OptionItem, Vector2)>();
            var menuItems = new List<(OptionItem opt, int posId)>();
            var i = 1;
            foreach (var (roomOpt, pos) in spawnMap.Positions)
            {
                if (roomOpt == null) continue;
                var src = roomOpt;
                var opt = BooleanOptionItem.Create(baseId + i, $"KedoroJail{map}{i}", false, TabGroup.MainSettings, false)
                    .SetTag(CustomOptionTags.Kedoro).SetParent(mapOpt)
                    .SetOptionName(() => src.GetName(disableColor: true))
                    .SetEnabled(() => false); // 一覧には出さず、地図の画面で切り替える
                list.Add((opt, pos));
                menuItems.Add((opt, src.Id));
                if (++i >= 50) break;
            }
            JailRoomOptions[map] = list;
            JailMenuItems[map] = menuItems;
        }
    }

    private static void BuildJailPositions()
    {
        var map = (MapNames)Main.NormalOptions.MapId;
        var candidates = new List<Vector2>();
        // 1) 「牢屋の位置を選ぶ」でONにした部屋
        if (JailMapOptions.TryGetValue(map, out var mapOpt) && mapOpt.CurrentValue != 0
            && JailRoomOptions.TryGetValue(map, out var rooms))
            candidates.AddRange(rooms.Where(r => r.Option.GetBool()).Select(r => r.Pos));
        // 2) 何も選んでいなければ、そのマップの部屋すべてからランダム
        if (candidates.Count == 0 && JailRoomOptions.TryGetValue(map, out var all))
            candidates.AddRange(all.Select(r => r.Pos));
        if (candidates.Count == 0 && PlayerControl.LocalPlayer != null)
            candidates.Add(PlayerControl.LocalPlayer.transform.position);

        // シャッフルして、設定した数だけ使う(候補より多くは置けない)
        var rnd = IRandom.Instance;
        for (var k = candidates.Count - 1; k > 0; k--)
        {
            var j = rnd.Next(k + 1);
            (candidates[k], candidates[j]) = (candidates[j], candidates[k]);
        }
        var count = Mathf.Clamp(Options.KedoroJailCount.GetInt(), 1, 30);
        JailPositions.AddRange(candidates.Take(count));
        Logger.Info($"牢屋: {JailPositions.Count}個 (設定{count}個 / 候補{candidates.Count}か所)", "Kedoro");
    }

    // ===== ベントの回数制限 =====
    private static readonly Dictionary<byte, int> VentUsed = new();

    /// <summary>ベントに入ってよいか(ホストが CanUse から呼ぶ)。入れるなら回数を数える</summary>
    private static readonly Dictionary<byte, float> LastVentCount = new();

    public static bool TryUseVent(PlayerControl pc, bool count = true)
    {
        if (!IsActive || pc == null || !IsRunner(pc)) return true;
        if (IsCaptured(pc)) return false;
        var max = Options.KedoroVentUses?.GetInt() ?? 0;
        if (max <= 0) return true;
        VentUsed.TryGetValue(pc.PlayerId, out var used);
        if (used >= max)
        {
            if (count) Utils.SendMessage("ベントを使える回数がなくなりました。", pc.PlayerId);
            return false;
        }
        // 1回の出入りで複数回呼ばれることがあるので、1秒以内の重複は数えない
        if (count && (!LastVentCount.TryGetValue(pc.PlayerId, out var last) || Time.time - last > 1f))
        {
            LastVentCount[pc.PlayerId] = Time.time;
            VentUsed[pc.PlayerId] = used + 1;
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: pc); // 名前の横の残り回数を更新
        }
        return true;
    }

    /// <summary>名前に付ける「ベント残り回数」(逃走者本人用・回数制限ありの時だけ)</summary>
    public static string GetVentMark(PlayerControl pc)
    {
        if (!IsActive || pc == null || !IsRunner(pc) || !pc.IsAlive()) return "";
        var left = VentLeft(pc);
        if (left < 0) return "";
        var color = left == 0 ? "#ff6b6b" : "#7fd8ff";
        return $"<{color}>({left})</color>";
    }

    public static int VentLeft(PlayerControl pc)
    {
        var max = Options.KedoroVentUses?.GetInt() ?? 0;
        if (max <= 0) return -1;
        VentUsed.TryGetValue(pc.PlayerId, out var used);
        return Mathf.Max(0, max - used);
    }

    /// <summary>移動はクライアント側で決まるので、牢屋から離れたら連れ戻す</summary>
    private static void KeepCapturedInJail()
    {
        foreach (var (id, jail) in Captured.ToArray())
        {
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc == null || !pc.IsAlive()) continue;
            if (Vector2.Distance(pc.GetTruePosition(), jail) > JailLeashDistance)
                pc.RpcSnapToForced(jail);
        }
    }

    // =====================================================================
    //  タスク進捗
    // =====================================================================
    /// <summary>逃走者全体のタスク進捗率(0〜100)</summary>
    private static float RunnerTaskProgress()
    {
        int done = 0, all = 0;
        foreach (var id in Runners)
        {
            var ts = PlayerState.GetByPlayerId(id)?.GetTaskState();
            if (ts == null || !ts.hasTasks) continue;
            all += ts.AllTasksCount;
            done += Math.Min(ts.CompletedTasksCount, ts.AllTasksCount);
        }
        return all <= 0 ? 0f : done * 100f / all;
    }

    /// <summary>しきい値から100%に向けて 0→1 になる割合</summary>
    private static float Ramp(float progress, float threshold)
    {
        if (progress < threshold) return 0f;
        if (threshold >= 100f) return 1f;
        return Mathf.Clamp01((progress - threshold) / (100f - threshold));
    }

    /// <summary>今の鬼のキルクール(タスク進捗で短くなる)</summary>
    public static float CurrentOniKillCooldown()
    {
        var baseCd = Options.KedoroKillCooldown.GetFloat();
        var minCd = Mathf.Min(baseCd, Options.KedoroMinKillCooldown.GetFloat());
        var t = Ramp(RunnerTaskProgress(), Options.KedoroKillCdReduceTaskPercent.GetFloat());
        return Mathf.Round(Mathf.Lerp(baseCd, minCd, t) * 10f) / 10f;
    }

    private static float CurrentOniSpeed()
    {
        var t = Ramp(RunnerTaskProgress(), Options.KedoroSpeedUpTaskPercent.GetFloat());
        return BaseSpeed * Mathf.Lerp(1f, Options.KedoroOniMaxSpeed.GetFloat(), t);
    }

    private static void UpdateOni()
    {
        var cd = CurrentOniKillCooldown();
        var speed = CurrentOniSpeed();
        foreach (var id in Oni)
        {
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc == null) continue;
            bool dirty = false;
            if (!Mathf.Approximately(sentOniKillCooldown, cd))
            {
                Main.AllPlayerKillCooldown[id] = cd;
                dirty = true;
            }
            if (!SentSpeed.TryGetValue(id, out var sent) || !Mathf.Approximately(sent, speed))
            {
                Main.AllPlayerSpeed[id] = speed;
                SentSpeed[id] = speed;
                dirty = true;
            }
            if (dirty) pc.MarkDirtySettings();
        }
        sentOniKillCooldown = cd;
    }

    private static void CheckTaskCompletion()
    {
        foreach (var id in Runners)
        {
            if (TaskCompleted.Contains(id)) continue;
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc == null || !pc.IsAlive() || !pc.AllTasksCompleted()) continue;
            TaskCompleted.Add(id);
            AddShields(id, 1, 0f); // 期限なし
            Utils.SendMessage("タスク全完了！シールド1枚とスピードアップが付きました。", id);
        }
    }

    // =====================================================================
    //  逃走者のバフ(スピード・シールド)
    // =====================================================================
    private static void AddSpeedBuff(byte id, float mult, float seconds)
    {
        if (mult <= 1f || seconds <= 0f) return;
        if (!SpeedBuffs.TryGetValue(id, out var list)) SpeedBuffs[id] = list = new();
        list.Add((mult, Time.time + seconds));
    }

    /// <summary>seconds が 0 のときは期限なし</summary>
    private static void AddShields(byte id, int count, float seconds)
    {
        if (count <= 0) return;
        if (!Shields.TryGetValue(id, out var list)) Shields[id] = list = new();
        var until = seconds > 0f ? Time.time + seconds : float.MaxValue;
        for (int i = 0; i < count; i++) list.Add(until);
    }

    /// <summary>有効なシールドを1枚使う(期限の近いものから)。使えたら true</summary>
    private static bool TryConsumeShield(byte id)
    {
        if (!Shields.TryGetValue(id, out var list)) return false;
        list.RemoveAll(until => until < Time.time);
        if (list.Count == 0) return false;
        list.Sort();
        list.RemoveAt(0);
        return true;
    }

    private static int ShieldCount(byte id)
        => Shields.TryGetValue(id, out var list) ? list.Count(until => until >= Time.time) : 0;

    private static void UpdateRunnerSpeeds()
    {
        foreach (var id in Runners)
        {
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc == null) continue;
            float mult = TaskCompleted.Contains(id) ? TaskCompleteSpeedMultiplier : 1f;
            if (SpeedBuffs.TryGetValue(id, out var list))
            {
                list.RemoveAll(b => b.Until < Time.time);
                foreach (var b in list) mult = Mathf.Max(mult, b.Mult);
            }
            var speed = BaseSpeed * mult;
            if (SentSpeed.TryGetValue(id, out var sent) && Mathf.Approximately(sent, speed)) continue;
            SentSpeed[id] = speed;
            Main.AllPlayerSpeed[id] = speed;
            pc.MarkDirtySettings();
        }
    }

    // =====================================================================
    //  捕獲・解放
    // =====================================================================
    public static void Capture(PlayerControl oni, PlayerControl runner)
    {
        if (!IsActive || !IsOni(oni) || !IsRunner(runner) || IsCaptured(runner) || !runner.IsAlive()) return;

        // シールドがあれば1枚使って防ぐ
        if (TryConsumeShield(runner.PlayerId))
        {
            oni.RpcProtectedMurderPlayer(runner);
            oni.SetKillCooldown();
            Utils.SendMessage($"シールドで捕獲を防ぎました(残り{ShieldCount(runner.PlayerId)}枚)", runner.PlayerId);
            return;
        }

        var location = JailPositions.Count == 0
            ? (Vector2)oni.transform.position
            : JailPositions[IRandom.Instance.Next(JailPositions.Count)];
        Captured[runner.PlayerId] = location;
        runner.RpcSnapToForced(location);
        oni.SetKillCooldown();
        UtilsNotifyRoles.NotifyRoles(NoCache: true);
    }

    private static void RescueNearby(PlayerControl rescuer)
    {
        if (!IsActive || !IsRunner(rescuer) || IsCaptured(rescuer) || !rescuer.IsAlive()) return;
        var cooldown = TaskCompleted.Contains(rescuer.PlayerId)
            ? Options.KedoroRescueCooldownAfterTasks.GetFloat()
            : Options.KedoroRescueCooldown.GetFloat();
        if (LastRescue.TryGetValue(rescuer.PlayerId, out var last) && cooldown > 0f && Time.time - last < cooldown) return;
        var target = PlayerCatch.AllPlayerControls
            .Where(pc => IsCaptured(pc) && pc.IsAlive())
            .Select(pc => (pc, d: Mathf.Min(Vector2.Distance(rescuer.GetTruePosition(), pc.GetTruePosition()), Vector2.Distance(rescuer.GetTruePosition(), Captured[pc.PlayerId]))))
            .Where(x => x.d <= RescueRange)
            .OrderBy(x => x.d)
            .Select(x => x.pc)
            .FirstOrDefault();
        if (target == null) return;

        Captured.Remove(target.PlayerId);
        target.RpcSnapToForced(rescuer.GetTruePosition() + new Vector2(0.35f, 0f));
        LastRescue[rescuer.PlayerId] = Time.time;

        // 解放した人・された人へのごほうび
        AddSpeedBuff(rescuer.PlayerId, Options.KedoroRescuerSpeed.GetFloat(), Options.KedoroRescuerSpeedTime.GetFloat());
        AddShields(rescuer.PlayerId, Options.KedoroRescuerShield.GetInt(), Options.KedoroRescuerShieldTime.GetFloat());
        AddSpeedBuff(target.PlayerId, Options.KedoroRescuedSpeed.GetFloat(), Options.KedoroRescuedSpeedTime.GetFloat());
        AddShields(target.PlayerId, Options.KedoroRescuedShield.GetInt(), Options.KedoroRescuedShieldTime.GetFloat());
        UpdateRunnerSpeeds();

        if (Options.KedoroNotifyOnRescue.GetBool())
            Utils.SendMessage($"{rescuer.Data?.PlayerName} が {target.Data?.PlayerName} を牢屋から解放しました！");
        UtilsNotifyRoles.NotifyRoles(NoCache: true);
    }

    public static void ApplyGameOptions(PlayerControl pc, IGameOptions opt)
    {
        if (!IsActive || !IsRunner(pc)) return;
        opt.SetFloat(FloatOptionNames.EngineerCooldown, Options.KedoroVentCooldown.GetFloat());
        var maxTime = Options.KedoroVentMaxTime.GetFloat();
        opt.SetFloat(FloatOptionNames.EngineerInVentMaxTime, maxTime >= 180f ? 0f : maxTime);
    }

    /// <summary>逃走者のスピード(PlayerGameOptionsSender が逃走者用の早期returnの前に使う)</summary>
    public static float GetRunnerSpeed(byte id) => SentSpeed.TryGetValue(id, out var s) ? s : BaseSpeed;

    public static bool CheckGameEnd(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (!IsActive || CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return false;
        var aliveRunners = Runners.Count(id => PlayerCatch.GetPlayerById(id)?.IsAlive() == true);
        var capturedRunners = Captured.Keys.Count(id => PlayerCatch.GetPlayerById(id)?.IsAlive() == true);
        if (aliveRunners > 0 && capturedRunners >= aliveRunners)
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
            Oni.ToList().ForEach(id => CustomWinnerHolder.WinnerIds.Add(id));
            return true;
        }
        var activeRunners = Runners.Select(PlayerCatch.GetPlayerById).Where(pc => pc != null && pc.IsAlive()).ToList();
        if (activeRunners.Count > 0 && activeRunners.All(pc => pc.AllTasksCompleted()))
        {
            reason = GameOverReason.CrewmatesByTask;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
            activeRunners.ForEach(pc => CustomWinnerHolder.WinnerIds.Add(pc.PlayerId));
            return true;
        }
        return false;
    }

    /// <summary>ペットを撫でた時(Petactionpatch.OnPetUse から。ホスト自身もリモートも通る)</summary>
    public static void OnPet(PlayerControl pc)
    {
        if (!IsActive || AmongUsClient.Instance?.AmHost != true) return;
        RescueNearby(pc);
    }

    public sealed class KedoroGameEndPredicate : GameEndPredicate
    {
        public override bool CheckForEndGame(out GameOverReason reason)
        {
            return CheckGameEnd(out reason);
        }
    }
}
