using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Cleaner : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Cleaner),
            player => new Cleaner(player),
            CustomRoles.Cleaner,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            97200,
            SetupOptionItem,
            "cln",
            "#FF1919",
            from: From.NebulaontheShip,
            isNewRole: true
        );

    public Cleaner(PlayerControl player) : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        cleanCooldown = OptionCleanCooldown.GetFloat();
        syncCooldown = OptionSyncCooldown.GetBool();
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionCleanCooldown;
    static OptionItem OptionSyncCooldown;
    enum OptionName { CleanerCleanCooldown, CleanerSyncCooldown }

    static float killCooldown;
    static float cleanCooldown;
    static bool syncCooldown;

    const float CleanRange = 1.5f;

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCleanCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.CleanerCleanCooldown, new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSyncCooldown = BooleanOptionItem.Create(RoleInfo, 12, OptionName.CleanerSyncCooldown, true, false);
    }

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = cleanCooldown;

    bool IUsePhantomButton.IsPhantomRole => true;
    public override string GetAbilityButtonText() => GetString("CleanerButton");
    bool IUsePhantomButton.IsresetAfterKill => syncCooldown;

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = !syncCooldown;
        ResetCooldown = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var body = DeadBodyEraser.FindNearestBody(Player.GetTruePosition(), CleanRange);
        if (body == null)
        {
            AdjustKillCooldown = true;
            return;
        }
        ResetCooldown = true;
        var id = body.ParentId;
        DeadBodyEraser.Erase(id);
        SendRPC(id);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が {id} の死体を消しました", "Cleaner");
        UtilsGameLog.AddGameLog("Cleaner", string.Format(GetString("CleanerLog"), UtilsName.GetPlayerColor(Player, true), UtilsName.GetPlayerColor(id, true)));
    }

    void SendRPC(byte bodyId)
    {
        using var sender = CreateSender();
        sender.Writer.Write(bodyId);
    }
    public override void ReceiveRPC(MessageReader reader) => DeadBodyEraser.HideLocal(reader.ReadByte());
}
