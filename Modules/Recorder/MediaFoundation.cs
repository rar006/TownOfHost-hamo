using System;
using System.Runtime.InteropServices;

namespace TownOfHost.Modules.Recorder;

// Windows に最初から入っている Media Foundation で MP4(H.264 + AAC) を書き出すための最低限の宣言。
// COM の関数は並び順で呼ばれるので、使わない関数も順番どおりに場所だけ置いている
static class MF
{
    public const int Version = 0x00020070;
    public const int StartupLite = 1;

    [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
    [DllImport("mfreadwrite.dll")]
    public static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes attributes, out IMFSinkWriter writer);

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} に失敗 (0x{hr:X8})", hr);
    }

    public static readonly Guid MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
    public static readonly Guid MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid MT_AAC_PAYLOAD_TYPE = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    public static readonly Guid READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

    public static readonly Guid MediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid AudioFormat_AAC = new("00001610-0000-0010-8000-00AA00389B71");
    public static readonly Guid AudioFormat_PCM = new("00000001-0000-0010-8000-00AA00389B71");

    public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64(); void GetDouble(); void GetGUID();
    void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems();
    [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, uint value);
    [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, ulong value);
    void SetDouble();
    [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64(); void GetDouble(); void GetGUID();
    void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems();
    [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, uint value);
    [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, ulong value);
    void SetDouble();
    [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64(); void GetDouble(); void GetGUID();
    void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID();
    void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
    void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime();
    [PreserveSig] int SetSampleTime(long time);
    void GetSampleDuration();
    [PreserveSig] int SetSampleDuration(long duration);
    void GetBufferCount(); void GetBufferByIndex(); void ConvertToContiguousBuffer();
    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
    [PreserveSig] int Unlock();
    void GetCurrentLength();
    [PreserveSig] int SetCurrentLength(int length);
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSinkWriter
{
    [PreserveSig] int AddStream(IMFMediaType targetType, out int streamIndex);
    [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputType, IMFAttributes encodingParameters);
    [PreserveSig] int BeginWriting();
    [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
    void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
    [PreserveSig] int Finalize_();
}

// MP4 に、映像(上から下の順の BGRA)と音(48kHz・ステレオ・16bit)を書き込む
sealed class Mp4Writer : IDisposable
{
    public const int AudioRate = 48000, AudioChannels = 2;
    readonly IMFSinkWriter writer;
    readonly int videoStream = -1, audioStream = -1;
    readonly int width, height;
    bool finished;

    public Mp4Writer(string path, int width, int height, int fps, int videoBitrate, bool withAudio)
    {
        this.width = width;
        this.height = height;
        MF.Check(MF.MFStartup(MF.Version, MF.StartupLite), "MFStartup");
        MF.Check(MF.MFCreateAttributes(out var attrs, 2), "MFCreateAttributes");
        attrs.SetUINT32(MF.READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
        attrs.SetUINT32(MF.SINK_WRITER_DISABLE_THROTTLING, 1);
        MF.Check(MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out writer), "MFCreateSinkWriterFromURL");
        Marshal.ReleaseComObject(attrs);

        MF.Check(MF.MFCreateMediaType(out var vOut), "MFCreateMediaType");
        vOut.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
        vOut.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_H264);
        vOut.SetUINT32(MF.MT_AVG_BITRATE, (uint)videoBitrate);
        vOut.SetUINT32(MF.MT_INTERLACE_MODE, 2);
        vOut.SetUINT64(MF.MT_FRAME_SIZE, MF.Pack((uint)width, (uint)height));
        vOut.SetUINT64(MF.MT_FRAME_RATE, MF.Pack((uint)fps, 1));
        vOut.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
        MF.Check(writer.AddStream(vOut, out videoStream), "AddStream(video)");
        Marshal.ReleaseComObject(vOut);

        MF.Check(MF.MFCreateMediaType(out var vIn), "MFCreateMediaType");
        vIn.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
        vIn.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_RGB32);
        vIn.SetUINT32(MF.MT_INTERLACE_MODE, 2);
        vIn.SetUINT64(MF.MT_FRAME_SIZE, MF.Pack((uint)width, (uint)height));
        vIn.SetUINT64(MF.MT_FRAME_RATE, MF.Pack((uint)fps, 1));
        vIn.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
        // 上の行から順に並べる(正の値)
        vIn.SetUINT32(MF.MT_DEFAULT_STRIDE, (uint)(width * 4));
        MF.Check(writer.SetInputMediaType(videoStream, vIn, null), "SetInputMediaType(video)");
        Marshal.ReleaseComObject(vIn);

        if (withAudio)
        {
            MF.Check(MF.MFCreateMediaType(out var aOut), "MFCreateMediaType");
            aOut.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Audio);
            aOut.SetGUID(MF.MT_SUBTYPE, MF.AudioFormat_AAC);
            aOut.SetUINT32(MF.MT_AUDIO_BITS_PER_SAMPLE, 16);
            aOut.SetUINT32(MF.MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
            aOut.SetUINT32(MF.MT_AUDIO_NUM_CHANNELS, AudioChannels);
            aOut.SetUINT32(MF.MT_AUDIO_AVG_BYTES_PER_SECOND, 24000);
            aOut.SetUINT32(MF.MT_AAC_PAYLOAD_TYPE, 0);
            MF.Check(writer.AddStream(aOut, out audioStream), "AddStream(audio)");
            Marshal.ReleaseComObject(aOut);

            MF.Check(MF.MFCreateMediaType(out var aIn), "MFCreateMediaType");
            aIn.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Audio);
            aIn.SetGUID(MF.MT_SUBTYPE, MF.AudioFormat_PCM);
            aIn.SetUINT32(MF.MT_AUDIO_BITS_PER_SAMPLE, 16);
            aIn.SetUINT32(MF.MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
            aIn.SetUINT32(MF.MT_AUDIO_NUM_CHANNELS, AudioChannels);
            aIn.SetUINT32(MF.MT_AUDIO_BLOCK_ALIGNMENT, AudioChannels * 2);
            aIn.SetUINT32(MF.MT_AUDIO_AVG_BYTES_PER_SECOND, AudioRate * AudioChannels * 2);
            aIn.SetUINT32(MF.MT_ALL_SAMPLES_INDEPENDENT, 1);
            MF.Check(writer.SetInputMediaType(audioStream, aIn, null), "SetInputMediaType(audio)");
            Marshal.ReleaseComObject(aIn);
        }
        MF.Check(writer.BeginWriting(), "BeginWriting");
    }

    // time・duration は 100 ナノ秒単位
    public void WriteVideo(byte[] bgraTopDown, long time, long duration)
        => Write(videoStream, bgraTopDown, width * height * 4, time, duration);

    public void WriteAudio(byte[] pcm, int length, long time, long duration)
    {
        if (audioStream < 0 || length <= 0) return;
        Write(audioStream, pcm, length, time, duration);
    }

    void Write(int stream, byte[] data, int length, long time, long duration)
    {
        MF.Check(MF.MFCreateMemoryBuffer(length, out var buffer), "MFCreateMemoryBuffer");
        try
        {
            MF.Check(buffer.Lock(out var ptr, out _, out _), "Lock");
            Marshal.Copy(data, 0, ptr, length);
            buffer.Unlock();
            buffer.SetCurrentLength(length);
            MF.Check(MF.MFCreateSample(out var sample), "MFCreateSample");
            try
            {
                sample.AddBuffer(buffer);
                sample.SetSampleTime(time);
                sample.SetSampleDuration(duration);
                MF.Check(writer.WriteSample(stream, sample), "WriteSample");
            }
            finally { Marshal.ReleaseComObject(sample); }
        }
        finally { Marshal.ReleaseComObject(buffer); }
    }

    public void Dispose()
    {
        if (finished) return;
        finished = true;
        try { MF.Check(writer.Finalize_(), "Finalize"); }
        finally
        {
            Marshal.ReleaseComObject(writer);
            MF.MFShutdown();
        }
    }
}
