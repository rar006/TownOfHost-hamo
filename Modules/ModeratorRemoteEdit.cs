using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules;

// ホスト以外でも、MOD を入れているモデレーターなら、ロビーのパソコンから設定画面を開いて部屋の設定を変えられる。
// 変えた値はホストに送り、ホストがモデレーターかどうかを確かめてから反映する(反映した値はいつも通りホストから全員に届く)。
// モデレーターを外されたら、ホストから知らせて、設定画面を閉じてから使えなくする
public static class ModeratorRemoteEdit
{
    // ---- 参加者(モデレーター)の側 ----
    public static bool LocalIsModerator { get; private set; }
    private static int statusHostId = -1;
    // ホストから届いた設定を反映している間は、ホストに送り返さない
    public static bool ApplyingFromHost;

    public static bool CanEditOnClient =>
        LocalIsModerator && AmongUsClient.Instance != null && !AmongUsClient.Instance.AmHost && GameStates.IsLobby;

    // ---- ホストの側 ----
    // 誰に「モデレーターです/ではありません」を伝えたか(PlayerId は使い回されるので接続番号も覚える)
    private static readonly Dictionary<byte, (int clientId, bool on)> sentStatus = new();
    private static float nextCheckAt;

    private static bool Disabled => Options.OptionCommandModeratorSetting?.GetBool() == true;

