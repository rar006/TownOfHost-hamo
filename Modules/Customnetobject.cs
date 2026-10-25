using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.InnerNet.GameDataMessages;
using Hazel;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

public class CustomNetObject
{
    public static readonly List<CustomNetObject> AllObjects = new();
    private static int MaxId = -1;

    private static readonly Queue<Action> SpawnQueue = new();
    private static bool IsSpawning = false;

    protected int Id;
    public PlayerControl PlayerControl;
    public Vector2 Position;

    protected virtual bool IsDynamic => false;

    public void Despawn()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        try
        {
            if (PlayerControl != null)
            {
                MessageWriter writer = MessageWriter.Get(SendOption.Reliable);
                writer.StartMessage(5);
                writer.Write(AmongUsClient.Instance.GameId);
                writer.StartMessage(5);
                writer.WritePacked(PlayerControl.NetId);
                writer.EndMessage();
                writer.EndMessage();
                AmongUsClient.Instance.SendOrDisconnect(writer);
                writer.Recycle();

                AmongUsClient.Instance.RemoveNetObject(PlayerControl);
                UnityEngine.Object.Destroy(PlayerControl.gameObject);
            }
            AllObjects.Remove(this);
        }
        catch (Exception e) { Logger.Error(e.ToString(), "CNO.Despawn"); }
    }

    protected void Hide(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (player.AmOwner)
        {
            _ = new LateTask(() =>
            {
                try { PlayerControl.transform.FindChild("Names").FindChild("NameText_TMP").gameObject.SetActive(false); }
                catch { }
                PlayerControl.Visible = false;
            }, 0.1f, "CNO.Hide.Local", true);
            return;
        }

        MessageWriter writer = MessageWriter.Get(SendOption.Reliable);
        writer.StartMessage(6);
        writer.Write(AmongUsClient.Instance.GameId);
        writer.WritePacked(player.OwnerId);
        writer.StartMessage(5);
        writer.WritePacked(PlayerControl.NetId);
        writer.EndMessage();
        writer.EndMessage();
        AmongUsClient.Instance.SendOrDisconnect(writer);
        writer.Recycle();
    }

    protected virtual void OnFixedUpdate()
    {
        if (!IsDynamic) return;
        try
        {
            if (!AmongUsClient.Instance.AmHost) return;
            ushort num = (ushort)(PlayerControl.NetTransform.lastSequenceId + 2U);
            MessageWriter mw = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.NetTransform.NetId, 21, SendOption.None);
            NetHelpers.WriteVector2(Position, mw);
            mw.Write(num);
            AmongUsClient.Instance.FinishRpcImmediately(mw);
        }
        catch { }
    }

    // 見た目と名前は、ホストの見た目を一瞬だけ書きかえて、ダミーをそれに変身させて送る。
    // 名前の変更(SetName)で送ると、MOD を入れていない人の画面ではダミーのプレイヤー情報が無いので待ち続け、
    // 30 秒で時間切れになって、その人がゲームから落ちていた
    private int appearanceColor = -1; // -1 の間は、ホストの今の見た目のまま
    private string appearanceSkin = "", appearanceHat = "", appearancePet = "", appearanceVisor = "";
    private string shownName = "";

    protected void SetAppearance(int colorId, string skinId = "", string hatId = "", string petId = "", string visorId = "")
    {
        appearanceColor = colorId;
        appearanceSkin = skinId ?? "";
        appearanceHat = hatId ?? "";
        appearancePet = petId ?? "";
        appearanceVisor = visorId ?? "";
        SendOutfit("CNO.SetAppearance");
    }

    // 色だけ変える(ダミーに RpcSetColor を使うと、名前と同じく MOD の無い人が 30 秒後に落ちるので、こちらに回す)
    public void SetColor(int colorId)
    {
        if (appearanceColor < 0)
        {
            appearanceSkin = appearanceHat = appearancePet = appearanceVisor = "";
        }
        appearanceColor = colorId;
        SendOutfit("CNO.SetColor");
    }

    public static CustomNetObject Find(PlayerControl pc)
    {
        if (pc == null || !pc.notRealPlayer) return null;
        foreach (var o in AllObjects) if (o?.PlayerControl == pc) return o;
        return null;
    }

    public void SetNameFromRpc(string name) => SetName(name);

    protected void SetName(string name)
    {
        shownName = name ?? "";
        SendOutfit("CNO.SetName");
    }

    private void SendOutfit(string label)
    {
        if (PlayerControl == null) return;

        var capturedPC = PlayerControl;
        var outfit = PlayerControl.LocalPlayer.Data.Outfits[PlayerOutfitType.Default];
        string origName = outfit.PlayerName;
        int origColor = outfit.ColorId;
        string origHat = outfit.HatId;
        string origSkin = outfit.SkinId;
        string origPet = outfit.PetId;
        string origVisor = outfit.VisorId;

        var sender = CustomRpcSender.Create(label, SendOption.Reliable);
        MessageWriter writer = sender.stream;
        sender.StartMessage();

        outfit.PlayerName = shownName;
        if (appearanceColor >= 0)
        {
            outfit.ColorId = appearanceColor;
            outfit.HatId = appearanceHat;
            outfit.SkinId = appearanceSkin;
            outfit.PetId = appearancePet;
            outfit.VisorId = appearanceVisor;
        }

        writer.StartMessage(1);
        writer.WritePacked(PlayerControl.LocalPlayer.Data.NetId);
        PlayerControl.LocalPlayer.Data.Serialize(writer, false);
        writer.EndMessage();

        try { capturedPC.Shapeshift(PlayerControl.LocalPlayer, false); }
        catch (Exception e) { Logger.Error(e.ToString(), "CNO.SendOutfit.Shapeshift"); }

        sender.StartRpc(capturedPC.NetId, RpcCalls.Shapeshift)
            .WriteNetObject(PlayerControl.LocalPlayer)
            .Write(false)
            .EndRpc();

        outfit.PlayerName = origName;
        outfit.ColorId = origColor;
        outfit.HatId = origHat;
        outfit.SkinId = origSkin;
        outfit.PetId = origPet;
        outfit.VisorId = origVisor;

        writer.StartMessage(1);
        writer.WritePacked(PlayerControl.LocalPlayer.Data.NetId);
        PlayerControl.LocalPlayer.Data.Serialize(writer, false);
        writer.EndMessage();

        sender.EndMessage();
        sender.SendMessage();

        if (capturedPC.cosmetics?.nameText != null)
            capturedPC.cosmetics.nameText.text = shownName;
    }

    protected void SnapToPosition(Vector2 position)
    {
        if (PlayerControl == null) return;
        Position = position;

        try { PlayerControl.NetTransform.SnapTo(position); }
        catch { }

        // テレポートは本数を数えられていて、多すぎるとサーバーにキックされるので、超えそうな時は送らない
        if (!NetworkSafety.AllowTeleport()) return;
        ushort sid = (ushort)(PlayerControl.NetTransform.lastSequenceId + 100U);
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
            PlayerControl.NetTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.Reliable);
        NetHelpers.WriteVector2(position, writer);
        writer.Write(sid);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    protected void CreateNetObject(Vector2 position)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (GameStates.IsLobby || GameStates.IsEnded) return;

        SpawnQueue.Enqueue(() => DoCreate(position));
        ProcessQueue();
    }

    private static bool retryScheduled;
    private static void ProcessQueue()
    {
        if (IsSpawning || SpawnQueue.Count == 0) return;
        // 会議が始まる時・会議中にダミーを作ると、会議の画面へ移れずに暗転する人が出ることがあるので、会議が終わるまで待つ
        if (GameStates.IsMeeting || GameStates.CalledMeeting)
        {
            if (retryScheduled) return;
            retryScheduled = true;
            _ = new LateTask(() => { retryScheduled = false; ProcessQueue(); }, 0.5f, "CNO.WaitMeeting", true);
            return;
        }
        IsSpawning = true;
        var action = SpawnQueue.Dequeue();
        try
        {
            action();
        }
        catch (Exception e)
        {
            Logger.Error(e.ToString(), "CNO.Spawn");
            IsSpawning = false;
            ProcessQueue();
        }
    }

    public static void ResetSpawnState()
    {
        SpawnQueue.Clear();
        IsSpawning = false;
    }

    private void DoCreate(Vector2 position)
    {
        PlayerControl = UnityEngine.Object.Instantiate(
            AmongUsClient.Instance.PlayerPrefab, Vector2.zero, Quaternion.identity);
        PlayerControl.PlayerId = 254;
        PlayerControl.isNew = false;
        PlayerControl.notRealPlayer = true;

        try { PlayerControl.NetTransform.SnapTo(new Vector2(50f, 50f)); }
        catch { }

        AmongUsClient.Instance.NetIdCnt += 1U;

        MessageWriter msg = MessageWriter.Get(SendOption.Reliable);
        msg.StartMessage(5);
        msg.Write(AmongUsClient.Instance.GameId);
        msg.StartMessage(4);
        SpawnGameDataMessage item = AmongUsClient.Instance.CreateSpawnMessage(PlayerControl, -2, SpawnFlags.None);
        item.SerializeValues(msg);
        msg.EndMessage();

        for (uint i = 1; i <= 3; ++i)
        {
            msg.StartMessage(4);
            msg.WritePacked(2U);
            msg.WritePacked(-2);
            msg.Write((byte)SpawnFlags.None);
            msg.WritePacked(1);
            msg.WritePacked(AmongUsClient.Instance.NetIdCnt - i);
            msg.StartMessage(1);
            msg.EndMessage();
            msg.EndMessage();
        }

        msg.EndMessage();
        AmongUsClient.Instance.SendOrDisconnect(msg);
        msg.Recycle();

        if (PlayerControl.AllPlayerControls.Contains(PlayerControl))
            PlayerControl.AllPlayerControls.Remove(PlayerControl);

        PlayerControl.cosmetics.colorBlindText.color = Color.clear;

        Position = position;
        ++MaxId;
        Id = MaxId;
        if (MaxId == int.MaxValue) MaxId = -1;

        AllObjects.Add(this);

        var capturedPC = PlayerControl;
        var capturedSelf = this;

        _ = new LateTask(() =>
        {
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc.AmOwner) continue;

                var sender = CustomRpcSender.Create("CNO.AssignId", SendOption.Reliable);
                MessageWriter writer = sender.stream;
                sender.StartMessage(pc.OwnerId);

                writer.StartMessage(1);
                writer.WritePacked(capturedPC.NetId);
                writer.Write(pc.PlayerId);
                writer.EndMessage();

                sender.StartRpc(capturedPC.NetId, RpcCalls.MurderPlayer)
                    .WriteNetObject(capturedPC)
                    .Write((int)MurderResultFlags.FailedError)
                    .EndRpc();

                writer.StartMessage(1);
                writer.WritePacked(capturedPC.NetId);
                writer.Write((byte)254);
                writer.EndMessage();

                sender.EndMessage();
                sender.SendMessage();
            }

            capturedPC.CachedPlayerData = PlayerControl.LocalPlayer.Data;
        }, 0.1f, "CNO.AssignId", true);

        _ = new LateTask(() =>
        {
            try
            {
                capturedSelf.OnCreated();
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString(), "CNO.OnCreated");
            }

            // ★ GameData.AllPlayers からも除去（会議でのUI崩れを防ぐ）
            try
            {
                var allPlayers = GameData.Instance?.AllPlayers;
                if (allPlayers != null && capturedPC != null)
                {
                    for (int i = allPlayers.Count - 1; i >= 0; i--)
                    {
                        var info = allPlayers[i];
                        if (info != null && info.PlayerId == 254
                            && info.Object?.NetId == capturedPC.NetId)
                        {
                            allPlayers.RemoveAt(i);
                            Logger.Info($"CNO: GameData.AllPlayersからダミーを除去 NetId={capturedPC.NetId}", "CustomNetObject");
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"CNO.RemoveFromGameData: {e}", "CustomNetObject");
            }

            IsSpawning = false;
            ProcessQueue();
        }, 0.4f, "CNO.OnCreated", true);
    }

    protected virtual void OnCreated() { }

    public virtual void OnMeeting()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        Despawn();
    }

    public static void FixedUpdate()
    {
        foreach (var cno in AllObjects.ToArray())
            cno?.OnFixedUpdate();
    }

    public static CustomNetObject Get(int id)
        => AllObjects.FirstOrDefault(x => x.Id == id);

    public static CustomNetObject GetKillableTarget(PlayerControl killer, float range = 1.5f)
    {
        if (killer == null) return null;
        var pos = killer.GetTruePosition();
        return AllObjects
            .Where(o => o is IKillableDummy)
            .OrderBy(o => Vector2.Distance(pos, o.Position))
            .FirstOrDefault(o => Vector2.Distance(pos, o.Position) <= range);
    }

    public static void Reset()
    {
        try
        {
            SpawnQueue.Clear();
            IsSpawning = false;
            foreach (var obj in AllObjects.ToArray())
                obj?.Despawn();
            AllObjects.Clear();
        }
        catch (Exception e) { Logger.Error(e.ToString(), "CNO.Reset"); }
    }
}

