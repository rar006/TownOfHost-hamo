using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TownOfHost.Roles.Core;
using static TownOfHost.Options;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.AddOns.Common;

/// <summary>
/// クルーメイトへ付与される継承属性。保持者の死亡後、設定された死因なら次の会議開始時に
/// 生存クルーのランダムな1人へ付与される。継承対象がいない場合は消滅する。
/// </summary>
public static class Inheritance
{
    // 属性はSimpleRoleInfoを持たないため、NEWバッジ用フラグを属性本体で公開する。
    public const bool IsNewRole = false;
    // 他役職の親設定ID・子設定IDと重ならない専用範囲。
    private const int Id = 95200;
    private static OptionItem OptionTransferOnKill;
    private static OptionItem OptionTransferOnExile;
    private static OptionItem OptionNotifyExistence;
    public static OptionItem OptionVisibleToAll;
    private static readonly HashSet<byte> ProcessedSources = new();

    public static string SubRoleMark = Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Inheritance), "I");

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Inheritance, fromtext: UtilsOption.GetFrom(From.TownOfHost_hamo));
        // 既存BUFF ADD-ONと同じ割当・個別設定ヘッダー構成にして、
        // 属性設定をBUFF ADD-ON欄から確実に展開できるようにする。
        // クルーメイトだけに初期付与する属性。継承後も同じ属性として同期する。
        AddOnsAssignData.Create(Id + 10, CustomRoles.Inheritance, true, false, false, false);
        ObjectOptionitem.Create(Id + 51, "AddonOption", true, "", TabGroup.Addons)
            .SetOptionName(() => "Role Option")
            .SetSubRoleOptionItem(CustomRoles.Inheritance);
        OptionTransferOnKill = BooleanOptionItem.Create(Id + 20, "InheritanceTransferOnKill", true, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.Inheritance);
        OptionTransferOnExile = BooleanOptionItem.Create(Id + 21, "InheritanceTransferOnExile", true, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.Inheritance);
        OptionNotifyExistence = BooleanOptionItem.Create(Id + 22, "InheritanceNotifyExistence", true, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.Inheritance);
        OptionVisibleToAll = BooleanOptionItem.Create(Id + 23, "InheritanceVisibleToAll", true, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.Inheritance);
    }

    public static void Init() => ProcessedSources.Clear();

    private static bool IsEligibleDeath(PlayerControl source)
    {
        var state = PlayerState.GetByPlayerId(source.PlayerId);
        if (state == null) return false;
        if (state.DeathReason == CustomDeathReason.Vote) return OptionTransferOnExile?.GetBool() == true;
        if (state.DeathReason == CustomDeathReason.Disconnected) return false;
        return OptionTransferOnKill?.GetBool() == true;
    }

    private static List<PlayerControl> GetAliveCrewCandidates()
        => PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc != null && pc.GetCustomRole().IsCrewmate() && !pc.Is(CustomRoles.Inheritance))
            .ToList();

    /// <summary>会議開始後、死亡済みの保持者から1回だけ継承する。</summary>
    public static void TransferAtMeetingStart()
    {
        if (!AmongUsClient.Instance.AmHost || !CustomRoles.Inheritance.IsPresent()) return;

        var sources = PlayerControl.AllPlayerControls
            .ToArray()
            .Where(pc => pc != null && !pc.IsAlive() && pc.Is(CustomRoles.Inheritance) && !ProcessedSources.Contains(pc.PlayerId))
            .ToList();

        foreach (var source in sources)
        {
            ProcessedSources.Add(source.PlayerId);
            if (!IsEligibleDeath(source)) continue;

            var candidates = GetAliveCrewCandidates();
            if (candidates.Count == 0) continue;

            var successor = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            successor.RpcSetCustomRole(CustomRoles.Inheritance);
            successor.SyncSettings();

            if (OptionVisibleToAll?.GetBool() == true)
                UtilsNotifyRoles.NotifyRoles();
            else
                UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: successor, NoCache: true);

            UtilsGameLog.AddGameLog("Inheritance", $"{UtilsName.GetPlayerColor(source, true)} の属性を {UtilsName.GetPlayerColor(successor, true)} へ継承しました。");
        }

        if (OptionNotifyExistence?.GetBool() == true && PlayerCatch.AllAlivePlayerControls.Any(pc => pc.Is(CustomRoles.Inheritance)))
            Utils.SendMessage(GetString("InheritanceNotification"), byte.MaxValue, GetString("Inheritance"));
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
internal static class InheritanceMeetingStartPatch
{
    public static void Postfix()
    {
        Inheritance.TransferAtMeetingStart();
    }
}
