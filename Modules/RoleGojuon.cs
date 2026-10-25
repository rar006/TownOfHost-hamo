using System;
using System.Collections.Generic;
using System.Linq;
using TownOfHost.Roles.Core;
using UnityEngine;

namespace TownOfHost;

public static class RoleGojuon
{
    public static readonly bool Enabled = true;

    public static readonly string[] RowNames =
    {
        "あ行", "か行", "さ行", "た行", "な行", "は行", "ま行", "や行", "ら行", "わ行", "その他", "NEW", "ネタ役職",
    };
    private static readonly string[] RowColors =
    {
        "#ff8a80", "#ffb74d", "#fff176", "#aed581", "#4db6ac", "#4fc3f7", "#7986cb", "#ba68c8", "#f06292", "#a1887f", "#b0bec5", "#ffd54f", "#ff7ce0",
    };
    public const int OtherRow = 10;
    public const int NewRow = 11;
    // ネタ役職: どの陣営でも、五十音の行やNEWには入れず、いちばん最後の「ネタ役職」見出しにまとめる
    public const int JokeRow = 12;
    private static readonly HashSet<CustomRoles> JokeRoles = new()
    {
        CustomRoles.Nova,
    };
    public static bool IsJoke(CustomRoles role) => JokeRoles.Contains(role);

    // ===== 見出しの開閉(この端末の画面だけの状態。設定の同期には関係しない) =====
    private static readonly Dictionary<CustomRoles, int> RoleHeader = new();
    private static readonly Dictionary<int, List<CustomRoles>> HeaderRoles = new();
    private static readonly HashSet<int> Collapsed = new();
    public static int StateHash
    {
        get
        {
            var h = Collapsed.Count;
            foreach (var id in Collapsed) h = unchecked(h + id * 7919);
            return h;
        }
    }

    public static bool IsNewRow(CustomRoles role) => role.IsNewRole() && !IsJoke(role);

    public static bool IsRowHeader(int optionId) => HeaderRoles.ContainsKey(optionId);

    public static bool IsHeaderCollapsed(int optionId) => Collapsed.Contains(optionId);

    public static void SetAllCollapsed(bool collapsed)
    {
        Collapsed.Clear();
        if (collapsed) foreach (var id in HeaderRoles.Keys) Collapsed.Add(id);
        foreach (var id in HeaderRoles.Keys) OnHeaderStateChanged?.Invoke(id);
    }

    public static bool AnyCollapsed => Collapsed.Count > 0;

    public static bool ToggleHeader(int optionId)
    {
        if (!Collapsed.Remove(optionId)) Collapsed.Add(optionId);
        return Collapsed.Contains(optionId);
    }

    public static string HeaderTitle(int optionId, string name)
        => (Collapsed.Contains(optionId) ? "[+] " : "[-] ") + name;

    public static bool ExpandFor(OptionItem option)
    {
        if (option == null) return false;
        var role = option.CustomRole is not CustomRoles.NotAssigned ? option.CustomRole : option.ParentRole;
        if (role is CustomRoles.NotAssigned || !RoleHeader.TryGetValue(role, out var header) || !Collapsed.Remove(header)) return false;
        OnHeaderStateChanged?.Invoke(header);
        return true;
    }

    public static System.Action<int> OnHeaderStateChanged;

    public static bool IsInCollapsedRow(OptionItem option)
    {
        if (Collapsed.Count == 0 || option == null) return false;
        var role = option.CustomRole is not CustomRoles.NotAssigned ? option.CustomRole : option.ParentRole;
        return role is not CustomRoles.NotAssigned
            && RoleHeader.TryGetValue(role, out var header)
            && Collapsed.Contains(header);
    }

    public static bool IsEmptyLimitedHeader(int optionId)
        => HeaderRoles.TryGetValue(optionId, out var roles) && roles.Count == 0;

