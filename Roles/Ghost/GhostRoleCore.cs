using TownOfHost.Roles.AddOns.Common;

using TownOfHost.Roles.AddOns.Crewmate;

using TownOfHost.Roles.AddOns.Impostor;

using TownOfHost.Roles.AddOns.Neutral;

using TownOfHost.Roles.Core;

using TownOfHost.Roles.Crewmate;

using TownOfHost.Roles.Ghost;



namespace TownOfHost;



class GhostRoleCore

{

    [Attributes.GameModuleInitializer]

    public static void Init()

    {

        AsistingAngel.Init();

        DemonicCrusher.Init();

        DemonicTracker.Init();

        DemonicVenter.Init();

        DemonicSupporter.Init();

        Ghostbuttoner.Init();

        GhostNoiseSender.Init();

        GhostReseter.Init();

        GhostRumour.Init();

        GuardianAngel.Init();

        GhostFloodlight.Init();
        TownOfHost.Roles.Vanilla.Influencer.Init();

        GhostSaboteur.Init();



        //アドオンもここ置かせて( ᐛ )

        LastImpostor.Init();

        LastNeutral.Init();

        Watching.Init();

        Serial.Init();
        BountyGuesser.Init();

        Management.Init();

        Speeding.Init();

        Guarding.Init();

        Connecting.Init();

        Opener.Init();

        //AntiTeleporter.Init();

        Moon.Init();

        Tiebreaker.Init();

        MagicHand.Init();

        Amnesia.Init();

        Lighting.Init();

        Seeing.Init();

        Revenger.Init();

        Amanojaku.Init();

        Guesser.Init();

        Autopsy.Init();

        Workhorse.Init();

        NonReport.Init();

        Notvoter.Init();

        PlusVote.Init();

        Elector.Init();

        News.Init();

        InfoPoor.Init();

        Water.Init();

        SlowStarter.Init();

        Slacker.Init();

        Stamina.Init();

        Jumbo.Init();

        Transparent.Init();

        Clumsy.Init();

        OneWolf.Init();

        Securer.Init();

        Sealer.Init();

        SilverBuzzer.Init();
        UserAddons2026.Init();
        TownOfHost.Roles.Core.UserRoles2026.Reset();

        Twins.Init();

        Triplets.Init();

        Stack.Init();

        Sunglasses.Init();

        Powerful.Init();

        Absorb.Init();

        LastCrewmate.Init();
        Inheritance.Init();
        Roles.Impostor.SurrogateSystem.Init();

        Reversal.Init();

    }

