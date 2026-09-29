using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using TownOfHost;
using TownOfHost.Roles;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Neutral;
using static TownOfHost.Roles.Core.Interfaces.ISchrodingerCatOwner;

namespace TownOfHost.Roles.Neutral
{
    public sealed class DarkHide : RoleBase, IKiller, ISchrodingerCatOwner
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(DarkHide),
                player => new DarkHide(player),
                CustomRoles.DarkHide,
                () => RoleTypes.Impostor,
                CustomRoleTypes.Neutral,
                77600,
                SetupOptionItem,
                "Drh",
                "#483d8b",
                (2, 6),
                true,
                isNewRole: true,
                from: From.TownOfHost_Y
            );
        public DarkHide(PlayerControl player)
        : base(
            RoleInfo,
            player,
            () => HasTask.False
        )
        {
            KillCooldown = OptionKillCooldown.GetFloat();
            HasImpostorVision = OptionHasImpostorVision.GetBool();
            CanCountNeutralKiller = OptionCanCountNeutralKiller.GetBool();

            IsWinKill = false;
        }

        private static OptionItem OptionKillCooldown;
        private static OptionItem OptionHasImpostorVision;
        public static OptionItem OptionCanCountNeutralKiller;
        enum OptionName
        {
            DarkHideCanCountNeutralKiller,
        }
        private static float KillCooldown;
        private static bool HasImpostorVision;
        public static bool CanCountNeutralKiller;

        public bool IsWinKill = false;
        static bool IsWin = false;
        public TeamType SchrodingerCatChangeTo => TeamType.DarkHide;

        private static void SetupOptionItem()
        {
            SoloWinOption.Create(RoleInfo, 9, defo: 1);
            OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, 11, GeneralOption.ImpostorVision, false, false);
            OptionCanCountNeutralKiller = BooleanOptionItem.Create(RoleInfo, 12, OptionName.DarkHideCanCountNeutralKiller, false, false);
        }

        public void OnMurderPlayerAsKiller(MurderInfo info)
        {
            if (!info.IsSuicide)
            {
                (var killer, var target) = info.AttemptTuple;

                var targetRole = target.GetCustomRole();
                if (!IsWinKill) IsWinKill = targetRole.IsImpostor();
                if (CanCountNeutralKiller && target.IsNeutralKiller() && target.GetCustomRole() is not CustomRoles.DarkHide) IsWinKill = true;

                foreach (var pc in PlayerCatch.AllPlayerControls)
                {
                    if (pc.Data.Disconnected) continue;
                    MessageWriter SabotageFixWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, pc.GetClientId());
                    SabotageFixWriter.Write((byte)SystemTypes.Electrical);
                    MessageExtensions.WriteNetObject(SabotageFixWriter, pc);
                    AmongUsClient.Instance.FinishRpcImmediately(SabotageFixWriter);
                }
            }
        }

        public float CalculateKillCooldown() => KillCooldown;
        public override void ApplyGameOptions(IGameOptions opt) => opt.SetVision(HasImpostorVision);
        public bool CanUseImpostorVentButton() => false;
        public void ApplySchrodingerCatOptions(IGameOptions option) => ApplyGameOptions(option);
        public bool CanUseSabotageButton() => false;
        public static void CheckWin(ref GameOverReason reason)
        {
            if (reason is GameOverReason.ImpostorsBySabotage or GameOverReason.CrewmatesByTask) return;
            if (CustomWinnerHolder.WinnerTeam is CustomWinner.Impostor or CustomWinner.Crewmate)
            {
                Win();
                return;
            }
        }
        static void Win()
        {
            if (CustomWinnerHolder.WinnerTeam is CustomWinner.Impostor)
            {
                foreach (var p in PlayerCatch.AllAlivePlayerControls)
                {
                    if (p.GetRoleClass() is DarkHide dark)
                    {
                        if (!IsWin)
                        {
                            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.DarkHide, byte.MaxValue))
                            {
                                CustomWinnerHolder.WinnerIds.Add(p.PlayerId);
                                IsWin = true;
                            }
                        }
                        else if (CustomWinnerHolder.WinnerTeam is CustomWinner.DarkHide)
                        {
                            CustomWinnerHolder.WinnerIds.Add(p.PlayerId);
                        }
                    }
                }
                return;
            }
            else
            {
                foreach (var p in PlayerCatch.AllAlivePlayerControls)
                {
                    if (p.GetRoleClass() is DarkHide dkh)
                    {
                        if (dkh.IsWinKill)
                        {
                            if (!IsWin)
                            {
                                if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.DarkHide, byte.MaxValue))
                                {
                                    CustomWinnerHolder.WinnerIds.Add(p.PlayerId);
                                    IsWin = true;
                                }
                            }
                            else if (CustomWinnerHolder.WinnerTeam is CustomWinner.DarkHide)
                            {
                                CustomWinnerHolder.WinnerIds.Add(p.PlayerId);
                            }
                        }
                    }
                }
            }
        }
    }
}