using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.AiPlayer;

// AIがゲームをプレイする(OTHER の設定)。ローカルの部屋で、AI がダミーを動かす。
// ・タスク中: マップを歩き回り、キルできる役職ならキル、ファントムのボタンを使う役職なら能力を使い、死体を見つけたら通報する
// ・会議中: 見たこと・チャットの流れをもとに、AI(API かローカル AI)が発言して投票する(AI に繋がらない時は簡単なルールで動く)
// 公式サーバーは偽のプレイヤーを受け付けないので、ローカルの部屋だけで動く。
public static class AiPlayers
{
    public static OptionItem Enabled;
    public static OptionItem Mode;
    public static OptionItem Count;
    public static OptionItem MeetingChat;
    public static OptionItem OpenSetting;
    // 番号は AiProvider と同じ並び(0: Claude, 1: ローカル, 2: ChatGPT, 3: Gemini)
    public static readonly string[] ModeKeys = { "AiPlayModeApi", "AiPlayModeLocal", "AiPlayModeChatGpt", "AiPlayModeGemini" };
    public static AiProvider Provider => (AiProvider)(Mode?.GetValue() ?? 0);
    public static bool UseApi => Provider != AiProvider.Local;

    // AI が動かしているダミーの PlayerId
    private static readonly HashSet<byte> aiIds = new();
    private static readonly Dictionary<byte, AiBrain> brains = new();
    public static bool IsAi(PlayerControl pc) => pc != null && aiIds.Contains(pc.PlayerId);
    public static IEnumerable<AiBrain> Brains => brains.Values;

    public static void SetupOptions()
    {
        Enabled = BooleanOptionItem.Create(1_000_900, "AiPlay", false, TabGroup.Other, true)
            .SetHeader(true)
            .SetColorcode("#8cffb4")
            .SetOptionName(() => "AIがゲームをプレイする(ローカルの部屋のみ)")
            .RegisterUpdateValueEvent((_, args) =>
            {
                if (args.CurrentValue == 0) return;
                ShowHowTo();
                SpawnSoon();
            });
        Mode = StringOptionItem.Create(1_000_901, "AiPlayMode", ModeKeys, 0, TabGroup.Other, true)
            .SetParent(Enabled).SetColorcode("#8cffb4")
            .SetOptionName(() => "使うAI")
            .RegisterUpdateValueEvent((_, _) => ShowHowTo());
        Count = IntegerOptionItem.Create(1_000_902, "AiPlayCount", new(1, 14, 1), 3, TabGroup.Other, true)
            .SetParent(Enabled).SetColorcode("#8cffb4")
            .SetValueFormat(OptionFormat.Players)
            .SetOptionName(() => "AIの人数");
        MeetingChat = BooleanOptionItem.Create(1_000_903, "AiPlayMeetingChat", true, TabGroup.Other, true)
            .SetParent(Enabled).SetColorcode("#8cffb4")
            .SetOptionName(() => "AIが会議でチャットする");
        // ボタンの代わり: ONにすると入力画面を開いて、すぐOFFに戻る
        OpenSetting = BooleanOptionItem.Create(1_000_904, "AiPlayOpenSetting", false, TabGroup.Other, true)
            .SetParent(Enabled).SetColorcode("#8cffb4")
            .SetOptionName(() => UseApi ? $"{ProviderName(Provider)}のAPIキーを入力する(ONにすると入力画面が開きます)" : "ローカルAIのモデル名を入力する(ONにすると入力画面が開きます)")
            .RegisterUpdateValueEvent((_, args) =>
            {
                if (args.CurrentValue == 0) return;
                _ = new LateTask(() => OpenSetting.SetValue(0, false, false), 0.1f, "AiPlayOpenSetting.Reset", true);
                AiSettingDialog.Open(Provider);
            });
    }

