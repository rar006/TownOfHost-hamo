using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace TownOfHost.Modules.Recorder;

// 録画の音の取り込み(WASAPI)。どれも 48kHz・ステレオ・16bit で受け取る
// - Game : Among Us の音だけ(このプロセスの音)
// - Other: Among Us 以外の PC の音(Discord の通話など)
// - Mic  : いつものマイク
public enum AudioSourceKind { Game, Other, Mic }

sealed class AudioSource : IDisposable
{
    public readonly AudioSourceKind Kind;
    public float Volume;
    // 取り込んだ音(左右交互)。混ぜる側が取り出す
    readonly Queue<short> buffer = new();
    readonly object gate = new();
    Thread thread;
    volatile bool running;
    readonly Action<string> log;
    readonly AutoResetEvent ready = new(false);

    public AudioSource(AudioSourceKind kind, float volume, Action<string> log)
    {
        Kind = kind;
        Volume = volume;
        this.log = log;
    }

    public void Start()
    {
        running = true;
        thread = new Thread(Run) { IsBackground = true, Name = $"TOH-hamo Rec {Kind}" };
        thread.Start();
    }

    // 最大 frames 個(左右で 1 個)を out に足し込む。足りない分は 0(無音)のまま
    public void MixInto(float[] mix, int frames)
    {
        lock (gate)
        {
            // 遅れて溜まりすぎたら(0.3 秒より多い)、古い分を捨てて映像と合わせる
            var limit = Mp4Writer.AudioRate * Mp4Writer.AudioChannels * 3 / 10;
            while (buffer.Count > limit + frames * Mp4Writer.AudioChannels) buffer.Dequeue();
            var n = Math.Min(frames * Mp4Writer.AudioChannels, buffer.Count);
            for (var i = 0; i < n; i++) mix[i] += buffer.Dequeue() / 32768f * Volume;
        }
    }

    void Run()
    {
        IAudioClient client = null;
        IAudioCaptureClient capture = null;
        var format = IntPtr.Zero;
        try
        {
            client = Activate();
            format = Wasapi.MakeFormat();
            // 音が来たら起こしてもらう(プロセスの音の取り込みは、この形でないと始められない)
            var flags = Wasapi.AUTOCONVERTPCM | Wasapi.SRC_DEFAULT_QUALITY | Wasapi.EVENTCALLBACK;
            if (Kind != AudioSourceKind.Mic) flags |= Wasapi.LOOPBACK;
            Wasapi.Check(client.Initialize(0, flags, 2_000_000, 0, format, IntPtr.Zero), "IAudioClient.Initialize");
            Wasapi.Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()), "SetEventHandle");
            var iid = typeof(IAudioCaptureClient).GUID;
            Wasapi.Check(client.GetService(ref iid, out var service), "GetService");
            capture = (IAudioCaptureClient)service;
            Wasapi.Check(client.Start(), "IAudioClient.Start");
            log?.Invoke($"録画の音({Kind})を取り込み始めました");
            while (running)
            {
                ready.WaitOne(20);
                while (running && capture.GetNextPacketSize(out var packet) >= 0 && packet > 0)
                {
                    if (capture.GetBuffer(out var data, out var frames, out var bufferFlags, out _, out _) < 0) break;
                    var count = (int)frames * Mp4Writer.AudioChannels;
                    var silent = (bufferFlags & 2) != 0 || data == IntPtr.Zero;
                    var samples = new short[count];
                    if (!silent) Marshal.Copy(data, samples, 0, count);
                    capture.ReleaseBuffer(frames);
                    lock (gate) foreach (var s in samples) buffer.Enqueue(s);
                }
            }
            client.Stop();
        }
        catch (Exception e)
        {
            log?.Invoke($"録画の音({Kind})を取り込めませんでした: {e.Message}");
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
        }
    }

    IAudioClient Activate()
    {
        if (Kind == AudioSourceKind.Mic)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                Wasapi.Check(enumerator.GetDefaultAudioEndpoint(1, 0, out var device), "GetDefaultAudioEndpoint");
                var iid = typeof(IAudioClient).GUID;
                Wasapi.Check(device.Activate(ref iid, 23, IntPtr.Zero, out var obj), "IMMDevice.Activate");
                Marshal.ReleaseComObject(device);
                return (IAudioClient)obj;
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        // このプロセス(とその子)の音だけ / それ以外の音だけ を取り込む(Windows 10 2004 以降)
        return Wasapi.ActivateProcessLoopback((uint)Environment.ProcessId, Kind == AudioSourceKind.Other);
    }

    public void Dispose()
    {
        running = false;
        thread?.Join(1000);
    }
}

