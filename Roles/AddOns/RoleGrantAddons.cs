using System.Collections.Generic;
using TownOfHost.Roles.Core;

namespace TownOfHost
{
    // 全役職の「この役職に付ける属性」: 属性の抽選とは別に、その役職になった人に選んだ属性を必ず付ける
    // (属性を確定にしなくても、テロリストにだけシーラーを付ける、などができる)
    public static class RoleGrantAddons
    {
        // 役職の設定の ID とぶつからないよう、離れた所を使う
        const int IdOffset = 3_000_000;
        static readonly Dictionary<CustomRoles, AssignOptionItem> AllData = new();
        static readonly List<(int id, TabGroup tab, CustomRoles role, OptionItem parent, CustomOptionTags tag)> reserved = new();

        // 役職の設定を作る時に覚えておき、全部の役職の後でまとめて作る
        // (役職の人数のすぐ後に作ると、役職の画面でほかの設定と重なって表示された)
        public static void Reserve(int roleId, TabGroup tab, CustomRoles role, OptionItem parent, CustomOptionTags tag)
        {
            if (role is CustomRoles.NotAssigned or CustomRoles.GM || role.IsAddOn() || role.IsGhostRole()) return;
            reserved.Add((roleId, tab, role, parent, tag));
        }

        public static void CreateAll()
        {
            foreach (var (roleId, tab, role, parent, tag) in reserved)
            {
                if (AllData.ContainsKey(role)) continue;
                var opt = (AssignOptionItem)AssignOptionItem.Create(IdOffset + roleId, "GrantAddons", 0, tab, false, addon: true,
                    notassing: [CustomRoles.LastImpostor, CustomRoles.LastNeutral, CustomRoles.Workhorse, CustomRoles.Amanojaku]);
                opt.SetParent(parent).SetParentRole(role).SetTag(tag);
                AllData[role] = opt;
            }
            reserved.Clear();
        }

        // 役職と属性の抽選が終わった後に呼ぶ
        public static void Assign()
        {
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc == null) continue;
                var role = pc.GetCustomRole();
                if (!AllData.TryGetValue(role, out var opt)) continue;
                var list = opt.GetNowRoleValue();
                if (list.Count == 0) continue;
                var state = PlayerState.GetByPlayerId(pc.PlayerId);
                if (state == null) continue;
                foreach (var addon in list)
                {
                    if (pc.Is(addon) || !CustomRolesHelper.CanHaveSubRole(role, addon)) continue;
                    state.SetSubRole(addon);
                    Logger.Info($"役職設定:{pc.Data?.GetLogPlayerName()} = {role} + {addon} (役職に付ける属性)", "AssignCustomSubRoles");
                }
            }
        }
    }
}
