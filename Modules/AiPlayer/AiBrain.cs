using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.AiPlayer;

// AI 1人ぶんの頭。ホストだけで動く。
public sealed class AiBrain
{
    public readonly PlayerControl Player;
    private readonly List<string> seen = new();
    private Vector2 target;
    private bool hasTarget;
    private List<Vector2> path;
    private int pathIndex;
    private float nextThinkAt;
    private float nextKillAt;
    private float nextAbilityAt;
    private float nextVentAt;
    private float nextShapeshiftAt;
    private float unshiftAt = -1f;
    private Vector2 lastPos;
    private float stuckSince;
    // タスク
    private PlayerTask currentTask;
    private float taskWorkUntil = -1f;
    // ベントの中にいる間
    private Vent inVent;
    private float ventExitAt;

    // 会議
    private MeetingHud meeting;
    private AiClient.Answer answer;
    private bool asking;
    // 会議中の返事
    private int repliesThisMeeting;
    private float nextReplyAt;
    private bool replying;
    // 引っかかった時に、同じ行き先への道を探し直した回数
    private int repathCount;

    // 役職の説明を読んで決めた、ボタンの使い方
    private sealed class RolePolicy
    {
        public bool Kill;
        public bool Phantom;
        public bool Shapeshift;
        public bool Vent;
        public string Note = "";
        public bool Learned;
    }
    private readonly RolePolicy policy = new();
    private bool said;
    private bool voted;
    private float meetingStartAt;
    private float sayAt;
    private float voteAt;

    private static readonly System.Random rand = new();
    private const byte SkipVote = 253;

    public AiBrain(PlayerControl player)
    {
        Player = player;
        nextKillAt = Time.time + 15f;
        nextAbilityAt = Time.time + 20f + rand.Next(0, 20);
        nextVentAt = Time.time + 20f + rand.Next(0, 20);
        nextShapeshiftAt = Time.time + 30f + rand.Next(0, 30);
        policy.Kill = Player.GetCustomRole().IsImpostor();
        policy.Vent = Player.GetCustomRole().IsImpostor();
    }

    private string RoleDescription()
    {
        var role = Player.GetCustomRole();
        var text = Translator.GetString($"{role}InfoLong");
        if (string.IsNullOrWhiteSpace(text) || text == $"{role}InfoLong") text = Translator.GetString($"{role}Info");
        return text.RemoveHtmlTags().Replace("\n", "\n");
    }

    // 試合の最初に、役職の説明を読んで、どのボタンをどう使う役職かを AI に決めてもらう
    public async void LearnRole()
    {
        var role = Player.GetCustomRole();
        var name = UtilsRoleText.GetRoleName(role).RemoveHtmlTags();
        var baseRole = Player.Data.Role.Role;
        var system = "あなたは Among Us(人狼に似た推理ゲーム)の MOD(Town Of Host)で遊ぶプレイヤーです。" +
                     "自分の役職の説明を読み、ボタンをどう使えばよいかを決めてください。" +
                     "kill: キルボタンで相手をキルしてよい役職か(シェリフのように間違えると自分が死ぬ役職や、キルボタンが別の能力の役職は false)。" +
                     "phantom: 透明化(ファントム)のボタンで能力を使う役職か。shapeshift: シェイプシフトで能力を使う・化けてよい役職か。vent: ベントを使ってよいか。" +
                     "必ず次の JSON だけを返してください: {\"kill\": true か false, \"phantom\": true か false, \"shapeshift\": true か false, \"vent\": true か false, \"note\": \"この役職でどう動くかを30文字以内で\"}";
        var user = $"役職: {name}\n陣営: {(role.IsImpostor() || role.IsMadmate() ? "インポスター側" : role.IsNeutral() ? "第三陣営" : "クルーメイト側")}\n" +
                   $"ゲーム上の役職(ボタンの種類): {baseRole}\n説明: {RoleDescription()}";
        var provider = AiPlayers.Provider;
        try
        {
            var text = await System.Threading.Tasks.Task.Run(() => AiClient.AskRawAsync(system, user, provider)).ConfigureAwait(false);
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return;
            using var doc = System.Text.Json.JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            bool Get(string key, bool def) => root.TryGetProperty(key, out var v) && v.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? v.GetBoolean() : def;
            var kill = Get("kill", policy.Kill);
            var phantom = Get("phantom", false);
            var shapeshift = Get("shapeshift", false);
            var vent = Get("vent", policy.Vent);
            var note = root.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
            MainThreadDispatcher.Enqueue(() =>
            {
                policy.Kill = kill;
                policy.Phantom = phantom;
                policy.Shapeshift = shapeshift;
                policy.Vent = vent;
                policy.Note = note.Length > 60 ? note[..60] : note;
                policy.Learned = true;
                Logger.Info($"{Player.Data.PlayerName}(AI) が役職を理解: {name} キル={kill} ファントム={phantom} シェイプ={shapeshift} ベント={vent} 「{policy.Note}」", "AiPlayers");
            });
        }
        catch (Exception e)
        {
            MainThreadDispatcher.Enqueue(() => Logger.Warn($"AIが役職の説明を読めませんでした(能力のボタンは使いません): {e.Message}", "AiPlayers"));
        }
    }

