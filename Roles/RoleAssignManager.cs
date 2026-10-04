using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Neutral;
using static TownOfHost.Translator;

namespace TownOfHost.Roles
{
    public static class RoleAssignManager
    {
        private static readonly int idStart = 500;
        class RandomAssignOptions
        {
            public int Min => min();
            private Func<int> min;
            public int Max => max();
            private Func<int> max;
            private IntegerOptionItem minOption;
            private IntegerOptionItem maxOption;

            private RandomAssignOptions(int id, OptionItem parent, CustomRoleTypes roleTypes, int maxCount)
            {
                var name = GetColoredRoleTypeName(roleTypes);
                var replacementDictionary = new Dictionary<string, string>()
                { { "%roleType%", name} };

                minOption = (IntegerOptionItem)IntegerOptionItem.Create(idStart + id + 1, "RoleTypeMin", new(0, maxCount, 1), 0, TabGroup.MainSettings, false)
                    .SetParent(parent)
                    .SetValueFormat(OptionFormat.Players);
                maxOption = (IntegerOptionItem)IntegerOptionItem.Create(idStart + id + 2, "RoleTypeMax", new(0, maxCount, 1), 0, TabGroup.MainSettings, false)
                    .SetParent(parent)
                    .SetValueFormat(OptionFormat.Players);

                minOption.ReplacementDictionary =
                maxOption.ReplacementDictionary = replacementDictionary;

                min = () => minOption.GetInt();
                max = () => maxOption.GetInt();

                RandomAssignOptionsCollection.Add(roleTypes, this);
            }
            public static RandomAssignOptions Create(int id, OptionItem parent, CustomRoleTypes roleTypes, int maxCount = 15)
                => new(id, parent, roleTypes, maxCount);

            // モデレーターコマンド(/cmd ma, /cmd mn)からの数値変更用。
            // 実際の値(人数)を渡し、内部でインデックスに変換してからSetValueする。
            public bool SetMinValue(int actualValue)
            {
                var index = minOption.Rule.GetNearestIndex(actualValue);
                if (index < 0 || index * minOption.Rule.Step + minOption.Rule.MinValue > minOption.Rule.MaxValue) return false;
                minOption.SetValue(index);
                return true;
            }
            public bool SetMaxValue(int actualValue)
            {
                var index = maxOption.Rule.GetNearestIndex(actualValue);
                if (index < 0 || index * maxOption.Rule.Step + maxOption.Rule.MinValue > maxOption.Rule.MaxValue) return false;
                maxOption.SetValue(index);
                return true;
            }
            public int MinLimit => minOption.Rule.MaxValue;
            public int MaxLimit => maxOption.Rule.MaxValue;
        }

        /// <summary>
        /// 人数ごとの個別設定(3人〜15人)。要望により、アサインモードがランダムのとき、
        /// 各プレイヤー人数ごとにインポスター/マッドメイト/クルーメイト/ニュートラルの
        /// 最大人数・最少人数と、マッドメイトをクルー枠からアサインする場合の最大人数を
        /// 個別に設定できるようにするためのもの。
        /// </summary>
        class PerCountAssignOptions
        {
            public int PlayerCount { get; }
            public OptionItem Enabled { get; }
            private readonly Dictionary<CustomRoleTypes, IntegerOptionItem> minOptions = new();
            private readonly Dictionary<CustomRoleTypes, IntegerOptionItem> maxOptions = new();
            private readonly IntegerOptionItem madFromCrewMaxOption;

            public PerCountAssignOptions(int idBase, OptionItem parent, int playerCount)
            {
                PlayerCount = playerCount;
                var header = BooleanOptionItem.Create(idBase, "PlayerCountSettingHeader", false, TabGroup.MainSettings, false)
                    .SetParent(parent)
                    .SetEnabled(() => OptionAssignMode.GetBool() && OptionAssignPerPlayerCount.GetBool());
                header.ReplacementDictionary = new Dictionary<string, string>() { { "%count%", playerCount.ToString() } };
                Enabled = header;

                var offset = 1;
                foreach (var roleTypes in new[] { CustomRoleTypes.Impostor, CustomRoleTypes.Madmate, CustomRoleTypes.Crewmate, CustomRoleTypes.Neutral })
                {
                    var maxCount = roleTypes == CustomRoleTypes.Impostor ? Math.Min(3, playerCount) : playerCount;
                    var name = GetColoredRoleTypeName(roleTypes);
                    var replacementDictionary = new Dictionary<string, string>() { { "%roleType%", name } };

                    var minOption = (IntegerOptionItem)IntegerOptionItem.Create(idBase + offset++, "RoleTypeMin", new(0, maxCount, 1), 0, TabGroup.MainSettings, false)
                        .SetParent(header)
                        .SetValueFormat(OptionFormat.Players)
                        .SetEnabled(() => OptionAssignMode.GetBool() && OptionAssignPerPlayerCount.GetBool() && header.GetBool());
                    minOption.ReplacementDictionary = replacementDictionary;

                    var maxOption = (IntegerOptionItem)IntegerOptionItem.Create(idBase + offset++, "RoleTypeMax", new(0, maxCount, 1), 0, TabGroup.MainSettings, false)
                        .SetParent(header)
                        .SetValueFormat(OptionFormat.Players)
                        .SetEnabled(() => OptionAssignMode.GetBool() && OptionAssignPerPlayerCount.GetBool() && header.GetBool());
                    maxOption.ReplacementDictionary = replacementDictionary;

                    minOptions[roleTypes] = minOption;
                    maxOptions[roleTypes] = maxOption;
                }

                madFromCrewMaxOption = (IntegerOptionItem)IntegerOptionItem.Create(idBase + offset++, "AssignMadmateFromCrewmateSlotMax", new(0, playerCount, 1), playerCount, TabGroup.MainSettings, false)
                    .SetParent(header)
                    .SetValueFormat(OptionFormat.Players)
                    .SetEnabled(() => OptionAssignMode.GetBool() && OptionAssignPerPlayerCount.GetBool() && header.GetBool() && OptionAssignMadmateFromCrewmateSlot.GetBool());
            }