    private static readonly Dictionary<string, (int Row, string Key)> Table = new()
    {
        ["VillainWolf"] = (0, "あくやくのおおかみ"),
        ["Administer"] = (0, "あとみにすた"),
        ["Experiencer"] = (0, "えくすへりんさ"),
        ["Obolus"] = (0, "おほるす"),
        ["Coroner"] = (1, "けんしかん"),
        ["Greedier"] = (1, "くりていあ"),
        ["Locksmith"] = (1, "かきし"),
        ["DarkHide"] = (3, "たくはいと"),
        ["NiceHacker"] = (4, "ないすはつか"),
        ["Framer"] = (5, "ふれま"),
        ["StepBomber"] = (5, "ほはくま"),
        ["MadKind"] = (6, "まつとかいんと"),
        ["Assassin"] = (0, "あさしん"),
        ["AsistingAngel"] = (0, "あしすていんくえんしえる"),
        ["Astel"] = (0, "あすてる"),
        ["Arsonist"] = (0, "あそにすと"),
        ["Archer"] = (0, "あちや"),
        ["Analyzer"] = (0, "あならいさ"),
        ["Absorb"] = (0, "あふそふ"),
        ["Amateras"] = (0, "あまてらす"),
        ["Amanojaku"] = (0, "あまのしやく"),
        ["Amnesia"] = (0, "あむねしあ"),
        ["SuspiciousTeller"] = (0, "あやしい占い師"),
        ["RaccoonParent"] = (0, "あらいくま"),
        ["RaccoonChild"] = (0, "あらいくまの子"),
        ["Arcana"] = (0, "あるかな"),
        ["AntiTeleporter"] = (0, "あんちてれほた"),
        ["AntiHero"] = (0, "あんちひろ"),
        ["AntiReporter"] = (0, "あんちれほた"),
        ["Android"] = (0, "あんとろいと"),
        ["UnFortuner"] = (0, "あんふおちゆな"),
        ["Eater"] = (0, "いた"),
        ["EarnestWolf"] = (0, "いちすなおおかみ"),
        ["TheFirstLittlePig"] = (0, "いちはんめのこふた"),
        ["Gang"] = (0, "いちみ"),
        ["OneWolf"] = (0, "いつひきおおかみ"),
        ["EvilAddoer"] = (0, "いひるあとな"),
        ["Evilgambler"] = (0, "いひるきやんふら"),
        ["EvilGuesser"] = (0, "いひるけつさ"),
        ["EvilCommander"] = (0, "いひるこまんた"),
        ["EvilSatellite"] = (0, "いひるさてらいと"),
        ["EvilSanta"] = (0, "いひるさんた"),
        ["EvilStandMaster"] = (0, "いひるすたんとますた"),
        ["EvilTeller"] = (0, "いひるてら"),
        ["EvilTracker"] = (0, "いひるとらつか"),
        ["EvilTrapper"] = (0, "いひるとらつは"),
        ["EvilHacker"] = (0, "いひるはつか"),
        ["EvilBlender"] = (0, "いひるふれんた"),
        ["EvilMoving"] = (0, "いひるむひんく"),
        ["EvilMaker"] = (0, "いひるめか"),
        ["EvilLinker"] = (0, "いひるりんか"),
        ["Eraser"] = (0, "いれいさ"),
        ["Elector"] = (0, "いれくた"),
        ["Insider"] = (0, "いんさいた"),
        ["InSender"] = (0, "いんせんた"),
        ["Infected"] = (0, "いんふえくてつと"),
        ["Infect"] = (0, "いんふえくと"),
        ["InfoPoor"] = (0, "いんふおふあ"),
        ["Influencer"] = (0, "いんふるえんさ"),
        ["Inheritance"] = (0, "いんへりたんす"),
        ["Impostor"] = (0, "いんほすた"),
        ["Vulture"] = (0, "うあるちや"),
        ["Vampire"] = (0, "うあんはいあ"),
        ["Witch"] = (0, "ういつち"),
        ["Villain"] = (0, "ういらん"),
        ["Walker"] = (0, "うおか"),
        ["Water"] = (0, "うおた"),
        ["Watching"] = (0, "うおつちんく"),
        ["Warlock"] = (0, "うおろつく"),
        ["Turncoat"] = (0, "うらきりもの"),
        ["FortuneTeller"] = (0, "うらないし"),
        ["UltraStar"] = (0, "うるとらすた"),
        ["Alien"] = (0, "えいりあん"),
        ["Executioner"] = (0, "えくすきゆしよな"),
        ["Express"] = (0, "えくすふれす"),
        ["Egoist"] = (0, "えこいすと"),
        ["Efficient"] = (0, "えふいしえんし"),
        ["Evolver"] = (0, "えほるは"),
        ["Engineer"] = (0, "えんしにあ"),
        ["WolfBoy"] = (0, "おおかみしようねん"),
        ["Autopsy"] = (0, "おとふし"),
        ["Ogre"] = (0, "おに"),
        ["Opener"] = (0, "おふな"),
        ["Opportunist"] = (0, "おほちゆにすと"),
        ["Vega"] = (0, "おりひめ"),
        ["AllArounder"] = (0, "おるらうんた"),
        ["Fool"] = (0, "おろかもの"),
        ["Onmyoji"] = (0, "おんみようし"),
        ["Pirate"] = (1, "かいそく"),
        ["PhantomThief"] = (1, "かいとう"),
        ["CountKiller"] = (1, "かうんときら"),
        ["Scientist"] = (1, "かかくしや"),
        ["Abuser"] = (1, "かきやくしや"),
        ["AmateurTeller"] = (1, "かけたし占い師"),
        ["Curser"] = (1, "かさ"),
        ["CurseMaker"] = (1, "かすめか"),
        ["Guarding"] = (1, "かていんく"),
        ["GuardMaster"] = (1, "かとますた"),
        ["God"] = (1, "かみ"),
        ["Comebacker"] = (1, "かむはつか"),
        ["Camouflager"] = (1, "かもふらしや"),
        ["Hunter"] = (1, "かりうと"),
        ["CharismaStar"] = (1, "かりすますた"),
        ["Observer"] = (1, "かんさつしや"),
        ["Amnesiac"] = (1, "きおくそうしつしや"),
        ["HASFox"] = (1, "きつね"),
        ["Gasp"] = (1, "きやすふ"),
        ["CandleLighter"] = (1, "きやんとるらいた"),
        ["Milkman"] = (1, "きゆうにゆうや"),
        ["Cupid"] = (1, "きゆひつと"),
        ["QuickKiller"] = (1, "くいつくきら"),
        ["Emptiness"] = (1, "くうきよしや"),
        ["Chain"] = (1, "くさり"),
        ["Clumsy"] = (1, "くらむし"),
        ["Crewmate"] = (1, "くるめいと"),
        ["BlackCat"] = (1, "くろねこ"),
        ["King"] = (1, "くんりんしや"),
        ["Police"] = (1, "けいさつかん"),
        ["Cakeshop"] = (1, "けき屋"),
        ["Guesser"] = (1, "けつさ"),
        ["Duelist"] = (1, "けつとうしや"),
        ["GM"] = (1, "けむますた"),
        ["Sage"] = (1, "けんしや"),
        ["IceOni"] = (1, "こおりおに"),
        ["Apprentice"] = (1, "こかた"),
        ["GhostSaboteur"] = (1, "こすとさほたしゆ"),
        ["GhostNoiseSender"] = (1, "こすとのいすせんた"),
        ["GhostFloodlight"] = (1, "こすとふらつとらいた"),
        ["Ghostbuttoner"] = (1, "こすとほたな"),
        ["GhostReseter"] = (1, "こすとりせつた"),
        ["GhostRumour"] = (1, "こすとるもあ"),
        ["Connector"] = (1, "こねくた"),
        ["Connecting"] = (1, "こねくていんく"),
        ["ConnectSaver"] = (1, "こねくとせは"),
        ["Corexis"] = (1, "これくしす"),
        ["Psychic"] = (2, "さいきつく"),
        ["SKMadmate"] = (2, "さいときつくまつとめいと"),
        ["SatsumatoImo"] = (2, "さつまといも"),
        ["SatsumatoImoC"] = (2, "さつまといも(C)"),
        ["SatsumatoImoM"] = (2, "さつまといも(M)"),
        ["Satellite"] = (2, "さてらいと"),
        ["Survivor"] = (2, "さはいは"),
        ["SabotageMaster"] = (2, "さほたしゆますた"),
        ["Samurai"] = (2, "さむらい"),
        ["Surrender"] = (2, "されんた"),
        ["Surrogate"] = (2, "さろけと"),
        ["Sunglasses"] = (2, "さんくらす"),
        ["Santa"] = (2, "さんた"),
        ["SantaClaus"] = (2, "さんたくろす"),
        ["TheThirdLittlePig"] = (2, "さんはんめのこふた"),
        ["Seer"] = (2, "しあ"),
        ["Seeing"] = (2, "しいんく"),
        ["ShapeKiller"] = (2, "しえいふきら"),
        ["Shapeshifter"] = (2, "しえいふしふた"),
        ["ShapeMaster"] = (2, "しえいふますた"),
        ["Jailer"] = (2, "しえいら"),
        ["Jester"] = (2, "しえすた"),
        ["Chef"] = (2, "しえふ"),
        ["Sheriff"] = (2, "しえりふ"),
        ["Shikigami"] = (2, "しきかみ"),
        ["Suicider"] = (2, "しさつかんほうしや"),
        ["Jizo"] = (2, "しそう"),
        ["GrimReaper"] = (2, "しにかみ"),
        ["SelfBomber"] = (2, "しはくま"),
        ["Shyboy"] = (2, "しやいほい"),
        ["Jackal"] = (2, "しやつかる"),
        ["JackalWolf"] = (2, "しやつかるうるふ"),
        ["JackalAlien"] = (2, "しやつかるえいりあん"),
        ["Jackaldoll"] = (2, "しやつかるとる"),
        ["JackalMafia"] = (2, "しやつかるまふいあ"),
        ["Judge"] = (2, "しやつし"),
        ["Jumper"] = (2, "しやんは"),
        ["Jumbo"] = (2, "しやんほ"),
        ["Guardian"] = (2, "しゆこしや"),
        ["GuardianAngel"] = (2, "しゆこてんし"),
        ["SchrodingerCat"] = (2, "しゆれていんかの猫"),
        ["Sealer"] = (2, "しら"),
        ["Serial"] = (2, "しりある"),
        ["SerialKiller"] = (2, "しりあるきら"),
        ["SilverBuzzer"] = (2, "しるはふさ"),
        ["Trauma"] = (3, "とらうま"),
        ["MadInvisible"] = (6, "まついんひしふる"),
        ["MadMagic"] = (6, "まつとましつく"),
        ["Flatfish"] = (1, "かれい"),
        ["Lupin"] = (8, "るはん"),
        ["Daoshi"] = (3, "たおしい"),
        ["Catalyst"] = (1, "かたりすと"),
        ["Conductor"] = (1, "こんたくたあ"),
        ["Sender"] = (2, "せんたあ"),
        ["MadSender"] = (6, "まつとせんたあ"),
        ["Parity"] = (5, "はりてい"),
        ["BetrayGuesser"] = (5, "へといけつさあ"),
        ["ArchVillain"] = (0, "ういらん"),
        ["LonelyWolf"] = (1, "ことくなおおかみ"),
        ["Clockworker"] = (1, "くろつくわあかあ"),
        ["Abductor"] = (0, "あふたくたあ"),
        ["Surmount"] = (2, "さあまうんと"),
        ["Messenger"] = (6, "めつせんしやあ"),
        ["Fugitive"] = (5, "ひゆうしていふ"),
        ["MemoryRestorer"] = (1, "きおくふくけんしや"),
        ["Surviving"] = (2, "さはいひんく"),
        ["Berserker"] = (5, "はあさあかあ"),
        ["ToughGuy"] = (3, "たふかい"),
        ["Comorbidity"] = (1, "きようそんしよう"),
        ["Murder"] = (6, "まあたあ"),

        ["Symbol"] = (2, "しんほる"),
        ["SwitchSheriff"] = (2, "すいつちしえりふ"),
        ["Swooper"] = (2, "すうは"),
        ["Scratcher"] = (2, "すくらつちや"),
        ["Stack"] = (2, "すたつく"),
        ["Staff"] = (2, "すたつふ"),
        ["Stamina"] = (2, "すたみな"),
        ["Stand"] = (2, "すたんと"),
        ["StandMaster"] = (2, "すたんとますた"),
        ["Stealth"] = (2, "すてるす"),
        ["Stolener"] = (2, "すとるな"),
        ["Sniper"] = (2, "すないは"),
        ["Snitch"] = (2, "すにつち"),
        ["Spider"] = (2, "すはいた"),
        ["Speeding"] = (2, "すひていんく"),
        ["SpeedStar"] = (2, "すひとすた"),
        ["SpeedBooster"] = (2, "すひとふすた"),
        ["Spelunker"] = (2, "すへらんか"),
        ["SmokeMaker"] = (2, "すもくめか"),
        ["Slacker"] = (2, "すらつか"),
        ["Sleeper"] = (2, "すりは"),
        ["SlowStarter"] = (2, "すろすたた"),
        ["Securer"] = (2, "せきゆあら"),
        ["Inspector"] = (2, "そうさかん"),
        ["VillageChief"] = (2, "そんちよう"),
        ["Zombie"] = (2, "そんひ"),
        ["President"] = (3, "たいとうりよう"),
        ["Typewriter"] = (3, "たいふらいた"),
        ["Tiebreaker"] = (3, "たいふれか"),
        ["TimeThief"] = (3, "たいむしふ"),
        ["TimeManager"] = (3, "たいむまねしや"),
        ["TimeSleeper"] = (3, "たいむりは"),
        ["Tairou"] = (3, "たいろう"),
        ["DarkSheriff"] = (3, "たくしえりふ"),
        ["TaskStar"] = (3, "たすくすた"),
        ["DoubleKiller"] = (3, "たふるきら"),
        ["Tama"] = (3, "たま"),
        ["DummyHunterPlayer"] = (3, "たみはんた"),
        ["Dancer"] = (3, "たんさ"),
        ["Detective"] = (3, "たんてい"),
        ["Chaser"] = (3, "ちえいさ"),
        ["Chatter"] = (3, "ちやつた"),
        ["Pursuer"] = (3, "ついせきしや"),
        ["Dictator"] = (3, "ていくてた"),
        ["Decrescendo"] = (3, "てくれつしえんと"),
        ["DemonicCrusher"] = (3, "てもんくらつしや"),
        ["DemonicSupporter"] = (3, "てもんさほた"),
        ["DemonicTracker"] = (3, "てもんとらつか"),
        ["DemonicVenter"] = (3, "てもんへんた"),
        ["Teleporter"] = (3, "てれほた"),
        ["TeleportKiller"] = (3, "てれほときら"),
        ["Terrorist"] = (3, "てろりすと"),
        ["Balancer"] = (3, "てんひん"),
        ["ToiletFan"] = (3, "といれふあん"),
        ["Tomorrow"] = (3, "とうもろ"),
        ["PoisonedBakery"] = (3, "とくいりはんや"),
        ["Autocrat"] = (3, "とくさいしや"),
        ["Doctor"] = (3, "とくた"),
        ["DoppelGanger"] = (3, "とつへるけんか"),
        ["Driver"] = (3, "とらいは"),
        ["Tracker"] = (3, "とらつか"),
        ["Trapper"] = (3, "とらつは"),
        ["Transparent"] = (3, "とらんすはれんと"),
        ["HASTroll"] = (3, "とろる"),
        ["NiceAddoer"] = (4, "ないすあとな"),
        ["NiceEraser"] = (4, "ないすいれいさ"),
        ["NiceGuesser"] = (4, "ないすけつさ"),
        ["NiceTeleporter"] = (4, "ないすてれほた"),
        ["NiceTrapper"] = (4, "ないすとらつは"),
        ["NiceLinker"] = (4, "ないすりんか"),
        ["NiceLogger"] = (4, "ないすろか"),
        ["NiceWorkaholic"] = (4, "ないすわかほりつく"),
        ["NiceNekomata"] = (4, "ないす猫又"),
        ["NiceRedRidingHood"] = (4, "ないす赤すきん"),
        ["TheSecondLittlePig"] = (4, "にはんめのこふた"),
        ["Nimrod"] = (4, "にむろつと"),
        ["News"] = (4, "にゆす"),
        ["Nue"] = (4, "ぬえ"),
        ["Null"] = (4, "ぬる"),
        ["NekoKabocha"] = (4, "ねこかほちや"),
        ["Noisemaker"] = (4, "のいすめか"),
        ["Nautilus"] = (4, "のちらす"),
        ["Notvoter"] = (4, "のつとうおうた"),
        ["Notifier"] = (4, "のていふあ"),
        ["NonReport"] = (4, "のんれほと"),
        ["AlienHijack"] = (5, "はいしやつくえいりあん"),
        ["Viper"] = (5, "はいは"),
        ["BountyGuesser"] = (5, "はうんていけつさ"),
        ["BountyHunter"] = (5, "はうんていはんた"),
        ["Bomber"] = (5, "はくたんま"),
        ["BakeCat"] = (5, "はけねこ"),
        ["LoversBreaker"] = (5, "はせし"),
        ["BatGirl"] = (5, "はつとかる"),
        ["HappyJester"] = (5, "はつひしえすた"),
        ["HadouHo"] = (5, "はとうほう"),
        ["SheriffHadouHo"] = (5, "はとうほうしえりふ"),
        ["JackalHadouHo"] = (5, "はとうほうしやつかる"),
        ["PuppeteerHadouHo"] = (5, "はとうほうはへつていあ"),
        ["HathorSekhmet"] = (5, "はとほるとせくめと"),
        ["FireWorks"] = (5, "はなひしよくにん"),
        ["PavlovDogImprint"] = (5, "はふろふ"),
        ["PavlovOwner"] = (5, "はふろふのおな"),
        ["PavlovDog"] = (5, "はふろふの犬"),
        ["Puppeteer"] = (5, "はへつていあ"),
        ["Ballooner"] = (5, "はるな"),
        ["Powerful"] = (5, "はわふる"),
        ["Banker"] = (5, "はんか"),
        ["Bakery"] = (5, "はん屋"),
        ["BeginnerImpostor"] = (5, "ひきないんほすた"),
        ["Victim"] = (5, "ひきやくしや"),
        ["Altair"] = (5, "ひこほし"),
        ["Hitchhiker"] = (5, "ひつちはいか"),
        ["Pharaoh"] = (5, "ふあらお"),
        ["Phantom"] = (5, "ふあんとむ"),
        ["FakeJester"] = (5, "ふえいくしえすた"),
        ["Fortuner"] = (5, "ふおちゆな"),
        ["Pukupuku"] = (5, "ふくふく"),
        ["Twins"] = (5, "ふたこ"),
        ["PlusVote"] = (5, "ふらすほと"),
        ["BlackSanta"] = (5, "ふらつくさんた"),
        ["BlackVisioner"] = (5, "ふらつくひしよな"),
        ["BlackMadMate"] = (5, "ふらつくまつとめいと"),
        ["Freeter"] = (5, "ふりた"),
        ["Braid"] = (5, "ふれいと"),
        ["TaskPlayerB"] = (5, "ふれいや"),
        ["ProgressKiller"] = (5, "ふろくれすきら"),
        ["Professional"] = (5, "ふろふえつしよなる"),
        ["ProBowler"] = (5, "ふろほら"),
        ["Bait"] = (5, "へいと"),
        ["HateKiller"] = (5, "へいときら"),
        ["PlagueDoctor"] = (5, "へすと医師"),
        ["Penguin"] = (5, "へんきん"),
        ["Lawyer"] = (5, "へんこし"),
        ["VentOpener"] = (5, "へんとおふな"),
        ["VentHunter"] = (5, "へんとはんた"),
        ["VentMaster"] = (5, "へんとますた"),
        ["MMArcher"] = (5, "ほあんかん"),
        ["Whiteshark"] = (5, "ほおしろさめ"),
        ["PokerFace"] = (5, "ほかふえいす"),
        ["BorderKiller"] = (5, "ほたきら"),
        ["WhiteHacker"] = (5, "ほわいとはつか"),
        ["PonkotuTeller"] = (5, "ほんこつ占い師"),
        ["Maestro"] = (6, "まえすとろ"),
        ["Tuna"] = (6, "まくろ"),
        ["Magician"] = (6, "まししやん"),
        ["MagicHand"] = (6, "ましつくはんと"),
        ["Conjurer"] = (6, "ましゆつし"),
        ["MassMurder"] = (6, "ますまた"),
        ["MassMedia"] = (6, "ますめていあ"),
        ["MadAvenger"] = (6, "まつとあへんしや"),
        ["MadWare"] = (6, "まつとうえあ"),
        ["MadKaiser"] = (6, "まつとかいさ"),
        ["MadCount"] = (6, "まつとかうんと"),
        ["MadGuardian"] = (6, "まつとかていあん"),
        ["MadPsycho"] = (6, "まつとさいこ"),
        ["MadJester"] = (6, "まつとしえすた"),
        ["MadSheriff"] = (6, "まつとしえりふ"),
        ["MadScream"] = (6, "まつとすくりむ"),
        ["MadSuicide"] = (6, "まつとすさいと"),
        ["MadSnitch"] = (6, "まつとすにつち"),
        ["MadChanger"] = (6, "まつとちえんしや"),
        ["MadTeller"] = (6, "まつとてら"),
        ["MadTracker"] = (6, "まつととらつか"),
        ["MadHacker"] = (6, "まつとはつか"),
        ["MadParasite"] = (6, "まつとはらさいと"),
        ["MadProxy"] = (6, "まつとふろきし"),
        ["MadBait"] = (6, "まつとへいと"),
        ["MadBetrayer"] = (6, "まつとへとれいや"),
        ["Madmate"] = (6, "まつとめいと"),
        ["MadReduced"] = (6, "まつとりてゆす"),
        ["MadWorker"] = (6, "まつとわか"),
        ["Madonna"] = (6, "まとんな"),
        ["MadonnaLovers"] = (6, "まとんならはす"),
        ["Management"] = (6, "まねしめんと"),
        ["Mafia"] = (6, "まふいあ"),
        ["MagicalGirl"] = (6, "まほうしようしよ"),
        ["Mermaid"] = (6, "まめいと"),
        ["Mario"] = (6, "まりお"),
        ["Merlin"] = (6, "まりん"),
        ["ShrineMaiden"] = (6, "みこ"),
        ["Triplets"] = (6, "みつこ"),
        ["Missioneer"] = (6, "みつしよな"),
        ["MeetingSheriff"] = (6, "みていんくしえりふ"),
        ["Minimalist"] = (6, "みにまりすと"),
        ["Muter"] = (6, "みゆた"),
        ["Moving"] = (6, "むひんく"),
        ["Moon"] = (6, "むん"),
        ["Mare"] = (6, "めあ"),
        ["Mayor"] = (6, "めいや"),
        ["Fanatic"] = (6, "もうしんしや"),
        ["Mole"] = (6, "もくら"),
        ["Monika"] = (6, "もにか(未完成)"),
        ["Monochromer"] = (6, "ものくら"),
        ["Snowman"] = (7, "ゆきたるま"),
        ["Fox"] = (7, "ようこ"),
        ["Lighter"] = (8, "らいた"),
        ["Lighting"] = (8, "らいていんく"),
        ["LastImpostor"] = (8, "らすといんほすた"),
        ["LastCrewmate"] = (8, "らすとくるめいと"),
        ["LastNeutral"] = (8, "らすとにゆとらる"),
        ["Lovers"] = (8, "らはす"),
        ["Rabbit"] = (8, "らひつと"),
        ["Resulter"] = (8, "りさるた"),
        ["Reverser"] = (8, "りはさ"),
        ["Reversal"] = (8, "りはさる"),
        ["Repeater"] = (8, "りひた"),
        ["Revenger"] = (8, "りへんしや"),
        ["Limiter"] = (8, "りみつた"),
        ["Remotekiller"] = (8, "りもときら"),
        ["Reloader"] = (8, "りろた"),
        ["Ruler"] = (8, "るら"),
        ["Medium"] = (8, "れいはいし"),
        ["LegendStar"] = (8, "れしえんとすた"),
        ["Reporting"] = (8, "れほていんく"),
        ["Rocket"] = (8, "ろけつと"),
        ["Workaholic"] = (9, "わかほりつく"),
        ["Workhorse"] = (9, "わくほす"),
        ["Strawdoll"] = (9, "わらにんきよう"),
        ["Walkure"] = (9, "わるきゆれ")
    };