    private bool CanAct => Player != null && Player.IsAlive() && GameStates.IsInTask && !GameStates.IsMeeting && !GameStates.Intro && !GameStates.CalledMeeting;

    public void OnFixedUpdate()
    {
        if (!CanAct) { hasTarget = false; path = null; return; }
        if (Time.time < nextThinkAt) return;
        nextThinkAt = Time.time + 0.4f;

        if (inVent != null) { UpdateVent(); return; }
        if (unshiftAt > 0 && Time.time >= unshiftAt) { unshiftAt = -1f; Player.CmdCheckShapeshift(Player, true); }

        var pos = Player.GetTruePosition();
        if (TryReport(pos)) return;
        if (TryKill(pos)) return;
        if (TryVent(pos)) return;
        TryShapeshift();
        TryAbility();

        // タスクをしている間は立ち止まる
        if (taskWorkUntil > 0)
        {
            hasTarget = false;
            if (Time.time < taskWorkUntil) return;
            taskWorkUntil = -1f;
            if (currentTask != null && !currentTask.IsComplete)
            {
                Player.RpcCompleteTask(currentTask.Id);
                Logger.Info($"{Player.Data.PlayerName}(AI) がタスクを終えました: {currentTask.TaskType}", "AiPlayers");
            }
            currentTask = null;
            PickTarget();
            return;
        }

        // 向かう先が無い・着いた・止まったままの時は、次の行き先を決める
        if (!hasTarget) PickTarget();
        else if (Vector2.Distance(pos, target) < (currentTask != null ? 1.0f : 0.8f)) OnArrived();
        if (Vector2.Distance(pos, lastPos) > 0.15f) { lastPos = pos; stuckSince = Time.time; }
        else if (Time.time - stuckSince > 2f)
        {
            stuckSince = Time.time;
            // タスクの場所のすぐ近くで止まった時は、着いたことにする(台が壁ぎわにあって、真上まで行けないことがある)
            if (currentTask != null && hasTarget && Vector2.Distance(pos, target) < 1.8f) { OnArrived(); return; }
            if (hasTarget && repathCount++ < 2) SetDestination(pos, target);
            else { repathCount = 0; PickTarget(); }
        }
    }

    private void OnArrived()
    {
        if (currentTask != null && !currentTask.IsComplete)
        {
            // タスクの場所に着いたら、少しの間作業する
            taskWorkUntil = Time.time + 3f + (float)rand.NextDouble() * 4f;
            return;
        }
        PickTarget();
    }

