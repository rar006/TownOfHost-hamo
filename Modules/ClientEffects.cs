using System;
using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using UnityEngine;

namespace TownOfHost.Modules;

// MOD を入れている人(ホストも)の画面だけに出す専用の演出。
// MOD の無い人には今まで通りダミーやサボタージュで見せ、MOD のある人はダミーを隠して、自分の画面で直接描く・専用の音を鳴らす。
// 通信は MOD を入れている人にだけ送る(MOD の無い人には何も届かないので、通信が増えない)
public static class ClientEffects
{
    public enum Kind : byte { HideDummy, NovaGuide, NovaBeam, NovaClear, Sound, SluggerHit, MeetingCutIn, Death, Shake, Charge, ChargeStop, MeetingKill, SeerSoul, Jumbo, Teleport, GuessUi, Bind, BloodStain }
    public enum SoundKind : byte { NovaCharge, NovaAlarm, NovaFire, SluggerHit, Heartbeat, MeetingSting, Boom, Gunshot, Flame, Sparkle, Chime, Swing, Push, Rocket, Footstep, Soul, ChargeLoop, SniperEquip, SniperShot, Clank }
    public enum DeathFx : byte { None, Explosion, Sniped, Flame, Evaporate, Magic, Blood, Fall, Launch, Jaws, Poison, Bubble, Vortex, Squash, Slash, Backfire, Heartbreak, Warp }
    static bool On => Options.ClientEffects?.GetBool() == true;
    public static bool NovaOn => On && Options.ClientEffectsNova.GetBool();
    public static bool SluggerOn => On && Options.ClientEffectsSlugger.GetBool();
    public static bool SoundOn => On && Options.ClientEffectsSound.GetBool();
    public static bool MeetingOn => On && Options.ClientEffectsMeeting.GetBool();
    public static bool DeathOn => On && Options.ClientEffectsDeath.GetBool();
    public static bool ShakeOn => On && Options.ClientEffectsShake.GetBool();
    public static bool TeleportOn => On && Options.ClientEffectsTeleport.GetBool();
    public static bool GuessUiOn => On && Options.ClientEffectsGuessUi.GetBool();
    static float Volume => Mathf.Clamp01((Options.ClientEffectsVolume?.GetInt() ?? 80) / 100f);

    // 全員が MOD を入れている時は、MOD の無い人用のダミーを出さなくてよい(通信がかなり減る)
    public static bool SkipNovaDummies => NovaOn && Options.ClientEffectsSkipDummy.GetBool() && EveryoneHasMod();

    static readonly HashSet<uint> hiddenDummies = new();
    static readonly Dictionary<uint, List<MeshRenderer>> dummyRenderers = new();
    static readonly List<Func<bool>> running = new();
    static readonly List<Func<bool>> runningHud = new();
    static readonly Dictionary<byte, Guide> guides = new();
    static readonly Dictionary<byte, List<SpriteRenderer>> beams = new();
    static readonly Dictionary<byte, float> lastDeathFx = new();
    static readonly Dictionary<byte, Charging> charges = new();
    static readonly List<byte> jumbos = new();
    static readonly Dictionary<byte, (Vector2 pos, float walked)> jumboSteps = new();
    static float jumboVolume;
    static bool jumboSent;

    static float nextHideAt;

    // ===== 送る =====

    static bool CanSend => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && PlayerControl.LocalPlayer != null;

    static bool IsModPlayer(PlayerControl pc)
        => pc != null && !pc.AmOwner && (pc.IsModClient() || NonHostRpcGuard.IsHidden(pc.PlayerId));

    // その人の画面では、ビームを名前の文字ではなく専用の演出で描くか
    public static bool DrawsBeamFor(PlayerControl seer)
        => NovaOn && seer != null && (seer.AmOwner || IsModPlayer(seer));

