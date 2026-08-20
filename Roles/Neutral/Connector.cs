using System.Collections.Generic;
using System.Linq;
using Hazel;
using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== コネクター (Connector) =====
// イントロ：一心同体
// 陣営：ニュートラル / 判定：クルーメイト
// ゲーム開始時に他のプレイヤー1名を「相方」として設定する。コネクターは相方を知覚できる(常時)。
// オプションで相方側からもコネクターを知覚できるようにできる(既定OFF)。
// 相方が生存かつ勝利すると、コネクターも追加勝利できる。
// (自身の死亡が勝利条件に必要な役職 = ExcludeFromPartner は相方の対象から除外する)
public sealed class Connector : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Connector),
            player => new Connector(player),
            CustomRoles.Connector,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            79400,
            SetupOptionItem,
            "cnc",
            "#37c1e0",
            (4, 2),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo,
            assignInfo: new RoleAssignInfo(CustomRoles.Connector, CustomRoleTypes.Neutral)
            {
                // ON/OFFのみ(1人まで)にする。2人以上は選べないようにする。
                AssignCountRule = new(0, 1, 1)
            }
        );

    public Connector(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        FollowDeath = OptionFollowDeath.GetBool();
        PartnerCanSense = OptionPartnerCanSense.GetBool();
        AddWinOnly = OptionAddWinOnly.GetBool();

        Connectors.Add(this);
        CustomRoleManager.OnMurderPlayerOthers.Add(OnMurderPlayerOthers);
    }

    public static OptionItem OptionFollowDeath;
    public static OptionItem OptionPartnerCanSense;
    public static OptionItem OptionAddWinOnly;

    enum OptionName
    {
        ConnectorFollowDeath,
        ConnectorPartnerCanSense,
        ConnectorAddWinOnly
    }

    private static bool FollowDeath;
    private static bool PartnerCanSense;
    private static bool AddWinOnly;

    public static HashSet<Connector> Connectors = new(15);
    public byte PartnerId = byte.MaxValue;
    private bool PartnerDead;

    // 自身の死亡が勝利条件に必要な役職は相方の対象から除外する
    public static readonly CustomRoles[] ExcludeFromPartner =
    {
        CustomRoles.GM,
        CustomRoles.Jester,
        CustomRoles.Ogre,
        CustomRoles.PokerFace,
        CustomRoles.King,
        CustomRoles.Vega,
        CustomRoles.Altair,
        CustomRoles.Limiter,
        CustomRoles.Madonna,
        CustomRoles.Cupid,
        CustomRoles.OneLove,
        CustomRoles.Connector,
    };

    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 0);
        OptionFollowDeath = BooleanOptionItem.Create(RoleInfo, 10, OptionName.ConnectorFollowDeath, true, false);
        OptionPartnerCanSense = BooleanOptionItem.Create(RoleInfo, 11, OptionName.ConnectorPartnerCanSense, false, false);
        OptionAddWinOnly = BooleanOptionItem.Create(RoleInfo, 12, OptionName.ConnectorAddWinOnly, false, false);
    }

    public override void Add()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var playerId = Player.PlayerId;
        List<PlayerControl> candidates = new();
        var rand = IRandom.Instance;

        foreach (var target in PlayerCatch.AllPlayerControls)
        {
            if (playerId == target.PlayerId) continue;
            if (ExcludeFromPartner.Contains(target.GetCustomRole())) continue;
            candidates.Add(target);
        }

        if (candidates.Count == 0) return;

        var partner = candidates[rand.Next(candidates.Count)];
        PartnerId = partner.PlayerId;
        SendRPC();
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} <=> {partner.GetNameWithRole().RemoveHtmlTags()}", "Connector");
    }
    public override void OnDestroy()
    {
        Connectors.Remove(this);
        if (Connectors.Count <= 0)
        {
            CustomRoleManager.OnMurderPlayerOthers.Remove(OnMurderPlayerOthers);
        }
    }
    public void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        using var sender = CreateSender();
        sender.Writer.Write(PartnerId);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        PartnerId = reader.ReadByte();
    }

    // 相方が(通殺/RPC経由で)死亡したときの後追い死。コネクター本人が殺された場合も相方を道連れにする。
    // ※ Lovers等と違い、これは「通常のMurderPlayerフロー」を通ったキルにしか反応しない。
    //   タイマー即死・自滅・特殊な直接Deadセットなど、別経路で死ぬロールと組み合わさると
    //   ここが発火せずコネクターの後追いが機能しないことがあるため、下の PollFollowDeath で
    //   Data.IsDead を直接ポーリングして拾う (Loversの FixedUpdate 定期チェックと同じ考え方)。
    private static void OnMurderPlayerOthers(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var target = info.AttemptTarget;

        foreach (var connector in Connectors.ToArray())
        {
            if (!FollowDeath) continue;
            if (connector.PartnerDead) continue;

            if (connector.PartnerId == target.PlayerId && connector.Player?.IsAlive() == true)
            {
                connector.PartnerDead = true;
                connector.Player.RpcMurderPlayerV2(connector.Player);
            }
            else if (connector.Player?.PlayerId == target.PlayerId)
            {
                var partner = PlayerCatch.GetPlayerById(connector.PartnerId);
                if (partner?.IsAlive() == true)
                {
                    connector.PartnerDead = true;
                    partner.RpcMurderPlayerV2(partner);
                }
            }
        }
    }

    // フォールバック: MurderPlayerのフローを通らない死亡(タイマーキル・自滅等の"別kill")を
    // 拾うための定期ポーリング。FixedUpatePatch.cs から Lovers 系と同じタイミングで呼ばれる想定。
    // 「一心同体」なので、コネクター→相方・相方→コネクターの両方向で後追いさせる。
    public static void PollFollowDeath()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Connectors.Count == 0) return;

        foreach (var connector in Connectors.ToArray())
        {
            if (!FollowDeath) continue;
            if (connector.PartnerDead) continue;

            var partner = PlayerCatch.GetPlayerById(connector.PartnerId);
            if (partner == null) continue;

            bool connectorDead = connector.Player?.Data?.IsDead == true;
            bool partnerDead = partner.Data?.IsDead == true;

            if (partnerDead && connector.Player?.IsAlive() == true)
            {
                // 相方が先に死亡 → コネクターが後追い
                connector.PartnerDead = true;
                connector.Player.RpcMurderPlayerV2(connector.Player);
            }
            else if (connectorDead && partner.IsAlive())
            {
                // コネクター自身が先に死亡 → 相方を道連れにする
                connector.PartnerDead = true;
                partner.RpcMurderPlayerV2(partner);
            }
        }
    }

    // 相方が会議で追放されたときの後追い死 (単独追放のケース)
    // コネクター自身が追放された場合も、相方を道連れにする。
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!FollowDeath) return;
        if (PartnerDead) return;

        if (exiled.PlayerId == PartnerId && Player?.IsAlive() == true)
        {
            PartnerDead = true;
            Player.RpcExileV3();
        }
        else if (exiled.PlayerId == Player?.PlayerId)
        {
            var partner = PlayerCatch.GetPlayerById(PartnerId);
            if (partner?.IsAlive() == true)
            {
                PartnerDead = true;
                partner.RpcExileV3();
            }
        }
    }

    // 相方が会議由来の連鎖死(道連れ・後追いの後追い等)で死んだケースを拾う。
    // MeetingHudPatch.CheckForDeathOnExile から、会議で死亡が確定した全プレイヤーIdについて呼ばれる想定。
    // OnExileWrapUp は「その会議で直接追放された1人」しか見ないため、これがないと
    // 道連れ経由で死んだ相方への後追いが反応しないことがある。双方向対応。
    public static void CheckExileDeath(byte deadPlayerId)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Connectors.Count == 0) return;

        foreach (var connector in Connectors.ToArray())
        {
            if (!FollowDeath) continue;
            if (connector.PartnerDead) continue;

            if (connector.PartnerId == deadPlayerId && connector.Player?.IsAlive() == true)
            {
                connector.PartnerDead = true;
                connector.Player.RpcExileV3();
            }
            else if (connector.Player?.PlayerId == deadPlayerId)
            {
                var partner = PlayerCatch.GetPlayerById(connector.PartnerId);
                if (partner?.IsAlive() == true)
                {
                    connector.PartnerDead = true;
                    partner.RpcExileV3();
                }
            }
        }
    }

    // 知覚: コネクターは常に相方が分かる。オプションONなら相方側からもコネクターが分かる。
    public override string GetMark(PlayerControl seer, PlayerControl seen, bool _ = false)
    {
        seen ??= seer;

        if (seer.PlayerId == Player.PlayerId && seen.PlayerId == PartnerId)
            return Utils.ColorString(RoleInfo.RoleColor, "⛓");

        if (PartnerCanSense && seer.PlayerId == PartnerId && seen.PlayerId == Player.PlayerId)
            return Utils.ColorString(RoleInfo.RoleColor, "⛓");

        return "";
    }

    // 追加勝利: 相方が生存していて勝利すれば、コネクターも追加勝利する
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;

        var partner = PlayerCatch.GetPlayerById(PartnerId);
        if (partner == null || !partner.IsAlive()) return false;

        var partnerWon = CustomWinnerHolder.WinnerIds.Contains(PartnerId);
        if (!partnerWon) return false;

        if (AddWinOnly && CustomWinnerHolder.WinnerIds.Contains(Player.PlayerId)) return false;

        return true;
    }
}