    // 次の行き先: クルー側ならまだ終わっていないタスクの場所、無ければ部屋をうろうろ
    private void PickTarget()
    {
        var pos = Player.GetTruePosition();
        currentTask = null;
        // クルー側は、まだ終わっていないタスクの場所へ(近い順に試して、道が見つかった所へ行く)
        if (!IsKiller() || rand.Next(0, 3) == 0)
        {
            var tasks = new List<(PlayerTask task, Vector2 spot)>();
            foreach (var task in Player.myTasks.ToArray())
            {
                if (task == null || task.IsComplete) continue;
                var spots = task.FindConsolesPos();
                if (spots == null || spots.Count == 0) continue;
                tasks.Add((task, spots.ToArray().OrderBy(sp => Vector2.Distance(pos, sp)).First()));
            }
            foreach (var (task, spot) in tasks.OrderBy(t => Vector2.Distance(pos, t.spot)).Take(4))
            {
                if (!TrySetDestination(pos, spot)) continue;
                currentTask = task;
                return;
            }
        }
        if (ShipStatus.Instance == null) return;
        // 部屋は、近い部屋の中から選ぶ(遠すぎると道探しが終わらず、壁へまっすぐ歩いていた)
        var rooms = ShipStatus.Instance.AllRooms.ToArray()
            .Where(r => r?.roomArea != null && r.RoomId != SystemTypes.Hallway)
            .Select(r => (Vector2)r.roomArea.bounds.center)
            .Where(c => Vector2.Distance(pos, c) > 2f)
            .OrderBy(c => Vector2.Distance(pos, c)).Take(6).OrderBy(_ => rand.Next()).ToList();
        foreach (var room in rooms)
            if (TrySetDestination(pos, room)) return;
        hasTarget = false;
    }

    // 道が見つかった時だけ、行き先にする
    private bool TrySetDestination(Vector2 from, Vector2 to)
    {
        var found = AiPath.Find(from, to);
        if (found == null || found.Count == 0) return false;
        if (to != target) repathCount = 0;
        target = to;
        path = found;
        pathIndex = 0;
        hasTarget = true;
        return true;
    }

    private void SetDestination(Vector2 from, Vector2 to)
    {
        if (!TrySetDestination(from, to)) PickTarget();
    }

    // ゲームの移動処理の後に、道に沿って歩く速さを付ける
    public void ApplyMovement(PlayerPhysics physics)
    {
        if (!CanAct || physics.body == null) return;
        if (!hasTarget || inVent != null || taskWorkUntil > 0 || path == null) { physics.body.velocity = Vector2.zero; return; }
        if (Player.inVent || Player.onLadder || physics.Animations.IsPlayingAnyLadderAnimation()) return;
        var pos = Player.GetTruePosition();
        while (pathIndex < path.Count - 1 && Vector2.Distance(pos, path[pathIndex]) < 0.35f) pathIndex++;
        var dir = path[pathIndex] - pos;
        if (dir.magnitude < 0.2f) { physics.body.velocity = Vector2.zero; return; }
        physics.body.velocity = dir.normalized * physics.TrueSpeed;
    }

    // ===== ベント =====
    private bool CanUseVent()
    {
        var role = Player.GetRoleClass();
        if (role is IKiller killer) return killer.CanUseImpostorVentButton();
        return Player.Data.Role.Role is RoleTypes.Engineer || Player.GetCustomRole().IsImpostor();
    }

    private bool TryVent(Vector2 pos)
    {
        if (Time.time < nextVentAt || ShipStatus.Instance == null) return false;
        nextVentAt = Time.time + 20f + rand.Next(0, 25);
        if (!policy.Vent || !CanUseVent()) return false;
        var vent = ShipStatus.Instance.AllVents.ToArray().Where(v => v != null).OrderBy(v => Vector2.Distance(pos, v.transform.position)).FirstOrDefault();
        if (vent == null || Vector2.Distance(pos, vent.transform.position) > 1.2f) return false;
        inVent = vent;
        ventExitAt = Time.time + 2f + (float)rand.NextDouble() * 3f;
        hasTarget = false;
        Player.MyPhysics.RpcEnterVent(vent.Id);
        return true;
    }