    public static void SetupCustomOptionAddonAndIsGhostRole()
    {
        // ※ Reversal.SetupCustomOption() は Modules/OptionHolder.cs 側で呼ぶ(二重登録を避けるため)
        // Add-Ons
        Amanojaku.SetupCustomOption();
        LastImpostor.SetupCustomOption();
        LastCrewmate.SetupCustomOption();
        Inheritance.SetupCustomOption();
        LastNeutral.SetupCustomOption();
        Workhorse.SetupCustomOption();
        Stack.SetupCustomOption();
        ObjectOptionitem.Create(1_000_116, "Buff-Addon", true, null, TabGroup.Addons)
            .SetOptionName(() => "Buff Add-on").SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Moon)).SetTag(CustomOptionTags.Role);
        // バフ属性: 「NEW → あ行〜わ行」の見出しに分けて並べる
        RoleGojuon.SetupGrouped(TabGroup.Addons, 12, new (CustomRoles, System.Action)[]
        {
            (CustomRoles.Guesser, Guesser.SetupCustomOption),
            (CustomRoles.Serial, Serial.SetupCustomOption),
            (CustomRoles.BountyGuesser, BountyGuesser.SetupCustomOption),
            (CustomRoles.MagicHand, MagicHand.SetupCustomOption),
            (CustomRoles.Powerful, Powerful.SetupCustomOption),
            (CustomRoles.Connecting, Connecting.SetupCustomOption),
            (CustomRoles.Watching, Watching.SetupCustomOption),
            (CustomRoles.PlusVote, PlusVote.SetupCustomOption),
            (CustomRoles.Tiebreaker, Tiebreaker.SetupCustomOption),
            (CustomRoles.Autopsy, Autopsy.SetupCustomOption),
            (CustomRoles.Revenger, Revenger.SetupCustomOption),
            (CustomRoles.Speeding, Speeding.SetupCustomOption),
            (CustomRoles.Guarding, Guarding.SetupCustomOption),
            (CustomRoles.Absorb, Absorb.SetupCustomOption),
            (CustomRoles.Symbol, Symbol.SetupCustomOption),
            (CustomRoles.Management, Management.SetupCustomOption),
            (CustomRoles.Seeing, Seeing.SetupCustomOption),
            (CustomRoles.Opener, Opener.SetupCustomOption),
            (CustomRoles.Lighting, Lighting.SetupCustomOption),
            (CustomRoles.Moon, Moon.SetupCustomOption),
        });
        //デバフ達
        ObjectOptionitem.Create(1_000_117, "Debuff-Addon", true, null, TabGroup.Addons)
            .SetOptionName(() => "Debuff Add-on").SetColor(UtilsRoleText.GetRoleColor(CustomRoles.SlowStarter)).SetTag(CustomOptionTags.Role);
        // デバフ属性: 「NEW → あ行〜わ行」の見出しに分けて並べる
        RoleGojuon.SetupGrouped(TabGroup.Addons, 13, new (CustomRoles, System.Action)[]
        {
            (CustomRoles.Amnesia, Amnesia.SetupCustomOption),
            (CustomRoles.News, News.SetupCustomOption),
            (CustomRoles.OneWolf, OneWolf.SetupCustomOption),
            (CustomRoles.Securer, Securer.SetupCustomOption),
            (CustomRoles.Sealer, Sealer.SetupCustomOption),
            (CustomRoles.SilverBuzzer, SilverBuzzer.SetupCustomOption),
            (CustomRoles.Surviving, Surviving.SetupCustomOption),
            (CustomRoles.Berserker, Berserker.SetupCustomOption),
            (CustomRoles.ToughGuy, ToughGuy.SetupCustomOption),
            (CustomRoles.Comorbidity, Comorbidity.SetupCustomOption),
            (CustomRoles.Murder, Murder.SetupCustomOption),
            (CustomRoles.BetrayGuesser, BetrayGuesser.SetupCustomOption),
            (CustomRoles.SlowStarter, SlowStarter.SetupCustomOption),
            (CustomRoles.Notvoter, Notvoter.SetupCustomOption),
            (CustomRoles.Elector, Elector.SetupCustomOption),
            (CustomRoles.InfoPoor, InfoPoor.SetupCustomOption),
            (CustomRoles.NonReport, NonReport.SetupCustomOption),
            (CustomRoles.Sunglasses, Sunglasses.SetupCustomOption),
            (CustomRoles.Transparent, Transparent.SetupCustomOption),
            (CustomRoles.Water, Water.SetupCustomOption),
            (CustomRoles.Clumsy, Clumsy.SetupCustomOption),
            (CustomRoles.Slacker, Slacker.SetupCustomOption),
            (CustomRoles.Stamina, Stamina.SetupCustomOption),
            (CustomRoles.Jumbo, Jumbo.SetupCustomOption),
            (CustomRoles.Surrender, Surrender.SetupCustomOption),
            (CustomRoles.Reporting, Reporting.SetupCustomOption),
        });
        // ゴースト役職は「NEW → あ行〜わ行」の見出しに分けて並べる(見出しを押すと開閉できる)
        RoleGojuon.SetupGrouped(TabGroup.GhostRoles, 11, new (CustomRoles, System.Action)[]
        {
            (CustomRoles.DemonicTracker, DemonicTracker.SetupCustomOption),
            (CustomRoles.DemonicCrusher, DemonicCrusher.SetupCustomOption),
            (CustomRoles.DemonicVenter, DemonicVenter.SetupCustomOption),
            (CustomRoles.DemonicSupporter, DemonicSupporter.SetupCustomOption),
            (CustomRoles.AsistingAngel, AsistingAngel.SetupCustomOption),
            (CustomRoles.Ghostbuttoner, Ghostbuttoner.SetupCustomOption),
            (CustomRoles.GhostSaboteur, GhostSaboteur.SetupCustomOption),
            (CustomRoles.GhostFloodlight, GhostFloodlight.SetupCustomOption),
            (CustomRoles.GhostNoiseSender, GhostNoiseSender.SetupCustomOption),
            (CustomRoles.GhostReseter, GhostReseter.SetupCustomOption),
            (CustomRoles.GhostRumour, GhostRumour.SetupCustomOption),
            (CustomRoles.GuardianAngel, GuardianAngel.SetupCustomOption),
            (CustomRoles.Influencer, TownOfHost.Roles.Vanilla.Influencer.SetupCustomOption),
        });
    }

}

