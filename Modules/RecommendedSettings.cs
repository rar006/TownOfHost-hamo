using AmongUs.GameOptions;

namespace TownOfHost.Modules;

public static class RecommendedSettings
{
    public static int PlayerCount => PlayerControl.AllPlayerControls?.Count ?? 0;

    public static (int impostors, float killCooldown) For(int players) => players switch
    {
        <= 6 => (1, 25f),
        <= 9 => (2, 25f),
        <= 12 => (2, 22.5f),
        _ => (3, 20f),
    };

    public static void Apply()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) return;
        OptionBackup.BeforeChange(); // まとめて変える前の状態を残す(自動バックアップがONの時)
        var players = PlayerCount;
        var (imp, kcd) = For(players);
        Main.NormalOptions.SetInt(Int32OptionNames.NumImpostors, imp);
        Main.NormalOptions.SetFloat(FloatOptionNames.KillCooldown, kcd);
        GameManager.Instance?.LogicOptions?.SyncOptions();
        Utils.SendMessage($"{players}人向けのおすすめ設定にしました。\nインポスター: {imp}人 / キルクール: {kcd}秒", PlayerControl.LocalPlayer.PlayerId, "<#7fd8ff>【おすすめ設定】</color>");
        Logger.Info($"おすすめ設定: {players}人 → インポスター{imp}人・キルクール{kcd}秒", "RecommendedSettings");
    }
}