            public int Min(CustomRoleTypes roleTypes) => minOptions.TryGetValue(roleTypes, out var opt) ? opt.GetInt() : 0;
            public int Max(CustomRoleTypes roleTypes) => maxOptions.TryGetValue(roleTypes, out var opt) ? opt.GetInt() : 0;
            public int MadFromCrewMax => madFromCrewMaxOption.GetInt();
        }
        private static AssignAlgorithm AssignMode => assignMode();
        private static Func<AssignAlgorithm> assignMode;
        private enum AssignAlgorithm
        {
            Fixed,
            Random
        }
        private static readonly string[] AssignModeSelections =
        {
            "AssignAlgorithm.Fixed",
            "AssignAlgorithm.Random"
        };
        private static CustomRoles[] AllMainRoles => GameModeManager.IsStandardClass() ? CustomRolesHelper.AllStandardRoles : CustomRolesHelper.AllHASRoles;
        public static OptionItem OptionAssignMode;
        public static OptionItem OptionAssignMadmateFromCrewmateSlot;
        public static OptionItem OptionAssignMadmateFromCrewmateSlotMax;
        public static OptionItem OptionAssignPerPlayerCount;
        // 参加/退出のたびに人数別設定を自動で反映するか(OFF=試合開始前にだけ反映して負荷を抑える)
        public static OptionItem OptionAssignPerPlayerCountAuto;
        private static Dictionary<CustomRoleTypes, RandomAssignOptions> RandomAssignOptionsCollection = new(CustomRolesHelper.AllRoleTypes.Length);
        // 要望により追加: 3人〜15人それぞれに個別の配役数設定を持たせるための辞書。
        private const int MinSupportedPlayerCount = 3;
        private const int MaxSupportedPlayerCount = 15;
        private static Dictionary<int, PerCountAssignOptions> PerCountOptionsCollection = new();