    public static int GetRow(CustomRoles role)
        => IsJoke(role) ? JokeRow : Table.TryGetValue(role.ToString(), out var v) ? v.Row : OtherRow;

    private static string GetKey(CustomRoles role)
        => Table.TryGetValue(role.ToString(), out var v) ? v.Key : role.ToString();

    public static List<SimpleRoleInfo> Order(IEnumerable<SimpleRoleInfo> roles)
    {
        var list = roles
            .OrderBy(info => GetRow(info.RoleName))
            .ThenBy(info => GetKey(info.RoleName), StringComparer.Ordinal)
            .ThenBy(info => info.OptionSort.TabNumber)
            .ThenBy(info => info.OptionSort.SortNumber)
            .ToList();
        // 2人1組の役職は、設定を持つ方(親)を先に作らないと、相方の設定の親が見つからずに設定の読み込みが止まる
        // (「ジュリエット」が「ロミオ」より先に来て、ロビーを建てられなくなっていた)。相方は親のすぐ後ろに移す
        var followers = list.Where(i => i.Combination != CombinationRoles.None && i.OptionCreator == null
            && list.Any(l => l != i && l.Combination == i.Combination && l.OptionCreator != null)).ToList();
        foreach (var f in followers)
        {
            list.Remove(f);
            var leader = list.FindIndex(l => l.Combination == f.Combination && l.OptionCreator != null);
            list.Insert(leader + 1, f);
        }
        Logger.Info($"役職を五十音順に並べました({list.Count}件)", "RoleGojuon");
        return list;
    }

