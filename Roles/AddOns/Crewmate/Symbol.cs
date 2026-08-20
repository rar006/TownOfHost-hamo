using TownOfHost.Roles.Core;
using static TownOfHost.Options;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.AddOns.Common;

/// <summary>
/// クルーメイトにのみ付与される、名前が黄色になる属性。
/// </summary>
public static class Symbol
{
    private static readonly int Id = 25200;
    public const string SymbolNameColor = "#ffe066";
    public static string SubRoleMark = Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Symbol), "☆");

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Symbol, fromtext: UtilsOption.GetFrom(From.TownOfHost_hamo));
        // クルーメイトのみに付与、最大1人まで(既定値)を維持する
        AddOnsAssignData.Create(Id + 10, CustomRoles.Symbol, true, false, false, false);
    }

    public static bool HasSymbol(this PlayerControl player)
        => player != null && player.Is(CustomRoles.Symbol);
}
