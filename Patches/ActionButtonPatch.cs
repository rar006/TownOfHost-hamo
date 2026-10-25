using HarmonyLib;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Patches;

[HarmonyPatch(typeof(SabotageButton), nameof(SabotageButton.DoClick))]
public static class SabotageButtonDoClickPatch
{
    public static bool Prefix()
    {
        // 参加者専用MODはボタン表示だけを残し、押下時はバニラのサボタージュ処理へ委ねる。
        if (Main.IsNonHostClient) return true;

        if (PlayerControl.LocalPlayer.CanUseSabotageButton() && !PlayerControl.LocalPlayer.inVent && GameManager.Instance.SabotagesEnabled())
        {
            DestroyableSingleton<HudManager>.Instance.ToggleMapVisible(new MapOptions
            {
                Mode = MapOptions.Modes.Sabotage
            });
        }

        return false;
    }
}
[HarmonyPatch(typeof(SabotageButton), nameof(SabotageButton.Refresh))]
public static class SabotageButtonRefreshPatch
{
    public static void Postfix()
    {
        // 参加者専用MODは表示状態もバニラへ委ねる。
        // ホストがMODを導入していない場合・ロビーでもカスタム表示処理は行わない。
        if (Main.IsNonHostClient || !GameStates.IsModHost || GameStates.IsLobby) return;
        if (GameStates.CalledMeeting) return;

        HudManager.Instance.SabotageButton.ToggleVisible(PlayerControl.LocalPlayer.CanUseSabotageButton());
    }
}

[HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.DoClick))]
public static class AbilityButtonDoClickPatch
{
    public static bool Prefix(AbilityButton __instance)
    {
        // 参加者専用MODでは、カスタム役職能力を自分で発動・同期せず、ボタンはバニラの動きのままにする(ホストが判定する)。
        // 以前は false を返してバニラの動きまで止めていたので、ファントムの能力などが押しても無反応だった。
        // カスタムボタン自体の見た目とHELPの役職説明は引き続き表示される。
        if (Main.IsNonHostClient) return true;

        var player = PlayerControl.LocalPlayer;

        // インフェクトの「自殺」ボタン: ワンクリックでその場に自殺する。
        // 【重要・バグ修正】以前はこのチェックを下記の共通ガード(isCoolingDown等)より
        // 後ろに置いていたため、透明化ボタンのクールダウン中はガードで弾かれて
        // return trueになり、自殺判定へ一切到達できず「押しても反応しない」状態に
        // なっていた。ワンクリック自殺ボタンという性質上クールダウンの概念自体
        // そぐわないため、共通ガードより先に判定し、生存確認のみで即座に処理する。
        if (!AmongUsClient.Instance.AmHost) return true;
        if (player.GetRoleClass() is TownOfHost.Roles.Impostor.Infect infectEarly)
        {
            if (player.IsAlive())
            {
                infectEarly.OnSuicideButtonClicked();
            }
            return false;
        }

        if (HudManager._instance.AbilityButton.isCoolingDown
        || !player.CanMove || !player.IsAlive()
        || (Utils.IsActive(SystemTypes.MushroomMixupSabotage) && player.Data.RoleType == RoleTypes.Shapeshifter)) return true;

        var role = player.GetCustomRole();
        var roleInfo = role.GetRoleInfo();
        var roleclass = player.GetRoleClass();

        if (role.GetRoleTypes() is RoleTypes.Scientist)
        {
            CloseVitals.Ability = true;
            return true;
        }
        if (roleclass is IUsePhantomButton pb && pb.UseOneclickButton)
        {
            //Shと違い、クリックしたときクールが発生しないことがあるため、
            //クリックしたってのを最低限可視化させる。
            __instance.OverrideColor(Palette.DisabledGrey);
            _ = new LateTask(() =>
            {
                __instance.OverrideColor(Palette.EnabledColor);
            }, 0.07f, "", true);
            //非クライアントの場合、役職調整の影響でキルクール弄らないとキルクールが正常の値にならないが、
            //クライアントの場合、別に役職変えてファントム状態解除をしなくていいので関係ない関数になる★

            bool AdjustKillCooldown = true;
            bool? ResetCooldown = true;

            pb.CheckOnClick(ref AdjustKillCooldown, ref ResetCooldown);

            float cooldown = IUsePhantomButton.GetRemainingKillCooldown(player);
            if (pb.SyncAbilityCooldownWithKillCooldown)
                pb.SetSyncedAbilityCooldown(cooldown);

            if (AdjustKillCooldown)
            {
                Main.AllPlayerKillCooldown[player.PlayerId] = cooldown;
                IUsePhantomButton.IPPlayerKillCooldown[player.PlayerId] = 0f;
                player.SetKillTimer(cooldown);
                player.SyncSettings();
            }

            if (ResetCooldown == true)
            {
                player.Data.Role.SetCooldown();
            }

            return false;
        }
        else
        if (roleInfo?.IsDesyncImpostor == true && roleInfo.BaseRoleType.Invoke() == RoleTypes.Shapeshifter)
        {
            if (!(roleclass?.CanUseAbilityButton() ?? false)) return false;
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                pc.Data.Role.NameColor = Color.white;
            }
            player.Data.Role.Cast<ShapeshifterRole>().UseAbility();
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                pc.Data.Role.NameColor = Color.white;
            }
            return true;
        }
        else
        if (roleInfo?.IsDesyncImpostor == true && roleInfo?.BaseRoleType.Invoke() == RoleTypes.Phantom)
        {
            if (!(roleclass?.CanUseAbilityButton() ?? false)) return false;
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                pc.Data.Role.NameColor = Color.white;
            }
            player.Data.Role.Cast<PhantomRole>().UseAbility();
            return true;
        }

        return true;
    }
}

/*[HarmonyPatch(typeof(KillButton), nameof(KillButton.DoClick))]
public static class KillButtonDoClickPatch
{
    public static void Prefix()
    {
        var players = PlayerControl.LocalPlayer.GetPlayersInAbilityRangeSorted(false);
        PlayerControl closest = players.Count <= 0 ? null : players[0];
        if (!GameStates.IsInTask || !PlayerControl.LocalPlayer.CanUseKillButton() || closest == null
            || PlayerControl.LocalPlayer.Data.IsDead || HudManager._instance.KillButton.isCoolingDown) return;
        PlayerControl.LocalPlayer.CheckMurder(closest); //一時的な修正
    }
}*/
