using static TownOfHost.Utils;

namespace TownOfHost
{
    public static class UtilsName
    {
        /// <summary></summary>
        /// <param name="player">色表示にするプレイヤー</param>
        /// <param name="bold">trueの場合ボールドで返します。</param>
        public static string GetPlayerColor(this PlayerControl player, bool bold = false)
        {
            if (player == null) return "";
            var name = Main.AllPlayerNames.TryGetValue(player.PlayerId, out var N) ? N : player.Data.PlayerName;
            /*if (bold) return "<b>" + ColorString(Main.PlayerColors[player.PlayerId], $"{name}</b>");
            else*/
            return ColorString(Main.PlayerColors[player.PlayerId], $"{name}");
        }
        /// <summary></summary>
        /// <param name="player">色表示にするプレイヤー</param>
        /// <param name="bold">trueの場合ボールドで返します。</param>
        public static string GetPlayerColor(this byte player, bool bold = false)
        {
            var pc = PlayerCatch.GetPlayerById(player);
            if (pc == null) return "";
            var name = Main.AllPlayerNames.TryGetValue(player, out var N) ? N : pc.Data.PlayerName;
            /*if (bold) return "<b>" + ColorString(Main.PlayerColors[player], $"{name}</b>");
            else*/
            return ColorString(Main.PlayerColors[player], $"{name}");
        }
        /// <summary></summary>
        /// <param name="player">色表示にするプレイヤー</param>
        /// <param name="bold">trueの場合ボールドで返します。</param>
        public static string GetPlayerColor(this NetworkedPlayerInfo player, bool bold = false)
        {
            if (player == null) return "";
            var name = player.PlayerName;
            /*if (bold) return "<b>" + ColorString(Main.PlayerColors[player.PlayerId], $"{name}</b>");
            else*/
            return ColorString(Main.PlayerColors[player.PlayerId], $"{name}");
        }

        public static bool SetNameCheck(this PlayerControl player, string name, PlayerControl seer = null, bool force = false)
        {
            if (seer == null) seer = player;

            if (Main.LastNotifyNames is null)
                Main.LastNotifyNames = new();

            if (!Main.LastNotifyNames.ContainsKey((player.PlayerId, seer.PlayerId)))
                Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = "nulldao"; //nullチェック

            if (!force && Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] == name)
            {
                return false;
            }
            {
                Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = name;
                if (!GameStates.IsLobby) HudManagerPatch.LastSetNameDesyncCount++;
            }

            return true;
        }
        public static string GetNameWithRole(this PlayerControl player)
        {
            return $"{player?.Data?.GetLogPlayerName()}" + (GameStates.IsInGame ? $"({player?.GetAllRoleName()})" : "");
        }
        public static string GetRealName(this PlayerControl player, bool isMeeting = false)
        {
            if (Main.ShapeshiftTarget.TryGetValue(player.PlayerId, out var targetid) && targetid != player.PlayerId && !isMeeting)
            {
                if (Camouflage.PlayerSkins.TryGetValue(targetid, out var outfit))
                {
                    return outfit.PlayerName;
                }
            }
            if (GameStates.InGame && Camouflage.PlayerSkins.TryGetValue(player?.PlayerId ?? byte.MaxValue, out var skin)) return skin.PlayerName;
            return isMeeting ? player?.Data?.PlayerName : player?.name;
        }
    }
}