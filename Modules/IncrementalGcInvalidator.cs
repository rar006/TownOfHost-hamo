using System;
using System.Runtime.InteropServices;

namespace TownOfHost.Modules;

/// <summary>
/// Unity(il2cpp)の「インクリメンタルGC」を起動時に無効化する(Windowsのみ)。
///
/// インクリメンタルGCが有効だと、MOD(Il2CppInterop)からゲーム側のオブジェクトの参照を書き換えたとき
/// 書き込みバリアが無い場合があり、まだ使っているオブジェクトがGCに回収されてしまう。
/// その結果、部屋から退出するとき(シーン切替でGCが走るとき)などに
/// 0xC0000005 で即落ちする(try/catch では止められない)。
/// インクリメンタルを切ると、この種類のクラッシュがまとめて起きなくなる。性能への影響はほぼ無い。
///
/// EndKnot(EndKnot.Modules.IncrementalGcInvalidator)の方法を参考にしている:
/// il2cpp_gc_is_incremental() が読んでいるグローバル変数の場所を機械語から探し、0 を書き込む。
/// 見つからない・書き込めない・書いても変わらない場合は何もしない(元に戻す)。
/// </summary>
public static class IncrementalGcInvalidator
{
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private const string Tag = "IncrementalGc";
    private const uint MemCommit = 0x1000, PageGuard = 0x100;
    private const uint WritableMask = 0x04 | 0x08 | 0x40 | 0x80;          // RW / WriteCopy / ExecRW / ExecWriteCopy
    private const uint ReadableMask = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;
    private static bool applied;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string proc);
    [DllImport("kernel32.dll")] private static extern UIntPtr VirtualQuery(IntPtr addr, out MemoryBasicInformation info, UIntPtr length);
    [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl, EntryPoint = "il2cpp_gc_is_incremental")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsIncrementalNative();

    public static void ApplyIfConfigured()
    {
        if (applied) return;
        applied = true;
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            bool incremental;
            try { incremental = IsIncrementalNative(); }
            catch (Exception e) { Logger.Warn($"il2cpp_gc_is_incremental unavailable ({e.Message}) - skipped", Tag); return; }

            if (Main.DisableIncrementalGc != null && !Main.DisableIncrementalGc.Value)
            {
                Logger.Info($"incremental={incremental} (left as-is; DisableIncrementalGc is off)", Tag);
                return;
            }
            if (!incremental) { Logger.Info("incremental GC already off", Tag); return; }
            Logger.Info("disabling incremental GC: " + Disable(), Tag);
        }
        catch (Exception e)
        {
            Logger.Warn($"unexpected failure ({e.GetType().Name}: {e.Message}) - GC mode untouched", Tag);
        }
    }

    private static unsafe string Disable()
    {
        var addr = DiscoverFlagAddress();
        if (addr == IntPtr.Zero) return "FAILED (flag address not found on this build)";
        if (!HasProtection(addr, WritableMask)) return $"FAILED (0x{addr.ToInt64():X} not writable)";
        var flag = (int*)addr;
        int before = *flag;
        if ((before != 0) != IsIncrementalNative()) return $"FAILED (sanity check mismatch: value={before})";
        *flag = 0;
        bool still;
        try { still = IsIncrementalNative(); }
        catch (Exception e) { *flag = before; return $"FAILED (verify threw: {e.Message}; rolled back)"; }
        if (still) { *flag = before; return "FAILED (still incremental; rolled back)"; }
        return $"OK (flag at 0x{addr.ToInt64():X}, {before} -> 0)";
    }

    private static unsafe bool HasProtection(IntPtr addr, uint mask)
    {
        if (VirtualQuery(addr, out var info, (UIntPtr)sizeof(MemoryBasicInformation)) == UIntPtr.Zero) return false;
        if (info.State != MemCommit || (info.Protect & PageGuard) != 0) return false;
        return (info.Protect & mask) != 0;
    }

    private static bool CanRead(IntPtr addr, int length)
        => addr != IntPtr.Zero && length > 0
           && HasProtection(addr, ReadableMask) && HasProtection(addr + length - 1, ReadableMask);

    private static IntPtr DiscoverFlagAddress()
    {
        var module = GetModuleHandleW("GameAssembly.dll");
        if (module == IntPtr.Zero) return IntPtr.Zero;
        var proc = GetProcAddress(module, "il2cpp_gc_is_incremental");
        if (proc == IntPtr.Zero) return IntPtr.Zero;
        var func = ResolveJmpThunk(proc);
        var target = FindGlobalReadTarget(func);
        if (target != IntPtr.Zero) return target;
        var call = FindFirstCallTarget(func);
        return call == IntPtr.Zero ? IntPtr.Zero : FindGlobalReadTarget(ResolveJmpThunk(call));
    }

    /// <summary>jmp rel32 (E9) の中継を最大5回たどる</summary>
    private static unsafe IntPtr ResolveJmpThunk(IntPtr addr)
    {
        for (int i = 0; i < 5; i++)
        {
            if (!CanRead(addr, 5)) return IntPtr.Zero;
            var p = (byte*)addr;
            if (*p != 0xE9) break;
            addr = (IntPtr)(addr.ToInt64() + 5 + *(int*)(p + 1));
        }
        return addr;
    }

    /// <summary>先頭付近の最初の call rel32 (E8) の行き先</summary>
    private static unsafe IntPtr FindFirstCallTarget(IntPtr func)
    {
        if (!CanRead(func, 24)) return IntPtr.Zero;
        var p = (byte*)func;
        for (int i = 0; i < 20; i++)
            if (p[i] == 0xE8) return (IntPtr)(func.ToInt64() + i + 5 + *(int*)(p + i + 1));
        return IntPtr.Zero;
    }

    /// <summary>mov eax,[rip+disp32] (8B 05) が読んでいるグローバル変数のアドレス</summary>
    private static unsafe IntPtr FindGlobalReadTarget(IntPtr func)
    {
        if (!CanRead(func, 24)) return IntPtr.Zero;
        var p = (byte*)func;
        for (int i = 0; i < 18; i++)
        {
            if (p[i] == 0x8B && p[i + 1] == 0x05)
            {
                int disp = *(int*)(p + i + 2);
                return Environment.Is64BitProcess ? (IntPtr)(func.ToInt64() + i + 6 + disp) : (IntPtr)disp;
            }
            if (!Environment.Is64BitProcess && p[i] == 0xA1) return (IntPtr)(*(int*)(p + i + 1));
        }
        return IntPtr.Zero;
    }
}
