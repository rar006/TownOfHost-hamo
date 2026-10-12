using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using static TownOfHost.Options;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.AddOns.Common;

/// <summary>
/// バウンティゲッサー(Bounty Guesser)
/// キル可能役職に配布されるアドオン。
/// 標的に指定されているプレイヤーを推測し、成功した場合は会議後のキルクールが減少する。
/// 失敗した場合は自殺する。
///
/// 配布対象の判定は Serial.cs (SetupCustomOption内のAddOnsAssignDataOnlyKiller) を参考にしている。
/// ターゲットの選び直し(会議ごとに変わる/前回と被らないようにする等)は
/// BountyHunter.cs の ResetTarget() のロジックを参考にしている。
/// </summary>
public static class BountyGuesser
{
    private static readonly int Id = 74400; // 既存の最大ブロック(74300番台)より後ろの空きブロックを使用
    private static Color RoleColor = UtilsRoleText.GetRoleColor(CustomRoles.BountyGuesser);
    public static string SubRoleMark = Utils.ColorString(RoleColor, "؟");

    private static List<byte> playerIdList = new();
    private static readonly Dictionary<byte, byte> TargetOf = new();

    public static OptionItem SuccessKillCooldown;
    public static OptionItem TargetDiesOnSuccess;
    public static OptionItem TargetPersistsUntilDead;

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.BountyGuesser);
        AddOnsAssignDataOnlyKiller.Create(Id + 10, CustomRoles.BountyGuesser, true, true, true, true);

        ObjectOptionitem.Create(Id + 20, "AddonOption", true, "", TabGroup.Addons)
            .SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.BountyGuesser);

        // 減少するキルクール: 2.5 ~ 60 (デフォルト10)
        SuccessKillCooldown = FloatOptionItem.Create(Id + 21, "BountyGuesserSuccessKillCooldown", new(2.5f, 60f, 0.5f), 10f, TabGroup.Addons, false)
            .SetValueFormat(OptionFormat.Seconds).SetSubRoleOptionItem(CustomRoles.BountyGuesser);

        // 推測されたプレイヤーが死ぬか (デフォルトoff)
        TargetDiesOnSuccess = BooleanOptionItem.Create(Id + 22, "BountyGuesserTargetDiesOnSuccess", false, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.BountyGuesser);

        // ターゲットが死ぬまで標的が変わらないか (デフォルトoff = 会議ごとに変わる)
        TargetPersistsUntilDead = BooleanOptionItem.Create(Id + 23, "BountyGuesserTargetPersistsUntilDead", false, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.BountyGuesser);
    }

    public static void Init()
    {
        playerIdList = new();
        TargetOf.Clear();
    }

    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId);
        // BountyHunterのAdd()と同様、付与された時点で最初のターゲットを決めておく
        if (AmongUsClient.Instance.AmHost) ResetTarget(playerId);
    }

    /// <summary>
    /// 指定したプレイヤーのターゲットを選び直す。
    /// BountyHunter.ResetTarget() を参考に、前回と同じ相手にはならないようにしている。
    /// </summary>
    private static PlayerControl ResetTarget(byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost) return null;
        if (!playerIdList.Contains(playerId)) return null;

        TargetOf.TryGetValue(playerId, out var previousTargetId);

        var candidates = new List<PlayerControl>(PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc.PlayerId != playerId));

        if (candidates.Count >= 2)
            candidates.RemoveAll(pc => pc.PlayerId == previousTargetId); // 前回のターゲットは除外

        if (candidates.Count <= 0)
        {
            Logger.Warn("ターゲットの指定に失敗しました:ターゲット候補が存在しません", "BountyGuesser");
            return null;
        }

        var rand = IRandom.Instance;
        var target = candidates[rand.Next(0, candidates.Count)];
        TargetOf[playerId] = target.PlayerId;
        Logger.Info($"BountyGuesser({playerId})のターゲットを{target.PlayerId}に変更", "BountyGuesser");
        return target;
    }

    /// <summary>
    /// 会議開始時に呼ばれ、必要なプレイヤーのターゲットを更新する。
    /// 「ターゲットが死ぬまで変わらない」設定がオンで、かつ現在のターゲットがまだ生きている場合は変更しない。
    /// </summary>
    private static void OnMeetingStartAll()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var persist = TargetPersistsUntilDead.GetBool();

        foreach (var playerId in playerIdList.ToList())
        {
            var owner = PlayerCatch.GetPlayerById(playerId);
            if (owner == null || !owner.IsAlive()) continue;

            if (persist && TargetOf.TryGetValue(playerId, out var currentTargetId))
            {
                var currentTarget = PlayerCatch.GetPlayerById(currentTargetId);
                if (currentTarget != null && currentTarget.IsAlive()) continue; // まだ有効なので変更しない
            }

            ResetTarget(playerId);
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    private static class MeetingStartPatch
    {
        private static void Postfix(MeetingHud __instance) => OnMeetingStartAll();
    }

    /// <summary>
    /// "/cmd bg <番号>" で推測を行う。GuessManager.GuesserMsgに割り込む形で処理する。
    /// (NiceGuesser.csの/cmd bt割り込みと同様の手法)
    /// </summary>
    private static bool HandleGuessMsg(PlayerControl pc, string msg)
    {
        if (pc == null || !playerIdList.Contains(pc.PlayerId)) return false;
        if (string.IsNullOrWhiteSpace(msg)) return false;

        var args = msg.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length < 3) return false;
        if (!args[0].Equals("/cmd", StringComparison.OrdinalIgnoreCase)) return false;
        if (!args[1].Equals("bg", StringComparison.OrdinalIgnoreCase)) return false;

        if (!AmongUsClient.Instance.AmHost) return true; // コマンドとしては消費するが処理はホストのみ

        if (!pc.IsAlive()) return true;

        if (!int.TryParse(args[2], out var num))
        {
            Utils.SendMessage(GetString("BountyGuesserInvalidTarget"), pc.PlayerId);
            return true;
        }

        var guessedId = Convert.ToByte(num);
        var guessed = PlayerCatch.GetPlayerById(guessedId);
        if (guessed == null)
        {
            Utils.SendMessage(GetString("BountyGuesserInvalidTarget"), pc.PlayerId);
            return true;
        }

        if (!TargetOf.TryGetValue(pc.PlayerId, out var targetId))
        {
            ResetTarget(pc.PlayerId);
            TargetOf.TryGetValue(pc.PlayerId, out targetId);
        }

        if (guessedId == targetId)
        {
            // 成功: 会議後のキルクールを減少させる
            Main.AllPlayerKillCooldown[pc.PlayerId] = SuccessKillCooldown.GetFloat();
            pc.SyncSettings();
            Utils.SendMessage(string.Format(GetString("BountyGuesserSuccess"), guessed.GetRealName()), pc.PlayerId);
            Logger.Info($"{pc.GetNameWithRole().RemoveHtmlTags()}:バウンティ推測成功", "BountyGuesser");

            if (TargetDiesOnSuccess.GetBool() && guessed.IsAlive() && guessed != pc)
            {
                pc.RpcMurderPlayer(guessed, true);
            }

            if (!TargetPersistsUntilDead.GetBool())
                ResetTarget(pc.PlayerId);
        }
        else
        {
            // 失敗: 自殺する
            Utils.SendMessage(GetString("BountyGuesserFail"), pc.PlayerId);
            Logger.Info($"{pc.GetNameWithRole().RemoveHtmlTags()}:バウンティ推測失敗、自殺", "BountyGuesser");
            pc.RpcMurderPlayer(pc, true);
        }

        return true;
    }

    [HarmonyPatch(typeof(GuessManager), nameof(GuessManager.GuesserMsg))]
    private static class GuesserMsgPatch
    {
        private static bool Prefix(PlayerControl pc, string msg, ref bool __result)
        {
            if (HandleGuessMsg(pc, msg))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
