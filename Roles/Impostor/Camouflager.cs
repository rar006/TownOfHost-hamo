using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Neutral;
using UnityEngine;

namespace TownOfHost.Roles.Impostor;

public sealed class Camouflager : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
        typeof(Camouflager),
        player => new Camouflager(player),
        CustomRoles.Camouflager,
        () => RoleTypes.Phantom,
        CustomRoleTypes.Impostor,
        127300,
        SetupOptionItem,
        "Mo",
        OptionSort: (6, 0),
        from: From.TheOtherRoles
    );

    private static OptionItem OptionKillCoolDown;
    private static OptionItem OptionCooldown;
    private static OptionItem OptionAblitytime;
    private static OptionItem OptionKoufuka;
    public static bool NowUse { get; set; }
    bool Use;

    private float _limit;
    private readonly List<byte> _ventPlayers = new();
    public static byte id;

    private enum OptionName
    {
        CamoKoufuka,
        GhostNoiseSenderTime 
    }
    public override void Add()
    {
        Use = false;
    }
    public Camouflager(PlayerControl player) : base(RoleInfo, player)
    {
        NowUse = false;
        _limit = -50;
        _ventPlayers.Clear();
        Use = false;
    }

    private static void SetupOptionItem()
    {
        OptionKillCoolDown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 11, GeneralOption.Cooldown, new(0f, 180f, 0.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAblitytime = FloatOptionItem.Create(RoleInfo, 12, OptionName.GhostNoiseSenderTime, new(0f, 100f, 0.5f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionKoufuka = BooleanOptionItem.Create(RoleInfo, 13, OptionName.CamoKoufuka, true, false);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = NowUse ? (OptionAblitytime.GetFloat()) : OptionCooldown.GetFloat();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !NowUse || GameStates.CalledMeeting || _limit <= -50) return;

        _limit -= Time.fixedDeltaTime;
        if (_limit <= 0)
        {
            SetCamouflage(false);
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        NowUse = false;
        _limit = -50;
        _ventPlayers.Clear();
    }

    public override bool NotifyRolesCheckOtherName => true;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;
        if (NowUse) return;
        SetCamouflage(true);
    }
    public void SetCamouflage(bool active, PlayerControl nottarget = null, bool totyu = false)
    {
        if (active)
        {
            if (NowUse && !totyu) return;
            if (totyu && !Use) return;
            Use = true;
            PlayerControl dummy;
            if (nottarget != null)
            {
                dummy = PlayerCatch.AllAlivePlayerControls.FirstOrDefault(pc => pc != null && pc != nottarget) ?? PlayerCatch.GetPlayerById(1);
                Camouflage.RpcSetSkin(nottarget);
            }
            else
            {
                dummy = PlayerCatch.AllAlivePlayerControls.FirstOrDefault(pc => pc != null) ?? PlayerCatch.GetPlayerById(1);
            }
            if (dummy == null) return;
            id = dummy.PlayerId;
            SendRPC();
            foreach (var p in PlayerCatch.AllAlivePlayerControls)
            {
                dummy.RpcChColor(p, 15, true);
                dummy.RpcHideSkinAndPet(p);
            }

            // かもふら
            foreach (var pl in PlayerCatch.AllAlivePlayerControls)
            {
                if (pl == dummy) continue;
                pl.RpcShapeshift(dummy, false);
                var sender = CustomRpcSender.Create("CamouflagerShape");
                sender.AutoStartRpc(pl.NetId, RpcCalls.Shapeshift)
                    .Write(dummy)
                    .Write(false)
                    .EndRpc();
                sender.EndMessage();
                sender.SendMessage();
                //設定は様子見つつ消すか考える。
                if (!OptionKoufuka.GetBool()) continue;
                //シェイプ相手の見た目になるので戻し処理。こっちのがRPC少なくて済むはず
                foreach (var p in PlayerCatch.AllPlayerControls)
                {
                    if (p.IsAlive()) continue;
                    pl.RpcShapeshift(pl, false);
                    var senderat = CustomRpcSender.Create("CamouflagerShape");
                    senderat.AutoStartRpc(pl.NetId, RpcCalls.Shapeshift, p.GetClientId())
                        .Write(pl)
                        .Write(false)
                        .EndRpc();
                    senderat.EndMessage();
                    senderat.SendMessage();
                }
            }
            if (!totyu)
            {
                _limit = OptionAblitytime.GetFloat();
                NowUse = true;
                Player.RpcResetAbilityCooldown(log: false, Sync: true);

                _ = new LateTask(() =>
                {
                    UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
                }, 0.2f, "", true);
            }
        }
        else
        {
            Use = false;
            _limit = -100;
            NowUse = false;
            var original = PlayerCatch.GetPlayerById(id);

            _ = new LateTask(() =>
            {
                if (GameStates.CalledMeeting) return;
                if (original != null)
                {
                    Camouflage.RpcSetSkin(original, force: null);
                }
                // しゅーりょー
                foreach (var pl in PlayerCatch.AllPlayerControls)
                {
                    pl.RpcShapeshift(pl, false);
                    var sender = CustomRpcSender.Create("CamouflagerShape");
                    sender.AutoStartRpc(pl.NetId, RpcCalls.Shapeshift)
                        .Write(pl)
                        .Write(false)
                        .EndRpc();
                    sender.EndMessage();
                    sender.SendMessage();
                }

                UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
            }, 0.2f, "", true);
            Player.RpcResetAbilityCooldown(log: false, Sync: true);
        }
    }
    public float CalculateKillCooldown() => OptionKillCoolDown.GetFloat();

    public override bool OverrideAbilityButton(out string text)
    {
        text = "Camouflager_Ability";
        return true;
    }

    public override string GetAbilityButtonText() => GetString("CamouflagerText");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";

        if (isForHud) return GetString("PhantomButtonLowertext");
        return $"<size=50%>{GetString("PhantomButtonLowertext")}</size>";
    }

    bool IUsePhantomButton.IsresetAfterKill => false;
    private void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(id);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        id = reader.ReadByte();
    }
}