    public static bool EveryoneHasMod()
    {
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;
            if (!IsModPlayer(pc)) return false;
        }
        return true;
    }

    static readonly List<int> targets = new();
    static List<int> Targets()
    {
        targets.Clear();
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || pc.Data.Disconnected || !IsModPlayer(pc)) continue;
            var id = pc.GetClientId();
            if (id >= 0 && !targets.Contains(id)) targets.Add(id);
        }
        return targets;
    }

    static void Send(Kind kind, Action<MessageWriter> write, SendOption option = SendOption.Reliable, PlayerControl only = null)
    {
        if (only != null)
        {
            if (!IsModPlayer(only)) return;
            targets.Clear();
            targets.Add(only.GetClientId());
        }
        else Targets();
        foreach (var clientId in targets)
        {
            var w = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ClientEffect, option, clientId);
            w.Write((byte)kind);
            write(w);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }
    }

    public static void HideDummy(uint netId)
    {
        if (!CanSend) return;
        Send(Kind.HideDummy, w => w.WritePacked(netId));
        hiddenDummies.Add(netId);
        nextHideAt = 0f;
    }

    public static void NovaGuide(PlayerControl owner, Vector2 body, float angle, bool locked)
    {
        if (!CanSend || !NovaOn || owner == null) return;
        var color = owner.Data.DefaultOutfit.ColorId;
        Send(Kind.NovaGuide, w =>
        {
            w.Write(owner.PlayerId);
            NetHelpers.WriteVector2(body, w);
            w.Write(angle);
            w.Write(locked);
            w.Write((byte)color);
        }, SendOption.None);
        ShowGuide(owner.PlayerId, body, angle, locked, color);
    }

    public static void NovaBeam(PlayerControl owner, Vector2 from, float angle, float length, float width, float time)
    {
        if (!CanSend || !NovaOn || owner == null) return;
        Send(Kind.NovaBeam, w =>
        {
            w.Write(owner.PlayerId);
            NetHelpers.WriteVector2(from, w);
            w.Write(angle);
            w.Write(length);
            w.Write(width);
            w.Write(time);
        });
        ShowBeam(owner.PlayerId, from, angle, length, width, time);
        Shake(0.6f, 0.5f);
    }

    public static void NovaClear(PlayerControl owner)
    {
        if (!CanSend || !NovaOn || owner == null) return;
        Send(Kind.NovaClear, w => w.Write(owner.PlayerId));
        Clear(owner.PlayerId);
    }

    public static void Sound(SoundKind sound, Vector2? at = null)
    {
        if (!CanSend || !SoundOn) return;
        var volume = (byte)Mathf.RoundToInt(Volume * 100f);
        Send(Kind.Sound, w =>
        {
            w.Write((byte)sound);
            w.Write(at.HasValue);
            if (at.HasValue) NetHelpers.WriteVector2(at.Value, w);
            w.Write(volume);
        });
        Play(sound, at, volume / 100f);
    }

    public static void SluggerHit(Vector2 pos, Vector2 dir, float dist)
    {
        if (!CanSend) return;
        if (SluggerOn)
        {
            Send(Kind.SluggerHit, w =>
            {
                NetHelpers.WriteVector2(pos, w);
                NetHelpers.WriteVector2(dir, w);
                w.Write(dist);
            });
            ShowSluggerHit(pos, dir, dist);
        }
        Sound(SoundKind.SluggerHit, pos);
    }

    // highlight: 会議のあいだ札を光らせる人(天秤会議で天秤にかけられた 2 人など)
    public static void MeetingCutIn(string title, string colorCode, byte style = 0, byte[] highlight = null)
    {
        if (!CanSend || !MeetingOn || string.IsNullOrEmpty(title)) return;
        var volume = (byte)Mathf.RoundToInt((SoundOn ? Volume : 0f) * 100f);
        highlight ??= Array.Empty<byte>();
        Send(Kind.MeetingCutIn, w =>
        {
            w.Write(title);
            w.Write(colorCode ?? "#ffffff");
            w.Write(volume);
            w.Write(style);
            w.Write((byte)highlight.Length);
            foreach (var id in highlight) w.Write(id);
        });
        QueueCutIn(title, colorCode, volume / 100f, style, highlight);
    }

    static void WriteColor(MessageWriter w, Color c)
    {
        w.Write((byte)(c.r * 255f));
        w.Write((byte)(c.g * 255f));
        w.Write((byte)(c.b * 255f));
    }

    static Color ReadColor(MessageReader r) => new(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);

    // ため: 体のまわりに光が集まり、輪が縮んでいく(波動砲・スラッガー)
    public static void Charge(PlayerControl owner, float duration, Color color)
    {
        if (!CanSend || !On || owner == null) return;
        var volume = (byte)Mathf.RoundToInt((SoundOn ? Volume : 0f) * 100f);
        Send(Kind.Charge, w =>
        {
            w.Write(owner.PlayerId);
            w.Write(duration);
            WriteColor(w, color);
            w.Write(volume);
        });
        ShowCharge(owner.PlayerId, duration, color, volume / 100f);
    }

    public static void ChargeStop(PlayerControl owner)
    {
        if (!CanSend || !On || owner == null) return;
        Send(Kind.ChargeStop, w => w.Write(owner.PlayerId));
        StopCharge(owner.PlayerId);
    }

    // 会議中のキル(ゲッサー・ミーティングシェリフなど): 投票の札に赤い斬り跡が走り、キルの音が鳴る
    // ゲッサーが当てた時は照準が札に絞られてから撃ち抜く。外した時は灰色の照準がぶれて、撃った本人の札が煙を上げる
    static void MeetingKill(PlayerControl target, byte style)
    {
        var volume = (byte)Mathf.RoundToInt((SoundOn ? Volume : 0f) * 100f);
        Send(Kind.MeetingKill, w =>
        {
            w.Write(target.PlayerId);
            w.Write(volume);
            w.Write(style);
        });
        ShowMeetingKill(target.PlayerId, volume / 100f, style);
    }

    // ゲッサーの画面: 会議の始めに、撃てる人(MOD を入れている人とホスト)にだけ、この試合で出る役職の一覧を送る
    public static void SendGuessUi()
    {
        if (!CanSend || !GuessUiOn || MeetingHud.Instance == null) return;
        List<TownOfHost.Roles.Core.CustomRoles> list = null;
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || pc.Data.IsDead || pc.Data.Disconnected) continue;
            if (!pc.AmOwner && !IsModPlayer(pc)) continue;
            if (!GuessManager.CanUseGuess(pc)) continue;
            list ??= GuessRoles();
            if (pc.AmOwner) { GuesserUi.Enable(list); continue; }
            Send(Kind.GuessUi, w =>
            {
                w.WritePacked(list.Count);
                foreach (var r in list) w.WritePacked((int)r);
            }, only: pc);
        }
    }

    static List<TownOfHost.Roles.Core.CustomRoles> GuessRoles()
    {
        var list = new List<TownOfHost.Roles.Core.CustomRoles>();
        foreach (TownOfHost.Roles.Core.CustomRoles r in Enum.GetValues(typeof(TownOfHost.Roles.Core.CustomRoles)))
        {
            if (r is TownOfHost.Roles.Core.CustomRoles.GM or TownOfHost.Roles.Core.CustomRoles.NotAssigned || r.IsAddOn() || r.IsGhostRole()) continue;
            if (r is not (TownOfHost.Roles.Core.CustomRoles.Crewmate or TownOfHost.Roles.Core.CustomRoles.Impostor) && !r.IsEnable()) continue;
            if (!list.Contains(r)) list.Add(r);
        }
        return list;
    }

    // トラッパー: 動けなくなったキラーの体に、鎖の輪が 2 本巻きついて回り、解けるまで残る
    public static void Bind(PlayerControl target, float duration)
    {
        if (!CanSend || !DeathOn || target == null) return;
        var volume = (byte)Mathf.RoundToInt((SoundOn ? Volume : 0f) * 100f);
        Send(Kind.Bind, w =>
        {
            w.Write(target.PlayerId);
            w.Write(duration);
            w.Write(volume);
        });
        ShowBind(target.PlayerId, duration, volume / 100f);
    }

    static void ShowBind(byte id, float duration, float volume)
    {
        var at = PlayerPos(id);
        if (at == null) return;
        Play(SoundKind.Clank, at.Value, volume);
        var steel = new Color(0.62f, 0.64f, 0.7f);
        const int links = 10;
        var parts = new List<SpriteRenderer>();
        for (var i = 0; i < links * 2; i++) parts.Add(Make(Ring, steel));
        var lockBody = Make(Square, new Color(0.85f, 0.7f, 0.25f));
        var lockLoop = Make(Ring, new Color(0.85f, 0.7f, 0.25f));
        parts.Add(lockBody);
        parts.Add(lockLoop);
        var nextSee = 0f;
        var visible = 0f;
        Animate(Mathf.Max(0.5f, duration), parts, (t, k) =>
        {
            var p = PlayerPos(id);
            if (p == null) return;
            var pos = p.Value;
            if (Time.time >= nextSee)
            {
                nextSee = Time.time + 0.1f;
                visible = CanSee(pos) ? 1f : 0f;
            }
            var wrap = Mathf.Clamp01(t / 0.25f);
            var fade = visible * (1f - Mathf.Clamp01((t - (duration - 0.4f)) / 0.4f));
            for (var r = 0; r < 2; r++)
                for (var i = 0; i < links; i++)
                {
                    var sr = parts[r * links + i];
                    var a = (i / (float)links * 360f + (r == 0 ? t : -t) * 70f) * Mathf.Deg2Rad;
                    var y = r == 0 ? 0.15f : -0.2f;
                    var front = Mathf.Sin(a) < 0f;
                    Set(sr, pos + new Vector2(Mathf.Cos(a) * 0.55f * wrap, y + Mathf.Sin(a) * 0.12f), front ? -0.03f : 0.02f);
                    sr.transform.localScale = new Vector3(0.2f, 0.13f, 1f);
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f);
                    sr.color = A(steel, fade * (front ? 1f : 0.55f));
                }
            Set(lockBody, pos + new Vector2(0f, -0.05f), -0.04f);
            lockBody.transform.localScale = new Vector3(0.22f, 0.18f, 1f) * wrap;
            lockBody.color = A(lockBody.color, fade);
            Set(lockLoop, pos + new Vector2(0f, 0.06f), -0.035f);
            lockLoop.transform.localScale = new Vector3(0.17f, 0.2f, 1f) * wrap;
            lockLoop.color = A(lockLoop.color, fade);
        });
    }

    // ヴァンパイアの血痕: MOD の人には、ダミーの名前の●の代わりに床の血だまりを描く。
    // ダミーがある間だけ出す(会議中は隠す)ので、消えるタイミングはホストのダミーと同じになる
    sealed class Stain
    {
        public readonly List<SpriteRenderer> parts = new();
        public Vector2 pos;
        public float since, nextSee, alpha, visible;
        public bool seen;
    }
    static readonly Dictionary<uint, Stain> stains = new();
    static readonly Color StainColor = new(0.42f, 0.02f, 0.04f, 0.9f);

    public static bool BloodStain(uint netId, Vector2 pos)
    {
        if (!CanSend || !DeathOn) return false;
        Send(Kind.BloodStain, w =>
        {
            w.WritePacked(netId);
            NetHelpers.WriteVector2(pos, w);
        });
        ShowBloodStain(netId, pos);
        HideDummy(netId);
        return true;
    }

    static void ShowBloodStain(uint netId, Vector2 pos)
    {
        if (stains.ContainsKey(netId)) return;
        var s = new Stain { pos = pos, since = Time.time };
        // 大きな血だまり 1 つと、まわりの小さなしぶき。形は netId から決める(全員同じ形になる)
        var rng = new System.Random((int)netId);
        void Blob(Vector2 offset, float w, float h)
        {
            var go = new GameObject("VampireStain");
            go.hideFlags |= HideFlags.DontSave;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Circle;
            sr.color = A(StainColor, 0f);
            // 床の上・プレイヤーの後ろ
            go.transform.position = new Vector3(pos.x + offset.x, pos.y + offset.y, (pos.y + offset.y) / 1000f + 0.002f);
            go.transform.localScale = new Vector3(w, h, 1f);
            s.parts.Add(sr);
        }
        Blob(Vector2.zero, 0.42f, 0.28f);
        Blob(new Vector2(0.12f, 0.05f), 0.26f, 0.2f);
        for (var i = 0; i < 5; i++)
        {
            var a = (float)rng.NextDouble() * Mathf.PI * 2f;
            var d = 0.22f + (float)rng.NextDouble() * 0.16f;
            var r = 0.05f + (float)rng.NextDouble() * 0.06f;
            Blob(new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d * 0.6f), r, r * 0.75f);
        }
        stains[netId] = s;
    }

    static readonly List<uint> stainsGone = new();
    static void UpdateStains()
    {
        stainsGone.Clear();
        var meeting = MeetingHud.Instance != null || !GameStates.IsInGame;
        foreach (var (netId, s) in stains)
        {
            var exists = !GameStates.IsLobby && AmongUsClient.Instance != null && AmongUsClient.Instance.FindObjectByNetId<PlayerControl>(netId) != null;
            if (exists) s.seen = true;
            // ダミーが消えたら消す(届くのが先になった時のために、最初の 5 秒は待つ)
            if (GameStates.IsLobby || (!exists && (s.seen || Time.time - s.since > 5f)))
            {
                stainsGone.Add(netId);
                continue;
            }
            if (Time.time >= s.nextSee)
            {
                s.nextSee = Time.time + 0.15f;
                s.visible = !meeting && CanSee(s.pos) ? 1f : 0f;
            }
            // 出てくる時は広がりながら、見えなくなる時はすっと消える
            s.alpha = Mathf.MoveTowards(s.alpha, s.visible, Time.deltaTime * 3f);
            foreach (var sr in s.parts)
                if (sr != null) sr.color = A(StainColor, StainColor.a * s.alpha);
        }
        foreach (var netId in stainsGone)
        {
            foreach (var sr in stains[netId].parts) if (sr != null) UnityEngine.Object.Destroy(sr.gameObject);
            stains.Remove(netId);
        }
    }

    // シーアにだけ、死んだ場所から魂がのぼっていくのが見える
    public static void SeerSoul(PlayerControl seer, Vector2 pos, int colorId)
    {
        if (!CanSend || !DeathOn || seer == null) return;
        var volume = (byte)Mathf.RoundToInt((SoundOn ? Volume : 0f) * 100f);
        if (seer.AmOwner) { ShowSoul(pos, colorId, volume / 100f); return; }
        Send(Kind.SeerSoul, w =>
        {
            NetHelpers.WriteVector2(pos, w);
            w.Write((byte)colorId);
            w.Write(volume);
        }, only: seer);
    }

    // ジャンボの足音: 試合の始めに一度だけ誰がジャンボかを送り、あとは各自の画面で歩いた距離から鳴らす(通信は増えない)
    static void SendJumbos()
    {
        jumboSent = true;
        jumbos.Clear();
        jumboSteps.Clear();
        if (!SoundOn) return;
        foreach (var pc in PlayerCatch.AllPlayerControls)
            if (pc != null && pc.Is(TownOfHost.Roles.Core.CustomRoles.Jumbo)) jumbos.Add(pc.PlayerId);
        if (jumbos.Count == 0) return;
        jumboVolume = Volume;
        var volume = (byte)Mathf.RoundToInt(Volume * 100f);
        Send(Kind.Jumbo, w =>
        {
            w.Write((byte)jumbos.Count);
            foreach (var id in jumbos) w.Write(id);
            w.Write(volume);
        });
    }

    public static DeathFx FxOf(CustomDeathReason reason) => reason switch
    {
        CustomDeathReason.Bombed => DeathFx.Explosion,
        CustomDeathReason.Sniped => DeathFx.Sniped,
        CustomDeathReason.Torched => DeathFx.Flame,
        CustomDeathReason.Evaporation => DeathFx.Evaporate,
        CustomDeathReason.Spell or CustomDeathReason.Magic or CustomDeathReason.MagicC or CustomDeathReason.Grim or CustomDeathReason.Execution => DeathFx.Magic,
        CustomDeathReason.Fall => DeathFx.Fall,
        CustomDeathReason.Launch => DeathFx.Launch,
        CustomDeathReason.Trap => DeathFx.Jaws,
        CustomDeathReason.Poisoned or CustomDeathReason.Infected => DeathFx.Poison,
        CustomDeathReason.Suffocation => DeathFx.Bubble,
        CustomDeathReason.Swallowed => DeathFx.Vortex,
        CustomDeathReason.Compression => DeathFx.Squash,
        CustomDeathReason.Revenge or CustomDeathReason.Counter or CustomDeathReason.Retaliation => DeathFx.Slash,
        CustomDeathReason.Bite => DeathFx.Blood,
        CustomDeathReason.Misfire => DeathFx.Backfire,
        CustomDeathReason.FollowingSuicide => DeathFx.Heartbreak,
        CustomDeathReason.TeleportKill => DeathFx.Warp,
        _ => DeathFx.None,
    };

    public static void Death(PlayerControl target, PlayerControl killer, CustomDeathReason reason)
    {
        if (!CanSend || !DeathOn || target == null) return;
        if (lastDeathFx.TryGetValue(target.PlayerId, out var last) && Time.time - last < 1.5f) return;
        lastDeathFx[target.PlayerId] = Time.time;
        if (GameStates.IsMeeting || MeetingHud.Instance != null)
        {
            MeetingKill(target, reason switch { CustomDeathReason.Guess => (byte)1, CustomDeathReason.Misfire => (byte)2, _ => (byte)0 });
            return;
        }
        var fx = FxOf(reason);
        if (fx == DeathFx.None) return;
        var pos = target.GetTruePosition();
        var from = killer != null && killer != target ? killer.GetTruePosition() : pos + Vector2.left;
        var volume = (byte)Mathf.RoundToInt((SoundOn ? Volume : 0f) * 100f);
        Send(Kind.Death, w =>
        {
            w.Write((byte)fx);
            NetHelpers.WriteVector2(pos, w);
            NetHelpers.WriteVector2(from, w);
            w.Write(volume);
        });
        ShowDeath(fx, pos, from, volume / 100f);
    }

    // ===== 受け取る =====

    public static void Receive(MessageReader reader)
    {
        if (AmongUsClient.Instance.AmHost) return;
        try
        {
            switch ((Kind)reader.ReadByte())
            {
                case Kind.HideDummy:
                    hiddenDummies.Add(reader.ReadPackedUInt32());
                    nextHideAt = 0f;
                    break;
                case Kind.NovaGuide:
                    {
                        var id = reader.ReadByte();
                        var body = NetHelpers.ReadVector2(reader);
                        var angle = reader.ReadSingle();
                        var locked = reader.ReadBoolean();
                        var color = reader.ReadByte();
                        ShowGuide(id, body, angle, locked, color);
                        break;
                    }
                case Kind.NovaBeam:
                    {
                        var id = reader.ReadByte();
                        var from = NetHelpers.ReadVector2(reader);
                        ShowBeam(id, from, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Shake(0.6f, 0.5f);
                        break;
                    }
                case Kind.NovaClear:
                    Clear(reader.ReadByte());
                    break;
                case Kind.Sound:
                    {
                        var sound = (SoundKind)reader.ReadByte();
                        Vector2? at = reader.ReadBoolean() ? NetHelpers.ReadVector2(reader) : null;
                        Play(sound, at, reader.ReadByte() / 100f);
                        break;
                    }
                case Kind.SluggerHit:
                    {
                        var pos = NetHelpers.ReadVector2(reader);
                        var dir = NetHelpers.ReadVector2(reader);
                        ShowSluggerHit(pos, dir, reader.ReadSingle());
                        break;
                    }
                case Kind.Charge:
                    {
                        var id = reader.ReadByte();
                        var duration = reader.ReadSingle();
                        var color = ReadColor(reader);
                        ShowCharge(id, duration, color, reader.ReadByte() / 100f);
                        break;
                    }
                case Kind.ChargeStop:
                    StopCharge(reader.ReadByte());
                    break;
                case Kind.MeetingKill:
                    {
                        var id = reader.ReadByte();
                        var volume = reader.ReadByte() / 100f;
                        ShowMeetingKill(id, volume, reader.ReadByte());
                        break;
                    }
                case Kind.GuessUi:
                    {
                        var count = reader.ReadPackedInt32();
                        var list = new List<TownOfHost.Roles.Core.CustomRoles>(count);
                        for (var i = 0; i < count; i++) list.Add((TownOfHost.Roles.Core.CustomRoles)reader.ReadPackedInt32());
                        GuesserUi.Enable(list);
                        break;
                    }
                case Kind.SeerSoul:
                    {
                        var pos = NetHelpers.ReadVector2(reader);
                        var color = reader.ReadByte();
                        ShowSoul(pos, color, reader.ReadByte() / 100f);
                        break;
                    }
                case Kind.Teleport:
                    {
                        var from = NetHelpers.ReadVector2(reader);
                        var to = NetHelpers.ReadVector2(reader);
                        ShowTeleport(from, to, reader.ReadByte() / 100f);
                        break;
                    }
                case Kind.BloodStain:
                    {
                        var netId = reader.ReadPackedUInt32();
                        ShowBloodStain(netId, NetHelpers.ReadVector2(reader));
                        break;
                    }
                case Kind.Bind:
                    {
                        var id = reader.ReadByte();
                        var duration = reader.ReadSingle();
                        ShowBind(id, duration, reader.ReadByte() / 100f);
                        break;
                    }
                case Kind.Jumbo:
                    {
                        jumbos.Clear();
                        jumboSteps.Clear();
                        var count = reader.ReadByte();
                        for (var i = 0; i < count; i++) jumbos.Add(reader.ReadByte());
                        jumboVolume = reader.ReadByte() / 100f;
                        break;
                    }
                case Kind.MeetingCutIn:
                    {
                        var title = reader.ReadString();
                        var color = reader.ReadString();
                        var volume = reader.ReadByte() / 100f;
                        var style = reader.ReadByte();
                        var ids = new byte[reader.ReadByte()];
                        for (var i = 0; i < ids.Length; i++) ids[i] = reader.ReadByte();
                        QueueCutIn(title, color, volume, style, ids);
                        break;
                    }
                case Kind.Death:
                    {
                        var fx = (DeathFx)reader.ReadByte();
                        var pos = NetHelpers.ReadVector2(reader);
                        var from = NetHelpers.ReadVector2(reader);
                        ShowDeath(fx, pos, from, reader.ReadByte() / 100f);
                        break;
                    }
            }
        }
        catch (Exception e) { Logger.Warn($"演出を受け取れませんでした: {e.Message}", "ClientEffects"); }
    }

    // ===== 毎フレーム =====

    internal static void Update()
    {
        if (stains.Count > 0) UpdateStains();
        if (GameStates.IsLobby)
        {
            if (hiddenDummies.Count > 0) { hiddenDummies.Clear(); dummyRenderers.Clear(); }
            if (jumboSent || jumbos.Count > 0 || lastDeathFx.Count > 0)
            {
                jumboSent = false;
                jumbos.Clear();
                jumboSteps.Clear();
                lastDeathFx.Clear();
            }
        }
        if (!On && running.Count == 0 && runningHud.Count == 0 && hiddenDummies.Count == 0) return;
        if (CanSend && !jumboSent && On && GameStates.IsInTask && !GameStates.Intro) SendJumbos();
        if (hiddenDummies.Count > 0 && Time.time >= nextHideAt && AmongUsClient.Instance != null)
        {
            nextHideAt = Time.time + 0.1f;
            nextHideAt = Time.time + 0.25f;
            foreach (var netId in hiddenDummies)
            {
                if (!dummyRenderers.TryGetValue(netId, out var renderers) || renderers == null)
                {
                    var text = AmongUsClient.Instance.FindObjectByNetId<PlayerControl>(netId)?.cosmetics?.nameText;
                    if (text == null) continue;
                    dummyRenderers[netId] = renderers = new List<MeshRenderer>(text.GetComponentsInChildren<MeshRenderer>(true));
                }
                foreach (var r in renderers) if (r != null && r.enabled) r.enabled = false;
            }
        }
        if (!GameStates.IsInGame)
        {
            if (running.Count > 0 || runningHud.Count > 0 || guides.Count > 0 || beams.Count > 0) ClearWorld(true);
            return;
        }
        if (MeetingHud.Instance != null || GameStates.Intro) quietUntil = Time.time + 4f;
        if (MeetingHud.Instance != null)
        {
            if (running.Count > 0 || guides.Count > 0 || beams.Count > 0) ClearWorld(false);
        }
        else
        {
            Tick(running);
            if (jumbos.Count > 0) StepJumbos();
        }
        Tick(runningHud);
    }

    static void Tick(List<Func<bool>> list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            bool alive;
            try { alive = list[i](); }
            catch { alive = false; }
            if (!alive) list.RemoveAt(i);
        }
    }

    static void ClearWorld(bool hudToo)
    {
        foreach (var id in new List<byte>(guides.Keys)) Clear(id);
        foreach (var id in new List<byte>(beams.Keys)) Clear(id);
        running.Clear();
        foreach (var c in charges.Values) { c.dead = true; StopChargeSound(c); }
        charges.Clear();
        foreach (var sr in new List<SpriteRenderer>(live)) if (sr != null) Release(sr);
        live.Clear();
        if (hudToo)
        {
            runningHud.Clear();
            pendingCutIn = null;
        }
    }

    // ===== 部品(使い回す) =====

    const float Z = -0.6f;
    static Sprite square, circle, ring, star, heart;
    static readonly Stack<SpriteRenderer> pool = new();
    static readonly HashSet<SpriteRenderer> live = new();

    static Sprite MakeSprite(int size, Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        tex.hideFlags |= HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.hideFlags |= HideFlags.HideAndDontSave;
        return sprite;
    }

    static Sprite Square => square ??= MakeSprite(4, (u, v) => 1f);
    static Sprite Circle => circle ??= MakeSprite(48, (u, v) => 1f - Mathf.SmoothStep(0.55f, 1f, Mathf.Sqrt(u * u + v * v)));
    static Sprite Ring => ring ??= MakeSprite(64, (u, v) => 1f - Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.85f) / 0.12f);
    static Sprite Star => star ??= MakeSprite(48, (u, v) =>
    {
        var a = Mathf.Atan2(v, u);
        var d = Mathf.Sqrt(u * u + v * v);
        var edge = 0.35f + 0.6f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2f)), 6f);
        return (edge - d) / 0.08f;
    });

    static Sprite Heart => heart ??= MakeSprite(48, (u, v) =>
    {
        var x = u * 1.25f;
        var y = v * 1.25f + 0.2f;
        var a = x * x + y * y - 1f;
        return -(a * a * a - x * x * y * y * y) * 6f;
    });

    static SpriteRenderer Make(Sprite sprite, Color color)
    {
        SpriteRenderer sr = null;
        while (pool.Count > 0 && sr == null) sr = pool.Pop();
        if (sr == null)
        {
            var go = new GameObject("ClientFx");
            go.hideFlags |= HideFlags.DontSave;
            sr = go.AddComponent<SpriteRenderer>();
        }
        sr.sprite = sprite;
        sr.color = color;
        sr.transform.rotation = Quaternion.identity;
        sr.transform.localScale = Vector3.one;
        sr.gameObject.SetActive(true);
        live.Add(sr);
        return sr;
    }

    static void Release(SpriteRenderer sr)
    {
        if (sr == null) return;
        live.Remove(sr);
        sr.gameObject.SetActive(false);
        if (pool.Count < 96) pool.Push(sr);
        else UnityEngine.Object.Destroy(sr.gameObject);
    }

    static void Set(SpriteRenderer sr, Vector2 pos, float zOff = 0f)
        => sr.transform.position = new Vector3(pos.x, pos.y, Z + zOff);

    static Color A(Color c, float a) => new(c.r, c.g, c.b, a);

    static Vector2 Dir(float angle) => new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

    // 自分の画面で見える所か(視界の中で、壁にさえぎられていない)。幽霊は全部見える
    static bool CanSee(Vector2 pos)
    {
        var me = PlayerControl.LocalPlayer;
        if (me == null || me.Data == null) return false;
        if (me.Data.IsDead) return true;
        var from = me.GetTruePosition();
        var d = Vector2.Distance(from, pos);
        var r = 3f;
        try { r = ShipStatus.Instance.CalculateLightRadius(me.Data); } catch { }
        if (d > r + 0.4f) return false;
        if (d < 0.05f) return true;
        return !PhysicsHelpers.AnyNonTriggersBetween(from, (pos - from) / d, d, Constants.ShadowMask);
    }

    static void Shake(float duration, float severity)
    {
        if (!ShakeOn) return;
        try { HudManager.Instance?.PlayerCam?.ShakeScreen(duration, severity); } catch { }
    }

    // 決まった時間だけ動く演出を、まとめて片付けられるようにする
    static void Animate(float life, List<SpriteRenderer> parts, Action<float, float> step)
    {
        var started = Time.time;
        running.Add(() =>
        {
            var t = Time.time - started;
            var broken = t >= life;
            for (var i = 0; !broken && i < parts.Count; i++) broken = parts[i] == null;
            if (broken)
            {
                foreach (var p in parts) Release(p);
                return false;
            }
            step(t, t / life);
            return true;
        });
    }

    // ===== ノヴァ =====

    // 照準: 体から向きへ流れる点線と、その先で回る輪と星。向きを決めると赤くなって速く点滅する
    sealed class Guide
    {
        public readonly List<SpriteRenderer> parts = new();
        public Vector2 body;
        public float angle;
        public bool locked;
        public Color color;
        public bool dead;
    }

    const int GuideDots = 6;

    static void ShowGuide(byte id, Vector2 body, float angle, bool locked, int colorId)
    {
        if (!guides.TryGetValue(id, out var g))
        {
            g = new Guide();
            for (var i = 0; i < GuideDots; i++) g.parts.Add(Make(Circle, Color.white));
            g.parts.Add(Make(Ring, Color.white));
            g.parts.Add(Make(Star, Color.white));
            guides[id] = g;
            var started = Time.time;
            running.Add(() =>
            {
                if (g.dead) return false;
                var t = Time.time - started;
                var dir = Dir(g.angle);
                var tint = g.locked ? new Color(1f, 0.15f, 0.25f) : g.color;
                for (var i = 0; i < GuideDots; i++)
                {
                    var k = (i + 1f) / (GuideDots + 1);
                    var flow = Mathf.Repeat(t * (g.locked ? 3f : 1.2f) - k, 1f);
                    var dot = g.parts[i];
                    Set(dot, g.body + dir * (0.4f + 1.6f * k));
                    dot.transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.16f, flow);
                    dot.color = A(tint, Mathf.Lerp(0.35f, 0.9f, flow));
                }
                var at = g.body + dir * 2.3f;
                var pulse = g.locked ? Mathf.Abs(Mathf.Sin(t * 14f)) : 0.5f + 0.5f * Mathf.Sin(t * 4f);
                var reticle = g.parts[GuideDots];
                Set(reticle, at);
                reticle.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 0.7f, pulse);
                reticle.transform.rotation = Quaternion.Euler(0f, 0f, t * (g.locked ? 360f : 90f));
                reticle.color = A(tint, Mathf.Lerp(0.6f, 1f, pulse));
                var core = g.parts[GuideDots + 1];
                Set(core, at, -0.01f);
                core.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.42f, pulse);
                core.transform.rotation = Quaternion.Euler(0f, 0f, -t * 60f);
                core.color = Color.Lerp(tint, Color.white, 0.5f);
                return true;
            });
        }
        g.body = body;
        g.angle = angle;
        g.locked = locked;
        g.color = Palette.PlayerColors[Mathf.Clamp(colorId, 0, Palette.PlayerColors.Length - 1)];
    }

    static void Clear(byte id)
    {
        if (guides.TryGetValue(id, out var g))
        {
            g.dead = true;
            foreach (var p in g.parts) Release(p);
            guides.Remove(id);
        }
        if (beams.TryGetValue(id, out var list))
        {
            foreach (var p in list) Release(p);
            beams.Remove(id);
        }
    }

    // ビーム: 外側の光・本体・白い芯の 3 重の帯に、流れる光の粒と、撃った所の衝撃の輪を重ねる
    static void ShowBeam(byte id, Vector2 from, float angle, float length, float width, float time)
    {
        Clear(id);
        length = Mathf.Max(0.1f, length);
        var dir = Dir(angle);
        var perp = new Vector2(-dir.y, dir.x);
        var rot = Quaternion.Euler(0f, 0f, angle);
        var beamColor = new Color(0.5f, 0.94f, 1f);
        var glow = Make(Square, beamColor);
        var body = Make(Square, beamColor);
        var core = Make(Square, Color.white);
        var muzzle = Make(Circle, Color.white);
        var tip = Make(Circle, beamColor);
        var shock1 = Make(Ring, beamColor);
        var shock2 = Make(Ring, Color.white);
        var sparks = new List<SpriteRenderer>();
        for (var i = 0; i < 12; i++) sparks.Add(Make(Square, Color.white));
        var all = new List<SpriteRenderer> { glow, body, core, muzzle, tip, shock1, shock2 };
        all.AddRange(sparks);
        beams[id] = all;
        glow.transform.rotation = body.transform.rotation = core.transform.rotation = rot;
        foreach (var s in sparks) s.transform.rotation = rot;
        Set(shock1, from, -0.03f);
        Set(shock2, from, -0.03f);
        Set(muzzle, from, -0.02f);
        var seeds = new float[sparks.Count];
        for (var i = 0; i < seeds.Length; i++) seeds[i] = UnityEngine.Random.value;

        var started = Time.time;
        running.Add(() =>
        {
            if (!beams.TryGetValue(id, out var mine) || mine != all) return false;
            var t = Time.time - started;
            if (t >= time) { Clear(id); return false; }
            var grow = Mathf.Clamp01(t / 0.12f);
            var fade = Mathf.Clamp01((time - t) / 0.35f);
            var flicker = 1f + 0.08f * Mathf.Sin(t * 70f) + 0.05f * Mathf.Sin(t * 23f);
            var settle = Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.12f) / 0.2f));
            var w = width * settle * grow * Mathf.Lerp(0.2f, 1f, fade) * flicker;
            var burst = Mathf.Min(width, 1.4f);
            var len = length * Mathf.Clamp01(t / 0.08f);
            var center = from + dir * (len * 0.5f);
            Set(glow, center, 0.02f);
            Set(body, center, 0.01f);
            Set(core, center);
            glow.transform.localScale = new Vector3(len, w * 1.7f, 1f);
            body.transform.localScale = new Vector3(len, w, 1f);
            core.transform.localScale = new Vector3(len, w * 0.4f, 1f);
            glow.color = A(beamColor, 0.3f * fade);
            body.color = A(beamColor, 0.85f * fade);
            core.color = A(Color.white, fade);
            muzzle.transform.localScale = Vector3.one * (burst * 1.2f * flicker * fade + 0.1f);
            muzzle.color = A(Color.white, 0.9f * fade);
            Set(tip, from + dir * len, -0.02f);
            tip.transform.localScale = Vector3.one * (burst * (1f + 0.25f * Mathf.Sin(t * 40f)) * fade);
            tip.color = A(beamColor, 0.8f * fade);
            for (var k = 0; k < 2; k++)
            {
                var ring = k == 0 ? shock1 : shock2;
                var rt = Mathf.Clamp01((t - k * 0.12f) / 0.55f);
                ring.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, burst * 3f, rt);
                ring.color = A(k == 0 ? beamColor : Color.white, (1f - rt) * 0.9f);
            }
            for (var i = 0; i < sparks.Count; i++)
            {
                var p = Mathf.Repeat(seeds[i] + t * (1.2f + seeds[i]), 1f);
                var side = (seeds[(i + 3) % seeds.Length] - 0.5f) * w * 0.8f;
                Set(sparks[i], from + dir * (len * p) + perp * side, -0.01f);
                sparks[i].transform.localScale = new Vector3(0.35f, 0.05f, 1f);
                sparks[i].color = A(Color.white, fade * (1f - p) * 0.9f);
            }
            return true;
        });
    }

    // ===== スラッガー =====

    // 当たった所に光の筋が弾け、輪が広がり、吹き飛ぶ道に残像の丸が残る
    static void ShowSluggerHit(Vector2 pos, Vector2 dir, float dist)
    {
        if (!CanSee(pos) && !CanSee(pos + dir * dist)) return;
        var hitColor = new Color(1f, 0.85f, 0.3f);
        const int count = 10;
        var flash = Make(Circle, Color.white);
        var wave = Make(Ring, hitColor);
        var parts = new List<SpriteRenderer> { flash, wave };
        var streaks = new List<SpriteRenderer>();
        for (var i = 0; i < count; i++) streaks.Add(Make(Square, Color.white));
        var trail = new List<SpriteRenderer>();
        for (var i = 0; i < 5; i++) trail.Add(Make(Circle, hitColor));
        parts.AddRange(streaks);
        parts.AddRange(trail);
        Set(flash, pos, -0.02f);
        Set(wave, pos, -0.01f);
        var angles = new float[count];
        var baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        for (var i = 0; i < count; i++)
        {
            angles[i] = baseAngle + 360f / count * i + UnityEngine.Random.Range(-12f, 12f);
            streaks[i].transform.rotation = Quaternion.Euler(0f, 0f, angles[i]);
        }
        if (PlayerControl.LocalPlayer != null && Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(), pos) < 2.5f) Shake(0.25f, 0.4f);
        Animate(0.55f, parts, (t, k) =>
        {
            flash.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.2f, k);
            flash.color = A(Color.white, 1f - Mathf.Clamp01(t / 0.15f));
            wave.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.2f, Mathf.Sqrt(k));
            wave.color = A(hitColor, 1f - k);
            var r = Mathf.Lerp(0.25f, 1.3f, Mathf.Sqrt(k));
            for (var i = 0; i < count; i++)
            {
                Set(streaks[i], pos + Dir(angles[i]) * r, -0.01f);
                streaks[i].transform.localScale = new Vector3(Mathf.Lerp(0.5f, 0.1f, k), 0.06f, 1f);
                streaks[i].color = A(i % 2 == 0 ? Color.white : hitColor, 1f - k);
            }
            for (var i = 0; i < trail.Count; i++)
            {
                var f = (i + 1f) / trail.Count;
                Set(trail[i], pos + dir * (dist * f));
                trail[i].transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 0.25f, f);
                trail[i].color = A(hitColor, t < f * 0.3f ? 0f : (1f - k) * 0.5f);
            }
        });
    }

    // ===== 死因ごとの演出 =====

    static void ShowDeath(DeathFx fx, Vector2 pos, Vector2 from, float volume)
    {
        if (!CanSee(pos)) return;
        switch (fx)
        {
            case DeathFx.Explosion:
                Explosion(pos);
                Play(SoundKind.Boom, pos, volume);
                Shake(0.45f, 0.8f);
                break;
            case DeathFx.Sniped:
                Sniped(pos, from);
                Play(SoundKind.Gunshot, pos, volume);
                break;
            case DeathFx.Flame:
                Particles(pos, 14, new Color(1f, 0.55f, 0.1f), new Color(1f, 0.95f, 0.4f), Vector2.up * 1.4f, 0.4f, 0.9f, Circle);
                Play(SoundKind.Flame, pos, volume);
                break;
            case DeathFx.Evaporate:
                Particles(pos, 16, new Color(0.5f, 0.94f, 1f), Color.white, Vector2.up * 1.8f, 0.5f, 1.0f, Square);
                Play(SoundKind.Sparkle, pos, volume);
                break;
            case DeathFx.Magic:
                Magic(pos);
                Play(SoundKind.Chime, pos, volume);
                break;
            case DeathFx.Blood:
                Particles(pos, 10, new Color(0.7f, 0f, 0.05f), new Color(0.35f, 0f, 0f), Vector2.down * 0.6f, 0.9f, 0.8f, Circle);
                break;
            case DeathFx.Fall:
                Fall(pos);
                Play(SoundKind.Push, pos, volume);
                break;
            case DeathFx.Launch:
                Launch(pos);
                Play(SoundKind.Rocket, pos, volume);
                Shake(0.5f, 0.6f);
                break;
            case DeathFx.Jaws:
                Jaws(pos);
                Play(SoundKind.Gunshot, pos, volume * 0.7f);
                break;
            case DeathFx.Poison:
                Particles(pos, 14, new Color(0.45f, 0.9f, 0.2f), new Color(0.6f, 0.2f, 0.8f), Vector2.up * 0.9f, 0.5f, 1.1f, Circle);
                Play(SoundKind.Sparkle, pos, volume * 0.6f);
                break;
            case DeathFx.Bubble:
                Particles(pos, 14, new Color(0.6f, 0.85f, 1f), Color.white, Vector2.up * 1.3f, 0.45f, 1.1f, Ring);
                Play(SoundKind.Sparkle, pos, volume * 0.6f);
                break;
            case DeathFx.Vortex:
                Vortex(pos);
                Play(SoundKind.Push, pos, volume);
                break;
            case DeathFx.Squash:
                Squash(pos);
                Play(SoundKind.Footstep, pos, volume);
                Shake(0.3f, 0.5f);
                break;
            case DeathFx.Slash:
                Slash(pos, new Color(1f, 0.2f, 0.25f));
                Play(SoundKind.Swing, pos, volume);
                break;
            case DeathFx.Backfire:
                Backfire(pos);
                Play(SoundKind.Gunshot, pos, volume * 0.8f);
                break;
            case DeathFx.Heartbreak:
                Heartbreak(pos);
                Play(SoundKind.Soul, pos, volume * 0.7f);
                break;
            case DeathFx.Warp:
                Warp(pos, false);
                Play(SoundKind.Sparkle, pos, volume * 0.8f);
                break;
        }
    }

    static void Explosion(Vector2 pos)
    {
        var fire = new Color(1f, 0.55f, 0.1f);
        var flash = Make(Circle, Color.white);
        var ball = Make(Circle, fire);
        var ring1 = Make(Ring, fire);
        var ring2 = Make(Ring, Color.white);
        var parts = new List<SpriteRenderer> { flash, ball, ring1, ring2 };
        var debris = new List<SpriteRenderer>();
        var vel = new List<Vector2>();
        for (var i = 0; i < 12; i++)
        {
            debris.Add(Make(i % 3 == 0 ? Square : Circle, i % 2 == 0 ? fire : new Color(0.35f, 0.35f, 0.35f)));
            vel.Add(Dir(UnityEngine.Random.Range(0f, 360f)) * UnityEngine.Random.Range(1.5f, 3.2f));
        }
        parts.AddRange(debris);
        Set(flash, pos, -0.03f);
        Set(ball, pos, -0.02f);
        Set(ring1, pos, -0.01f);
        Set(ring2, pos, -0.01f);
        Animate(0.9f, parts, (t, k) =>
        {
            flash.transform.localScale = Vector3.one * Mathf.Lerp(2.4f, 0.5f, Mathf.Clamp01(t / 0.2f));
            flash.color = A(Color.white, 1f - Mathf.Clamp01(t / 0.2f));
            ball.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 2f, Mathf.Sqrt(k));
            ball.color = A(Color.Lerp(fire, new Color(0.3f, 0.3f, 0.3f), k), (1f - k) * 0.85f);
            ring1.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 4f, Mathf.Sqrt(k));
            ring1.color = A(fire, 1f - k);
            var k2 = Mathf.Clamp01((t - 0.1f) / 0.7f);
            ring2.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 3f, Mathf.Sqrt(k2));
            ring2.color = A(Color.white, (1f - k2) * 0.7f);
            for (var i = 0; i < debris.Count; i++)
            {
                Set(debris[i], pos + vel[i] * (t * (1f - k * 0.5f)), -0.01f);
                debris[i].transform.localScale = Vector3.one * Mathf.Lerp(0.22f, 0.05f, k);
                debris[i].color = A(debris[i].color, 1f - k);
            }
        });
    }

    static void Sniped(Vector2 pos, Vector2 from)
    {
        var dir = (pos - from).sqrMagnitude > 0.01f ? (pos - from).normalized : Vector2.right;
        var tracer = Make(Square, new Color(1f, 0.95f, 0.7f));
        var hit = Make(Circle, Color.white);
        var ring = Make(Ring, new Color(1f, 0.3f, 0.3f));
        var parts = new List<SpriteRenderer> { tracer, hit, ring };
        tracer.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        Set(hit, pos, -0.02f);
        Set(ring, pos, -0.01f);
        Animate(0.4f, parts, (t, k) =>
        {
            const float len = 1.6f;
            Set(tracer, pos - dir * (len * 0.5f + k * 0.4f));
            tracer.transform.localScale = new Vector3(len * (1f - k), 0.05f, 1f);
            tracer.color = A(tracer.color, 1f - k);
            hit.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 0.1f, k);
            hit.color = A(Color.white, 1f - k);
            ring.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.2f, Mathf.Sqrt(k));
            ring.color = A(ring.color, 1f - k);
        });
    }

    static void Particles(Vector2 pos, int count, Color a, Color b, Vector2 drift, float spread, float life, Sprite sprite)
    {
        var parts = new List<SpriteRenderer>();
        var offs = new List<Vector2>();
        var delays = new float[count];
        for (var i = 0; i < count; i++)
        {
            parts.Add(Make(sprite, Color.Lerp(a, b, UnityEngine.Random.value)));
            offs.Add(new Vector2(UnityEngine.Random.Range(-spread, spread), UnityEngine.Random.Range(-0.2f, 0.3f)));
            delays[i] = UnityEngine.Random.Range(0f, life * 0.4f);
        }
        Animate(life, parts, (t, k) =>
        {
            for (var i = 0; i < parts.Count; i++)
            {
                var lt = Mathf.Clamp01((t - delays[i]) / (life - delays[i]));
                Set(parts[i], pos + offs[i] + drift * lt + new Vector2(Mathf.Sin((t + i) * 9f) * 0.05f, 0f), -0.01f);
                parts[i].transform.localScale = Vector3.one * (t < delays[i] ? 0f : Mathf.Lerp(0.28f, 0.04f, lt));
                parts[i].color = A(parts[i].color, 1f - lt);
            }
        });
    }

    static void Magic(Vector2 pos)
    {
        var purple = new Color(0.7f, 0.35f, 1f);
        var circle1 = Make(Ring, purple);
        var circle2 = Make(Ring, Color.white);
        var mark = Make(Star, purple);
        var parts = new List<SpriteRenderer> { circle1, circle2, mark };
        Set(circle1, pos, 0.01f);
        Set(circle2, pos, 0.01f);
        Set(mark, pos, -0.01f);
        Animate(1f, parts, (t, k) =>
        {
            var grow = Mathf.Clamp01(t / 0.25f);
            var fade = 1f - Mathf.Clamp01((k - 0.6f) / 0.4f);
            circle1.transform.localScale = new Vector3(1.6f, 0.8f, 1f) * grow;
            circle1.transform.rotation = Quaternion.Euler(0f, 0f, t * 120f);
            circle1.color = A(purple, fade);
            circle2.transform.localScale = new Vector3(1.1f, 0.55f, 1f) * grow;
            circle2.transform.rotation = Quaternion.Euler(0f, 0f, -t * 200f);
            circle2.color = A(Color.white, fade * 0.8f);
            Set(mark, pos + Vector2.up * (k * 0.8f), -0.01f);
            mark.transform.localScale = Vector3.one * (0.5f * grow);
            mark.transform.rotation = Quaternion.Euler(0f, 0f, t * 90f);
            mark.color = A(purple, fade);
        });
    }

    // ===== 画面全体(会議中も動く) =====

    static GameObject CloneFullScreen(string name)
    {
        var hud = HudManager.Instance;
        if (hud == null || hud.FullScreen == null) return null;
        var t = hud.transform.Find(name);
        if (t != null) return t.gameObject;
        var obj = UnityEngine.Object.Instantiate(hud.FullScreen.gameObject, hud.transform);
        obj.name = name;
        return obj;
    }

    // 特殊会議のカットイン: 会議の画面が出てから、会議の名前が入った帯が横から滑り込み、少し止まって抜けていく
    static (string title, string color, float volume, float at, byte style)? pendingCutIn;
    static byte[] pendingHighlight = Array.Empty<byte>();

    static void QueueCutIn(string title, string color, float volume, byte style = 0, byte[] highlight = null)
    {
        pendingCutIn = (title, color, volume, Time.time, style);
        pendingHighlight = highlight ?? Array.Empty<byte>();
        runningHud.Add(() =>
        {
            if (pendingCutIn == null) return false;
            if (MeetingHud.Instance == null)
            {
                if (Time.time - pendingCutIn.Value.at > 15f) { pendingCutIn = null; return false; }
                return true;
            }
            if (Time.time - pendingCutIn.Value.at < 0.2f) return true;
            var (t, c, v, _, s) = pendingCutIn.Value;
            pendingCutIn = null;
            PlayCutIn(t, c, v);
            if (s == 1) PlayBalancer(c);
            StartMeetingTheme(t, c, s, pendingHighlight);
            return false;
        });
    }

    static void PlayCutIn(string title, string colorCode, float volume)
    {
        var hud = HudManager.Instance;
        var band = CloneFullScreen("ClientFx_CutInBand");
        var stripe = CloneFullScreen("ClientFx_CutInStripe");
        if (band == null || stripe == null) return;
        if (!ColorUtility.TryParseHtmlString(colorCode, out var color)) color = Color.white;
        var bandSr = band.GetComponent<SpriteRenderer>();
        var stripeSr = stripe.GetComponent<SpriteRenderer>();
        var baseScale = hud.FullScreen.transform.localScale;
        var basePos = hud.FullScreen.transform.localPosition;
        TMPro.TextMeshPro text = null;
        try
        {
            var holder = hud.transform.Find("ClientFx_CutInText");
            text = holder != null ? holder.GetComponent<TMPro.TextMeshPro>() : UnityEngine.Object.Instantiate(hud.TaskPanel.taskText, hud.transform);
            text.name = "ClientFx_CutInText";
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.fontSize = text.fontSizeMax = text.fontSizeMin = 4f;
            text.enableAutoSizing = false;
            text.rectTransform.sizeDelta = new Vector2(12f, 1.2f);
            text.text = "<b>" + title.RemoveHtmlTags() + "</b>";
            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
            text.gameObject.SetActive(true);
        }
        catch (Exception e) { Logger.Warn($"会議の名前を出せませんでした: {e.Message}", "ClientEffects"); }
        if (volume > 0f) Play(SoundKind.MeetingSting, null, volume);
        band.SetActive(true);
        stripe.SetActive(true);
        var started = Time.time;
        const float life = 2.4f;
        runningHud.Add(() =>
        {
            if (bandSr == null) return false;
            var t = Time.time - started;
            if (t >= life)
            {
                band.SetActive(false);
                stripe.SetActive(false);
                if (text != null) text.gameObject.SetActive(false);
                return false;
            }
            var open = Mathf.Clamp01(t / 0.18f);
            var close = Mathf.Clamp01((life - t) / 0.25f);
            var h = Mathf.SmoothStep(0f, 1f, open) * Mathf.SmoothStep(0f, 1f, close);
            band.transform.localScale = new Vector3(baseScale.x, baseScale.y * 0.2f * h, baseScale.z);
            band.transform.localPosition = new Vector3(basePos.x, basePos.y + 0.6f, basePos.z - 2f);
            bandSr.color = new Color(0f, 0f, 0f, 0.75f);
            stripe.transform.localScale = new Vector3(baseScale.x, baseScale.y * 0.015f * h, baseScale.z);
            stripe.transform.localPosition = new Vector3(basePos.x, basePos.y + 0.6f - 0.62f * h, basePos.z - 2.1f);
            stripeSr.color = A(color, 0.95f);
            if (text != null)
            {
                var slideIn = 1f - Mathf.Pow(1f - Mathf.Clamp01((t - 0.08f) / 0.35f), 3f);
                var slideOut = Mathf.Pow(Mathf.Clamp01((t - (life - 0.4f)) / 0.4f), 2f);
                var x = Mathf.Lerp(8f, 0f, slideIn) - slideOut * 8f;
                text.transform.localPosition = new Vector3(basePos.x + x, basePos.y + 0.6f, basePos.z - 2.2f);
                text.color = Color.Lerp(Color.white, color, 0.5f + 0.5f * Mathf.Sin(t * 6f));
            }
            return true;
        });
    }

    static void Fall(Vector2 pos)
    {
        var dust = new Color(0.75f, 0.7f, 0.6f);
        Particles(pos + Vector2.down * 0.3f, 10, dust, new Color(0.5f, 0.45f, 0.4f), Vector2.zero, 0.7f, 0.7f, Circle);
        var hole = Make(Circle, new Color(0f, 0f, 0f, 0.8f));
        var ring = Make(Ring, dust);
        Set(hole, pos, 0.01f);
        Set(ring, pos, -0.01f);
        Animate(0.8f, new List<SpriteRenderer> { hole, ring }, (t, k) =>
        {
            hole.transform.localScale = new Vector3(1.2f, 0.5f, 1f) * Mathf.Sin(Mathf.Min(k * 2f, 1f) * Mathf.PI * 0.5f) * (1f - k * 0.6f);
            hole.color = A(Color.black, 0.8f * (1f - k));
            ring.transform.localScale = new Vector3(1.6f, 0.8f, 1f) * Mathf.Lerp(0.3f, 1.2f, k);
            ring.color = A(dust, 1f - k);
        });
    }

    static void Launch(Vector2 pos)
    {
        var flame = new Color(1f, 0.6f, 0.15f);
        var body = Make(Circle, Color.white);
        var parts = new List<SpriteRenderer> { body };
        var trail = new List<SpriteRenderer>();
        for (var i = 0; i < 14; i++) trail.Add(Make(Circle, i % 2 == 0 ? flame : new Color(0.6f, 0.6f, 0.6f)));
        parts.AddRange(trail);
        var offs = new float[trail.Count];
        for (var i = 0; i < offs.Length; i++) offs[i] = UnityEngine.Random.Range(-0.15f, 0.15f);
        Animate(1.4f, parts, (t, k) =>
        {
            var height = 12f * k * k;
            Set(body, pos + Vector2.up * height, -0.02f);
            body.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 0.2f, k);
            body.color = A(Color.white, 1f - k);
            for (var i = 0; i < trail.Count; i++)
            {
                var back = (i + 1) * 0.35f;
                var h = Mathf.Max(0f, height - back);
                Set(trail[i], pos + new Vector2(offs[i] * (1f + i * 0.2f), h), -0.01f);
                trail[i].transform.localScale = Vector3.one * Mathf.Lerp(0.45f, 0.1f, (float)i / trail.Count) * (1f - k * 0.5f);
                trail[i].color = A(trail[i].color, (1f - k) * (height > back ? 0.9f : 0f));
            }
        });
    }

    sealed class Charging
    {
        public readonly List<SpriteRenderer> parts = new();
        public bool dead;
        public float nextSee, visible, volume;
        public AudioSource source;
    }

    // ための音は同じ音を使い回すので、ほかにためている人がいない時だけ止める
    static void StopChargeSound(Charging c)
    {
        if (c.source == null) return;
        c.source = null;
        foreach (var other in charges.Values) if (other != c && other.source != null) return;
        try { if (clips.TryGetValue(SoundKind.ChargeLoop, out var loop) && loop != null) SoundManager.Instance?.StopSound(loop); } catch { }
    }

    static Vector2? PlayerPos(byte id)
    {
        var pc = GameData.Instance?.GetPlayerById(id)?.Object;
        return pc == null ? null : pc.GetTruePosition() + new Vector2(0f, 0.35f);
    }

    static void ShowCharge(byte id, float duration, Color color, float volume)
    {
        StopCharge(id);
        var c = new Charging { volume = volume };
        if (volume > 0.01f && SoundManager.Instance != null)
        {
            try
            {
                if (!clips.TryGetValue(SoundKind.ChargeLoop, out var loop) || loop == null) clips[SoundKind.ChargeLoop] = loop = Build(SoundKind.ChargeLoop);
                c.source = SoundManager.Instance.PlaySound(loop, true, 0f);
            }
            catch (Exception e) { Logger.Warn($"ための音を鳴らせませんでした: {e.Message}", "ClientEffects"); }
        }
        var glow = Make(Circle, color);
        var ring = Make(Ring, color);
        c.parts.Add(glow);
        c.parts.Add(ring);
        const int count = 10;
        var angles = new float[count];
        for (var i = 0; i < count; i++)
        {
            c.parts.Add(Make(Square, Color.white));
            angles[i] = UnityEngine.Random.Range(0f, 360f);
        }
        charges[id] = c;
        var started = Time.time;
        running.Add(() =>
        {
            if (c.dead) return false;
            var at = PlayerPos(id);
            var t = Time.time - started;
            if (at == null || t > duration + 12f) { StopCharge(id); return false; }
            var pos = at.Value;
            if (Time.time >= c.nextSee)
            {
                c.nextSee = Time.time + 0.1f;
                c.visible = CanSee(pos) ? 1f : 0f;
            }
            var visible = c.visible;
            var full = Mathf.Clamp01(t / Mathf.Max(0.1f, duration));
            if (c.source != null)
            {
                // ためるほど高く・大きくなり、たまりきったら細かく震える
                var me = PlayerControl.LocalPlayer;
                var d = me == null ? 0f : Vector2.Distance(me.GetTruePosition(), pos);
                c.source.pitch = Mathf.Lerp(0.7f, 2.1f, full) * (full >= 1f ? 1f + 0.04f * Mathf.Sin(t * 50f) : 1f);
                c.source.volume = c.volume * Mathf.Lerp(0.35f, 0.8f, full) * Mathf.Clamp01(1.2f - d / 10f) * Mathf.Clamp01(t / 0.15f);
            }
            var pulse = 0.5f + 0.5f * Mathf.Sin(t * (full >= 1f ? 18f : 6f));
            Set(glow, pos, 0.01f);
            glow.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.3f, full) * (0.9f + 0.1f * pulse);
            glow.color = A(color, visible * Mathf.Lerp(0.15f, 0.45f, full));
            var cycle = Mathf.Repeat(t * 1.5f, 1f);
            Set(ring, pos);
            ring.transform.localScale = Vector3.one * Mathf.Lerp(2.2f, 0.9f, cycle);
            ring.color = A(color, visible * 0.7f * (1f - cycle));
            for (var i = 0; i < count; i++)
            {
                var p = Mathf.Repeat(t * 1.3f + i / (float)count, 1f);
                var a = angles[i] + t * 40f;
                var r = Mathf.Lerp(1.6f, 0.1f, p);
                var sr = c.parts[i + 2];
                Set(sr, pos + Dir(a) * r, -0.01f);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, a);
                sr.transform.localScale = new Vector3(0.25f * (1f - p) + 0.05f, 0.04f, 1f);
                sr.color = A(Color.Lerp(color, Color.white, p), visible * p);
            }
            return true;
        });
    }

    static void StopCharge(byte id)
    {
        if (!charges.TryGetValue(id, out var c)) return;
        c.dead = true;
        foreach (var p in c.parts) Release(p);
        charges.Remove(id);
        StopChargeSound(c);
    }

    static float nextJumboStep;

    static void StepJumbos()
    {
        if (Time.time < nextJumboStep) return;
        nextJumboStep = Time.time + 0.05f;
        foreach (var id in jumbos)
        {
            var pc = GameData.Instance?.GetPlayerById(id)?.Object;
            if (pc == null || pc.Data == null || pc.Data.IsDead) continue;
            var pos = pc.GetTruePosition();
            if (!jumboSteps.TryGetValue(id, out var step)) { jumboSteps[id] = (pos, 0f); continue; }
            var moved = Vector2.Distance(step.pos, pos);
            if (moved > 3f) moved = 0f;
            var walked = step.walked + moved;
            if (walked >= 1.1f)
            {
                walked = 0f;
                Play(SoundKind.Footstep, pos, jumboVolume);
                var me = PlayerControl.LocalPlayer;
                if (me != null && !pc.AmOwner && Vector2.Distance(me.GetTruePosition(), pos) < 2.5f) Shake(0.15f, 0.25f);
                if (CanSee(pos)) Particles(pos + Vector2.down * 0.1f, 4, new Color(0.7f, 0.65f, 0.55f), new Color(0.5f, 0.45f, 0.4f), Vector2.zero, 0.4f, 0.4f, Circle);
            }
            jumboSteps[id] = (pos, walked);
        }
    }

    static void ShowSoul(Vector2 pos, int colorId, float volume)
    {
        var color = Palette.PlayerColors[Mathf.Clamp(colorId, 0, Palette.PlayerColors.Length - 1)];
        var head = Make(Circle, color);
        var tail = Make(Circle, color);
        var halo = Make(Ring, Color.white);
        Play(SoundKind.Soul, null, volume);
        Animate(2.2f, new List<SpriteRenderer> { head, tail, halo }, (t, k) =>
        {
            var sway = Mathf.Sin(t * 3f) * 0.15f;
            var up = pos + new Vector2(sway, 0.3f + k * 1.6f);
            var alpha = Mathf.Clamp01(t / 0.3f) * (1f - k);
            Set(head, up, -0.02f);
            head.transform.localScale = Vector3.one * 0.55f;
            head.color = A(Color.Lerp(color, Color.white, 0.5f), 0.75f * alpha);
            Set(tail, up + new Vector2(-sway * 0.5f, -0.3f), -0.01f);
            tail.transform.localScale = new Vector3(0.35f, 0.6f, 1f);
            tail.color = A(color, 0.5f * alpha);
            Set(halo, up + Vector2.up * 0.35f, -0.03f);
            halo.transform.localScale = new Vector3(0.45f, 0.15f, 1f);
            halo.color = A(new Color(1f, 0.95f, 0.6f), alpha);
        });
    }

    static SpriteRenderer HudSprite(Sprite sprite, Color color)
    {
        var hud = HudManager.Instance;
        var go = new GameObject("ClientFx_Hud");
        go.transform.SetParent(hud.transform, false);
        go.layer = hud.FullScreen != null ? hud.FullScreen.gameObject.layer : hud.gameObject.layer;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        return sr;
    }

    static void ShowMeetingKill(byte id, float volume, byte style)
    {
        var meeting = MeetingHud.Instance;
        PlayerVoteArea area = null;
        if (meeting != null)
            foreach (var pva in meeting.playerStates) if (pva != null && pva.PlayerId == id) { area = pva; break; }
        // 照準が絞られるまで少し待ってから撃つ(ゲッサー)
        var aim = style == 0 ? 0f : 0.35f;
        var shot = false;
        void Fire()
        {
            shot = true;
            if (volume <= 0f) return;
            try { SoundManager.Instance.PlaySound(PlayerControl.LocalPlayer.KillSfx, false, 0.8f * volume); } catch { }
            Play(SoundKind.Gunshot, null, style == 2 ? volume * 0.7f : volume);
        }
        if (area == null) { Fire(); return; }
        var slashColor = style == 2 ? new Color(0.55f, 0.55f, 0.6f) : new Color(1f, 0.1f, 0.15f);
        var sightColor = style == 2 ? new Color(0.7f, 0.7f, 0.75f) : new Color(1f, 0.25f, 0.25f);
        SpriteRenderer Part(Sprite sprite, Color color, float z, float rot)
        {
            var go = new GameObject("ClientFx_MeetingKill");
            go.transform.SetParent(area.transform, false);
            go.layer = area.gameObject.layer;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            go.transform.localScale = Vector3.zero;
            return sr;
        }
        var parts = new List<SpriteRenderer>
        {
            Part(Square, slashColor, -5f, 15f),
            Part(Square, slashColor, -5.1f, -15f),
            Part(Circle, Color.white, -5.2f, 0f),
        };
        SpriteRenderer sight = null, lineH = null, lineV = null;
        if (style != 0)
        {
            sight = Part(Ring, sightColor, -5.3f, 0f);
            lineH = Part(Square, sightColor, -5.3f, 0f);
            lineV = Part(Square, sightColor, -5.3f, 90f);
            parts.Add(sight);
            parts.Add(lineH);
            parts.Add(lineV);
        }
        var smoke = new List<SpriteRenderer>();
        if (style == 2)
            for (var i = 0; i < 5; i++)
            {
                var puff = Part(Circle, new Color(0.5f, 0.5f, 0.5f), -5.4f, 0f);
                smoke.Add(puff);
                parts.Add(puff);
            }
        var started = Time.time;
        runningHud.Add(() =>
        {
            var t = Time.time - started;
            if (t >= aim + 1.4f || parts[0] == null)
            {
                foreach (var p in parts) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
                return false;
            }
            if (sight != null)
            {
                var close = Mathf.Clamp01(t / aim);
                var wobble = style == 2 ? new Vector3(Mathf.Sin(t * 37f), Mathf.Cos(t * 29f), 0f) * 0.12f * (1f - close * 0.5f) : Vector3.zero;
                var sightFade = 1f - Mathf.Clamp01((t - aim) / 0.3f);
                var size = Mathf.Lerp(2.4f, 0.75f, 1f - (1f - close) * (1f - close));
                sight.transform.localPosition = new Vector3(wobble.x, wobble.y, -5.3f);
                sight.transform.localScale = Vector3.one * size;
                sight.color = A(sightColor, close * sightFade);
                lineH.transform.localPosition = lineV.transform.localPosition = sight.transform.localPosition;
                lineH.transform.localScale = lineV.transform.localScale = new Vector3(size * 1.3f, 0.03f, 1f);
                lineH.color = lineV.color = A(sightColor, close * sightFade * 0.8f);
            }
            if (t < aim) return true;
            if (!shot) Fire();
            var st = t - aim;
            var cut = Mathf.Clamp01(st / 0.12f);
            var fade = 1f - Mathf.Clamp01((st - 0.9f) / 0.5f);
            for (var i = 0; i < 2; i++)
            {
                parts[i].transform.localScale = new Vector3(2.6f * cut, 0.09f, 1f);
                parts[i].color = A(slashColor, fade);
            }
            parts[2].transform.localScale = new Vector3(2.8f, 0.7f, 1f) * Mathf.Lerp(0.6f, 1.1f, Mathf.Clamp01(st / 0.25f));
            parts[2].color = A(Color.white, 0.6f * (1f - Mathf.Clamp01(st / 0.25f)));
            for (var i = 0; i < smoke.Count; i++)
            {
                var k = Mathf.Clamp01((st - i * 0.08f) / 1.0f);
                smoke[i].transform.localPosition = new Vector3(-0.9f + i * 0.45f + Mathf.Sin(st * 4f + i) * 0.1f, k * 0.5f, -5.4f);
                smoke[i].transform.localScale = Vector3.one * (k <= 0f ? 0f : Mathf.Lerp(0.25f, 0.6f, k));
                smoke[i].color = A(smoke[i].color, 0.6f * (1f - k));
            }
            return true;
        });
    }

    // 天秤会議: 金色の天秤が上から降りてきて、左右の皿がゆらゆら揺れてからつり合う
    // 特殊会議のあいだずっと出す飾り: 画面のふちが会議の色で光り、会議の名前が題に出る。
    // 天秤会議は、天秤の紋章が左右にゆれ、天秤にかけられた 2 人の札が光る
    static void StartMeetingTheme(string title, string colorCode, byte style, byte[] highlight)
    {
        var meeting = MeetingHud.Instance;
        var hud = HudManager.Instance;
        if (meeting == null || hud == null || hud.FullScreen == null) return;
        if (!ColorUtility.TryParseHtmlString(colorCode, out var color)) color = Color.white;
        var cam = Camera.main;
        var halfH = cam != null ? cam.orthographicSize : 3f;
        var halfW = cam != null ? halfH * cam.aspect : 5.33f;
        var basePos = hud.FullScreen.transform.localPosition;
        var z = basePos.z - 2.5f;
        var gold = new Color(1f, 0.82f, 0.3f);
        var parts = new List<SpriteRenderer>();
        SpriteRenderer Hud(Sprite s, Color c) { var sr = HudSprite(s, c); sr.name = "ClientFx_MeetingTheme"; parts.Add(sr); return sr; }
        SpriteRenderer OnPlate(Transform parent, Color c)
        {
            var go = new GameObject("ClientFx_MeetingThemePlate");
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = c;
            parts.Add(sr);
            return sr;
        }
        // 画面のふち
        var edges = new SpriteRenderer[4];
        for (var i = 0; i < 4; i++) edges[i] = Hud(Square, color);
        // 札の光る枠(上下左右の 4 本)
        var plates = new List<(SpriteRenderer[] lines, Vector2 size)>();
        foreach (var pva in meeting.playerStates)
        {
            if (pva == null || Array.IndexOf(highlight, pva.PlayerId) < 0 || pva.Background == null) continue;
            var bg = pva.Background;
            Vector2 size = bg.drawMode != SpriteDrawMode.Simple ? bg.size : (bg.sprite != null ? (Vector2)bg.sprite.bounds.size : new Vector2(2.7f, 0.65f));
            var lines = new SpriteRenderer[4];
            for (var i = 0; i < 4; i++) lines[i] = OnPlate(bg.transform, style == 1 ? gold : color);
            plates.Add((lines, size));
        }
        // 紋章: 天秤会議は天秤、それ以外は回る星
        var emblem = new List<SpriteRenderer>();
        if (style == 1)
        {
            for (var i = 0; i < 6; i++) emblem.Add(Hud(Square, gold));
            emblem.Add(Hud(Star, color));
            emblem.Add(Hud(Circle, color));
            emblem.Add(Hud(Circle, color));
        }
        else
        {
            emblem.Add(Hud(Star, color));
            emblem.Add(Hud(Ring, color));
        }
        var titleText = $"<color={colorCode}><b>{title.RemoveHtmlTags()}</b></color>";
        var started = Time.time;
        runningHud.Add(() =>
        {
            if (MeetingHud.Instance != meeting || meeting == null || parts.Count == 0 || parts[0] == null
                || meeting.state is MeetingHud.MeetingStates.Results or MeetingHud.MeetingStates.Proceeding)
            {
                foreach (var p in parts) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
                return false;
            }
            var t = Time.time - started;
            var appear = Mathf.Clamp01(t / 0.6f);
            var pulse = 0.55f + 0.25f * Mathf.Sin(t * 2.6f);
            void Put(SpriteRenderer sr, Vector2 p, Vector2 scale, float rot, Color c, float dz = 0f)
            {
                sr.transform.localPosition = new Vector3(basePos.x + p.x, basePos.y + p.y, z + dz);
                sr.transform.localScale = new Vector3(scale.x, scale.y, 1f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
                sr.color = A(c, c.a * appear);
            }
            const float edge = 0.09f;
            Put(edges[0], new Vector2(0f, halfH - edge * 0.5f), new Vector2(halfW * 2f, edge), 0f, A(color, pulse));
            Put(edges[1], new Vector2(0f, -halfH + edge * 0.5f), new Vector2(halfW * 2f, edge), 0f, A(color, pulse));
            Put(edges[2], new Vector2(-halfW + edge * 0.5f, 0f), new Vector2(edge, halfH * 2f), 0f, A(color, pulse));
            Put(edges[3], new Vector2(halfW - edge * 0.5f, 0f), new Vector2(edge, halfH * 2f), 0f, A(color, pulse));
            foreach (var (lines, size) in plates)
            {
                if (lines[0] == null) continue;
                var a = 0.6f + 0.4f * Mathf.Sin(t * 4f);
                const float w = 0.06f;
                void Line(SpriteRenderer sr, Vector2 p, Vector2 s)
                {
                    sr.transform.localPosition = new Vector3(p.x, p.y, -0.5f);
                    sr.transform.localScale = new Vector3(s.x, s.y, 1f);
                    sr.color = A(sr.color, a * appear);
                }
                Line(lines[0], new Vector2(0f, size.y * 0.5f), new Vector2(size.x + w, w));
                Line(lines[1], new Vector2(0f, -size.y * 0.5f), new Vector2(size.x + w, w));
                Line(lines[2], new Vector2(-size.x * 0.5f, 0f), new Vector2(w, size.y + w));
                Line(lines[3], new Vector2(size.x * 0.5f, 0f), new Vector2(w, size.y + w));
            }
            // 紋章は左上
            var center = new Vector2(-halfW + 0.9f, halfH - 0.85f);
            if (style == 1)
            {
                var tilt = 10f * Mathf.Sin(t * 1.4f);
                var pivot = center + new Vector2(0f, 0.32f);
                var arm = (Vector2)(Quaternion.Euler(0f, 0f, tilt) * Vector3.right) * 0.55f;
                Put(emblem[0], center, new Vector2(0.06f, 0.62f), 0f, gold);
                Put(emblem[1], center + Vector2.down * 0.31f, new Vector2(0.36f, 0.06f), 0f, gold);
                Put(emblem[2], pivot, new Vector2(1.15f, 0.045f), tilt, gold, -0.01f);
                for (var side = 0; side < 2; side++)
                {
                    var end = pivot + (side == 0 ? -arm : arm);
                    Put(emblem[3 + side], end + Vector2.down * 0.16f, new Vector2(0.02f, 0.32f), 0f, gold);
                    Put(emblem[7 + side], end + Vector2.down * 0.32f, new Vector2(0.38f, 0.1f), 0f, color, -0.01f);
                }
                Put(emblem[5], center + Vector2.down * 0.34f, new Vector2(0.2f, 0.04f), 0f, gold);
                Put(emblem[6], pivot + Vector2.up * 0.06f, new Vector2(0.18f, 0.18f), t * 60f, color, -0.02f);
            }
            else
            {
                Put(emblem[0], center, new Vector2(0.55f, 0.55f), t * 40f, color, -0.01f);
                Put(emblem[1], center, Vector2.one * (0.85f + 0.08f * Mathf.Sin(t * 3f)), 0f, A(color, 0.8f));
            }
            try { if (meeting.TitleText != null && meeting.TitleText.text != titleText) meeting.TitleText.text = titleText; } catch { }
            return true;
        });
    }

    static void PlayBalancer(string colorCode)
    {
        var hud = HudManager.Instance;
        if (hud == null || hud.FullScreen == null) return;
        if (!ColorUtility.TryParseHtmlString(colorCode, out var color)) color = Color.white;
        var basePos = hud.FullScreen.transform.localPosition;
        var gold = new Color(1f, 0.82f, 0.3f);
        var pillar = HudSprite(Square, gold);
        var foot = HudSprite(Square, gold);
        var beam = HudSprite(Square, gold);
        var jewel = HudSprite(Star, color);
        var chainL = HudSprite(Square, gold);
        var chainR = HudSprite(Square, gold);
        var panL = HudSprite(Circle, color);
        var panR = HudSprite(Circle, color);
        var glow = HudSprite(Circle, color);
        var parts = new List<SpriteRenderer> { glow, pillar, foot, beam, jewel, chainL, chainR, panL, panR };
        var started = Time.time;
        const float life = 2.6f;
        runningHud.Add(() =>
        {
            var t = Time.time - started;
            if (t >= life || parts[0] == null)
            {
                foreach (var p in parts) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
                return false;
            }
            var fade = 1f - Mathf.Clamp01((t - (life - 0.4f)) / 0.4f);
            var drop = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 3f);
            var center = new Vector2(basePos.x, basePos.y + 2.0f + (1f - drop) * 3f);
            var z = basePos.z - 3f;
            void Put(SpriteRenderer sr, Vector2 p, Vector2 scale, float rot, Color c, float dz = 0f)
            {
                sr.transform.localPosition = new Vector3(p.x, p.y, z + dz);
                sr.transform.localScale = new Vector3(scale.x, scale.y, 1f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
                sr.color = A(c, c.a * fade);
            }
            var tilt = 16f * Mathf.Sin(t * 5f) * Mathf.Exp(-t * 1.3f);
            var pivot = center + new Vector2(0f, 0.75f);
            var arm = (Vector2)(Quaternion.Euler(0f, 0f, tilt) * Vector3.right) * 1.5f;
            Put(glow, center + Vector2.up * 0.2f, new Vector2(3.6f, 2.4f), 0f, A(color, 0.25f + 0.1f * Mathf.Sin(t * 6f)), 0.05f);
            Put(pillar, center, new Vector2(0.12f, 1.5f), 0f, gold);
            Put(foot, center + Vector2.down * 0.75f, new Vector2(0.9f, 0.12f), 0f, gold);
            Put(beam, pivot, new Vector2(3.1f, 0.09f), tilt, gold, -0.01f);
            Put(jewel, pivot + Vector2.up * 0.12f, new Vector2(0.38f, 0.38f), t * 90f, color, -0.02f);
            for (var side = 0; side < 2; side++)
            {
                var end = pivot + (side == 0 ? -arm : arm);
                var pan = end + Vector2.down * 0.75f;
                Put(side == 0 ? chainL : chainR, end + Vector2.down * 0.37f, new Vector2(0.03f, 0.75f), 0f, gold);
                Put(side == 0 ? panL : panR, pan, new Vector2(0.9f, 0.22f), 0f, color, -0.01f);
            }
            return true;
        });
    }

    static Bounds? mapBounds;
    static int mapBoundsFor;

    // その向きに進んだ時、マップの外側の端までの長さ(壁を貫通するビーム用)
    public static float MapReach(Vector2 from, Vector2 dir, float max = 100f)
    {
        var ship = ShipStatus.Instance;
        if (ship == null) return max;
        if (mapBounds == null || mapBoundsFor != ship.GetInstanceID())
        {
            var any = false;
            var b = new Bounds();
            foreach (var room in ship.AllRooms)
            {
                var area = room?.roomArea;
                if (area == null) continue;
                if (!any) { b = area.bounds; any = true; }
                else b.Encapsulate(area.bounds);
            }
            if (!any) return max;
            b.Expand(3f);
            mapBounds = b;
            mapBoundsFor = ship.GetInstanceID();
        }
        var bb = mapBounds.Value;
        var reach = max;
        if (Mathf.Abs(dir.x) > 0.0001f)
        {
            var tx = ((dir.x > 0f ? bb.max.x : bb.min.x) - from.x) / dir.x;
            if (tx > 0f) reach = Mathf.Min(reach, tx);
        }
        if (Mathf.Abs(dir.y) > 0.0001f)
        {
            var ty = ((dir.y > 0f ? bb.max.y : bb.min.y) - from.y) / dir.y;
            if (ty > 0f) reach = Mathf.Min(reach, ty);
        }
        return Mathf.Clamp(reach, 5f, max);
    }

    // テレポート: 消えた所と現れた所に、光の渦が出る(テレポーター・テレポートキラーなど、遠くへ飛ばす能力すべて)
    static float quietUntil;
    static float soundlessUntil;
    // 湧く時(会議の後のランダムスポーンなど)は、光の渦だけ出して音は鳴らさない
    public static void SilentTeleport() => soundlessUntil = Mathf.Max(soundlessUntil, Time.time + 0.05f);

    public static void MuteTeleport() => quietUntil = Mathf.Max(quietUntil, Time.time + 0.05f);
    public static void Teleport(PlayerControl pc, Vector2 from, Vector2 to)
    {
        if (!CanSend || !TeleportOn || pc == null || pc.notRealPlayer) return;
        if (!GameStates.IsInTask || GameStates.Intro || GameStates.CalledMeeting || Time.time < quietUntil) return;
        if (Vector2.Distance(from, to) < 3f) return;
        var volume = (byte)Mathf.RoundToInt((SoundOn && Time.time >= soundlessUntil ? Volume : 0f) * 100f);
        Send(Kind.Teleport, w =>
        {
            NetHelpers.WriteVector2(from, w);
            NetHelpers.WriteVector2(to, w);
            w.Write(volume);
        });
        ShowTeleport(from, to, volume / 100f);
    }

    static void ShowTeleport(Vector2 from, Vector2 to, float volume)
    {
        for (var k = 0; k < 2; k++)
        {
            var pos = k == 0 ? from : to;
            if (!CanSee(pos)) continue;
            Warp(pos, k == 1);
            Play(SoundKind.Sparkle, pos, volume * 0.8f);
        }
    }

    // 光の渦(テレポート・テレポートキル)。appear なら外から縮んで現れ、そうでなければ広がって消える
    static void Warp(Vector2 pos, bool appear)
    {
        var purple = new Color(0.65f, 0.4f, 1f);
        {
            var ring1 = Make(Ring, purple);
            var ring2 = Make(Ring, Color.white);
            var core = Make(Circle, purple);
            Set(ring1, pos + Vector2.up * 0.3f, -0.01f);
            Set(ring2, pos + Vector2.up * 0.3f, -0.01f);
            Set(core, pos + Vector2.up * 0.3f, -0.02f);
            Particles(pos, 8, purple, Color.white, Vector2.up * 1.2f, 0.4f, 0.7f, Square);
            Animate(0.6f, new List<SpriteRenderer> { ring1, ring2, core }, (t, f) =>
            {
                var s = appear ? f : 1f - f;
                ring1.transform.localScale = new Vector3(1.2f, 0.5f, 1f) * Mathf.Lerp(0.3f, 1.4f, appear ? 1f - f : f);
                ring1.transform.rotation = Quaternion.Euler(0f, 0f, t * 400f);
                ring1.color = A(purple, 1f - f);
                ring2.transform.localScale = new Vector3(0.8f, 0.35f, 1f) * Mathf.Lerp(1.2f, 0.2f, f);
                ring2.color = A(Color.white, (1f - f) * 0.8f);
                core.transform.localScale = new Vector3(0.6f, 1.1f, 1f) * (0.3f + 0.7f * Mathf.Sin(s * Mathf.PI));
                core.color = A(Color.Lerp(purple, Color.white, 0.4f), (1f - f) * 0.8f);
            });
        }
    }

    // 誤射: 銃が暴発して、灰色の煙と火花が上がる
    static void Backfire(Vector2 pos)
    {
        var flash = Make(Circle, new Color(1f, 0.85f, 0.5f));
        Set(flash, pos + Vector2.up * 0.2f, -0.02f);
        Particles(pos, 10, new Color(0.45f, 0.45f, 0.45f), new Color(0.75f, 0.75f, 0.75f), Vector2.up * 1.0f, 0.35f, 1.0f, Circle);
        Particles(pos, 6, new Color(1f, 0.8f, 0.3f), Color.white, Vector2.zero, 0.7f, 0.35f, Square);
        Animate(0.3f, new List<SpriteRenderer> { flash }, (t, k) =>
        {
            flash.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.2f, k);
            flash.color = A(flash.color, 1f - k);
        });
    }

    // 後追い: ハートが割れて、左右に落ちていく
    static void Heartbreak(Vector2 pos)
    {
        var pink = new Color(1f, 0.45f, 0.7f);
        var left = Make(Heart, pink);
        var right = Make(Heart, pink);
        var crack = Make(Square, Color.white);
        Animate(1.2f, new List<SpriteRenderer> { left, right, crack }, (t, k) =>
        {
            var split = Mathf.Clamp01((t - 0.3f) / 0.9f);
            var top = pos + Vector2.up * (0.9f + Mathf.Clamp01(t / 0.3f) * 0.2f);
            var fall = split * split * 1.2f;
            Set(left, top + new Vector2(-split * 0.35f, -fall), -0.02f);
            Set(right, top + new Vector2(split * 0.35f, -fall), -0.02f);
            left.transform.rotation = Quaternion.Euler(0f, 0f, split * 25f);
            right.transform.rotation = Quaternion.Euler(0f, 0f, -split * 25f);
            left.transform.localScale = right.transform.localScale = Vector3.one * 0.6f;
            left.color = right.color = A(pink, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
            Set(crack, top, -0.03f);
            crack.transform.rotation = Quaternion.Euler(0f, 0f, 80f);
            crack.transform.localScale = new Vector3(0.6f * Mathf.Clamp01((t - 0.2f) / 0.1f), 0.05f, 1f);
            crack.color = A(Color.white, split > 0f ? 1f - split : 1f);
        });
    }

    static void Jaws(Vector2 pos)
    {
        var metal = new Color(0.75f, 0.75f, 0.8f);
        var top = Make(Square, metal);
        var bottom = Make(Square, metal);
        var spark = Make(Circle, Color.white);
        Set(spark, pos, -0.02f);
        Animate(0.7f, new List<SpriteRenderer> { top, bottom, spark }, (t, k) =>
        {
            var close = Mathf.Clamp01(t / 0.1f);
            var gap = Mathf.Lerp(0.6f, 0.05f, close);
            Set(top, pos + Vector2.up * gap, -0.01f);
            Set(bottom, pos + Vector2.down * gap, -0.01f);
            top.transform.localScale = bottom.transform.localScale = new Vector3(1.1f, 0.12f, 1f);
            top.color = bottom.color = A(metal, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
            spark.transform.localScale = Vector3.one * (close >= 1f ? Mathf.Lerp(0.8f, 0.1f, k) : 0f);
            spark.color = A(Color.white, 1f - k);
        });
    }

    static void Vortex(Vector2 pos)
    {
        var dark = new Color(0.15f, 0.05f, 0.25f);
        var hole = Make(Circle, dark);
        var ring = Make(Ring, new Color(0.5f, 0.2f, 0.7f));
        Set(hole, pos, 0.01f);
        Set(ring, pos);
        Animate(0.9f, new List<SpriteRenderer> { hole, ring }, (t, k) =>
        {
            var open = Mathf.Sin(k * Mathf.PI);
            hole.transform.localScale = new Vector3(1.3f, 0.7f, 1f) * open;
            hole.color = A(dark, 0.9f * open);
            ring.transform.localScale = new Vector3(1.6f, 0.8f, 1f) * Mathf.Lerp(1.2f, 0.1f, k);
            ring.transform.rotation = Quaternion.Euler(0f, 0f, -t * 500f);
            ring.color = A(ring.color, 1f - k);
        });
    }

    static void Squash(Vector2 pos)
    {
        var dust = new Color(0.7f, 0.65f, 0.55f);
        var slab = Make(Square, new Color(0.35f, 0.35f, 0.4f));
        Particles(pos, 10, dust, new Color(0.5f, 0.45f, 0.4f), Vector2.zero, 0.9f, 0.6f, Circle);
        Animate(0.6f, new List<SpriteRenderer> { slab }, (t, k) =>
        {
            var fall = Mathf.Clamp01(t / 0.12f);
            Set(slab, pos + Vector2.up * Mathf.Lerp(2.5f, 0.2f, fall), -0.02f);
            slab.transform.localScale = new Vector3(1.4f, 0.35f, 1f);
            slab.color = A(slab.color, 1f - Mathf.Clamp01((k - 0.5f) / 0.5f));
        });
    }

    static void Slash(Vector2 pos, Color color)
    {
        var a = Make(Square, color);
        var b = Make(Square, color);
        a.transform.rotation = Quaternion.Euler(0f, 0f, 35f);
        b.transform.rotation = Quaternion.Euler(0f, 0f, -35f);
        Set(a, pos + Vector2.up * 0.3f, -0.02f);
        Set(b, pos + Vector2.up * 0.3f, -0.02f);
        Animate(0.6f, new List<SpriteRenderer> { a, b }, (t, k) =>
        {
            var cutA = Mathf.Clamp01(t / 0.08f);
            var cutB = Mathf.Clamp01((t - 0.08f) / 0.08f);
            a.transform.localScale = new Vector3(1.4f * cutA, 0.07f, 1f);
            b.transform.localScale = new Vector3(1.4f * cutB, 0.07f, 1f);
            a.color = b.color = A(color, 1f - Mathf.Clamp01((k - 0.5f) / 0.5f));
        });
    }

    // ===== 音(その場で作る) =====

    const int Rate = 22050;
    static readonly Dictionary<SoundKind, AudioClip> clips = new();

    static void Play(SoundKind sound, Vector2? at, float volume)
    {
        try
        {
            if (volume <= 0.01f || SoundManager.Instance == null) return;
            if (at.HasValue && PlayerControl.LocalPlayer != null)
            {
                var from = PlayerControl.LocalPlayer.GetTruePosition();
                var d = Vector2.Distance(from, at.Value);
                volume *= Mathf.Clamp01(1.2f - d / (sound == SoundKind.SniperShot ? 35f : 10f));
                if (d > 0.3f && sound is not (SoundKind.NovaCharge or SoundKind.NovaAlarm or SoundKind.NovaFire or SoundKind.ChargeLoop)
                    && PhysicsHelpers.AnyNonTriggersBetween(from, (at.Value - from) / d, d, Constants.ShipOnlyMask))
                    volume *= 0.3f;
            }
            if (volume <= 0.01f) return;
            if (!clips.TryGetValue(sound, out var clip) || clip == null)
                clips[sound] = clip = Build(sound);
            _ = SoundManager.Instance.PlaySound(clip, false, volume);
        }
        catch (Exception e) { Logger.Warn($"音を鳴らせませんでした: {e.Message}", "ClientEffects"); }
    }

    static float Sin(float phase) => Mathf.Sin(phase * Mathf.PI * 2f);

    static AudioClip Build(SoundKind sound)
    {
        var length = sound switch
        {
            SoundKind.NovaCharge => 1.3f,
            SoundKind.NovaAlarm => 0.24f,
            SoundKind.NovaFire => 1.5f,
            SoundKind.Heartbeat => 0.6f,
            SoundKind.MeetingSting => 1.6f,
            SoundKind.Boom => 1.4f,
            SoundKind.Gunshot => 0.5f,
            SoundKind.Flame => 0.9f,
            SoundKind.Sparkle => 0.9f,
            SoundKind.Chime => 1.2f,
            SoundKind.Swing => 0.35f,
            SoundKind.Push => 0.8f,
            SoundKind.Rocket => 1.8f,
            SoundKind.Footstep => 0.4f,
            SoundKind.Soul => 1.6f,
            SoundKind.ChargeLoop => 1f,
            SoundKind.SniperEquip => 0.45f,
            SoundKind.SniperShot => 1.8f,
            SoundKind.Clank => 0.9f,
            _ => 0.32f,
        };
        var n = Mathf.CeilToInt(length * Rate);
        var data = new float[n];
        var rng = new System.Random(1234 + (int)sound);
        float p1 = 0f, p2 = 0f, p3 = 0f, p4 = 0f, low = 0f, high = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = (float)i / Rate;
            var k = t / length;
            var noise = (float)rng.NextDouble() * 2f - 1f;
            float s;
            switch (sound)
            {
                case SoundKind.NovaCharge:
                    {
                        var f = Mathf.Lerp(180f, 980f, k * k);
                        p1 += f / Rate;
                        p2 += f * 1.5f / Rate;
                        var env = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((length - t) / 0.15f);
                        var wobble = 0.75f + 0.25f * Sin(t * Mathf.Lerp(12f, 40f, k));
                        s = (Sin(p1) * 0.6f + Sin(p2) * 0.25f) * env * wobble * 0.5f;
                        break;
                    }
                case SoundKind.SniperEquip:
                    {
                        // ボルトを引いて戻す、金属の 2 回のカチャッ
                        var a = t;
                        var b = t - 0.2f;
                        var click = 0f;
                        if (a >= 0f) click += Mathf.Exp(-a * 90f) * (noise * 0.7f + Sin(a * 2600f) * 0.5f);
                        if (b >= 0f) click += Mathf.Exp(-b * 70f) * (noise * 0.8f + Sin(b * 3100f) * 0.6f);
                        s = click * 0.55f;
                        break;
                    }
                case SoundKind.SniperShot:
                    {
                        // 乾いた破裂音のあと、遠くの壁に跳ね返るこだまが何度か続く
                        high += (noise - high) * 0.6f;
                        low += (noise - low) * 0.05f;
                        var crack = Mathf.Exp(-t * 45f) * high * 1.2f + Mathf.Exp(-t * 9f) * low * 3f;
                        var echo = 0f;
                        for (var e = 1; e <= 3; e++)
                        {
                            var et = t - e * 0.28f;
                            if (et >= 0f) echo += Mathf.Exp(-et * 12f) * low * 2.2f / (e + 1);
                        }
                        s = (crack + echo) * Mathf.Clamp01((length - t) / 0.3f) * 0.6f;
                        break;
                    }
                case SoundKind.Clank:
                    {
                        // 鉄の鎖が巻きつく、にごった金属の響き
                        p1 += 520f / Rate;
                        p2 += 1340f / Rate;
                        p3 += 2210f / Rate;
                        var ring = Sin(p1) * 0.5f + Sin(p2) * 0.3f + Sin(p3) * 0.2f;
                        var rattle = Mathf.Exp(-Mathf.Repeat(t, 0.11f) * 40f) * noise * (t < 0.45f ? 0.5f : 0f);
                        s = (ring * Mathf.Exp(-t * 5f) + rattle) * 0.5f;
                        break;
                    }
                case SoundKind.ChargeLoop:
                    {
                        // 1 秒ちょうどで途切れずにつながる周波数だけを使う(ループ用)
                        p1 += 110f / Rate;
                        p2 += 220f / Rate;
                        p3 += 330f / Rate;
                        p4 += 880f / Rate;
                        var buzz = 0.8f + 0.2f * Sin(t * 8f);
                        s = (Sin(p1) * 0.45f + Sin(p2) * 0.3f + Sin(p3) * 0.15f + Sin(p4) * 0.06f) * buzz * 0.45f;
                        break;
                    }
                case SoundKind.NovaAlarm:
                    {
                        p1 += (t < length * 0.5f ? 1480f : 1110f) / Rate;
                        var tri = Mathf.PingPong(p1 * 2f, 1f) * 2f - 1f;
                        var local = t % (length * 0.5f);
                        s = tri * Mathf.Clamp01(local / 0.005f) * Mathf.Clamp01((length * 0.5f - local) / 0.02f) * 0.35f;
                        break;
                    }
                case SoundKind.NovaFire:
                    {
                        p1 += Mathf.Lerp(150f, 38f, Mathf.Sqrt(k)) / Rate;
                        p2 += Mathf.Lerp(2400f, 500f, Mathf.Clamp01(t / 0.18f)) / Rate;
                        low += (noise - low) * 0.08f;
                        var env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((length - t) / 0.2f);
                        s = (Sin(p1) * Mathf.Exp(-t * 2.2f) * 0.7f + Sin(p2) * Mathf.Exp(-t * 14f) * 0.5f + low * 2.5f * Mathf.Exp(-t * 2.8f)) * env * 0.6f;
                        break;
                    }
                case SoundKind.Heartbeat:
                    {
                        var b1 = t;
                        var b2 = t - 0.28f;
                        p1 += Mathf.Lerp(75f, 45f, Mathf.Clamp01(b1 / 0.15f)) / Rate;
                        var e1 = b1 < 0.005f ? b1 / 0.005f : Mathf.Exp(-b1 * 22f);
                        var e2 = b2 < 0f ? 0f : b2 < 0.005f ? b2 / 0.005f : Mathf.Exp(-b2 * 22f);
                        s = Sin(p1) * (e1 + e2 * 0.75f) * 0.9f;
                        break;
                    }
                case SoundKind.MeetingSting:
                    {
                        p1 += 523.25f / Rate;
                        p2 += 659.25f / Rate;
                        p3 += 783.99f / Rate;
                        p4 += 130.81f / Rate;
                        var brass = (Sin(p1) + 0.5f * Sin(p1 * 2f)) + (Sin(p2) + 0.5f * Sin(p2 * 2f)) + (Sin(p3) + 0.4f * Sin(p3 * 2f));
                        var env = Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t * 2.2f);
                        var hit = noise * Mathf.Exp(-t * 30f) * 0.4f;
                        s = (brass * 0.16f + Sin(p4) * 0.35f) * env + hit;
                        break;
                    }
                case SoundKind.Boom:
                    {
                        p1 += Mathf.Lerp(90f, 30f, Mathf.Sqrt(k)) / Rate;
                        low += (noise - low) * 0.05f;
                        high += (noise - high) * 0.5f;
                        s = (Sin(p1) * Mathf.Exp(-t * 3f) * 0.8f + low * 3f * Mathf.Exp(-t * 2.5f) + high * Mathf.Exp(-t * 25f) * 0.6f) * Mathf.Clamp01((length - t) / 0.2f) * 0.7f;
                        break;
                    }
                case SoundKind.Gunshot:
                    {
                        high += (noise - high) * 0.7f;
                        low += (noise - low) * 0.1f;
                        p1 += 3200f / Rate;
                        s = (high * Mathf.Exp(-t * 60f) + low * 1.5f * Mathf.Exp(-t * 8f) + Sin(p1) * Mathf.Exp(-t * 120f) * 0.5f) * 0.7f;
                        break;
                    }
                case SoundKind.Flame:
                    {
                        var cutoff = Mathf.Lerp(0.05f, 0.35f, Mathf.Sin(k * Mathf.PI));
                        low += (noise - low) * cutoff;
                        var env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                        var crackle = rng.NextDouble() < 0.002 ? 1f : 0f;
                        s = (low * 1.4f + crackle * 0.6f) * env * 0.6f;
                        break;
                    }
                case SoundKind.Sparkle:
                    {
                        var step = Mathf.FloorToInt(t / 0.07f);
                        var local = t - step * 0.07f;
                        var f = 1200f + (step * 337 % 7) * 180f + k * 600f;
                        p1 += f / Rate;
                        s = Sin(p1) * Mathf.Exp(-local * 30f) * (1f - k) * 0.35f;
                        break;
                    }
                case SoundKind.Chime:
                    {
                        p1 += 880f / Rate;
                        p2 += 1318.5f / Rate;
                        p3 += 1760f / Rate;
                        var e2 = t < 0.15f ? 0f : Mathf.Exp(-(t - 0.15f) * 3f);
                        s = (Sin(p1) * Mathf.Exp(-t * 3f) + Sin(p2) * e2 * 0.8f + Sin(p3) * Mathf.Exp(-t * 6f) * 0.3f) * 0.3f;
                        break;
                    }
                case SoundKind.Swing:
                    {
                        low += (noise - low) * Mathf.Lerp(0.05f, 0.6f, Mathf.Sin(k * Mathf.PI));
                        s = low * Mathf.Sin(k * Mathf.PI) * 1.2f;
                        break;
                    }
                case SoundKind.Push:
                    {
                        p1 += Mathf.Lerp(900f, 180f, k) / Rate;
                        low += (noise - low) * 0.2f;
                        s = (Sin(p1) * 0.35f * (1f - k) + low * Mathf.Exp(-t * 6f) * 0.8f) * Mathf.Clamp01(t / 0.02f) * 0.7f;
                        break;
                    }
                case SoundKind.Rocket:
                    {
                        low += (noise - low) * Mathf.Lerp(0.08f, 0.4f, k);
                        p1 += Mathf.Lerp(80f, 400f, k) / Rate;
                        s = (low * 1.8f + Sin(p1) * 0.3f) * Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((length - t) / 0.6f) * 0.6f;
                        break;
                    }
                case SoundKind.Footstep:
                    {
                        p1 += Mathf.Lerp(70f, 35f, k) / Rate;
                        low += (noise - low) * 0.03f;
                        s = (Sin(p1) * 0.9f + low * 2f) * Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t * 11f);
                        break;
                    }
                case SoundKind.Soul:
                    {
                        p1 += Mathf.Lerp(520f, 880f, k) / Rate;
                        p2 += Mathf.Lerp(523f, 886f, k) / Rate;
                        s = (Sin(p1) + Sin(p2)) * Mathf.Sin(k * Mathf.PI) * 0.15f;
                        break;
                    }
                default:
                    {
                        p1 += 1900f / Rate;
                        p2 += Mathf.Lerp(140f, 70f, k) / Rate;
                        s = (noise * Mathf.Exp(-t * 45f) * 0.8f + Sin(p1) * Mathf.Exp(-t * 80f) * 0.6f + Sin(p2) * Mathf.Exp(-t * 14f) * 0.8f) * 0.6f;
                        break;
                    }
            }
            data[i] = Mathf.Clamp(s, -1f, 1f);
        }
        var clip = AudioClip.Create("ClientFx_" + sound, n, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags |= HideFlags.HideAndDontSave;
        return clip;
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class ClientEffectsUpdatePatch
{
    static void Postfix()
    {
        try { ClientEffects.Update(); }
        catch (Exception e) { Logger.Warn(e.Message, "ClientEffects"); }
    }
}