    internal static void Tick()
    {
        var client = AmongUsClient.Instance;
        if (client == null) return;
        if (!client.AmHost)
        {
            // 別の部屋に入った・ホストが変わったら、前の部屋の権限は使わない
            if (LocalIsModerator && client.HostId != statusHostId) SetLocal(false, false);
            if (LocalIsModerator && GameSettingMenu.Instance != null && !GameStates.IsLobby) CloseMenu();
            return;
        }
        LocalIsModerator = false;
        if (!GameStates.IsLobby || PlayerControl.LocalPlayer == null) return;
        if (Time.unscaledTime < nextCheckAt) return;
        nextCheckAt = Time.unscaledTime + 0.5f;
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc.AmOwner) continue;
            // MOD を入れている人にだけ送る(MOD の無い人には関係ない)
            if (!pc.IsModClient() && !NonHostRpcGuard.IsHidden(pc.PlayerId)) continue;
            var clientId = pc.GetClientId();
            var on = !Disabled && Moderator.IsModerator(pc);
            if (sentStatus.TryGetValue(pc.PlayerId, out var last) && last.clientId == clientId && last.on == on) continue;
            // 初めての人で、モデレーターでなければ送らなくてよい
            if ((!sentStatus.TryGetValue(pc.PlayerId, out last) || last.clientId != clientId) && !on)
            {
                sentStatus[pc.PlayerId] = (clientId, false);
                continue;
            }
            sentStatus[pc.PlayerId] = (clientId, on);
            var writer = client.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ModeratorStatus, SendOption.Reliable, clientId);
            writer.Write(on);
            client.FinishRpcImmediately(writer);
            Logger.Info($"{pc.Data?.GetLogPlayerName()} に「設定を変えられる: {on}」を送りました", "ModeratorRemoteEdit");
        }
    }

    // ホストから: 設定を変えられるかどうか
    public static void ReceiveStatus(PlayerControl sender, MessageReader reader)
    {
        var on = reader.ReadBoolean();
        var client = AmongUsClient.Instance;
        if (client == null || client.AmHost || sender == null || sender.OwnerId != client.HostId) return;
        statusHostId = client.HostId;
        SetLocal(on, true);
    }

    private static void SetLocal(bool on, bool notify)
    {
        if (LocalIsModerator == on) return;
        if (!on)
        {
            // 先に設定画面を閉じてから、権限をなくす
            CloseMenu();
            LocalIsModerator = false;
            if (notify) Logger.seeingame("モデレーターではなくなったので、部屋の設定は変えられなくなりました");
            return;
        }
        LocalIsModerator = true;
        if (notify) Logger.seeingame("モデレーターなので、ロビーのパソコンから部屋の設定を変えられます");
    }

    private static void CloseMenu()
    {
        var menu = GameSettingMenu.Instance;
        if (menu == null) return;
        try { menu.Close(); } catch (Exception e) { Logger.Warn($"設定画面を閉じられませんでした: {e.Message}", "ModeratorRemoteEdit"); }
        // 小さい窓が開いていて閉じなかった時は、そのまま消す
        if (GameSettingMenu.Instance != null) Object.Destroy(GameSettingMenu.Instance.gameObject);
    }

    // ---- 参加者 → ホスト: MOD の設定 ----
    // 送ったら true(この後のいつもの同期はしない)
    public static bool TrySendOption(OptionItem item)
    {
        if (ApplyingFromHost || !CanEditOnClient || GameSettingMenu.Instance == null || item is AssignOptionItem) return false;
        var client = AmongUsClient.Instance;
        var writer = client.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ModeratorSetOption, SendOption.Reliable, client.HostId);
        writer.WritePacked(item.Id);
        writer.WritePacked(item.CurrentValue);
        client.FinishRpcImmediately(writer);
        return true;
    }

    // ---- 参加者 → ホスト: バニラの設定(キルクール・視界など) ----
    public static bool TrySendVanilla(OptionBehaviour option)
    {
        if (!CanEditOnClient || option == null) return false;
        byte kind;
        int name;
        float value;
        if (option.TryCast<NumberOption>() is { } number)
        {
            if (number.floatOptionName != FloatOptionNames.Invalid) { kind = 0; name = (int)number.floatOptionName; value = number.GetFloat(); }
            else { kind = 1; name = (int)number.intOptionName; value = number.GetInt(); }
        }
        else if (option.TryCast<StringOption>() is { } str) { kind = 1; name = (int)str.stringOptionName; value = str.GetInt(); }
        else if (option.TryCast<ToggleOption>() is { } toggle) { kind = 2; name = (int)toggle.boolOptionName; value = toggle.GetBool() ? 1f : 0f; }
        else return true; // 知らない種類は送らず、自分の画面でも変えない
        var client = AmongUsClient.Instance;
        var writer = client.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ModeratorSetVanilla, SendOption.Reliable, client.HostId);
        writer.Write(kind);
        writer.WritePacked(name);
        writer.Write(value);
        client.FinishRpcImmediately(writer);
        return true;
    }

    private static bool CheckSender(PlayerControl sender, string what)
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || sender == null || sender.AmOwner) return false;
        if (GameStates.IsLobby && !Disabled && Moderator.IsModerator(sender)) return true;
        Logger.Warn($"{sender.Data?.GetLogPlayerName()} から{what}の変更が届きましたが、モデレーターでない(またはロビーでない)ので反映しませんでした", "ModeratorRemoteEdit");
        return false;
    }

    // ホストで: MOD の設定を反映
    public static void ReceiveOption(PlayerControl sender, MessageReader reader)
    {
        var id = reader.ReadPackedInt32();
        var value = reader.ReadPackedInt32();
        if (!OptionItem.FastOptions.TryGetValue(id, out var item) || item is AssignOptionItem) return;
        if (!CheckSender(sender, "設定"))
        {
            // 向こうの画面の値を、部屋の値に戻す
            item.SyncOptions();
            return;
        }
        if (item.CurrentValue == value) return;
        Logger.Info($"モデレーター {sender.Data?.GetLogPlayerName()} が設定を変更: {item.Name} {item.CurrentValue} → {value}", "ModeratorRemoteEdit");
        item.SetValue(value);
    }

    // ホストで: バニラの設定を反映
    public static void ReceiveVanilla(PlayerControl sender, MessageReader reader)
    {
        var kind = reader.ReadByte();
        var name = reader.ReadPackedInt32();
        var value = reader.ReadSingle();
        var resync = !CheckSender(sender, "バニラの設定");
        try
        {
            if (!resync)
            {
                var opt = GameOptionsManager.Instance.CurrentGameOptions;
                switch (kind)
                {
                    case 0: opt.SetFloat((FloatOptionNames)name, value); break;
                    case 1: opt.SetInt((Int32OptionNames)name, Mathf.RoundToInt(value)); break;
                    case 2: opt.SetBool((BoolOptionNames)name, value >= 0.5f); break;
                    default: return;
                }
                Logger.Info($"モデレーター {sender.Data?.GetLogPlayerName()} がバニラの設定を変更: {kind}/{name} → {value}", "ModeratorRemoteEdit");
                OptionShower.Update = true;
            }
            GameManager.Instance.LogicOptions.SyncOptions();
        }
        catch (Exception e) { Logger.Warn($"バニラの設定を反映できませんでした: {e.Message}", "ModeratorRemoteEdit"); }
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class ModeratorRemoteEditTickPatch
{
    static void Postfix()
    {
        try { ModeratorRemoteEdit.Tick(); }
        catch (Exception e) { Logger.Warn($"モデレーターの設定の確認に失敗: {e.Message}", "ModeratorRemoteEdit"); }
    }
}

// ロビーのパソコン(ホストだけ使える)を、モデレーターも使えるようにする
[HarmonyPatch(typeof(OptionsConsole), nameof(OptionsConsole.CanUse))]
static class ModeratorOptionsConsolePatch
{
    static void Postfix(OptionsConsole __instance, NetworkedPlayerInfo pc, ref bool canUse, ref bool couldUse, ref float __result)
    {
        if (!ModeratorRemoteEdit.CanEditOnClient || !__instance.HostOnly || pc == null || pc.Object == null || !pc.Object.AmOwner) return;
        var player = pc.Object;
        couldUse = player.CanMove && !pc.IsDead;
        canUse = couldUse;
        __result = float.MaxValue;
        if (!canUse) return;
        __result = Vector2.Distance(player.GetTruePosition(), __instance.transform.position);
        canUse = __result <= __instance.UsableDistance;
    }
}

// バニラの設定を変えた時は、自分で反映せずにホストに送る
[HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.ValueChanged))]
static class ModeratorVanillaValueChangedPatch
{
    static bool Prefix(OptionBehaviour option) => !ModeratorRemoteEdit.TrySendVanilla(option);
}
