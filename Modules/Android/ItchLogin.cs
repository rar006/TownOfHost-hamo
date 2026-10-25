#if HAMO_ANDROID
using System;
using System.IO;
using System.Reflection;
using AmongUs.Data.Legacy;
using BepInEx;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Unity.Services.LevelPlay;

namespace TownOfHost.Modules.Android;

/// <summary>
/// Android用: ランチャーが itch.io でログインして渡してくるトークンで、Among Us(EOS)にログインする。
/// これが無いと Google ログインしかなく(改造APKでは必ず失敗する)、仮アカウントになってオンラインで部屋を作れない。
/// EndKnot(EndKnot.Modules.Android.ItchLogin)の処理を参考にしている。
///
/// トークンの受け取り方(ランチャー側):
///   環境変数 TOHHAMO_ITCH_TOKEN (hamo用ランチャー) または ENDKNOT_ITCH_TOKEN (EndKnotのランチャー)
///   無ければ FUSION_APP_DATA_DIR か BepInEx/config の TOHhamo.ItchApiKey.txt / EndKnot.ItchApiKey.txt
/// </summary>
internal static class ItchLogin
{
    private const string Tag = "ItchLogin";
    private static readonly string[] TokenEnvs = { "TOHHAMO_ITCH_TOKEN", "ENDKNOT_ITCH_TOKEN" };
    private static readonly string[] KeyFileNames = { "TOHhamo.ItchApiKey.txt", "EndKnot.ItchApiKey.txt" };
    private static readonly string[] FailedMarkerNames = { "TOHhamo.ItchApiKey.failed", "EndKnot.ItchApiKey.failed" };
    private const int MaxAttempts = 3;
    // EOS の ExternalCredentialType.ItchioKey
    private const int ItchioKeyCredentialType = 15;

    private static int attempts;
    private static string keyFromEnvironment;

    private static string PrivateDir => Environment.GetEnvironmentVariable("FUSION_APP_DATA_DIR");

    private static string ResolvePlaintextPath()
    {
        foreach (var name in KeyFileNames)
        {
            var priv = string.IsNullOrEmpty(PrivateDir) ? null : Path.Combine(PrivateDir, name);
            if (priv != null && File.Exists(priv)) return priv;
            var cfg = Path.Combine(Paths.ConfigPath, name);
            if (File.Exists(cfg)) return cfg;
        }
        return null;
    }

