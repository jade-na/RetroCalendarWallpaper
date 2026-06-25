using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RetroCalendarWallpaper.Platform;

[SupportedOSPlatform("windows")]
public static class WallpaperSetter
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    /// <summary>지정한 이미지 파일을 바탕화면 배경으로 적용한다(현재 사용자).</summary>
    public static void Set(string imagePath)
    {
        var full = Path.GetFullPath(imagePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("배경화면 이미지가 없습니다.", full);

        int ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, full,
                                      SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        if (ok == 0)
            throw new InvalidOperationException(
                $"바탕화면 설정 실패 (Win32 error {Marshal.GetLastWin32Error()})");
    }
}