    // ベントの中で少し待ってから、つながっている別のベントから出る
    private void UpdateVent()
    {
        if (Time.time < ventExitAt) return;
        var exit = inVent;
        var next = inVent.NearbyVents?.Where(v => v != null).ToList();
        if (next != null && next.Count > 0 && rand.Next(0, 3) != 0)
        {
            exit = next[rand.Next(next.Count)];
            Player.NetTransform.RpcSnapTo(exit.transform.position);
        }
        Player.MyPhysics.RpcExitVent(exit.Id);
        inVent = null;
        PickTarget();
    }

    // ===== シェイプシフト =====
    private void TryShapeshift()
    {
        if (Time.time < nextShapeshiftAt) return;
        nextShapeshiftAt = Time.time + 40f + rand.Next(0, 30);
        if (!policy.Shapeshift || Player.Data.Role.Role is not RoleTypes.Shapeshifter || Player.shapeshifting) return;
        var others = PlayerCatch.AllAlivePlayerControls.Where(p => p != null && p != Player).ToList();
        if (others.Count == 0) return;
        Player.CmdCheckShapeshift(others[rand.Next(others.Count)], true);
        unshiftAt = Time.time + 12f;
    }

    // タスクを持っていない AI に、タスクを配る(ダミーは最初タスクを持たないため)
    public void GiveTasks()
    {
        if (Player == null || ShipStatus.Instance == null || Player.myTasks.Count > 0) return;
        var ids = new List<byte>();
        void Pick(IEnumerable<NormalPlayerTask> list, int count)
        {
            var all = list?.Where(t => t != null).OrderBy(_ => rand.Next()).Take(count);
            if (all != null) ids.AddRange(all.Select(t => (byte)t.Index));
        }
        Pick(ShipStatus.Instance.ShortTasks.ToArray(), Math.Max(1, Main.NormalOptions.NumShortTasks));
        Pick(ShipStatus.Instance.LongTasks.ToArray(), Math.Max(0, Main.NormalOptions.NumLongTasks));
        if (ids.Count == 0) return;
        Player.Data.RpcSetTasks(ids.ToArray());
    }

    // 近くの死体を通報する(自分が殺した人の死体は、すぐには通報しない)
    private bool TryReport(Vector2 pos)
    {
        foreach (var body in Object.FindObjectsOfType<DeadBody>())
        {
            if (body == null || body.Reported) continue;
            if (Vector2.Distance(pos, body.TruePosition) > 1.6f) continue;
            if (IsKiller() && rand.Next(0, 3) != 0) continue; // 殺す側は、たまにしか通報しない
            var info = GameData.Instance.GetPlayerById(body.ParentId);
            if (info == null) continue;
            Remember($"{RoomName(pos)}で{info.PlayerName}の死体を見つけて通報した");
            Player.CmdReportDeadBody(info);
            return true;
        }
        return false;
    }

    private bool IsKiller()
    {
        var role = Player.GetRoleClass();
        if (role is IKiller killer) return killer.CanUseKillButton() && killer.CanKill;
        return Player.GetCustomRole().IsImpostor();
    }

