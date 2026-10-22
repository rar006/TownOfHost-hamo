using System;
using System.Runtime.InteropServices;
using System.Text;

using TownOfHost;

namespace TownOfHost.Modules;

/// <summary>
/// Windows標準のファイル選択ダイアログ(GetOpenFileName、いわゆる旧来のコモンダイアログ)を
/// P/Invokeで直接呼び出す薄いラッパー。
///
/// IL2CPPビルド環境では、System.Windows.Forms(WinForms)や最新のCOMベースの
/// IFileOpenDialog APIは依存関係やスレッドモデルの問題で不安定になりやすいため、
/// 単純なcomdlg32.dllのGetOpenFileNameW(1990年代からある古典的なAPIで、
/// 単一のP/Invoke呼び出しだけで完結する)を採用している。
/// 対応OSはWindowsのみ。Windows以外(Mac等)では常にキャンセル扱いで何もしない。
/// </summary>
public static class Win32FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_EXPLORER = 0x00080000;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private const int OFN_ALLOWMULTISELECT = 0x00000200;

    public const int MaxAttachments = 3;

    static volatile bool dialogOpen;

    public static void ShowOpenImageOrVideoDialogAsync(Action<string[], bool> onSelected)
    {
        if (!OperatingSystem.IsWindows() || dialogOpen) return;
        dialogOpen = true;
        // ゲームの窓を親にして、選ぶ画面がゲームの後ろに隠れないようにする。
        // (親が無いと後ろに隠れて見えず、開いたままになるので、再起動するまで添付ボタンが効かなくなっていた)
        var owner = GetActiveWindow();
        if (owner == IntPtr.Zero) owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        var thread = new System.Threading.Thread(() =>
        {
            string[] paths = null;
            try { paths = ShowOpenImageOrVideoDialog(owner); }
            finally { dialogOpen = false; }
            if (paths == null || paths.Length == 0) return;
            var truncated = paths.Length > MaxAttachments;
            if (truncated) paths = paths[..MaxAttachments];
            MainThreadDispatcher.Enqueue(() => onSelected?.Invoke(paths, truncated));
        })
        { IsBackground = true, Name = "TOH-hamo FileDialog" };
        thread.SetApartmentState(System.Threading.ApartmentState.STA); // コモンダイアログはSTAのスレッドで開く必要がある
        thread.Start();
    }

    public static string[] ShowOpenImageOrVideoDialog(IntPtr owner)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // 複数選択すると「フォルダ\0ファイル1\0ファイル2\0\0」の形で返るので、バッファは大きめにする
            const int bufferChars = 32 * 1024;
            // フィルタ文字列はヌル文字区切り、末尾は二重ヌルで終端する仕様。
            var filter = "画像・動画ファイル\0*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.webm;*.mov;*.avi\0すべてのファイル\0*.*\0\0";

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = owner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = Marshal.AllocHGlobal(bufferChars * 2),
                nMaxFile = bufferChars,
                lpstrTitle = $"添付する画像・動画を選択(Ctrlを押しながらで{MaxAttachments}つまで選べます)",
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_ALLOWMULTISELECT,
            };

            try
            {
                Marshal.Copy(new byte[bufferChars * 2], 0, ofn.lpstrFile, bufferChars * 2);
                var result = GetOpenFileNameW(ref ofn);
                if (!result) return null;

                // ヌル文字区切りの文字列を、空の要素(二重ヌル)が来るまで読む
                var parts = new System.Collections.Generic.List<string>();
                var ptr = ofn.lpstrFile;
                while (true)
                {
                    var part = Marshal.PtrToStringUni(ptr);
                    if (string.IsNullOrEmpty(part)) break;
                    parts.Add(part);
                    ptr += (part.Length + 1) * 2;
                }
                if (parts.Count == 0) return null;
                if (parts.Count == 1) return new[] { parts[0] }; // 1つだけ選んだ時はフルパスがそのまま返る
                var dir = parts[0];
                var files = new string[parts.Count - 1];
                for (var i = 1; i < parts.Count; i++) files[i - 1] = System.IO.Path.Combine(dir, parts[i]);
                return files;
            }
            finally
            {
                if (ofn.lpstrFile != IntPtr.Zero) Marshal.FreeHGlobal(ofn.lpstrFile);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ファイル選択ダイアログの表示に失敗しました: {ex.Message}", "Win32FileDialog");
            return null;
        }
    }
}