    // 使い方をホストのチャットに出す
    public static void ShowHowTo()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
        var text = Provider switch
        {
            AiProvider.Claude =>
                "【Claude(API)】Anthropic の Claude を使います。\n" +
                "1. https://platform.claude.com で API キーを作る\n",
            AiProvider.ChatGpt =>
                "【ChatGPT(API)】OpenAI の ChatGPT を使います。\n" +
                "1. https://platform.openai.com で API キーを作る\n",
            AiProvider.Gemini =>
                "【Gemini(API)】Google の Gemini を使います。\n" +
                "1. https://aistudio.google.com で API キーを作る\n",
            _ => "",
        };
        if (UseApi)
        {
            text += $"2. 設定の「{ProviderName(Provider)}のAPIキーを入力する」をONにして、入力画面にキーを貼り付けて保存\n" +
                    "3. 試合を始める\n" +
                    "※ API は、キーを入れた人(ホスト)の料金で使われます。キーは人に見せないでください。" +
                    (AiConfig.HasApiKey(Provider) ? "\n(APIキーは入力済みです)" : "\n(まだAPIキーが入っていません)") +
                    $"\nモデルは {AiConfig.FilePath} で変えられます。";
        }
        else
        {
            text = "【ローカルAIを使用する】自分のPCで動く AI(Ollama)を使います。\n" +
                   "1. https://ollama.com から Ollama を入れる\n" +
                   "2. コマンドで ollama pull qwen2.5:7b を実行(好きなモデルでOK)\n" +
                   "3. 設定の「ローカルAIのモデル名を入力する」でモデル名を入れる(最初は qwen2.5:7b)\n" +
                   "4. Ollama を起動したまま、試合を始める";
        }
        text += "\n\nAIはローカルの部屋だけで動きます(公式サーバーは偽のプレイヤーを受け付けないため)。AIに繋がらない時は、簡単なルールで動きます。";
        Utils.SendMessage(text, PlayerControl.LocalPlayer.PlayerId, "<#8cffb4>【AIプレイヤーの使い方】</color>");
    }

    // ロビーで先に AI ダミーを出しておく(試合を始める直前に出すと、ゲームの準備が間に合わないことがあった)
    public static void SpawnSoon()
    {
        _ = new LateTask(() =>
        {
            try { if (AmongUsClient.Instance != null && GameStates.IsLobby) SpawnMissing(); }
            catch (Exception e) { Logger.Warn($"AIの召喚に失敗: {e}", "AiPlayers"); }
        }, 3f, "AiPlayers.SpawnSoon", true);
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    private static class SpawnOnJoinPatch
    {
        public static void Postfix()
        {
            if (Enabled?.GetBool() == true && AmongUsClient.Instance.AmHost) SpawnSoon();
        }
    }

    public static string ProviderName(AiProvider p) => p switch
    {
        AiProvider.Claude => "Claude",
        AiProvider.ChatGpt => "ChatGPT",
        AiProvider.Gemini => "Gemini",
        _ => "ローカルAI",
    };

    // 試合を始める時に、足りない分の AI ダミーを出す
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame)), HarmonyPriority(Priority.First)]
    private static class SpawnOnBeginGame
    {
        public static void Prefix()
        {
            try { SpawnMissing(); }
            catch (Exception e) { Logger.Warn($"AIの召喚に失敗: {e}", "AiPlayers"); }
        }
    }

    private static void SpawnMissing()
    {
        if (Enabled?.GetBool() != true || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        if (GameData.Instance == null || PlayerControl.LocalPlayer == null) return;
        if (!GameStates.IsLocalGame)
        {
            Utils.SendMessage("AIプレイヤーは、ローカルの部屋だけで動きます(公式サーバーでは使えません)。", PlayerControl.LocalPlayer.PlayerId, "<#8cffb4>【AIプレイヤー】</color>");
            return;
        }
        aiIds.RemoveWhere(id => PlayerCatch.GetPlayerById(id) == null);
        var want = Count.GetInt();
        var have = aiIds.Count;
        for (var i = have; i < want; i++)
        {
            if (PlayerControl.AllPlayerControls.Count >= 15) break;
            var id = NextFreePlayerId();
            if (id == byte.MaxValue) break;
            var dummy = Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
            dummy.isDummy = true;
            dummy.PlayerId = id;
            var info = GameData.Instance.AddDummy(dummy);
            var color = (byte)((PlayerControl.LocalPlayer.Data.DefaultOutfit.ColorId + id) % Palette.PlayerColors.Length);
            dummy.NetTransform.enabled = true;
            dummy.RpcSetColor(color);
            dummy.RpcSetName($"AI{i + 1}");
            // 見た目を空のままにしない(空だと、試合を始める前の「全員のデータがそろうのを待つ」が終わらず、試合が始まらなかった)
            dummy.SetHat("", color);
            dummy.SetVisor("", color);
            dummy.SetSkin("", color);
            dummy.SetPet("");
            // バニラのダミーの動きは止めない(止めると、試合を始める準備が終わらず、ゲームが始まらなかった)。
            // 会議でかってに投票する所だけ、下の DummyVotePatch で止める
            var behaviour = dummy.GetComponent<DummyBehaviour>();
            if (behaviour != null) behaviour.enabled = true;
            AmongUsClient.Instance.Spawn(dummy);
            info.RpcSetTasks(Array.Empty<byte>());
            aiIds.Add(id);
            Logger.Info($"AIプレイヤーを出しました: AI{i + 1} (id={id})", "AiPlayers");
        }
    }

    private static byte NextFreePlayerId()
    {
        var used = PlayerControl.AllPlayerControls.ToArray().Where(p => p != null).Select(p => p.PlayerId).ToHashSet();
        for (byte id = 0; id < 15; id++) if (!used.Contains(id)) return id;
        return byte.MaxValue;
    }

    // AI が動かすダミーは、会議でバニラのダミーの動き(ランダムに投票)をさせない。投票は AI が自分でする
    [HarmonyPatch(typeof(DummyBehaviour), nameof(DummyBehaviour.Update))]
    private static class DummyVotePatch
    {
        public static bool Prefix(DummyBehaviour __instance)
        {
            if (aiIds.Count == 0 || __instance == null) return true;
            var pc = __instance.GetComponent<PlayerControl>();
            return pc == null || !aiIds.Contains(pc.PlayerId) || MeetingHud.Instance == null;
        }
    }

    // 試合が始まったら、AI ごとの頭を作る
    public static void OnGameStart()
    {
        brains.Clear();
        AiMemory.Reset();
        AiPath.Reset();
        if (Enabled?.GetBool() != true || !AmongUsClient.Instance.AmHost) return;
        foreach (var id in aiIds)
        {
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc != null) brains[id] = new AiBrain(pc);
        }
        // 役職の説明を読ませる(役職は、イントロが始まる時にはもう決まっている)
        foreach (var brain in brains.Values) brain.LearnRole();
        if (brains.Count > 0) Logger.Info($"AIプレイヤー {brains.Count} 人で試合を始めます({ProviderName(Provider)})", "AiPlayers");
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    private static class StartPatch
    {
        public static void Prefix()
        {
            try { OnGameStart(); } catch (Exception e) { Logger.Warn($"AIの準備に失敗: {e.Message}", "AiPlayers"); }
        }
    }

    // イントロが終わったら、クルー側の AI にタスクを配る(ダミーは最初タスクを持たないため)
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class GiveTasksPatch
    {
        public static void Postfix()
        {
            if (brains.Count == 0 || !AmongUsClient.Instance.AmHost) return;
            _ = new LateTask(() =>
            {
                foreach (var brain in brains.Values)
                {
                    try
                    {
                        var role = brain.Player?.GetCustomRole() ?? CustomRoles.NotAssigned;
                        if (role.IsImpostor()) continue;
                        brain.GiveTasks();
                    }
                    catch (Exception e) { Logger.Warn($"AIにタスクを配れませんでした: {e.Message}", "AiPlayers"); }
                }
            }, 2f, "AiPlayers.GiveTasks", true);
        }
    }

    // タスク中の動き(ホストだけ)
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    private static class ThinkPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (brains.Count == 0 || __instance == null || !brains.TryGetValue(__instance.PlayerId, out var brain)) return;
            try { brain.OnFixedUpdate(); } catch (Exception e) { Logger.Warn($"AIの動きでエラー: {e.Message}", "AiPlayers"); }
        }
    }

    // 歩かせる(ゲームの移動処理の後で、AI の向かう先へ速さを付ける)
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    private static class MovePatch
    {
        public static void Postfix(PlayerPhysics __instance)
        {
            if (brains.Count == 0 || __instance?.myPlayer == null) return;
            if (!brains.TryGetValue(__instance.myPlayer.PlayerId, out var brain)) return;
            brain.ApplyMovement(__instance);
        }
    }

    // 会議が始まったら、AI に考えさせる
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    private static class MeetingStartPatch
    {
        public static void Postfix()
        {
            if (brains.Count == 0 || !AmongUsClient.Instance.AmHost) return;
            foreach (var brain in brains.Values) brain.OnMeetingStart();
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    private static class MeetingUpdatePatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            if (brains.Count == 0 || !AmongUsClient.Instance.AmHost || __instance == null) return;
            foreach (var brain in brains.Values)
            {
                try { brain.OnMeetingUpdate(__instance); } catch (Exception e) { Logger.Warn($"AIの会議でエラー: {e.Message}", "AiPlayers"); }
            }
        }
    }

    // キルを見ていた AI に覚えさせる
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    private static class MurderWitnessPatch
    {
        public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] MurderResultFlags resultFlags)
        {
            if (brains.Count == 0 || target == null || __instance == null) return;
            if (!resultFlags.HasFlag(MurderResultFlags.Succeeded)) return;
            foreach (var brain in brains.Values) brain.OnMurderSeen(__instance, target);
        }
    }

    // チャットの流れを覚える(会議で AI に渡す)
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    private static class ChatLogPatch
    {
        public static void Postfix([HarmonyArgument(0)] PlayerControl sourcePlayer, [HarmonyArgument(1)] string chatText)
        {
            if (brains.Count == 0 || sourcePlayer == null || string.IsNullOrEmpty(chatText)) return;
            // システムメッセージ(見出しの付いた名前で送られるもの)は覚えない・返事もしない。
            // ホストの画面には、ホストだけに送られた役職のお知らせなども出るので、覚えると AI が役職を知っているように動いていた
            var name = sourcePlayer.Data?.PlayerName ?? "";
            if (name.Contains("【") || name.Contains("<") || chatText.Contains("【") || !AmongUsClient.Instance.AmHost) return;
            AiMemory.AddChat(sourcePlayer, chatText);
            foreach (var brain in brains.Values)
            {
                try { brain.OnChat(sourcePlayer, chatText); } catch (Exception e) { Logger.Warn($"AIの返事でエラー: {e.Message}", "AiPlayers"); }
            }
        }
    }
}
