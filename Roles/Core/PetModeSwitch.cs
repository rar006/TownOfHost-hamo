using AmongUs.GameOptions;
using UnityEngine;

namespace TownOfHost.Roles.Core;

/// <summary>
/// シェリフと同じ「ペットを撫でるとキルモード ⇔ タスクモードを切り替える」仕組み(ダークシェリフ・記憶喪失者で使う)。
/// ・タスクモード: 本人の画面では自分がクルーメイトになり、キルボタンが出ない
/// ・キルモード: 本人の画面では自分がインポスターになり、キルボタンが出る
/// シェリフの設定「タスクモードで開始」がONの時だけ使う(シェリフのふりをする役職なので、本物のシェリフと同じ動きにする)。
/// 会議が始まるとタスクモードに戻る(シェリフと同じ)。
/// </summary>
public sealed class PetModeSwitch
{
    private readonly PlayerControl player;
    /// <summary>今タスクモードか</summary>
    public bool TaskMode { get; private set; }

    public PetModeSwitch(PlayerControl player) => this.player = player;

    /// <summary>シェリフの「タスクモードで開始」がONか(この時だけ切り替えを使う)</summary>
    public static bool Enabled => Roles.Crewmate.Sheriff.StartInTaskMode?.GetBool() == true;

    /// <summary>試合開始時: 設定ONならタスクモードで始める</summary>
    public void Start()
    {
        if (!AmongUsClient.Instance.AmHost || !Enabled) { TaskMode = false; return; }
        Set(true);
    }

    /// <summary>ペットを撫でた時: 切り替える。切り替えたら true</summary>
    public bool Toggle()
    {
        if (!AmongUsClient.Instance.AmHost || !Enabled || player == null || !player.IsAlive()) return false;
        Set(!TaskMode);
        return true;
    }

    /// <summary>モードを決める(本人の画面での自分の役職を変える)</summary>
    public void Set(bool taskMode)
    {
        TaskMode = taskMode;
        if (!AmongUsClient.Instance.AmHost || player == null || !player.IsAlive()) return;
        var role = taskMode ? RoleTypes.Crewmate : RoleTypes.Impostor;
        if (player == PlayerControl.LocalPlayer) RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, role);
        else
        {
            var clientId = player.GetClientId();
            if (clientId != -1) player.RpcSetRoleDesync(role, clientId);
        }
        if (!taskMode) player.SetKillCooldown(delay: true);
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
    }

    /// <summary>名前の横に出す今のモード(シェリフと同じ見た目)</summary>
    public string ModeText()
        => !Enabled ? "" : Utils.ColorString(Color.yellow, TaskMode ? " [タスク]" : " [シェリフ]");
}
