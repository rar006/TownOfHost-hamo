using System;

using System.Collections.Generic;

using System.Linq;

using AmongUs.GameOptions;

using Hazel;

using TownOfHost.Attributes;

using TownOfHost.Modules;

using TownOfHost.Roles;

using TownOfHost.Roles.Core;

using TownOfHost.Roles.Core.Interfaces;



namespace TownOfHost;



static class Event

{

    public const int OutOfPeriodRoleIdOffset = 5;



    public static bool IsChristmas = DateTime.Now.Month == 12 && DateTime.Now.Day is 24 or 25;

    public static bool White = DateTime.Now.Month == 3 && DateTime.Now.Day is 14;

    public static bool IsInitialRelease = DateTime.Now.Month == 10 && DateTime.Now.Day is 31;

    public static bool IsHalloween = (DateTime.Now.Month == 10 && DateTime.Now.Day is 31) || (DateTime.Now.Month == 11 && DateTime.Now.Day is 1 or 2 or 3 or 4 or 5 or 6 or 7);

    public static bool GoldenWeek = DateTime.Now.Month == 5 && DateTime.Now.Day is 3 or 4 or 5;

    public static bool April = DateTime.Now.Month == 4 && DateTime.Now.Day is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8;

    public static bool Tanabata = DateTime.Now.Month == 7 && DateTime.Now.Day is > 6 and < 15;

    public static bool IsEventDay => IsChristmas || White || IsInitialRelease || IsHalloween || GoldenWeek || April;

    public static bool Special = false;

    public static bool NowRoleEvent => false;

    public static List<string> OptionLoad = new();


    public static bool IsEventRole(CustomRoles role) => EventRoles.ContainsKey(role);

    public static int GetRoleId(CustomRoles role, bool isInEventPeriod)

    {

        var roleId = (int)role;

        if (!IsEventRole(role)) return roleId;

        return isInEventPeriod ? roleId : roleId + OutOfPeriodRoleIdOffset;

    }



    public static int GetCurrentRoleId(CustomRoles role, bool useApiData = true)

    {

        if (!IsEventRole(role)) return (int)role;

        return GetRoleId(role, CheckRole(role, useApiData));

    }



    public static int GetConfigId(CustomRoles role, int configId, bool isInEventPeriod)

    {

        if (!IsEventRole(role)) return configId;

        return isInEventPeriod ? configId : configId + OutOfPeriodRoleIdOffset;

    }



    public static int GetCurrentConfigId(CustomRoles role, int configId, bool useApiData = true)

    {

        if (!IsEventRole(role)) return configId;

        return GetConfigId(role, configId, CheckRole(role, useApiData));

    }



    /// <summary>

    /// 期間限定ロールが使用可能かをチェックします<br/>

    /// 通常ロールはtrueを返します

    /// </summary>

    /// <returns>ロールが使用可能ならtrueを返します</returns>

    public static bool CheckRole(CustomRoles role, bool useApiData = true)

    {

        //イベント役職以外はtrueを返す!!

        if (!EventRoles.TryGetValue(role, out var check)) return true;



        //キャッシュ済みならそっちを使う

        if (cachedEventFlags.TryGetValue((role, useApiData), out bool cachedData))

            return cachedData;



        bool result = check.Invoke();

        if (!useApiData || (VersionInfoManager.version?.Events == null && VersionInfoManager.allversion?.Events == null))

        {

            //apiが使えない状態orフラグがfalseの場合はローカルの状態を使用

            cachedEventFlags[(role, false)] = result;

            return result;

        }



        var events = VersionInfoManager.version?.Events;

        var allverevents = VersionInfoManager.allversion?.Events;



        //全イベント情報をチェック

        if (events != null)

        {

            foreach (var data in events)

            {

                if (result) continue;

                result |= IsEventDataActiveForRole(data, role);

            }

        }

        if (allverevents != null)

        {

            foreach (var data in allverevents)

            {

                if (result) continue;

                result |= IsEventDataActiveForRole(data, role);

            }

        }

        cachedEventFlags[(role, true)] = result;

        return result;

    }



    private static bool IsEventDataActiveForRole(VersionInfoManager.VersionInfo.EventData data, CustomRoles role)

    {

        if (data?.Period == null) return false;



        var isInEventPeriod = data.Period.IsActive;

        return data.RoleId == GetRoleId(role, isInEventPeriod) && isInEventPeriod;

    }



    public static Dictionary<CustomRoles, Func<bool>> EventRoles = new()

    {

        {CustomRoles.Altair,() => Tanabata},

        {CustomRoles.Vega,() => Tanabata},

        {CustomRoles.Amateras,() => Tanabata},



        {CustomRoles.UnFortuner , () => 4 <= DateTime.Now.Month},

        {CustomRoles.Fortuner , () => 4 <= DateTime.Now.Month},

        {CustomRoles.Cakeshop , () => NowRoleEvent},
        {CustomRoles.Daoshi , () => DateTime.Now.Month == 10}

    };



    public static Dictionary<(CustomRoles role, bool isApiData), bool> cachedEventFlags = new(EventRoles.Count);

}
