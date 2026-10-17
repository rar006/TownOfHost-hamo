using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Madmate;

/// <summary>
/// マッドマジック: 自投票すると変身モードになり、チャット(/cmd mm 番号)で選んだマッドメイト役職に一定ターン変身する。
/// 変身はターン経過で元に戻る。タスク数は変わらない。マッドワーカーにはならない。
/// </summary>
public sealed class MadMagic : RoleBase, IKillFlashSeeable, IDeathReasonSeeable, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadMagic),
            player => new MadMagic(player),
            CustomRoles.MadMagic,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            93000,
            SetupOptionItem,
            "mmg",
            "#ff1919",
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            isNewRole: true
        );

    static OptionItem OptionTurns;
    static OptionItem[] OptionChoices;
    enum OptionName { MadMagicTurns, MadMagicChoice1, MadMagicChoice2, MadMagicChoice3 }

    /// <summary>変身先の候補(マッドワーカーとマッドマジック自身は除く)</summary>
    static CustomRoles[] Candidates => CustomRolesHelper.AllRoles
        .Where(r => r.GetRoleInfo()?.CustomRoleType == CustomRoleTypes.Madmate && r is not (CustomRoles.MadWorker or CustomRoles.MadMagic or CustomRoles.SKMadmate))
        .ToArray();

    static void SetupOptionItem()
    {
        OptionTurns = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MadMagicTurns, new(1, 99, 1), 2, false).SetValueFormat(OptionFormat.Turns);
        var names = new[] { "Random" }.Concat(Candidates.Select(r => r.ToString())).ToArray();
        OptionChoices = new[]
        {
            StringOptionItem.Create(RoleInfo, 11, OptionName.MadMagicChoice1, names, 0, false),
            StringOptionItem.Create(RoleInfo, 12, OptionName.MadMagicChoice2, names, 0, false),
            StringOptionItem.Create(RoleInfo, 13, OptionName.MadMagicChoice3, names, 0, false),
        };
        OverrideTasksData.Create(RoleInfo, 20);
    }

    public MadMagic(PlayerControl player) : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        canSeeKillFlash = Options.MadmateCanSeeKillFlash.GetBool();
        canSeeDeathReason = Options.MadmateCanSeeDeathReason.GetBool();
    }
    static bool canSeeKillFlash;
    static bool canSeeDeathReason;
    public bool? CheckKillFlash(MurderInfo info) => canSeeKillFlash;
    public bool? CheckSeeDeathReason(PlayerControl seen) => canSeeDeathReason;

    /// <summary>この試合で選べる3つの役職(ランダム指定はここで決まる)</summary>
    readonly List<CustomRoles> choices = new();
    bool choosing;

    // ===== 変身中の人(役職が変わるとこのクラスは消えるので static で覚える) =====
    /// <summary>変身中: プレイヤー → 元に戻る日</summary>
    static readonly Dictionary<byte, int> transformed = new();

    public override void Add()
    {
        choices.Clear();
        var pool = Candidates.ToList();
        foreach (var opt in OptionChoices)
        {
            var v = opt.GetValue();
            CustomRoles r;
            if (v <= 0 || v - 1 >= pool.Count) r = pool.Count > 0 ? pool[IRandom.Instance.Next(pool.Count)] : CustomRoles.Madmate;
            else r = pool[v - 1];
            choices.Add(r);
        }
    }

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability() || voter.PlayerId != Player.PlayerId || !Player.IsAlive()) return true;
        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
            {
                choosing = true;
                var list = string.Join("\n", choices.Select((r, i) => $"{i + 1}. {UtilsRoleText.GetRoleColorAndtext(r)}"));
                Utils.SendMessage(string.Format(GetString("MadMagicChoose"), list, OptionTurns.GetInt()), Player.PlayerId);
            }
            if (status is VoteStatus.Skip)
            {
                choosing = false;
                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            }
            SetMode(Player, status is VoteStatus.Self);
            return false;
        }
        return true;
    }

    /// <summary>/cmd mm 番号</summary>
    public void HandleCommand(string[] args)
    {
        var me = Player.PlayerId;
        if (!GameStates.IsMeeting || !choosing) { Utils.SendMessage(GetString("MadMagicNotChoosing"), me); return; }
        if (args.Length < 2 || !int.TryParse(args[1], out var n) || n < 1 || n > choices.Count)
        {
            Utils.SendMessage(GetString("MadMagicUsage"), me);
            return;
        }
        var role = choices[n - 1];
        choosing = false;
        transformed[me] = UtilsGameLog.day + OptionTurns.GetInt();
        Utils.SendMessage(string.Format(GetString("MadMagicTransformed"), UtilsRoleText.GetRoleColorAndtext(role), OptionTurns.GetInt()), me);
        Transform(Player, role);
    }

    static void Transform(PlayerControl pc, CustomRoles role)
    {
        // タスク数は変わらない(PlayerState のタスク情報はそのまま)
        pc.RpcSetCustomRole(role, log: null);
        var baseRole = role.GetRoleInfo()?.BaseRoleType?.Invoke() ?? RoleTypes.Crewmate;
        pc.RpcSetRoleDesync(baseRole, pc.GetClientId());
        pc.MarkDirtySettings();
        UtilsNotifyRoles.NotifyRoles();
    }

    /// <summary>会議が終わるたびに(ホスト): 変身の期限が来た人を元に戻す</summary>
    public static void AfterMeeting()
    {
        if (transformed.Count == 0) return;
        foreach (var (id, until) in transformed.ToList())
        {
            if (UtilsGameLog.day < until) continue;
            transformed.Remove(id);
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc == null || !pc.IsAlive()) continue;
            Transform(pc, CustomRoles.MadMagic);
            Utils.SendMessage(GetString("MadMagicReverted"), id);
        }
    }

    [Attributes.GameModuleInitializer]
    public static void Clear() => transformed.Clear();

    /// <summary>変身中かどうか(ゲッサーは変身先でもマッドマジックでも撃てる)</summary>
    public static bool IsTransformed(byte id) => transformed.ContainsKey(id);
}