    public static int CreateRowHeader(TabGroup tab, CustomRoleTypes roleType, int row)
    {
        // 役職タイプごとにIDを分ける(同じタブを複数のタイプが使っても重ならない)
        var id = 1_900_000 + (int)roleType * 100 + row;
        HeaderRoles[id] = new();
        // 最初は全部閉じた状態にする(見出しを押すと開く)。クライアント設定で「開いておく」ならそのまま
        if (Main.ExpandRoleHeaders?.Value != true) Collapsed.Add(id);
        var title = RowNames[row];
        ObjectOptionitem.Create(id, $"GojuonRow_{roleType}_{row}", true, null, tab)
            .SetOptionName(() => title)
            .SetColorcode(RowColors[row]);
        return id;
    }

    public static int CreateGroupHeader(TabGroup tab, int groupKey, int row)
    {
        var id = 1_950_000 + groupKey * 100 + row;
        HeaderRoles[id] = new();
        if (Main.ExpandRoleHeaders?.Value != true) Collapsed.Add(id);
        var title = RowNames[row];
        ObjectOptionitem.Create(id, $"GojuonGroup_{groupKey}_{row}", true, null, tab)
            .SetOptionName(() => title)
            .SetColorcode(RowColors[row])
            .SetTag(CustomOptionTags.Role);
        return id;
    }

