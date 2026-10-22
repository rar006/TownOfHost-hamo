using System.Collections.Generic;
using UnityEngine;
using TownOfHost.Roles.Core;
using static TownOfHost.Options;

namespace TownOfHost.Roles.AddOns.Common
{
    public static class Revenger
    {
        private static readonly int Id = 72500;
        private static Color RoleColor = UtilsRoleText.GetRoleColor(CustomRoles.Revenger);
        public static string SubRoleMark = Utils.ColorString(RoleColor, "я");
        public static List<byte> playerIdList = new();
        public static OptionItem RevengeToImpostor;
        public static OptionItem RevengeToCrewmate;
        public static OptionItem RevengeToMadmate;
        public static OptionItem RevengeToNeutral;
        // 追放された時に道連れにする人数(リベンジャー以外の道連れ役職も含む)
        public static OptionItem ExileRevengeCount;
        // 道連れにした相手がリベンジャーなら、その人もさらに道連れを起こす
        public static OptionItem ChainRevenge;
        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Revenger, fromtext: UtilsOption.GetFrom(From.TownOfHost_Y));
            AddOnsAssignData.Create(Id + 10, CustomRoles.Revenger, true, true, true, true);
            ObjectOptionitem.Create(Id + 54, "AddonOption", true, "", TabGroup.Addons).SetOptionName(() => "Role Option").SetSubRoleOptionItem(CustomRoles.Revenger);
            RevengeToImpostor = BooleanOptionItem.Create(Id + 50, "NekoKabochaImpostorsGetRevenged", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Revenger);
            RevengeToCrewmate = BooleanOptionItem.Create(Id + 51, "RevengeToCrewmate", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Revenger);
            RevengeToMadmate = BooleanOptionItem.Create(Id + 52, "NekoKabochaMadmatesGetRevenged", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Revenger);
            RevengeToNeutral = BooleanOptionItem.Create(Id + 53, "RevengeToNeutral", true, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Revenger);
            ExileRevengeCount = IntegerOptionItem.Create(Id + 55, "RevengerExileCount", new(1, 15, 1), 1, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Revenger)
                .SetValueFormat(OptionFormat.Players)
                .SetOptionName(() => "追放された時に道連れにする人数(道連れする役職すべて)");
            ChainRevenge = BooleanOptionItem.Create(Id + 56, "RevengerChain", false, TabGroup.Addons, false).SetSubRoleOptionItem(CustomRoles.Revenger)
                .SetOptionName(() => "道連れにした相手がリベンジャーなら、さらに道連れを起こす");
        }
        public static void Init()
        {
            playerIdList = new();
        }
        public static void Add(byte playerId)
        {
            playerIdList.Add(playerId);
        }
    }
}