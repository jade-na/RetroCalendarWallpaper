using SkiaSharp;

namespace RetroCalendarWallpaper.Rendering;

/// <summary>색상·폰트·치수 등 렌더 상수. 디자인 튜닝은 여기서.</summary>
public sealed class Theme
{
    public string FontFamily { get; init; } = "Malgun Gothic";

    // 색상 (레트로 인쇄 달력 느낌)
    public SKColor Background      = SKColors.White;
    public SKColor PanelBorder     = new(0x33, 0x66, 0xCC);   // 파란 테두리
    public SKColor GridLine        = new(0xDD, 0xDD, 0xDD);
    public SKColor TextDefault     = new(0x1A, 0x1A, 0x1A);
    public SKColor TextSunday      = new(0xE0, 0x20, 0x20);   // 빨강
    public SKColor TextSaturday    = new(0x20, 0x50, 0xE0);   // 파랑
    public SKColor TextHoliday     = new(0xE0, 0x20, 0x20);
    public SKColor TextDim         = new(0xBB, 0xBB, 0xBB);   // 전월/익월
    public SKColor TextLunar       = new(0x88, 0x88, 0x88);
    public SKColor TextGanji       = new(0x55, 0x55, 0x55);
    public SKColor TextSolarTerm   = new(0x1E, 0x88, 0x60);   // 절기(초록)
    public SKColor MarkerColor     = new(0xE6, 0x9A, 0x2E);   // 마커(주황)

    // 폰트 캐시
    public SKTypeface Regular { get; }
    public SKTypeface Bold { get; }

    public Theme(string? fontFamily = null)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily)) FontFamily = fontFamily;
        Regular = SKTypeface.FromFamilyName(FontFamily,
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
        Bold = SKTypeface.FromFamilyName(FontFamily,
            SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
    }

    public static readonly string[] WeekdayHanja   = { "日", "月", "火", "水", "木", "金", "土" };
    public static readonly string[] WeekdayEnglish = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
    public static readonly string[] MonthEnglish =
        { "JANUARY","FEBRUARY","MARCH","APRIL","MAY","JUNE","JULY","AUGUST","SEPTEMBER","OCTOBER","NOVEMBER","DECEMBER" };

    public SKColor WeekdayColor(int col) => col switch
    {
        0 => TextSunday,
        6 => TextSaturday,
        _ => TextDefault
    };
}
