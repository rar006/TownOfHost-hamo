using System;
using System.Collections;
using HarmonyLib;
using Newtonsoft.Json;
using InnerNet;
using AmongUs.HTTP;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace TownOfHost.Patches;

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoFindGameInfoFromCode))]
class CoFindGameInfoFromCodePatch
{
    public static bool Prefix(AmongUsClient __instance, ref Il2CppSystem.Collections.IEnumerator __result, int gameId, Il2CppSystem.Action<HttpMatchmakerManager.FindGameByCodeResponse, string> callback)
    {
        if (EnterCodeManagerPatch.vServerOnlyCheckBox?.IsChecked != true) return true;
        __result = CoFindGameInfoFromCode(__instance, gameId, callback).WrapToIl2Cpp();
        return false;
    }
    public static IEnumerator CoFindGameInfoFromCode(AmongUsClient __instance, int gameId, Il2CppSystem.Action<HttpMatchmakerManager.FindGameByCodeResponse, string> callback)
    {
        AmongUsClient amongUsClient = __instance;
        amongUsClient.NetworkMode = NetworkModes.OnlineGame;
        amongUsClient.GameId = gameId;

        while (!DestroyableSingleton<EOSManager>.InstanceExists)
            yield return null;
        yield return DestroyableSingleton<EOSManager>.Instance.WaitForLoginFlow();

        if (DestroyableSingleton<ServerManager>.Instance.IsHttp)
        {
            string matchmakerToken = null;
            yield return CoFindGameInfo(gameId, (response, mmToken) =>
            {
                matchmakerToken = mmToken;
                callback.Invoke(response, matchmakerToken);
            });
        }
        else
            yield return amongUsClient.CoConnectToGameServer(MatchMakerModes.Client, DestroyableSingleton<ServerManager>.Instance.UdpNetAddress, DestroyableSingleton<ServerManager>.Instance.UdpNetPort, (string)null);
    }

    public static IEnumerator CoFindGameInfo(int gameId, Action<HttpMatchmakerManager.FindGameByCodeResponse, string> onGameInfo)
    {
        HttpMatchmakerManager matchmakerManager = DestroyableSingleton<HttpMatchmakerManager>.Instance;
        IRegionInfo region = DestroyableSingleton<ServerManager>.Instance.CurrentRegion;

        bool foundGame = false;
        string uri = string.Format("{0}api/games/{1}", DestroyableSingleton<ServerManager>.Instance.CurrentUdpServer.HttpUrl, gameId);
        string matchmakerToken = string.Empty;

        System.Action<string> tokenCallback = token => matchmakerToken = token;
        yield return matchmakerManager.CoGetOrRefreshToken((Il2CppSystem.Action<string>)tokenCallback);
        if (string.IsNullOrEmpty(matchmakerToken))
        {
            DestroyableSingleton<ServerManager>.Instance.SetRegion(region);
            yield break;
        }
        else
        {
            var request = RetryableWebRequest.Get(uri);
            request.SetOrReplaceRequestHeader("Accept", "application/json");
            request.SetOrReplaceRequestHeader("Authorization", "Bearer " + matchmakerToken);
            System.Action<string> successCallback = response =>
            {
                try
                {
                    HttpMatchmakerManager.FindGameByCodeResponse gameByCodeResponse = JsonConvert.DeserializeObject<HttpMatchmakerManager.FindGameByCodeResponse>(response);
                    gameByCodeResponse.Region = region.TranslateName;
                    gameByCodeResponse.UntranslatedRegion = region.Name;
                    onGameInfo.Invoke(gameByCodeResponse, matchmakerToken);
                    foundGame = true;
                }
                catch { }
            };
            request.SetOrReplaceSuccessCallback((Il2CppSystem.Action<string>)successCallback);

            System.Action<HttpMatchmakerManager.MatchmakerFailure> failureCallback = failure =>
                matchmakerManager.logger.Info("Unable to find game in region: " + DestroyableSingleton<ServerManager>.Instance.CurrentRegion.Name);
            yield return matchmakerManager.CoSendRequest(request, "request gamecode server", 2, (Il2CppSystem.Action<HttpMatchmakerManager.MatchmakerFailure>)failureCallback);
            if (foundGame) yield break;
        }

        matchmakerManager.SetDisconnectInfoAndShowPopup(new HttpMatchmakerManager.MatchmakerFailure()
        {
            Reason = DisconnectReasons.GameNotFound,
            CustomDisconnect = DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.ErrorNotFoundGame),
            MatchmakerError = null,
            ShouldGoOffline = false
        });
    }
}