public interface IKillableDummy
{
    void OnKilled(PlayerControl killer);
}

// ダミーに RpcSetColor / RpcSetName を使うと、MOD を入れていない人の画面ではダミーのプレイヤー情報が無いので待ち続け、
// 30 秒で時間切れになってゲームから落ちる。ダミーの時は、変身で見た目を送る方法に回す
[HarmonyLib.HarmonyPatch(typeof(PlayerControl))]
static class CustomNetObjectRpcGuard
{
    [HarmonyLib.HarmonyPatch(nameof(PlayerControl.RpcSetColor)), HarmonyLib.HarmonyPrefix]
    static bool SetColor(PlayerControl __instance, [HarmonyLib.HarmonyArgument(0)] byte bodyColor)
    {
        var cno = CustomNetObject.Find(__instance);
        if (cno == null) return true;
        try { __instance.RawSetColor(bodyColor); } catch { }
        cno.SetColor(bodyColor);
        return false;
    }

    [HarmonyLib.HarmonyPatch(nameof(PlayerControl.RpcSetName)), HarmonyLib.HarmonyPrefix]
    static bool SetName(PlayerControl __instance, [HarmonyLib.HarmonyArgument(0)] string name)
    {
        var cno = CustomNetObject.Find(__instance);
        if (cno == null) return true;
        cno.SetNameFromRpc(name);
        return false;
    }
}