    public static void SetupGrouped(TabGroup tab, int groupKey, IEnumerable<(CustomRoles Role, System.Action Setup)> items)
    {
        var list = items.ToList();
        IEnumerable<(CustomRoles Role, System.Action Setup)> Sort(IEnumerable<(CustomRoles Role, System.Action Setup)> src)
            => src.OrderBy(x => GetRow(x.Role)).ThenBy(x => GetKey(x.Role), StringComparer.Ordinal);
        var news = Sort(list.Where(x => IsNewRow(x.Role))).ToList();
        if (news.Count > 0)
        {
            var header = CreateGroupHeader(tab, groupKey, NewRow);
            foreach (var x in news) { AddRoleToHeader(header, x.Role); x.Setup(); }
        }
        int lastRow = -1, rowHeader = -1;
        foreach (var x in Sort(list.Where(x => !IsNewRow(x.Role))))
        {
            var row = GetRow(x.Role);
            if (row != lastRow) { lastRow = row; rowHeader = CreateGroupHeader(tab, groupKey, row); }
            AddRoleToHeader(rowHeader, x.Role);
            x.Setup();
        }
    }

    public static void AddRoleToHeader(int headerId, CustomRoles role)
    {
        RoleHeader[role] = headerId;
        if (HeaderRoles.TryGetValue(headerId, out var list)) list.Add(role);
    }
}
