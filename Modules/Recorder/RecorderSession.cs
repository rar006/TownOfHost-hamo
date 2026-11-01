using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace TownOfHost.Modules.Recorder;

// 1 回分の録画。映像は呼ぶ側(ゲーム)から受け取り、音は自分で取り込んで、別のスレッドで MP4 に書く
sealed class RecorderSession
{
    public readonly int Width, Height, Fps;
    public readonly string Path;
    public int DroppedFrames { get; private set; }
    public string Error { get; private set; }
    public bool IsRunning => thread != null && thread.IsAlive;
    public TimeSpan Elapsed => clock.Elapsed;

    readonly int bitrate;
    readonly (AudioSourceKind, float)[] audio;
    readonly Action<string> log;
    readonly BlockingCollection<(byte[] data, long time)> frames = new(new ConcurrentQueue<(byte[], long)>());
    readonly ConcurrentBag<byte[]> pool = new();
    readonly Stopwatch clock = new();
    Thread thread;
    volatile bool stopping;
    long lastFrameTime = -1;

    // ゲームから来る絵は下の行から順の RGBA なので、書く前に上下を入れ替えて BGRA にする
    readonly bool bottomUpRgba;

    public RecorderSession(string path, int width, int height, int fps, int bitrate, (AudioSourceKind, float)[] audio, Action<string> log, bool bottomUpRgba = false)
    {
        this.bottomUpRgba = bottomUpRgba;
        Path = path;
        // H.264 は縦横が偶数でないと書けない
        Width = width & ~1;
        Height = height & ~1;
        Fps = fps;
        this.bitrate = bitrate;
        this.audio = audio;
        this.log = log;
    }

    public void Start()
    {
        clock.Start();
        // ゲームの動きを優先する(書き出しは少し後回しでも間に合う)
        thread = new Thread(Run) { IsBackground = true, Name = "TOH-hamo Recorder", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    // 書き込み待ちが多すぎる(PC が追いつかない)時は、その絵を捨てる
    public bool WantsFrame => !stopping && frames.Count < 6;

    public byte[] RentBuffer()
    {
        var size = Width * Height * 4;
        while (pool.TryTake(out var b)) if (b.Length == size) return b;
        return new byte[size];
    }

    public void PushFrame(byte[] bgraTopDown)
    {
        if (!WantsFrame) { DroppedFrames++; pool.Add(bgraTopDown); return; }
        frames.Add((bgraTopDown, clock.Elapsed.Ticks));
    }

    public void Stop()
    {
        stopping = true;
        frames.CompleteAdding();
    }

    public void Wait(int ms) => thread?.Join(ms);

    static unsafe void FlipAndSwizzle(byte[] data, int width, int height)
    {
        fixed (byte* bytes = data)
        {
            var px = (uint*)bytes;
            static uint Swap(uint v) => (v & 0xFF00FF00u) | ((v & 0xFFu) << 16) | ((v >> 16) & 0xFFu);
            for (int top = 0, bottom = height - 1; top <= bottom; top++, bottom--)
            {
                var a = px + top * width;
                var b = px + bottom * width;
                if (top == bottom)
                {
                    for (var x = 0; x < width; x++) a[x] = Swap(a[x]);
                    break;
                }
                for (var x = 0; x < width; x++)
                {
                    var va = a[x];
                    a[x] = Swap(b[x]);
                    b[x] = Swap(va);
                }
            }
        }
    }

    void Run()
    {
        Mp4Writer writer = null;
        AudioMixer mixer = null;
        try
        {
            var wanted = new List<(AudioSourceKind, float)>();
            foreach (var a in audio) if (a.Item2 > 0f) wanted.Add(a);
            mixer = new AudioMixer(wanted, log);
            writer = new Mp4Writer(Path, Width, Height, Fps, bitrate, mixer.HasSources);
            var frameDuration = 10_000_000L / Fps;
            byte[] pcm = null;
            while (true)
            {
                var got = frames.TryTake(out var frame, 20);
                if (got)
                {
                    // 同じ時刻に 2 枚書かないように、前の絵の後ろにずらす
                    if (bottomUpRgba) FlipAndSwizzle(frame.data, Width, Height);
                    var t = (frame.time + frameDuration / 2) / frameDuration * frameDuration;
                    if (t <= lastFrameTime) t = lastFrameTime + frameDuration;
                    writer.WriteVideo(frame.data, t, frameDuration);
                    lastFrameTime = t;
                    pool.Add(frame.data);
                }
                if (mixer.HasSources)
                {
                    var bytes = mixer.Mix(clock.Elapsed.Ticks, ref pcm, out var at, out var length);
                    if (bytes > 0) writer.WriteAudio(pcm, bytes, at, length);
                }
                if (!got && frames.IsCompleted) break;
            }
        }
        catch (Exception e)
        {
            Error = e.Message;
            log?.Invoke($"録画を書き込めませんでした: {e}");
            stopping = true;
        }
        finally
        {
            mixer?.Dispose();
            try { writer?.Dispose(); }
            catch (Exception e) { Error ??= e.Message; log?.Invoke($"録画の仕上げに失敗しました: {e.Message}"); }
            clock.Stop();
        }
    }
}
