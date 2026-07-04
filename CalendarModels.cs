using System.Text.RegularExpressions;

namespace RetroCalendarWallpaper.Models;

/// <summary>특일(공휴일/기념일/절기/마커) 분류.</summary>
public enum SpecialDayType
{
    Holiday,      // 공공기관 휴일(빨간날) — 대체공휴일 포함
    Anniversary,  // 기념일(노동절 등, 휴무 아님)
    SolarTerm,    // 24절기
    Marker        // 개인 마커(택배 등)
}

public sealed record SpecialDay(string Name, SpecialDayType Type);

/// <summary>12지지(地支) → 띠 동물 매핑.</summary>
public static class EarthlyBranch
{
    // 지지 한자 → (한글, 동물, 이모지). 아이콘 에셋 준비 전 폴백으로 이모지 사용.
    private static readonly Dictionary<char, (string Hangul, string Animal, string Emoji)> Map = new()
    {
        ['子'] = ("자", "쥐",   "🐭"),
        ['丑'] = ("축", "소",   "🐮"),
        ['寅'] = ("인", "호랑이","🐯"),
        ['卯'] = ("묘", "토끼", "🐰"),
        ['辰'] = ("진", "용",   "🐲"),
        ['巳'] = ("사", "뱀",   "🐍"),
        ['午'] = ("오", "말",   "🐴"),
        ['未'] = ("미", "양",   "🐑"),
        ['申'] = ("신", "원숭이","🐵"),
        ['酉'] = ("유", "닭",   "🐔"),
        ['戌'] = ("술", "개",   "🐶"),
        ['亥'] = ("해", "돼지", "🐷"),
    };

    /// <summary>일진 문자열("경오(庚午)")에서 지지 한자를 뽑아 동물 정보를 반환.</summary>
    public static (string Hangul, string Animal, string Emoji)? FromIljin(string? iljin)
    {
        if (string.IsNullOrWhiteSpace(iljin)) return null;
        // 괄호 안 한자(庚午)에서 두 번째 글자가 지지.
        var m = Regex.Match(iljin, @"\(([\u4E00-\u9FFF]{2})\)");
        if (!m.Success) return null;
        char branch = m.Groups[1].Value[1];
        return Map.TryGetValue(branch, out var v) ? v : null;
    }
}

/// <summary>달력 한 칸(하루)에 들어가는 모든 정보.</summary>
public sealed class DayCell
{
    public required DateOnly Date { get; init; }

    /// <summary>이 칸이 렌더 대상 '주(主) 월'에 속하는지 (전월/익월 흐림 처리용).</summary>
    public bool IsInCurrentMonth { get; init; }

    // KASI 음양력 정보
    public int LunarMonth { get; set; }
    public int LunarDay { get; set; }
    public bool IsLeapMonth { get; set; }
    public string? Iljin { get; set; }   // 일진 "경오(庚午)"
    public string? Secha { get; set; }   // 세차(연 간지) "병오(丙午)"

    public readonly List<SpecialDay> Specials = new();

    public string LunarLabel => $"{LunarMonth:D2}.{LunarDay:D2}";

    /// <summary>일진에서 추출한 한자 간지(庚午) — 칸 우측에 표기.</summary>
    public string GanjiHanja
    {
        get
        {
            if (string.IsNullOrEmpty(Iljin)) return "";
            var m = Regex.Match(Iljin, @"\(([\u4E00-\u9FFF]{2})\)");
            return m.Success ? m.Groups[1].Value : "";
        }
    }

    /// <summary>오늘 날짜인지 (동그라미 강조용).</summary>
    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Today);

    public bool IsHoliday => Specials.Any(s => s.Type == SpecialDayType.Holiday);
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;

    public IEnumerable<string> HolidayOrAnniversaryNames =>
        Specials.Where(s => s.Type is SpecialDayType.Holiday or SpecialDayType.Anniversary)
                .Select(s => s.Name);

    public string? SolarTermName =>
        Specials.FirstOrDefault(s => s.Type == SpecialDayType.SolarTerm)?.Name;

    public bool HasMarker => Specials.Any(s => s.Type == SpecialDayType.Marker);
}

/// <summary>렌더 단위: 한 달치 셀(앞뒤 빈칸 포함, 6주 그리드).</summary>
public sealed class MonthPanel
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required IReadOnlyList<DayCell> Cells { get; init; }   // 42칸(6주 x 7일)
    public string YearGanji { get; set; } = "";                  // "丙午年"
}
