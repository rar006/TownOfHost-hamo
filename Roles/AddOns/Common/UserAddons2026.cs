using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Roles.Core;
using static TownOfHost.Options;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.AddOns.Common
{
    // =====================================================================
    //  2026-10 ユーザー提案の属性
    //  サバイビング / バーサーカー / タフガイ / 共存症 / マーダー
    // =====================================================================

    internal static class AddonDay
    {
        public static int Now => UtilsGameLog.day;
    }

    // ---------------------------------------------------------------------
    public static class Surviving
    {
        private const int Id = 92000;
        public static string SubRoleMark => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Surviving), "Sv");
        public static List<byte> playerIdList = new();
        public static OptionItem OptionDays;

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Surviving);
            AddOnsAssignData.Create(Id + 10, CustomRoles.Surviving, true, true, true, true);
            ObjectOptionitem.Create(Id + 8, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Surviving);
            OptionDays = IntegerOptionItem.Create(Id + 9, "SurvivingDays", new(1, 99, 1), 3, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Surviving).SetValueFormat(OptionFormat.day);
        }
        public static void Init() => playerIdList = new();
        public static void Add(byte playerId) { if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId); }

        public static void CheckWin()
        {
            var need = OptionDays?.GetInt() ?? 3;
            foreach (var id in playerIdList)
            {
                var state = PlayerState.GetByPlayerId(id);
                if (state == null || !CustomWinnerHolder.WinnerIds.Contains(id)) continue;
                var ok = state.IsDead ? state.DeadDay >= need : true; // 生きていれば、日数前に終わっても勝てる
                if (ok) continue;
                CustomWinnerHolder.WinnerIds.Remove(id);
                CustomWinnerHolder.CantWinPlayerIds.Add(id);
                Logger.Info($"サバイビング: {id} は{need}日生き残れなかったため敗北", "Surviving");
            }
        }
    }

    // ---------------------------------------------------------------------
    public static class Berserker
    {
        private const int Id = 92100;
        public static string SubRoleMark => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Berserker), "Bk");
        public static List<byte> playerIdList = new();
        public static OptionItem OptionKillCooldown;
        public static OptionItem OptionAutoKillTime;
        public static OptionItem OptionAccumulate;
        static readonly Dictionary<(byte, byte), float> together = new();

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Berserker);
            AddOnsAssignDataOnlyKiller.Create(Id + 10, CustomRoles.Berserker, true, true, true, true);
            ObjectOptionitem.Create(Id + 5, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Berserker);
            OptionKillCooldown = FloatOptionItem.Create(Id + 6, "BerserkerKillCooldown", new(2.5f, 60f, 2.5f), 15f, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Berserker).SetValueFormat(OptionFormat.Seconds);
            OptionAutoKillTime = FloatOptionItem.Create(Id + 7, "BerserkerAutoKillTime", new(0f, 30f, 1f), 10f, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Berserker).SetValueFormat(OptionFormat.Seconds);
            OptionAccumulate = BooleanOptionItem.Create(Id + 8, "BerserkerAccumulate", false, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Berserker);
        }
        public static void Init() { playerIdList = new(); together.Clear(); }
        public static void Add(byte playerId) { if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId); }

        public static void ApplyKillCooldown(PlayerControl pc)
        {
            if (pc != null && pc.Is(CustomRoles.Berserker)) Main.AllPlayerKillCooldown[pc.PlayerId] = OptionKillCooldown.GetFloat();
        }

        static bool IsTeammate(PlayerControl a, PlayerControl b)
            => (a.GetCustomRole().IsImpostor() || a.GetCustomRole().IsMadmate()) && b.GetCustomRole().IsImpostor();

        public static void Tick(float dt)
        {
            if (playerIdList.Count == 0) return;
            var limit = OptionAutoKillTime.GetFloat();
            var dist = NormalGameOptionsV12.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
            foreach (var id in playerIdList)
            {
                var killer = PlayerCatch.GetPlayerById(id);
                if (killer == null || !killer.IsAlive() || !killer.CanUseKillButton()) continue;
                // キルクールが明けているか(最後のキルからの経過時間で判断)
                var cd = Main.AllPlayerKillCooldown.TryGetValue(id, out var c) ? c : OptionKillCooldown.GetFloat();
                if (CheckMurderPatch.TimeSinceLastKill.TryGetValue(id, out var since) && since < cd) continue;
                foreach (var target in PlayerCatch.AllAlivePlayerControls)
                {
                    if (target == null || target.PlayerId == id || IsTeammate(killer, target)) continue;
                    var key = (id, target.PlayerId);
                    var near = Vector2.Distance(killer.GetTruePosition(), target.GetTruePosition()) <= dist;
                    if (!near)
                    {
                        if (!OptionAccumulate.GetBool()) together.Remove(key);
                        continue;
                    }
                    together[key] = (together.TryGetValue(key, out var t) ? t : 0f) + dt;
                    if (together[key] < limit) continue;
                    together.Remove(key);
                    Logger.Info($"バーサーカー: {killer.Data?.PlayerName} が {target.Data?.PlayerName} を勝手にキル", "Berserker");
                    CustomRoleManager.OnCheckMurder(killer, target);
                    break;
                }
            }
        }

        public static void OnKill(byte killerId)
        {
            foreach (var k in together.Keys.Where(k => k.Item1 == killerId).ToList()) together.Remove(k);
        }
    }

    // ---------------------------------------------------------------------
    public static class ToughGuy
    {
        private const int Id = 92200;
        public static string SubRoleMark => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.ToughGuy), "Tg");
        public static List<byte> playerIdList = new();
        public static OptionItem OptionTurns;
        public static OptionItem OptionNotify;
        static readonly Dictionary<byte, int> guardedDay = new();
        static readonly HashSet<byte> notifyPending = new();

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.ToughGuy);
            AddOnsAssignData.Create(Id + 10, CustomRoles.ToughGuy, true, true, true, true);
            ObjectOptionitem.Create(Id + 6, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.ToughGuy);
            OptionTurns = IntegerOptionItem.Create(Id + 7, "ToughGuyTurns", new(0, 99, 1), 1, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.ToughGuy).SetValueFormat(OptionFormat.Turns);
            OptionNotify = BooleanOptionItem.Create(Id + 8, "ToughGuyNotify", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.ToughGuy);
        }
        public static void Init() { playerIdList = new(); guardedDay.Clear(); notifyPending.Clear(); }
        public static void Add(byte playerId) { if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId); }

        public static bool TryGuard(PlayerControl target)
        {
            if (target == null || !target.Is(CustomRoles.ToughGuy) || guardedDay.ContainsKey(target.PlayerId)) return false;
            guardedDay[target.PlayerId] = AddonDay.Now;
            if (OptionNotify.GetBool()) notifyPending.Add(target.PlayerId);
            Logger.Info($"タフガイ: {target.Data?.PlayerName} がキルを耐えた", "ToughGuy");
            return true;
        }

        public static string OnMeetingStart()
        {
            foreach (var id in notifyPending)
                Utils.SendMessage(GetString("ToughGuyGuardedMessage"), id, Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.ToughGuy), GetString("ToughGuy")));
            notifyPending.Clear();
            var need = Mathf.Max(1, OptionTurns.GetInt());
            foreach (var (id, day) in guardedDay.ToList())
            {
                var pc = PlayerCatch.GetPlayerById(id);
                if (pc == null || !pc.IsAlive()) continue;
                // 防いだターンの会議を1として数える(0ターン設定はその会議で死亡)
                var meetings = AddonDay.Now - day + 1;
                if (meetings < need) continue;
                MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, id);
                guardedDay.Remove(id);
            }
            return "";
        }
    }

    // ---------------------------------------------------------------------
    public static class Comorbidity
    {
        private const int Id = 92300;
        public static string SubRoleMark => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Comorbidity), "Cm");
        public static List<byte> playerIdList = new();
        public static OptionItem OptionStartTurn;

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Comorbidity);
            AddOnsAssignData.Create(Id + 10, CustomRoles.Comorbidity, true, true, true, true);
            ObjectOptionitem.Create(Id + 8, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Comorbidity);
            OptionStartTurn = IntegerOptionItem.Create(Id + 9, "ComorbidityStartTurn", new(1, 30, 1), 4, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Comorbidity).SetValueFormat(OptionFormat.Turns);
        }
        public static void Init() => playerIdList = new();
        public static void Add(byte playerId) { if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId); }

        public static bool IsActive => AddonDay.Now >= (OptionStartTurn?.GetInt() ?? 4);

        public static void CheckWin()
        {
            if (!IsActive) return;
            foreach (var id in playerIdList)
            {
                var pc = PlayerCatch.GetPlayerById(id);
                if (pc == null || !pc.IsAlive() || CustomWinnerHolder.WinnerIds.Contains(id)) continue;
                CustomWinnerHolder.WinnerIds.Add(id);
                CustomWinnerHolder.CantWinPlayerIds.Remove(id);
                CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Comorbidity);
                Logger.Info($"共存症: {id} が生存により追加勝利", "Comorbidity");
            }
        }
    }

    // ---------------------------------------------------------------------
    public static class Murder
    {
        private const int Id = 92400;
        public static string SubRoleMark => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Murder), "Md");
        public static List<byte> playerIdList = new();
        public static OptionItem OptionNeedAlive;
        public static OptionItem OptionReflectSheriff;
        public static OptionItem OptionWinOnTask;

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Murder);
            AddOnsAssignData.Create(Id + 10, CustomRoles.Murder, true, false, false, false);
            ObjectOptionitem.Create(Id + 5, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Murder);
            OptionNeedAlive = BooleanOptionItem.Create(Id + 6, "MurderNeedAlive", false, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Murder);
            OptionReflectSheriff = BooleanOptionItem.Create(Id + 7, "MurderReflectSheriff", false, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Murder);
            OptionWinOnTask = BooleanOptionItem.Create(Id + 8, "MurderWinOnTask", false, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Murder);
        }
        public static void Init() => playerIdList = new();
        public static void Add(byte playerId) { if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId); }

        public static bool CanBeGiven(CustomRoles role) => role.IsCrewmate() && role is not (CustomRoles.King or CustomRoles.Walkure);

        static bool IsSheriffLike(CustomRoles role) => role.ToString().Contains("Sheriff");

        public static bool TryReflect(PlayerControl killer, PlayerControl target)
        {
            if (killer == null || target == null || !target.Is(CustomRoles.Murder)) return false;
            if (!OptionReflectSheriff.GetBool() || !IsSheriffLike(killer.GetCustomRole())) return false;
            var state = PlayerState.GetByPlayerId(killer.PlayerId);
            if (state != null) state.DeathReason = CustomDeathReason.Revenge;
            killer.RpcMurderPlayerV2(killer);
            Logger.Info($"マーダー: {target.Data?.PlayerName} がシェリフ系のキルを反射", "Murder");
            return true;
        }

        public static void CheckWin(GameOverReason reason)
        {
            foreach (var id in playerIdList)
            {
                var me = PlayerCatch.GetPlayerById(id);
                if (me == null) continue;
                var othersCrewAlive = PlayerCatch.AllAlivePlayerControls.Any(p => p.PlayerId != id && p.GetCustomRole().IsCrewmate());
                var aliveOk = !OptionNeedAlive.GetBool() || me.IsAlive();
                var taskWin = OptionWinOnTask.GetBool() && reason == GameOverReason.CrewmatesByTask;
                var win = (!othersCrewAlive && aliveOk) || taskWin;
                if (win)
                {
                    if (!CustomWinnerHolder.WinnerIds.Contains(id))
                    {
                        CustomWinnerHolder.WinnerIds.Add(id);
                        CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Murder);
                    }
                    CustomWinnerHolder.CantWinPlayerIds.Remove(id);
                }
                else
                {
                    // クルーの勝利では勝てない(勝利条件が変わっている)
                    CustomWinnerHolder.WinnerIds.Remove(id);
                    CustomWinnerHolder.CantWinPlayerIds.Add(id);
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    public static class BetrayGuesser
    {
        private const int Id = 93700;
        public static string SubRoleMark => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.BetrayGuesser), "Btg");
        public static List<byte> playerIdList = new();
        public static OptionItem OptionWinWhenDead;
        public static OptionItem OptionExileCount;
        public static OptionItem OptionDeathCount;
        public static OptionItem OptionGuessCount;
        public static OptionItem OptionGiveChance;
        public static OptionItem OptionNeedSuccess;
        static readonly Dictionary<byte, int> success = new();
        static bool given;

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.BetrayGuesser);
            ObjectOptionitem.Create(Id + 4, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.BetrayGuesser);
            OptionWinWhenDead = BooleanOptionItem.Create(Id + 5, "BetrayGuesserWinWhenDead", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.BetrayGuesser);
            OptionExileCount = IntegerOptionItem.Create(Id + 6, "BetrayGuesserExileCount", new(0, 15, 1), 2, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.BetrayGuesser).SetValueFormat(OptionFormat.Players);
            OptionDeathCount = IntegerOptionItem.Create(Id + 7, "BetrayGuesserDeathCount", new(0, 15, 1), 4, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.BetrayGuesser).SetValueFormat(OptionFormat.Players);
            OptionGuessCount = IntegerOptionItem.Create(Id + 8, "BetrayGuesserGuessCount", new(0, 15, 1), 2, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.BetrayGuesser).SetValueFormat(OptionFormat.Times);
            OptionGiveChance = IntegerOptionItem.Create(Id + 9, "BetrayGuesserGiveChance", new(0, 100, 1), 50, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.BetrayGuesser).SetValueFormat(OptionFormat.Percent);
            OptionNeedSuccess = IntegerOptionItem.Create(Id + 10, "BetrayGuesserNeedSuccess", new(0, 15, 1), 1, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.BetrayGuesser).SetValueFormat(OptionFormat.Players);
        }
        public static void Init() { playerIdList = new(); success.Clear(); given = false; }
        public static void Add(byte playerId) { if (!playerIdList.Contains(playerId)) playerIdList.Add(playerId); }

        public static int GuessLimit => OptionGuessCount?.GetInt() ?? 2;
        public static void OnGuessSuccess(byte id)
        {
            if (!playerIdList.Contains(id)) return;
            success[id] = (success.TryGetValue(id, out var n) ? n : 0) + 1;
        }

        static bool CanBeGiven(PlayerControl pc)
            => pc != null && pc.IsAlive() && pc.GetCustomRole().IsCrewmate()
               && pc.GetCustomRole() is not (CustomRoles.King or CustomRoles.Walkure)
               && !pc.Is(CustomRoles.BetrayGuesser) && !pc.Is(CustomRoles.Guesser);

        public static void CheckGive()
        {
            if (given || !CustomRoles.BetrayGuesser.IsEnable()) return;
            int exiled = 0, died = 0;
            foreach (var st in PlayerState.AllPlayerStates.Values)
            {
                if (!st.IsDead || !st.MainRole.IsCrewmate()) continue;
                if (st.DeathReason == CustomDeathReason.Vote) exiled++;
                else died++;
            }
            var needExile = OptionExileCount.GetInt();
            var needDeath = OptionDeathCount.GetInt();
            var ok = (needExile > 0 && exiled >= needExile) || (needDeath > 0 && died >= needDeath);
            if (!ok) return;
            given = true; // 条件を満たした時の1回だけ抽選する
            if (IRandom.Instance.Next(100) >= OptionGiveChance.GetInt()) { Logger.Info("ベトレイゲッサー: 抽選に外れた", "BetrayGuesser"); return; }
            var list = PlayerCatch.AllAlivePlayerControls.Where(CanBeGiven).ToList();
            if (list.Count == 0) return;
            var pc = list[IRandom.Instance.Next(list.Count)];
            pc.RpcSetCustomRole(CustomRoles.BetrayGuesser);
            Add(pc.PlayerId);
            Utils.SendMessage(GetString("BetrayGuesserGiven"), pc.PlayerId, Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.BetrayGuesser), GetString("BetrayGuesser")));
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: pc);
            Logger.Info($"ベトレイゲッサー: {pc.Data?.GetLogPlayerName()} に付与 (追放{exiled} / 死亡{died})", "BetrayGuesser");
        }

        public static void CheckWin()
        {
            var crewWon = CustomWinnerHolder.WinnerTeam == CustomWinner.Crewmate;
            foreach (var id in playerIdList)
            {
                var pc = PlayerCatch.GetPlayerById(id);
                var hit = success.TryGetValue(id, out var n) ? n : 0;
                var aliveOk = OptionWinWhenDead.GetBool() || (pc != null && pc.IsAlive());
                var win = !crewWon && hit >= OptionNeedSuccess.GetInt() && aliveOk;
                if (win)
                {
                    if (!CustomWinnerHolder.WinnerIds.Contains(id))
                    {
                        CustomWinnerHolder.WinnerIds.Add(id);
                        CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.BetrayGuesser);
                    }
                    CustomWinnerHolder.CantWinPlayerIds.Remove(id);
                }
                else
                {
                    CustomWinnerHolder.WinnerIds.Remove(id);
                    CustomWinnerHolder.CantWinPlayerIds.Add(id);
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    public static class UserAddons2026
    {
        public static void SetupCustomOption()
        {
            Surviving.SetupCustomOption();
            Berserker.SetupCustomOption();
            ToughGuy.SetupCustomOption();
            Comorbidity.SetupCustomOption();
            Murder.SetupCustomOption();
            BetrayGuesser.SetupCustomOption();
        }
        public static void Init()
        {
            Surviving.Init();
            Berserker.Init();
            ToughGuy.Init();
            Comorbidity.Init();
            Murder.Init();
            BetrayGuesser.Init();
        }
        public static void CheckWin(GameOverReason reason)
        {
            Murder.CheckWin(reason);
            BetrayGuesser.CheckWin();
            Comorbidity.CheckWin();
            Surviving.CheckWin(); // 最後: どの勝利でも条件を満たさなければ外す
        }

        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
        static class TickPatch
        {
            public static void Postfix()
            {
                try
                {
                    if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                    if (!GameStates.IsInTask || GameStates.IsMeeting) return;
                    Berserker.Tick(Time.deltaTime);
                }
                catch (System.Exception e) { Logger.Warn($"UserAddons2026.Tick: {e.Message}", "UserAddons2026"); }
            }
        }
    }
}
