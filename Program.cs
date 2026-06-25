using Microsoft.Extensions.Configuration;
using SkiaSharp;
using RetroCalendarWallpaper.Data;
using RetroCalendarWallpaper.Models;
using RetroCalendarWallpaper.Platform;
using RetroCalendarWallpaper.Rendering;

var baseDir = AppContext.BaseDirectory;

try
{
    return await RunAsync(baseDir);
}
catch (KasiApiException ex)
{
    // 부분 성공으로도 못 살린 치명적 KASI 오류(예: 음력·특일 모두 즉시 실패는 부분성공으로 처리되므로
    // 여기 도달하는 경우는 드물다). 상세 원인을 출력한다.
    Console.Error.WriteLine("[오류] KASI API 호출에 실패했습니다.");
    Console.Error.WriteLine($"       {ex.Message}");
    return 2;
}
catch (ConfigException ex)
{
    Console.Error.WriteLine($"[오류] 설정 문제: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[오류] 예기치 않은 문제가 발생했습니다: {ex.GetType().Name}: {ex.Message}");
    return 99;
}

static async Task<int> RunAsync(string baseDir)
{
var config = new ConfigurationBuilder()
    .SetBasePath(baseDir)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string serviceKey = config["Kasi:ServiceKey"] ?? throw new ConfigException("appsettings.json 에 Kasi:ServiceKey 가 없습니다.");
if (string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Contains("여기에"))
    throw new ConfigException(
        "Kasi:ServiceKey 가 설정되지 않았습니다(기본 안내 문구 그대로입니다). " +
        "공공데이터포털에서 발급받은 일반 인증키(Decoding)를 appsettings.json 에 넣으세요.");
string lunarBase  = config["Kasi:LunarBaseUrl"]!;
string spcdeBase  = config["Kasi:SpcdeBaseUrl"]!;

int width   = config.GetValue("Render:Width", 1920);
int height  = config.GetValue("Render:Height", 1080);
int months  = config.GetValue("Render:MonthsToShow", 2);
string font = config["Render:FontFamily"] ?? "Malgun Gothic";
string outPath = Expand(config["Render:OutputPath"] ?? "wallpaper.png");

bool setWallpaper = config.GetValue("Behavior:SetWallpaper", true);
bool useCache     = config.GetValue("Behavior:UseCache", true);
string cacheDir   = Expand(config["Behavior:CacheDir"] ?? Path.Combine(baseDir, "cache"));

var manual = config.GetSection("ManualSpecialDays").Get<List<ManualSpecialDay>>() ?? new();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var kasi = new KasiClient(http, serviceKey, lunarBase, spcdeBase);
// 재시도가 진행 중임을 사용자에게 알린다(앞의 진행 카운터 줄을 깨고 새 줄에 출력).
kasi.OnRetry = msg => Console.WriteLine($"\n        ↻ {msg}");
var dataService = new CalendarDataService(kasi, cacheDir, useCache, manual);

// 이번 달부터 MonthsToShow 개월
var now = DateOnly.FromDateTime(DateTime.Now);
var panels = new List<MonthPanel>();
for (int i = 0; i < months; i++)
{
    var m = new DateOnly(now.Year, now.Month, 1).AddMonths(i);
    Console.WriteLine($"[데이터] {m.Year}년 {m.Month}월 구성 중...");
    panels.Add(await dataService.BuildPanelAsync(m.Year, m.Month));
}

Console.WriteLine("[렌더] 배경화면 생성 중...");
var theme = new Theme(font);
var renderer = new CalendarRenderer(theme, Path.Combine(baseDir, "Assets"));
using var bmp = renderer.RenderWallpaper(width, height, panels);

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
using (var img = SKImage.FromBitmap(bmp))
using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
using (var fs = File.Create(outPath))
    data.SaveTo(fs);
Console.WriteLine($"[저장] {outPath}");

if (setWallpaper && OperatingSystem.IsWindows())
{
    WallpaperSetter.Set(outPath);
    Console.WriteLine("[바탕화면] 적용 완료");
}

// 부분 성공 요약: 일부 데이터가 빠진 채로 렌더링됐다면 사용자에게 분명히 알린다.
if (dataService.LunarUnavailable || dataService.SpecialsUnavailable)
{
    Console.WriteLine();
    Console.WriteLine("[알림] 일부 데이터 없이 '부분 렌더링'되었습니다:");
    if (dataService.LunarUnavailable)    Console.WriteLine("        - 음력/간지: 표시되지 않음");
    if (dataService.SpecialsUnavailable) Console.WriteLine("        - 공휴일/절기/기념일: 표시되지 않음");
    Console.WriteLine("        해당 API 승인/활성화가 완료되면 다시 실행하세요(빠진 월은 캐시되지 않아 재시도됩니다).");
    return 10;   // 성공이지만 부분 데이터임을 나타내는 종료코드
}

return 0;
}

static string Expand(string p) => Environment.ExpandEnvironmentVariables(p);

/// <summary>설정(appsettings.json) 관련 사용자 조치가 필요한 오류.</summary>
sealed class ConfigException(string message) : Exception(message);
