namespace TownOfHost.Roles.Crewmate;

// 守護者・ガードマスターの、ゲッサーへの守り(設定で 撃たれない / 撃った人に跳ね返す)
public static class GuessShield
{
    public enum Mode { None, Block, Reflect }
    public static readonly string[] Selections = { "GuessShieldNone", "GuessShieldBlock", "GuessShieldReflect" };

    public static Mode Of(PlayerControl target)
    {
        var mode = Guardian.GuessShieldFor(target);
        var master = GuardMaster.GuessShieldFor(target);
        // 両方に当てはまる時は、強い方(跳ね返す)
        return master > mode ? master : mode;
    }
}