    // キルできる役職なら、まわりに人が少ない時に近くの相手をキルする
    private bool TryKill(Vector2 pos)
    {
        if (Time.time < nextKillAt || !policy.Kill || !IsKiller()) return false;
        var range = NormalGameOptionsV12.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)] + 0.2f;
        var others = PlayerCatch.AllAlivePlayerControls.Where(p => p != null && p != Player && !p.inVent).ToList();
        var candidates = others.Where(p => !p.GetCustomRole().IsImpostor() || !Player.GetCustomRole().IsImpostor())
            .Where(p => Vector2.Distance(pos, p.GetTruePosition()) <= range).ToList();
        if (candidates.Count == 0) return false;
        var victim = candidates.OrderBy(p => Vector2.Distance(pos, p.GetTruePosition())).First();
        // 見ている人が多い時はやめる
        var witnesses = others.Count(p => p != victim && Vector2.Distance(pos, p.GetTruePosition()) < 5f);
        if (witnesses >= 2 && rand.Next(0, 4) != 0) return false;
        nextKillAt = Time.time + Main.AllPlayerKillCooldown.GetValueOrDefault(Player.PlayerId, Main.NormalOptions.KillCooldown) + 1f;
        Remember($"{RoomName(pos)}で{victim.Data.PlayerName}をキルした(秘密)");
        Player.CmdCheckMurder(victim);
        return true;
    }

    // ファントムのボタンで使う能力(ワンクリックの能力)を、ときどき使う
    private void TryAbility()
    {
        if (Time.time < nextAbilityAt) return;
        nextAbilityAt = Time.time + 25f + rand.Next(0, 25);
        if (!policy.Phantom || Player.GetRoleClass() is not IUsePhantomButton phantom || !phantom.UseOneclickButton) return;
        var adjust = false;
        bool? reset = null;
        phantom.OnClick(ref adjust, ref reset);
        Logger.Info($"{Player.Data.PlayerName} が能力を使いました", "AiPlayers");
    }

    public void OnMurderSeen(PlayerControl killer, PlayerControl victim)
    {
        if (Player == null || !Player.IsAlive() || killer == Player) return;
        if (Vector2.Distance(Player.GetTruePosition(), victim.GetTruePosition()) > 5f) return;
        // 壁の向こうは見えない
        if (!AiPath.Clear(Player.GetTruePosition(), victim.GetTruePosition())) return;
        Remember($"{RoomName(victim.GetTruePosition())}で{killer.Data.PlayerName}が{victim.Data.PlayerName}をキルするのを見た");
    }

    private void Remember(string text)
    {
        seen.Add(text);
        if (seen.Count > 12) seen.RemoveAt(0);
    }

    private static string ColorName(PlayerControl pc)
    {
        try
        {
            var id = pc.Data.DefaultOutfit.ColorId;
            return DestroyableSingleton<TranslationController>.Instance.GetString(Palette.ColorNames[id]);
        }
        catch { return "?"; }
    }

    private static string RoomName(Vector2 pos)
    {
        var room = ShipStatus.Instance?.AllRooms.ToArray().FirstOrDefault(r => r?.roomArea != null && r.roomArea.OverlapPoint(pos));
        return room == null ? "通路" : DestroyableSingleton<TranslationController>.Instance.GetString(room.RoomId);
    }

    // ===== 会議 =====
    public void OnMeetingStart()
    {
        meeting = MeetingHud.Instance;
        answer = null;
        asking = false;
        said = false;
        voted = false;
        repliesThisMeeting = 0;
        nextReplyAt = Time.time + 8f;
        replying = false;
        meetingStartAt = Time.time;
        sayAt = Time.time + 4f + rand.Next(0, 10);
        if (Player == null || !Player.IsAlive()) return;
        Ask();
    }

    private async void Ask()
    {
        asking = true;
        var system = "あなたは Among Us(人狼に似た推理ゲーム)のプレイヤーです。会議で、短く自然な日本語で1回だけ発言し、投票先を決めてください。" +
                     "自分の役職がインポスター側の時は、正体を隠して他の人を疑ってください。" +
                     "必ず次の JSON だけを返してください: {\"say\": \"発言(60文字以内)\", \"vote\": \"投票するプレイヤーの名前 か skip\"}";
        var user = BuildContext();
        var provider = AiPlayers.Provider;
        AiClient.Answer result;
        try { result = await System.Threading.Tasks.Task.Run(() => AiClient.AskAsync(system, user, provider)).ConfigureAwait(false); }
        catch (Exception e)
        {
            MainThreadDispatcher.Enqueue(() => Logger.Warn($"AIに繋がらないので簡単なルールで動きます: {e.Message}", "AiPlayers"));
            result = FallbackAnswer();
        }
        MainThreadDispatcher.Enqueue(() => { answer = result; asking = false; });
    }

    private string BuildContext()
    {
        var sb = new StringBuilder();
        var role = Player.GetCustomRole();
        sb.AppendLine($"あなたの名前: {Player.Data.PlayerName}(色: {ColorName(Player)})");
        sb.AppendLine("あなた自身を疑ったり、自分に投票したりしないでください。ほかの人の役職は、見たことやチャットから推理するしかありません。");
        sb.AppendLine($"あなたの役職: {UtilsRoleText.GetRoleName(role).RemoveHtmlTags()}({(role.IsImpostor() || role.IsMadmate() ? "インポスター側" : role.IsNeutral() ? "第三陣営" : "クルーメイト側")})");
        sb.AppendLine($"あなたの役職の説明: {RoleDescription()}");
        if (policy.Note.Length > 0) sb.AppendLine($"あなたの動き方: {policy.Note}");
        if (role.IsImpostor())
        {
            var mates = PlayerCatch.AllAlivePlayerControls.Where(p => p != Player && p.GetCustomRole().IsImpostor()).Select(p => p.Data.PlayerName);
            if (mates.Any()) sb.AppendLine($"仲間のインポスター(投票しない): {string.Join("、", mates)}");
        }
        sb.AppendLine($"生きている人: {string.Join("、", PlayerCatch.AllAlivePlayerControls.Where(p => p != Player).Select(p => $"{p.Data.PlayerName}({ColorName(p)})"))}");
        var dead = PlayerCatch.AllPlayerControls.Where(p => p != null && !p.IsAlive()).Select(p => p.Data.PlayerName).ToList();
        if (dead.Count > 0) sb.AppendLine($"死んだ人: {string.Join("、", dead)}");
        if (seen.Count > 0) sb.AppendLine("あなたが見たこと:\n- " + string.Join("\n- ", seen));
        var chat = AiMemory.RecentChat(12);
        if (chat.Length > 0) sb.AppendLine("最近のチャット:\n" + chat);
        return sb.ToString();
    }

    // AI に繋がらない時: 見たことがあればそれを言い、無ければスキップ
    private AiClient.Answer FallbackAnswer()
    {
        var a = new AiClient.Answer();
        var murder = seen.LastOrDefault(s => s.Contains("キルするのを見た"));
        if (murder != null)
        {
            a.Say = murder.Replace("キルするのを見た", "キルするのを見た！");
            var name = PlayerCatch.AllAlivePlayerControls.Select(p => p.Data.PlayerName).FirstOrDefault(n => murder.Contains($"で{n}が"));
            a.Vote = name ?? "skip";
        }
        else
        {
            a.Say = rand.Next(0, 2) == 0 ? "特に怪しい人は見てないです" : "今回はスキップでいいかも";
        }
        return a;
    }

    // 会議中に誰かが発言した時。名前を呼ばれたら必ず、そうでなくても時々返事をする(1回の会議で3回まで)
    public void OnChat(PlayerControl source, string text)
    {
        if (meeting == null || MeetingHud.Instance != meeting || Player == null || !Player.IsAlive() || source == Player) return;
        if (!AiPlayers.MeetingChat.GetBool() || replying || repliesThisMeeting >= 3 || Time.time < nextReplyAt) return;
        var myName = Player.Data.PlayerName;
        var mentioned = text.Contains(myName) || text.Contains("AI") || text.Contains("ＡＩ");
        if (AiPlayers.IsAi(source) && !mentioned) return; // AI どうしで延々と話し続けないように
        if (!mentioned && rand.Next(0, 3) != 0) return;
        replying = true;
        repliesThisMeeting++;
        nextReplyAt = Time.time + 6f;
        Reply(source.Data.PlayerName, text);
    }

    private async void Reply(string fromName, string text)
    {
        var system = "あなたは Among Us(人狼に似た推理ゲーム)のプレイヤーです。会議のチャットで話しかけられました。" +
                     "短く自然な日本語で返事をしてください。質問にはちゃんと答え、意見を言ってください。" +
                     "自分の役職がインポスター側の時は、正体を隠してください。" +
                     "必ず次の JSON だけを返してください: {\"say\": \"返事(60文字以内)\", \"vote\": \"投票するつもりのプレイヤーの名前 か skip\"}";
        var user = BuildContext() + $"\n今の発言: {fromName}「{text.RemoveHtmlTags()}」";
        var provider = AiPlayers.Provider;
        AiClient.Answer result = null;
        try { result = await System.Threading.Tasks.Task.Run(() => AiClient.AskAsync(system, user, provider)).ConfigureAwait(false); }
        catch (Exception e) { MainThreadDispatcher.Enqueue(() => Logger.Warn($"AIの返事に失敗: {e.Message}", "AiPlayers")); }
        MainThreadDispatcher.Enqueue(() =>
        {
            replying = false;
            if (result == null || string.IsNullOrWhiteSpace(result.Say)) return;
            if (MeetingHud.Instance != meeting || meeting == null || Player == null || !Player.IsAlive() || GameStates.ExiledAnimate) return;
            Player.RpcSendChat(result.Say);
            Logger.Info($"{Player.Data.PlayerName}(AI) の返事: {result.Say}", "AiPlayers");
            if (!voted && answer != null && !string.IsNullOrWhiteSpace(result.Vote)) answer.Vote = result.Vote;
        });
    }

    public void OnMeetingUpdate(MeetingHud hud)
    {
        if (hud != meeting || Player == null || !Player.IsAlive()) return;
        // 投票の時間が終わった後(結果を出している間)は何もしない
        if (GameStates.ExiledAnimate || Time.time - meetingStartAt > Main.NormalOptions.DiscussionTime + Main.NormalOptions.VotingTime - 1f) return;
        if (answer == null) return;

        if (!said && Time.time >= sayAt)
        {
            said = true;
            if (AiPlayers.MeetingChat.GetBool() && !string.IsNullOrWhiteSpace(answer.Say))
            {
                Player.RpcSendChat(answer.Say);
                Logger.Info($"{Player.Data.PlayerName}(AI): {answer.Say}", "AiPlayers");
            }
            // 話し合いの時間が終わってから、少しずつ投票する
            voteAt = Math.Max(Time.time + 3f, meetingStartAt + Main.NormalOptions.DiscussionTime + 2f + rand.Next(0, 8));
        }
        if (said && !voted && Time.time >= voteAt)
        {
            voted = true;
            var targetId = ResolveVote(answer.Vote);
            hud.CmdCastVote(Player.PlayerId, targetId);
            Logger.Info($"{Player.Data.PlayerName}(AI) が投票: {(targetId == SkipVote ? "スキップ" : PlayerCatch.GetPlayerById(targetId)?.Data?.PlayerName)}", "AiPlayers");
        }
    }

    private byte ResolveVote(string vote)
    {
        if (string.IsNullOrWhiteSpace(vote) || vote.Trim().Equals("skip", StringComparison.OrdinalIgnoreCase)) return SkipVote;
        var v = vote.Trim();
        if (v == Player.Data.PlayerName) return SkipVote;
        var pc = PlayerCatch.AllAlivePlayerControls.FirstOrDefault(p => p != Player && p.Data.PlayerName == v)
              ?? PlayerCatch.AllAlivePlayerControls.FirstOrDefault(p => p != Player && v.Contains(p.Data.PlayerName));
        // インポスターは仲間に投票しない
        if (pc == null || (Player.GetCustomRole().IsImpostor() && pc.GetCustomRole().IsImpostor())) return SkipVote;
        return pc.PlayerId;
    }
}
