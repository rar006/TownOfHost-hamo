namespace TownOfHost.Modules;



/// <summary>

/// 旧DiscordRPCクライアントとの互換用スタブ。

/// Discordのプレイ中表示はPatches/DiscordActivityPatch.csがゲーム標準の

/// ActivityManagerへ直接適用するため、外部クライアントやバックグラウンド接続は不要。

/// 既存の呼び出し元を壊さないため、メソッドは安全なno-opとして残す。

/// </summary>

public static class DiscordRichPresenceService

{

    public static void Initialize() { }

    public static void UpdatePresence(string details, string state) { }

    public static void Invoke() { }

    public static void Shutdown() { }

}

