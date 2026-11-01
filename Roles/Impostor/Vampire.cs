using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Neutral;
using TownOfHost.Roles.Crewmate;

namespace TownOfHost.Roles.Impostor
{
    public sealed class Vampire : RoleBase, IImpostor
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(Vampire),
                player => new Vampire(player),
                CustomRoles.Vampire,
                () => RoleTypes.Impostor,
                CustomRoleTypes.Impostor,
                8000,
                SetupOptionItem,
                "va",
                OptionSort: (4, 2),
                introSound: () => GetIntroSound(RoleTypes.Shapeshifter),
                from: From.TheOtherRoles
            );
        public Vampire(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
            KillDelay = OptionKillDelay.GetFloat();
            leaveBloodstain = OptionLeaveBloodstain.GetBool();
            bloodstainLifeTime = OptionBloodstainLifeTime.GetFloat();

            BittenPlayers.Clear();
            Spped = SpeedDownCount.GetFloat();
            tmpSpeed = Main.NormalOptions.PlayerSpeedMod;

            pendingBloodstainPositions.Clear();
            activeBloodstains.Clear();
            wasInTask = GameStates.IsInTask;
        }
        static OptionItem OptionKillCool;
        static OptionItem OptionKillDelay;
        static OptionItem SpeedDown;
        static OptionItem SpeedDownCount;
        static OptionItem OptionLeaveBloodstain;
        static OptionItem OptionBloodstainLifeTime;
        enum OptionName
        {
            VampireKillDelay, VampireSpeedDown, VampireSpeedDownCount,
            VampireLeaveBloodstain, VampireBloodstainLifeTime
        }

        static float KillDelay;
        static float Spped;
        static float tmpSpeed;
        // 血痕を残すかどうか
        readonly bool leaveBloodstain;
        // 血痕が消えるまでの時間
        readonly float bloodstainLifeTime;
        // 会議中に噛んだ相手の座標を一時的に貯めておくリスト。
        // 会議が終わりタスクフェーズへ戻ったタイミングでまとめて血痕をスポーンさせる。
        readonly List<Vector2> pendingBloodstainPositions = new();
        // 現在表示中の血痕一覧(寿命管理用)
        readonly List<VampireBloodStain> activeBloodstains = new();
        // 直前のFixedUpdateでタスクフェーズだったかどうか(会議→タスクへの切り替わり検出用)
        bool wasInTask;
        // 会議があったか。OnFixedUpdate は会議中には呼ばれないので、会議中かどうかを見張っても気付けない(血痕が一度も出ていなかった)
        bool meetingPassed;
        public bool CanBeLastImpostor { get; } = false;
        Dictionary<byte, float> BittenPlayers = new(14);

        private static void SetupOptionItem()
        {
            OptionKillCool = FloatOptionItem.Create(RoleInfo, 9, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionKillDelay = FloatOptionItem.Create(RoleInfo, 10, OptionName.VampireKillDelay, new(1f, 1000f, 0.1f), 10f, false)
                .SetValueFormat(OptionFormat.Seconds);
            SpeedDown = BooleanOptionItem.Create(RoleInfo, 11, OptionName.VampireSpeedDown, true, false);
            SpeedDownCount = FloatOptionItem.Create(RoleInfo, 12, OptionName.VampireSpeedDownCount, new(0f, 1000f, 1f), 10f, false, SpeedDown)
            .SetValueFormat(OptionFormat.Seconds);
            // 噛んだ相手の足元に血痕を残すか。ONの場合、噛んだ瞬間ではなく
            // 「その会議が終わってタスクフェーズに戻ったタイミング」で血痕が出現する。
            OptionLeaveBloodstain = BooleanOptionItem.Create(RoleInfo, 13, OptionName.VampireLeaveBloodstain, false, false);
            OptionBloodstainLifeTime = FloatOptionItem.Create(RoleInfo, 14, OptionName.VampireBloodstainLifeTime, new(1f, 300f, 1f), 15f, false, OptionLeaveBloodstain)
                .SetValueFormat(OptionFormat.Seconds);
        }

        public float CalculateKillCooldown() => OptionKillCool.GetFloat();
        public void OnCheckMurderAsKiller(MurderInfo info)
        {
            if (!info.CanKill) return; //キル出来ない相手には無効
            var (killer, target) = info.AttemptTuple;

            if (target.Is(CustomRoles.Bait) || target.Is(CustomRoles.InSender) || info.IsFakeSuicide)
            {
                Jizo.Checkroom(Player.GetPlainShipRoom(), Player);
                return;
            }
            if (info.CheckHasGuard())
            {
                info.IsGuard = true;
                return;
            }

            //誰かに噛まれていなければ登録
            if (!BittenPlayers.ContainsKey(target.PlayerId))
            {
                Jizo.Checkroom(Player.GetPlainShipRoom(), Player);
                killer.SetKillCooldown();
                BittenPlayers.Add(target.PlayerId, 0f);

                // 血痕を残す設定がONの場合、噛んだ瞬間の相手の足元座標を記録しておく。
                // 実際に血痕が出現するのは、この会議が終わってタスクフェーズへ戻った後。
                if (leaveBloodstain)
                {
                    pendingBloodstainPositions.Add(target.GetTruePosition());
                    Logger.Info($"Vampire: {target.name}の血痕位置を記録しました " +
                        $"(保留中:{pendingBloodstainPositions.Count}件)", "Vampire.Bloodstain");
                }
            }
            info.DoKill = false;
        }
        public override void OnFixedUpdate(PlayerControl player)
        {
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask)
            {
                wasInTask = GameStates.IsInTask;
                return;
            }

            // 会議中(またはロビー)からタスクフェーズへ切り替わった瞬間を検出する。
            // 【重要】血痕は噛んだ瞬間ではなく、その会議が終わってタスクフェーズに
            // 戻ったこのタイミングで初めて出現させる、という要望に基づく処理。
            // 【重要・不具合対策】CustomNetObjectの生成キューは1体あたり約0.4秒かかる
            // 設計のため、複数体を同一フレームでnewすると生成処理が競合し、2体目以降が
            // 正しく表示されないことがあった。LateTaskで少しずつ間隔を空けて生成する。
            if (meetingPassed && pendingBloodstainPositions.Count > 0)
            {
                meetingPassed = false;
                Logger.Info($"Vampire: タスクフェーズ開始を検知、保留中の血痕{pendingBloodstainPositions.Count}件をスポーンします", "Vampire.Bloodstain");
                var positions = new List<Vector2>(pendingBloodstainPositions);
                for (int i = 0; i < positions.Count; i++)
                {
                    var pos = positions[i];
                    var delay = i * 0.6f;
                    var task = new LateTask(() =>
                    {
                        activeBloodstains.Add(new VampireBloodStain(pos, bloodstainLifeTime));
                        Logger.Info($"Vampire: 血痕をスポーンしました position={pos}", "Vampire.Bloodstain");
                    }, delay, $"Vampire.SpawnBloodstain.{i}", true);
                }
                pendingBloodstainPositions.Clear();
            }
            wasInTask = true;

            // 表示中の血痕の寿命を進める。
            for (int i = activeBloodstains.Count - 1; i >= 0; i--)
            {
                var stain = activeBloodstains[i];
                stain.Tick(Time.fixedDeltaTime);
                if (!stain.IsAlive) activeBloodstains.RemoveAt(i);
            }

            foreach (var (targetId, timer) in BittenPlayers.ToArray())
            {
                if (timer >= KillDelay)
                {
                    var target = PlayerCatch.GetPlayerById(targetId);
                    KillBitten(target);
                    BittenPlayers.Remove(targetId);
                }
                else
                {
                    BittenPlayers[targetId] += Time.fixedDeltaTime;

                    if (SpeedDown.GetBool() && timer >= Spped)
                    {
                        var target = PlayerCatch.GetPlayerById(targetId);
                        if (target.IsAlive())
                        {
                            var x = KillDelay - Spped;
                            float Swariai = (KillDelay - Spped - (timer - Spped)) / x;
                            float Sp = tmpSpeed * Swariai;

                            if (KillDelay - timer <= 0.5f) Sp = Main.MinSpeed;//これは残り0,5sになったら静止させてｳｸﾞｯ...ｺｺﾏﾃﾞｶｯ...ってするやつ。

                            if (Sp >= Main.MinSpeed && Sp < tmpSpeed)
                            {
                                Main.AllPlayerSpeed[target.PlayerId] = Sp;
                                target.MarkDirtySettings();
                            }
                        }
                    }
                }
            }
        }
        public override void OnReportDeadBody(PlayerControl repo, NetworkedPlayerInfo __)
        {
            meetingPassed = true;
            if (AddOns.Common.Amnesia.CheckAbilityreturn(Player)) return;
            foreach (var targetId in BittenPlayers.Keys)
            {
                var target = PlayerCatch.GetPlayerById(targetId);
                KillBitten(target, true);
            }
            BittenPlayers.Clear();
        }
        public bool OverrideKillButtonText(out string text)
        {
            text = GetString("VampireBiteButtonText");
            return true;
        }
        public bool OverrideKillButton(out string text)
        {
            text = "Vampire_Kill";
            return true;
        }

        private void KillBitten(PlayerControl target, bool isButton = false)
        {
            if (target == null) return;
            var vampire = Player;

            _ = new LateTask(() =>
            {
                Main.AllPlayerSpeed[target.PlayerId] = tmpSpeed;
                _ = new LateTask(() => target.MarkDirtySettings(), 0.9f, "Do-ki");
            }, 0.4f, "Modosu");

            if (target.IsAlive())
            {
                if (CustomRoleManager.OnCheckMurder(vampire, target, target, target, true, Killpower: 1, deathReason: CustomDeathReason.Bite))
                {
                    target.SetRealKiller(vampire);
                    Logger.Info($"Vampireに噛まれている{target.name}を自爆させました。", "Vampire");
                    if (!isButton && vampire.IsAlive())
                        RPC.PlaySoundRPC(vampire.PlayerId, Sounds.KillSound);
                    Achievements.RpcCompleteAchievement(Player.PlayerId, 1, achievements[0]);
                    Achievements.RpcCompleteAchievement(Player.PlayerId, 1, achievements[1]);
                }
                else Logger.Info($"Vampireに噛まれた{target.name}にキルが通りませんでした。", "Vampire");
            }
            else
            {
                Logger.Info($"Vampireに噛まれている{target.name}はすでに死んでいました。", "Vampire.KillBitten");
            }
        }
        public override void OnMurderPlayerAsTarget(MurderInfo info)
        {
            var roleclass = info.AttemptKiller.GetRoleClass();
            if ((roleclass as Alien)?.mode is Alien.AlienMode.Vampire ||
                (roleclass as JackalAlien)?.mode is Alien.AlienMode.Vampire ||
                roleclass is Vampire)
                Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[2]);
        }
        public override void OnDestroy()
        {
            // 表示中の血痕を確実に片付ける(死亡・役職変化時のリーク防止)。
            foreach (var stain in activeBloodstains) stain.Remove();
            activeBloodstains.Clear();
            pendingBloodstainPositions.Clear();
        }
        public static Dictionary<int, Achievement> achievements = new();
        [Attributes.PluginModuleInitializer]
        public static void Load()
        {
            var n1 = new Achievement(RoleInfo, 0, 3, 0, 0);
            var l1 = new Achievement(RoleInfo, 1, 30, 0, 1);
            var sp1 = new Achievement(RoleInfo, 2, 1, 0, 3, true);
            achievements.Add(0, n1);
            achievements.Add(1, l1);
            achievements.Add(2, sp1);
        }
    }
}