    public static string ReadKey()
    {
        try
        {
            foreach (var env in TokenEnvs)
            {
                var v = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrWhiteSpace(v)) continue;
                keyFromEnvironment = env;
                var token = v.Trim();
                Logger.Info($"token from launcher ({env}), length={token.Length}", Tag);
                return token;
            }
            keyFromEnvironment = null;
            var path = ResolvePlaintextPath();
            if (path == null)
            {
                Logger.Warn("no itch token in environment and no key file", Tag);
                return null;
            }
            var text = File.ReadAllText(path).Trim().Trim('﻿');
            Logger.Info($"key file found ({Path.GetFileName(path)}), length={text.Length}", Tag);
            return text.Length == 0 ? null : text;
        }
        catch (Exception e)
        {
            Logger.Error($"key read failed: {e.Message}", Tag);
            return null;
        }
    }

    /// <summary>トークンが拒否されたことをランチャーに伝える(次回は再ログインを促せるように)</summary>
    private static void MarkKeyFailed()
    {
        try
        {
            if (keyFromEnvironment != null)
            {
                if (string.IsNullOrEmpty(PrivateDir)) { Logger.Warn("token rejected but FUSION_APP_DATA_DIR is unset", Tag); return; }
                Directory.CreateDirectory(PrivateDir);
                var idx = Array.IndexOf(TokenEnvs, keyFromEnvironment);
                File.WriteAllText(Path.Combine(PrivateDir, FailedMarkerNames[Math.Max(0, idx)]), "rejected" + Environment.NewLine);
                Environment.SetEnvironmentVariable(keyFromEnvironment, null);
                Logger.Warn("token rejected; failure marker written for the launcher", Tag);
                return;
            }
            var path = ResolvePlaintextPath();
            if (path == null) return;
            var failed = path + ".failed";
            if (File.Exists(failed)) File.Delete(failed);
            File.Move(path, failed);
            Logger.Warn($"key file set aside as {Path.GetFileName(failed)}", Tag);
        }
        catch (Exception e) { Logger.Warn($"could not report the rejected key: {e.Message}", Tag); }
    }

    /// <summary>EOS Connect.Login を直接呼ぶ(interop の生成コードは LoginOptions を ref で渡せないため、ネイティブのメソッドを直接呼ぶ)</summary>
    private static unsafe void InvokeConnectLogin(ConnectInterface connect, LoginOptions opts, OnLoginCallback cb)
    {
        var field = typeof(ConnectInterface).GetField("NativeMethodInfoPtr_Login_Public_Void_byref_LoginOptions_Object_OnLoginCallback_0",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Connect.Login native method pointer not found in interop (EOS SDK / interop version mismatch)");
        var method = (IntPtr)field.GetValue(null);
        var args = stackalloc IntPtr[3];
        args[0] = IL2CPP.il2cpp_object_unbox(IL2CPP.Il2CppObjectBaseToPtrNotNull(opts));
        args[1] = IntPtr.Zero;
        args[2] = IL2CPP.Il2CppObjectBaseToPtr(cb);
        var exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(method, IL2CPP.Il2CppObjectBaseToPtrNotNull(connect), (void**)args, ref exc);
        Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
    }

    [HarmonyPatch(typeof(EOSManager), nameof(EOSManager.LoginWithCorrectPlatformImpl))]
    internal static class LoginPatch
    {
        public static bool Prefix(EOSManager __instance, [HarmonyArgument(0)] OnLoginCallback successCallbackIn)
        {
            var token = ReadKey();
            if (token == null)
            {
                Logger.Warn("no itch token -> vanilla (Google) login", Tag);
                return true;
            }
            if (++attempts > MaxAttempts)
            {
                Logger.Warn($"itch login failed {MaxAttempts} times -> vanilla login (guest)", Tag);
                MarkKeyFailed();
                return true;
            }
            try
            {
                var cred = new Credentials();
                cred._Token_k__BackingField = new Utf8String(token);
                cred._Type_k__BackingField = (ExternalCredentialType)ItchioKeyCredentialType;
                var nullable = new Il2CppSystem.Nullable<Credentials>();
                nullable.value = cred;
                nullable.hasValue = true;
                var opts = new LoginOptions();
                opts._Credentials_k__BackingField = nullable;
                InvokeConnectLogin(__instance.PlatformInterface.GetConnectInterface(), opts, successCallbackIn);
                Logger.Info("Connect.Login(ItchioKey) issued", Tag);
                return false;
            }
            catch (Exception e)
            {
                Logger.Error($"Connect.Login threw, falling back: {e}", Tag);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(EOSManager), nameof(EOSManager.EndFinalPartsOfLoginFlowFullAccount))]
    internal static class FullAccountPatch
    {
        public static void Postfix(EOSManager __instance)
        {
            attempts = 0;
            Logger.Info($"FULL account login. FriendCode={__instance.FriendCode}", Tag);
        }
    }

    [HarmonyPatch(typeof(EOSManager), nameof(EOSManager.EndFinalPartsOfLoginFlowTempAccount))]
    internal static class TempAccountPatch
    {
        public static void Postfix(EOSManager __instance)
            => Logger.Warn($"TEMP (guest) account login. FriendCode={__instance.FriendCode}", Tag);
    }

    // ---- 広告(LevelPlay)は改造APKでは正しく動かないので止める(EndKnotと同じ) ----
    [HarmonyPatch(typeof(AdsManager), nameof(AdsManager.InitLevelPlay))]
    internal static class AdsManagerInitPatch
    {
        public static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(AdsMenu), nameof(AdsMenu.OnEnable))]
    internal static class AdsMenuPatch
    {
        public static void Postfix(AdsMenu __instance)
        {
            if (__instance.adButton != null) __instance.adButton.SetActive(false);
            if (__instance.loadingObject != null) __instance.loadingObject.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(AdsMenu), nameof(AdsMenu.ClickAdButton))]
    internal static class AdsMenuClickPatch
    {
        public static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(AdsMenu), nameof(AdsMenu.AdReadyCallback))]
    internal static class AdsMenuReadyPatch
    {
        public static void Postfix(AdsMenu __instance)
        {
            if (__instance.adButton != null) __instance.adButton.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(AdsManager), nameof(AdsManager.RewardedOnAdLoadedEvent))]
    internal static class AdsManagerLoadedPatch
    {
        public static bool Prefix(AdsManager __instance)
        {
            try
            {
                LevelPlayRewardedAd ad = __instance.rewardedAd;
                if (ad != null && ad.IsAdReady()) return true;
            }
            catch (Exception) { return true; }
            try
            {
                __instance.rewardedAd = null;
                __instance.adMenuCallbak?.Invoke(false);
            }
            catch (Exception e) { Logger.Error($"RewardedOnAdLoadedEvent: {e}", Tag); }
            return false;
        }
    }
}
#endif
