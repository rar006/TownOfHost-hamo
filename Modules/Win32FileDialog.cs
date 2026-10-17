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

    /// <summary>
    /// 画像・動画ファイルを1つ選ぶダイアログを表示する。
    /// 呼び出しはブロッキング(ダイアログを閉じるまで戻らない)なので、
    /// フレーム内で完結する短い処理からのみ呼ぶこと。
    /// </summary>
    /// <returns>選択されたファイルの絶対パス。キャンセルされた場合はnull。</returns>
    static volatile bool dialogOpen;

    /// <summary>
    /// ファイル選択ダイアログを別スレッドで開く(ゲームを止めない)。選んだら onSelected がゲームのスレッドで呼ばれる。
    /// ゲームのスレッドで開くと、閉じるまでゲーム全体が止まり、全画面などではフリーズやクラッシュの原因になる。
    /// </summary>
    public static void ShowOpenImageOrVideoDialogAsync(Action<string> onSelected)
    {
        if (!OperatingSystem.IsWindows() || dialogOpen) return;
        dialogOpen = true;
        var thread = new System.Threading.Thread(() =>
        {
            string path = null;
            try { path = ShowOpenImageOrVideoDialog(ownerless: true); }
            finally { dialogOpen = false; }
            if (!string.IsNullOrEmpty(path)) MainThreadDispatcher.Enqueue(() => onSelected?.Invoke(path));
        })
        { IsBackground = true, Name = "TOH-hamo FileDialog" };
        thread.SetApartmentState(System.Threading.ApartmentState.STA); // コモンダイアログはSTAのスレッドで開く必要がある
        thread.Start();
    }

    public static string ShowOpenImageOrVideoDialog(bool ownerless = false)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var fileBuffer = new StringBuilder(1024);
            // フィルタ文字列はヌル文字区切り、末尾は二重ヌルで終端する仕様。
            var filter = "画像・動画ファイル\0*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.webm;*.mov;*.avi\0すべてのファイル\0*.*\0\0";

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = ownerless ? IntPtr.Zero : GetActiveWindow(), // 別スレッドの時はゲームの窓を親にしない(ゲームが止まらないように)
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = Marshal.StringToHGlobalUni(new string('\0', fileBuffer.Capacity)),
                nMaxFile = fileBuffer.Capacity,
                lpstrTitle = "添付する画像・動画を選択",
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
            };

            try
            {
                var result = GetOpenFileNameW(ref ofn);
                if (!result) return null;

                var path = Marshal.PtrToStringUni(ofn.lpstrFile);
                return string.IsNullOrEmpty(path) ? null : path;
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