        private static string GetColoredRoleTypeName(CustomRoleTypes roleTypes)
        {
            var name = GetString($"CustomRoleTypes.{roleTypes}");
            return roleTypes switch
            {
                CustomRoleTypes.Crewmate => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Crewmate), name),
                CustomRoleTypes.Impostor => Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Impostor), name),
                CustomRoleTypes.Madmate => Utils.ColorString(ModColors.MadMateOrenge, name),
                CustomRoleTypes.Neutral => Utils.ColorString(ModColors.Gray, name),
                _ => name,
            };
        }

        /// <summary>現在のプレイヤー人数に対して有効な人数別設定があれば取得する。</summary>
        private static bool TryGetPerCountOverride(out PerCountAssignOptions options)
        {
            options = null;
            if (OptionAssignPerPlayerCount?.GetBool() != true) return false;
            if (GameData.Instance == null) return false;
            var count = GetAssignTargetPlayerCount();
            if (!PerCountOptionsCollection.TryGetValue(count, out var pc)) return false;
            if (pc.Enabled?.GetBool() != true) return false;
            options = pc;
            return true;
        }

        /// <summary>
        /// アサインモードの「人数ごとの設定」の判定に使う人数を返す。
        /// GMは実際に役職を割り振られるプレイヤーではないため、GM機能が有効な場合は
        /// 接続人数から1人分減らしてから、人数ごとの設定(3人〜15人)と突き合わせる。
        /// (例: 10人部屋+GM ON の場合、実際にアサインされるのは9人分なので、
        ///  9人設定の枠を見るようにする)
        /// </summary>
        private static int GetAssignTargetPlayerCount()
        {
            var count = GameData.Instance?.PlayerCount ?? 0;
            if (Options.EnableGM?.GetBool() == true) count -= 1;
            return Math.Max(0, count);
        }

        private static int EffectiveMin(CustomRoleTypes roleTypes)
            => TryGetPerCountOverride(out var pc) ? pc.Min(roleTypes) : RandomAssignOptionsCollection[roleTypes].Min;
        private static int EffectiveMax(CustomRoleTypes roleTypes)
            => TryGetPerCountOverride(out var pc) ? pc.Max(roleTypes) : RandomAssignOptionsCollection[roleTypes].Max;
        private static int EffectiveMadFromCrewMax()
            => TryGetPerCountOverride(out var pc) ? pc.MadFromCrewMax : GlobalMaxMadmateFromCrewmateSlotInRandom;
        private static Dictionary<CustomRoleTypes, int> AssignCount = new(CustomRolesHelper.AllRoleTypes.Length);
        private static List<CustomRoles> AssignRoleList = new(CustomRolesHelper.AllRoles.Length);
        private static bool UseCrewmateSlotForMadmateInRandom
            => AssignMode == AssignAlgorithm.Random && OptionAssignMadmateFromCrewmateSlot?.GetBool() == true;
        private static int GlobalMaxMadmateFromCrewmateSlotInRandom
            => OptionAssignMadmateFromCrewmateSlotMax?.GetInt() ?? 15;

        // ===== モデレーターコマンド(/cmd ma, /cmd mn)向けの公開API =====
        // アサインモードが「ランダム」の場合のみ、配役数の最大/最小をコマンドで変更できるようにする。
        public static bool IsRandomAssignMode => AssignMode == AssignAlgorithm.Random;

        /// <summary>
        /// I(インポスター)/M(マッドメイト)/C(クルーメイト)/N(ニュートラル) の1文字を
        /// 対応するCustomRoleTypesへ変換する。該当しなければnull。
        /// </summary>
        public static CustomRoleTypes? ParseRoleTypeLetter(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return null;
            return letter.Trim().ToUpperInvariant() switch
            {
                "I" => CustomRoleTypes.Impostor,
                "M" => CustomRoleTypes.Madmate,
                "C" => CustomRoleTypes.Crewmate,
                "N" => CustomRoleTypes.Neutral,
                _ => null,
            };
        }

        /// <summary>最大配役数を変更する。成功時true。範囲外の値等で失敗時false。</summary>
        public static bool TrySetMaxAssignCount(CustomRoleTypes roleType, int value)
        {
            if (!RandomAssignOptionsCollection.TryGetValue(roleType, out var option)) return false;
            return option.SetMaxValue(value);
        }

        /// <summary>最小配役数を変更する。成功時true。範囲外の値等で失敗時false。</summary>
        public static bool TrySetMinAssignCount(CustomRoleTypes roleType, int value)
        {
            if (!RandomAssignOptionsCollection.TryGetValue(roleType, out var option)) return false;
            return option.SetMinValue(value);
        }

        /// <summary>指定した陣営種別の、設定可能な人数の上限(スライダーの最大値)を取得する。</summary>
        public static int GetAssignCountLimit(CustomRoleTypes roleType)
            => RandomAssignOptionsCollection.TryGetValue(roleType, out var option) ? option.MaxLimit : 15;

        private static CustomRoleTypes NormalizeRandomAssignRoleType(CustomRoleTypes roleType)
            => UseCrewmateSlotForMadmateInRandom && roleType == CustomRoleTypes.Madmate
                ? CustomRoleTypes.Crewmate
                : roleType;
        private static Dictionary<CustomRoleTypes, int> GetRequiredAssignCounts(IEnumerable<CustomRoles> roles)
        {
            var counts = new Dictionary<CustomRoleTypes, int>();
            foreach (var role in roles)
            {
                var type = NormalizeRandomAssignRoleType(role.GetAssignRoleType());
                counts[type] = counts.TryGetValue(type, out var current) ? current + 1 : 1;
            }
            return counts;
        }
        public static void SetupOptionItem()
        {
            OptionAssignMode = StringOptionItem.Create(idStart, "AssignMode", AssignModeSelections, 0, TabGroup.MainSettings, false)
                .SetHeader(true)
                .SetColorcode("#48a630");

            assignMode = () => (AssignAlgorithm)OptionAssignMode.GetInt();
            RandomAssignOptionsCollection.Clear();
            RandomAssignOptions.Create(10, OptionAssignMode, CustomRoleTypes.Impostor, 3);
            RandomAssignOptions.Create(20, OptionAssignMode, CustomRoleTypes.Madmate);
            RandomAssignOptions.Create(30, OptionAssignMode, CustomRoleTypes.Crewmate);
            RandomAssignOptions.Create(40, OptionAssignMode, CustomRoleTypes.Neutral);
            OptionAssignMadmateFromCrewmateSlot = BooleanOptionItem.Create(idStart + 50, "AssignMadmateFromCrewmateSlot", false, TabGroup.MainSettings, false)
                .SetParent(OptionAssignMode)
                .SetEnabled(() => OptionAssignMode.GetBool());
            OptionAssignMadmateFromCrewmateSlotMax = IntegerOptionItem.Create(idStart + 51, "AssignMadmateFromCrewmateSlotMax", new(0, 15, 1), 15, TabGroup.MainSettings, false)
                .SetParent(OptionAssignMadmateFromCrewmateSlot)
                .SetValueFormat(OptionFormat.Players)
                .SetEnabled(() => OptionAssignMode.GetBool() && OptionAssignMadmateFromCrewmateSlot.GetBool());

            // ===== 要望により追加: 人数(3人〜15人)ごとの個別設定 =====
            OptionAssignPerPlayerCount = BooleanOptionItem.Create(idStart + 60, "AssignPerPlayerCount", false, TabGroup.MainSettings, false)
                .SetParent(OptionAssignMode)
                .SetEnabled(() => OptionAssignMode.GetBool());

            OptionAssignPerPlayerCountAuto = BooleanOptionItem.Create(idStart + 61, "AssignPerPlayerCountAuto", true, TabGroup.MainSettings, false)
                .SetParent(OptionAssignPerPlayerCount)
                .SetEnabled(() => OptionAssignMode.GetBool() && OptionAssignPerPlayerCount.GetBool());
            PerCountOptionsCollection.Clear();
            const int perCountIdBase = 590000;
            const int perCountIdStep = 20; // 1人分につきヘッダー+9項目=10使用。将来の項目追加に備え余裕を持たせる。
            for (var count = MinSupportedPlayerCount; count <= MaxSupportedPlayerCount; count++)
            {
                var idBase = perCountIdBase + (count - MinSupportedPlayerCount) * perCountIdStep;
                PerCountOptionsCollection[count] = new PerCountAssignOptions(idBase, OptionAssignPerPlayerCount, count);
            }
        }
        public static (bool, int, int) CheckRoleTypeCount(CustomRoleTypes role)
        {
            if (AssignMode == AssignAlgorithm.Fixed) return (false, 0, 0);
            if (RandomAssignOptionsCollection.TryGetValue(role, out _))
            {
                return (true, EffectiveMax(role), EffectiveMin(role));
            }
            return (true, -1, -1);
        }
        public static bool CheckRoleCount()
        {
            if (AssignMode == AssignAlgorithm.Fixed) return true;
            ApplyPerCountSettings("開始チェック");
            var result = true;
            var opt = Main.NormalOptions.Cast<IGameOptions>();

            // GM有効時はGM自身が役職アサインの対象にならないため、実際にアサインされる
            // 人数(GM分を除いた人数)を基準に整合性チェックする。
            var playerCount = GetAssignTargetPlayerCount();
            var numImpostors = Math.Min(playerCount, opt.GetInt(Int32OptionNames.NumImpostors));

            var min = EffectiveMin(CustomRoleTypes.Impostor);
            var max = EffectiveMax(CustomRoleTypes.Impostor);
            if (min > max || min > numImpostors || max > numImpostors)
            {
                var msg = GetString("Warning.NotMatchImpostorCount");
                Logger.seeingame(msg);
                Logger.Warn(msg, "BeginGame");
                result = false;
            }
            var roleMinCount = 0;
            foreach (var roleType in RandomAssignOptionsCollection.Keys)
                roleMinCount += EffectiveMin(roleType);
            if (roleMinCount > playerCount)
            {
                var msg = GetString("Warning.NotMatchRoleCount");
                Logger.seeingame(msg);
                Logger.Warn(msg, "BeginGame");
                result = false;
            }

            return result;
        }
        /// <summary>
        /// 現在の人数(GMがONの場合はGMを人数に含めない)に対応する「人数ごとの設定」を、
        /// 通常の最少/最大人数の設定へ反映する。値が変わらない項目は触らない(負荷軽減)。
        /// </summary>
        public static void ApplyPerCountSettings(string reason)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (AssignMode != AssignAlgorithm.Random) return;
                if (!TryGetPerCountOverride(out var pc)) return;
                var changed = false;
                foreach (var kv in RandomAssignOptionsCollection)
                {
                    var type = kv.Key;
                    var option = kv.Value;
                    var newMax = pc.Max(type);
                    var newMin = pc.Min(type);
                    if (option.Max != newMax && option.SetMaxValue(newMax)) changed = true;
                    if (option.Min != newMin && option.SetMinValue(newMin)) changed = true;
                }
                if (OptionAssignMadmateFromCrewmateSlot?.GetBool() == true && OptionAssignMadmateFromCrewmateSlotMax != null)
                {
                    var madMax = Math.Clamp(pc.MadFromCrewMax, 0, 15);
                    if (OptionAssignMadmateFromCrewmateSlotMax.GetInt() != madMax)
                    {
                        OptionAssignMadmateFromCrewmateSlotMax.SetValue(madMax);
                        changed = true;
                    }
                }
                if (changed) Logger.Info($"人数別アサイン設定を反映({reason}): {GetAssignTargetPlayerCount()}人", "RoleAssignManager");
            }
            catch (Exception e)
            {
                Logger.Warn($"ApplyPerCountSettings: {e.Message}", "RoleAssignManager");
            }
        }
        private static bool perCountApplyScheduled;
        /// <summary>ロビーで人が増減した時に呼ぶ。「自動で変更」がONの時だけ、少し待ってから(連続参加をまとめて)反映する。</summary>
        public static void OnLobbyPlayerCountChanged()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (!GameStates.IsLobby) return;
            if (OptionAssignPerPlayerCount?.GetBool() != true || OptionAssignPerPlayerCountAuto?.GetBool() != true) return;
            if (perCountApplyScheduled) return;
            perCountApplyScheduled = true;
            _ = new LateTask(() =>
            {
                perCountApplyScheduled = false;
                if (GameStates.IsLobby) ApplyPerCountSettings("参加/退出");
            }, 0.5f, "ApplyPerCountSettings", true);
        }
        public static void SelectAssignRoles()
        {
            // 「自動で変更」がOFFでも、試合開始前には必ず人数別設定を反映する
            ApplyPerCountSettings("試合開始前");
            AssignCount.Clear();
            AssignRoleList.Clear();

            switch (AssignMode)
            {
                case AssignAlgorithm.Fixed:
                    SetFixedAssignRole();
                    SetAddOnsList();
                    break;
                case AssignAlgorithm.Random:
                    SetRandomAssignCount();
                    SetRandomAssignRoleList();
                    SetAddOnsList();
                    break;
            }

            if (AssignRoleList.Contains(CustomRoles.LoversBreaker)
                && LoversBreaker.ShouldRemoveFromAssignment(AssignRoleList))
            {
                int removedCount = AssignRoleList.RemoveAll(role => role == CustomRoles.LoversBreaker);
                if (AssignCount.TryGetValue(CustomRoleTypes.Neutral, out var neutralCount))
                    AssignCount[CustomRoleTypes.Neutral] = Math.Max(0, neutralCount - removedCount);
                Logger.Info($"ラバーズ系役職が配役されなかったため爆ぜ師を{removedCount}人分除外", "AssignRoleList");
            }

            AssignRoleList.Sort();

            if (SuddenDeathMode.SuddenSharingRoles.GetBool())
            {
                var roles = AssignRoleList.Where(role => role != CustomRoles.Impostor && !role.IsAddOn() && !role.IsGhostRole() && !role.IsLovers()).ToArray();
                var addons = AssignRoleList.Where(role => role.IsAddOn() || role.IsLovers())?.ToArray();
                var rand = IRandom.Instance;
                var role = CustomRoles.Impostor;

                if (roles.Length != 0) role = roles[rand.Next(0, roles.Length)];

                AssignRoleList.Clear();

                for (var i = 0; i <= PlayerCatch.AllPlayerControls.Count() + 1; i++)
                    AssignRoleList.Add(role);

                if (addons.Length != 0)
                    foreach (var addon in addons)
                        if (!AssignRoleList.Contains(addon)) AssignRoleList.Add(addon);
            }
            else
                if (Modules.SuddenDeathMode.NowSuddenDeathMode)
                {
                    var roles = AssignRoleList.Where(role => role != CustomRoles.Impostor && !role.IsAddOn() && !role.IsGhostRole() && !role.IsLovers()).ToArray();

                    if (roles.Length < PlayerCatch.AllPlayerControls.Count())
                    {
                        for (var i = roles.Length; i < PlayerCatch.AllPlayerControls.Count(); i++)
                        {
                            AssignRoleList.Add(CustomRoles.Impostor);
                        }
                    }
                }

            foreach (var role in AssignRoleList)
            {
                if (role.IsCombinationRole())
                {
                    if (AssignRoleList.Contains(role.GetCombination()) is false)
                    {
                        AssignRoleList.Remove(role);
                        AssignCount[role.GetCustomRoleTypes()]--;
                        Logger.Error($"{role} - {role.GetCombination()}が無いため、片方もアサインされない", "AssignError");
                    }
                }
            }
            Logger.Info($"{string.Join(", ", AssignCount)}", "AssignCount");
            Logger.Info($"{string.Join(", ", AssignRoleList)}", "AssignRoleList");
        }
        ///<summary>
        ///役職の固定アサイン抽選
        ///chanceが10%以上の役職を全て追加
        ///</summary>
        private static void SetFixedAssignRole()
        {
            //インポスター以外の人数
            int numImpostorsLeft = Math.Min(GameData.Instance.PlayerCount, Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors));
            //マッド、クルー、ニュートラル合計の限界値
            int numOthersLeft = GameData.Instance.PlayerCount - numImpostorsLeft;

            foreach (var _role in GetCandidateRoleList(10).OrderBy(x => Guid.NewGuid()))
            {
                if (numImpostorsLeft <= 0 && numOthersLeft <= 0) break;

                var role = _role;
                var result = false;
                SlotRoleAssign.SlotRoles.OrderBy(x => Guid.NewGuid()).ToList().Do(info =>
                {
                    var id = info.CheckAssignRole(ref role);
                    result = (result || id is 1) && id is not 2;
                    if (id is 2) return;
                });
                if (result) continue;

                var targetRoles = role.GetAssignUnitRolesArray();
                var numImpostorAssign = targetRoles.Count(role => role.GetAssignRoleType() == CustomRoleTypes.Impostor);
                var numOthersAssign = targetRoles.Length - numImpostorAssign;
                //アサイン枠が足りてない場合
                if ((numImpostorAssign > numImpostorsLeft || numOthersAssign > numOthersLeft) && Options.CurrentGameMode is not CustomGameMode.SuddenDeath) continue;

                AssignRoleList.AddRange(targetRoles);
                numImpostorsLeft -= numImpostorAssign;
                numOthersLeft -= numOthersAssign;
            }

            foreach (var roleType in CustomRolesHelper.AllRoleTypes)
            {
                var count = AssignRoleList.Count(role => role.GetAssignRoleType() == roleType);
                AssignCount.Add(roleType, count);
            }
        }
        ///<summary>
        ///設定と実際の人数から各役職のアサイン数を決定
        ///</summary>
        private static void SetRandomAssignCount()
        {
            var rand = IRandom.Instance;
            int numImpostors = Math.Min(GameData.Instance.PlayerCount, Main.RealOptionsData.GetInt(Int32OptionNames.NumImpostors));
            //インポスター以外の人数
            //マッド、クルー、ニュートラル合計の限界値
            int numOthers = GameData.Instance.PlayerCount - numImpostors;

            List<CustomRoleTypes> otherRoleTypesList = new();
            if (numOthers > 0) //マッド、クルー、ニュートラルの人数決定
            {
                var otherRoleTypes = RandomAssignOptionsCollection.Keys.Where(x => x != CustomRoleTypes.Impostor).ToList();
                //一旦最少人数を設定
                foreach (var roleType in otherRoleTypes)
                    otherRoleTypesList.AddRange(Enumerable.Repeat(roleType, EffectiveMin(roleType)).ToList());

                //超えている場合はランダムに削除
                while (otherRoleTypesList.Count > numOthers)
                    otherRoleTypesList.RemoveAt(rand.Next(otherRoleTypesList.Count));

                int numAdditional = numOthers - otherRoleTypesList.Count;
                if (numAdditional > 0) //最少人数で限界値に満たない場合
                {
                    List<CustomRoleTypes> additionalList = new();
                    foreach (var roleType in otherRoleTypes)
                    {
                        //追加人数を取得
                        int additionalCount = Math.Max(0, rand.Next(EffectiveMax(roleType) - EffectiveMin(roleType) + 1));

                        additionalList.AddRange(Enumerable.Repeat(roleType, additionalCount).ToList());
                    }

                    //超えている場合はランダムに削除
                    while (additionalList.Count > numAdditional)
                        additionalList.RemoveAt(rand.Next(additionalList.Count));

                    otherRoleTypesList.AddRange(additionalList);
                }
            }

            //Dictionaryに変換
            foreach (var roleTypes in RandomAssignOptionsCollection.Keys)
            {
                if (roleTypes == CustomRoleTypes.Impostor)
                {
                    int impAssignCount = Math.Min(numImpostors, rand.Next(EffectiveMin(roleTypes), EffectiveMax(roleTypes) + 1));
                    AssignCount.Add(roleTypes, impAssignCount);
                }
                else
                    AssignCount.Add(roleTypes, otherRoleTypesList.Count(x => x == roleTypes));
            }
        }
        ///<summary>
        ///役職のアサイン抽選
        ///既に決まったアサイン枠数に合わせて決定
        ///</summary>
        private static void SetRandomAssignRoleList()
        {
            List<(CustomRoles, int)> randomRoleTicketPool = new(); //ランダム抽選時のプール
            var rand = IRandom.Instance;
            var assignCount = new Dictionary<CustomRoleTypes, int>(AssignCount); //アサイン枠のDictionary
            int assignedMadmateFromCrewSlot = 0;

            if (UseCrewmateSlotForMadmateInRandom)
            {
                var madmateCount = assignCount.TryGetValue(CustomRoleTypes.Madmate, out var madCount) ? madCount : 0;
                assignCount[CustomRoleTypes.Crewmate] = (assignCount.TryGetValue(CustomRoleTypes.Crewmate, out var crewCount) ? crewCount : 0) + madmateCount;
                assignCount[CustomRoleTypes.Madmate] = 0;
            }

            foreach (var role in GetCandidateRoleList(100).OrderBy(x => Guid.NewGuid()))
            {
                var targetRoles = role.GetAssignUnitRolesArray();
                var requiredCounts = GetRequiredAssignCounts(targetRoles);
                if (UseCrewmateSlotForMadmateInRandom)
                {
                    var madmateNeed = targetRoles.Count(x => x.GetAssignRoleType() == CustomRoleTypes.Madmate);
                    if (assignedMadmateFromCrewSlot + madmateNeed > EffectiveMadFromCrewMax()) continue;
                }
                //アサイン枠が足りてない場合
                if (requiredCounts.Any(kvp => !assignCount.TryGetValue(kvp.Key, out var count) || kvp.Value > count)) continue;

                foreach (var _targetRole in targetRoles)
                {
                    var targetRole = _targetRole;
                    var result = false;
                    SlotRoleAssign.SlotRoles.OrderBy(x => Guid.NewGuid()).ToList().Do(info =>
                    {
                        var id = info.CheckAssignRole(ref targetRole);
                        result = (result || id is 1) && id is not 2;
                        if (id is 2) return;
                    });
                    if (result) continue;
                    AssignRoleList.Add(targetRole);
                    var targetRoleType = NormalizeRandomAssignRoleType(targetRole.GetAssignRoleType());
                    if (assignCount.ContainsKey(targetRoleType))
                        assignCount[targetRoleType]--;
                    if (UseCrewmateSlotForMadmateInRandom && targetRole.GetAssignRoleType() == CustomRoleTypes.Madmate)
                        assignedMadmateFromCrewSlot++;
                }
            }

            if (assignCount.All(kvp => kvp.Value <= 0)) return;

            foreach (var role in AllMainRoles.OrderBy(x => Guid.NewGuid())) //確定枠が偏らないようにシャッフル
            {
                if (!Event.CheckRole(role)) continue;
                if (!role.IsAssignable()) continue;

                var chance = role.GetChance();
                var count = role.GetCount();
                if (chance is 0 or 100) continue;
                if (count == 0) continue;
                //確率がそのまま追加枚数に
                for (var i = 0; i < count; i++)
                    randomRoleTicketPool.AddRange(Enumerable.Repeat((role, i), chance / 10).ToList());
            }

            //確定分では足りない場合に抽選を行う
            while (assignCount.Any(kvp => kvp.Value > 0) && randomRoleTicketPool.Count > 0)
            {
                var selectedTicket = randomRoleTicketPool[rand.Next(randomRoleTicketPool.Count)];
                var targetRoles = selectedTicket.Item1.GetAssignUnitRolesArray();
                var requiredCounts = GetRequiredAssignCounts(targetRoles);
                if (UseCrewmateSlotForMadmateInRandom)
                {
                    var madmateNeed = targetRoles.Count(x => x.GetAssignRoleType() == CustomRoleTypes.Madmate);
                    if (assignedMadmateFromCrewSlot + madmateNeed > EffectiveMadFromCrewMax())
                    {
                        randomRoleTicketPool.RemoveAll(x => x == selectedTicket);
                        continue;
                    }
                }
                //アサイン枠が足りていれば追加
                if (requiredCounts.All(kvp => assignCount.TryGetValue(kvp.Key, out var count) && kvp.Value <= count))
                {
                    foreach (var _targetRole in targetRoles)
                    {
                        var targetRole = _targetRole;
                        var result = false;
                        SlotRoleAssign.SlotRoles.OrderBy(x => Guid.NewGuid()).ToList().Do(info =>
                        {
                            var id = info.CheckAssignRole(ref targetRole);
                            result = (result || id is 1) && id is not 2;//割り当て済みならtrue。別アサインされるならfalseにもどす
                            if (id is 2) return;//別アサインを見つけたなら一回きりあげる
                        });
                        if (result) continue;
                        AssignRoleList.Add(targetRole);
                        assignCount[NormalizeRandomAssignRoleType(targetRole.GetAssignRoleType())]--;
                        if (UseCrewmateSlotForMadmateInRandom && targetRole.GetAssignRoleType() == CustomRoleTypes.Madmate)
                            assignedMadmateFromCrewSlot++;
                    }
                }
                //1-9個ある同じチケットを削除
                randomRoleTicketPool.RemoveAll(x => x == selectedTicket);
            }
        }
        ///<summary>
        ///属性のアサイン抽選
        ///枠制限が無いので個別に抽選
        ///</summary>
        private static void SetAddOnsList()
        {
            //固定だとしても確率でアサインさせる
            foreach (var subRole in CustomRolesHelper.AllAddOns)
            {
                if (!Event.CheckRole(subRole)) continue;

                var chance = subRole.GetChance();
                var count = subRole.GetAssignCount();
                if (chance == 0 || count == 0) continue;
                var rnd = IRandom.Instance;
                for (var i = 0; i < count; i++) //役職の単位数ごとに抽選
                    if (rnd.Next(100) < chance)
                        AssignRoleList.AddRange(subRole.GetAssignUnitRolesArray());
            }
        }
        public static List<CustomRoles> GetCandidateRoleList(int availableRate, bool shutoku = false)
        {
            var candidateRoleList = new List<CustomRoles>();
            foreach (var role in AllMainRoles)
            {
                if (!Event.CheckRole(role)) continue;

                if (!shutoku)
                    if (!role.IsAssignable()) continue;

                var chance = role.GetChance();
                var count = role.GetAssignCount();
                if (chance < availableRate || count == 0) continue;
                if (Options.CustomRoleSpawnChances.TryGetValue(role, out var option))
                {
                    if ((option.Tag != CustomOptionTags.All && !GameModeManager.GetTags(Options.CurrentGameMode).Contains(option.Tag))
                    || GameModeManager.GetTags(Options.CurrentGameMode).Any(tag => option.DisableTag.Contains(tag))) continue;
                }
                candidateRoleList.AddRange(Enumerable.Repeat(role, count).ToList());
            }
            return candidateRoleList;
        }
        private static RoleAssignInfo GetRoleAssignInfo(this CustomRoles role) =>
            CustomRoleManager.GetRoleInfo(role)?.AssignInfo;
        private static CustomRoleTypes GetAssignRoleType(this CustomRoles role) =>
            role.GetRoleAssignInfo()?.AssignRoleType ?? role.GetCustomRoleTypes();
        private static bool IsAssignable(this CustomRoles role)
            => role.GetRoleAssignInfo()?.IsInitiallyAssignable ?? true;
        /// <summary>
        /// アサインの抽選回数
        /// </summary>
        private static int GetAssignCount(this CustomRoles role)
        {
            int maximumCount = role.GetCount();
            int assignUnitCount = role.GetRoleAssignInfo()?.AssignUnitCount ??
                role switch
                {
                    CustomRoles.Lovers => 2,
                    CustomRoles.RedLovers => 2,
                    CustomRoles.YellowLovers => 2,
                    CustomRoles.BlueLovers => 2,
                    CustomRoles.GreenLovers => 2,
                    CustomRoles.WhiteLovers => 2,
                    CustomRoles.PurpleLovers => 2,
                    CustomRoles.OneLove => 1,
                    _ => 1,
                };
            return maximumCount / assignUnitCount;
        }
        ///<summary>
        ///RoleOptionのKey => 実際にアサインされる役職の配列
        ///両陣営役職、コンビ役職向け
        ///</summary>
        private static CustomRoles[] GetAssignUnitRolesArray(this CustomRoles role)
            => role.GetRoleAssignInfo()?.AssignUnitRoles ??
            role switch
            {
                CustomRoles.Lovers => new CustomRoles[2] { CustomRoles.Lovers, CustomRoles.Lovers },
                CustomRoles.RedLovers => new CustomRoles[2] { CustomRoles.RedLovers, CustomRoles.RedLovers },
                CustomRoles.YellowLovers => new CustomRoles[2] { CustomRoles.YellowLovers, CustomRoles.YellowLovers },
                CustomRoles.BlueLovers => new CustomRoles[2] { CustomRoles.BlueLovers, CustomRoles.BlueLovers },
                CustomRoles.GreenLovers => new CustomRoles[2] { CustomRoles.GreenLovers, CustomRoles.GreenLovers },
                CustomRoles.WhiteLovers => new CustomRoles[2] { CustomRoles.WhiteLovers, CustomRoles.WhiteLovers },
                CustomRoles.PurpleLovers => new CustomRoles[2] { CustomRoles.PurpleLovers, CustomRoles.PurpleLovers },
                CustomRoles.Faction => [],
                _ => new CustomRoles[1] { role },
            };
        public static bool IsPresent(this CustomRoles role) => AssignRoleList.Any(x => x == role);
        public static int GetRealCount(this CustomRoles role) => AssignRoleList.Count(x => x == role);
    }
    public class RoleAssignInfo
    {
        public RoleAssignInfo(CustomRoles role, CustomRoleTypes roleType)
        {
            AssignRoleType = roleType;
            IsInitiallyAssignableCallBack = () => true;
            AssignCountRule =
                roleType == CustomRoleTypes.Impostor ? new(1, 3, 1) : new(1, 15, 1);
            AssignUnitRoles =
                Enumerable.Repeat(role, AssignCountRule.Step).ToArray();
        }
        /// <summary>
        /// どのアサイン枠を消費するか
        /// </summary>
        public CustomRoleTypes AssignRoleType { get; init; }
        /// <summary>
        /// 試合開始時にアサインされるかどうかのデリゲート
        /// </summary>
        public Func<bool> IsInitiallyAssignableCallBack { get; init; }
        public bool IsInitiallyAssignable => IsInitiallyAssignableCallBack.Invoke();
        /// <summary>
        /// 人数設定の最小人数, 最大人数, 一単位数
        /// </summary>
        public IntegerValueRule AssignCountRule { get; init; }
        /// <summary>
        /// 人数設定に対し何人単位でアサインするか
        /// 役職の抽選回数 = 設定人数 / AssignUnitCount
        /// </summary>
        public int AssignUnitCount => AssignCountRule.Step;
        /// <summary>
        /// 実際にアサインされる役職の内訳
        /// </summary>
        public CustomRoles[] AssignUnitRoles { get; init; }
    }
}