// 複数の音を、時間に合わせて混ぜる(鳴っていない時は何も来ないので、足りない分は無音で埋める)
sealed class AudioMixer : IDisposable
{
    readonly List<AudioSource> sources = new();
    long writtenFrames;

    public bool HasSources => sources.Count > 0;

    public AudioMixer(IEnumerable<(AudioSourceKind kind, float volume)> wanted, Action<string> log)
    {
        foreach (var (kind, volume) in wanted)
        {
            if (volume <= 0f) continue;
            var s = new AudioSource(kind, volume, log);
            sources.Add(s);
            s.Start();
        }
    }

    // elapsed(100ナノ秒)までの分を混ぜて PCM にする。返り値は書いたバイト数、time はその頭の時刻
    public int Mix(long elapsed, ref byte[] pcm, out long time, out long duration)
    {
        var due = elapsed * Mp4Writer.AudioRate / 10_000_000L;
        var frames = (int)Math.Max(0, due - writtenFrames);
        time = writtenFrames * 10_000_000L / Mp4Writer.AudioRate;
        duration = frames * 10_000_000L / Mp4Writer.AudioRate;
        if (frames <= 0) return 0;
        var count = frames * Mp4Writer.AudioChannels;
        var mix = new float[count];
        foreach (var s in sources) s.MixInto(mix, frames);
        var bytes = count * 2;
        if (pcm == null || pcm.Length < bytes) pcm = new byte[bytes];
        for (var i = 0; i < count; i++)
        {
            var v = (short)Math.Clamp((int)(mix[i] * 32767f), short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)v;
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        writtenFrames += frames;
        return bytes;
    }

    public void Dispose()
    {
        foreach (var s in sources) s.Dispose();
        sources.Clear();
    }
}

static class Wasapi
{
    public const int LOOPBACK = 0x00020000;
    public const int AUTOCONVERTPCM = unchecked((int)0x80000000);
    public const int SRC_DEFAULT_QUALITY = 0x08000000;
    public const int EVENTCALLBACK = 0x00040000;

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} に失敗 (0x{hr:X8})", hr);
    }

    // WAVEFORMATEX: PCM・2ch・48000Hz・16bit
    public static IntPtr MakeFormat()
    {
        var p = Marshal.AllocCoTaskMem(18);
        Marshal.WriteInt16(p, 0, 1);
        Marshal.WriteInt16(p, 2, (short)Mp4Writer.AudioChannels);
        Marshal.WriteInt32(p, 4, Mp4Writer.AudioRate);
        Marshal.WriteInt32(p, 8, Mp4Writer.AudioRate * Mp4Writer.AudioChannels * 2);
        Marshal.WriteInt16(p, 12, (short)(Mp4Writer.AudioChannels * 2));
        Marshal.WriteInt16(p, 14, 16);
        Marshal.WriteInt16(p, 16, 0);
        return p;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams, IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

    public static IAudioClient ActivateProcessLoopback(uint processId, bool exclude)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { 種類 = 1(プロセスの音), 対象のプロセス, 0=そのプロセスだけ / 1=そのプロセス以外 }
        var parameters = Marshal.AllocHGlobal(12);
        // PROPVARIANT(VT_BLOB)
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parameters, 0, 1);
            Marshal.WriteInt32(parameters, 4, (int)processId);
            Marshal.WriteInt32(parameters, 8, exclude ? 1 : 0);
            for (var i = 0; i < 24; i++) Marshal.WriteByte(variant, i, 0);
            Marshal.WriteInt16(variant, 0, 65);
            Marshal.WriteInt32(variant, 8, 12);
            Marshal.WriteIntPtr(variant, 16, parameters);
            var handler = new ActivationHandler();
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, variant, handler, out var operation), "ActivateAudioInterfaceAsync");
            try
            {
                if (!handler.Done.Wait(5000)) throw new TimeoutException("音の取り込みの準備が終わりませんでした");
                Check(operation.GetActivateResult(out var hr, out var obj), "GetActivateResult");
                Check(hr, "プロセスの音の取り込み");
                return (IAudioClient)obj;
            }
            finally { Marshal.ReleaseComObject(operation); }
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    [ComVisible(true)]
    sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            Done.Set();
            return 0;
        }
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumerator { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints();
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
    void GetBufferSize(); void GetStreamLatency(); void GetCurrentPadding(); void IsFormatSupported(); void GetMixFormat(); void GetDevicePeriod();
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    void Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

[ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAgileObject { }
