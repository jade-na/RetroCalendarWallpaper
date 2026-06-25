using RetroCalendarWallpaper.Models;

namespace RetroCalendarWallpaper.Data;

public sealed record ManualSpecialDay(string Date, string Name, string Type);

/// <summary>
/// 대체공휴일 보정 + 수동 특일(임시공휴일·선거일·개인 마커) 주입.
/// 최신 getRestDeInfo 는 대체공휴일을 이미 내려주므로 1차로 그걸 신뢰하고,
/// 누락 시 규칙 기반 폴백을 적용한다.
/// </summary>
public static class HolidayResolver
{
    // 대체공휴일이 적용되는 공휴일(설날·추석·어린이날·삼일절·광복절·개천절·한글날·부처님오신날·성탄절).
    private static readonly HashSet<string> SubstituteEligible = new()
    {
        "설날", "추석", "어린이날", "3·1절", "삼일절", "광복절",
        "개천절", "한글날", "부처님오신날", "기독탄신일", "성탄절"
    };

    /// <summary>이미 부여된 공휴일을 보고, 빠진 대체공휴일을 계산해 추가.</summary>
    public static void ApplySubstituteFallback(IReadOnlyDictionary<DateOnly, DayCell> cells)
    {
        // 이미 "대체공휴일"이 들어있는 날짜는 건드리지 않는다.
        bool alreadyHasSubstitute = cells.Values
            .Any(c => c.Specials.Any(s => s.Name.Contains("대체공휴일")));
        if (alreadyHasSubstitute) return;

        foreach (var (date, cell) in cells.OrderBy(kv => kv.Key))
        {
            bool eligible = cell.Specials.Any(s =>
                s.Type == SpecialDayType.Holiday && SubstituteEligible.Contains(s.Name));
            if (!eligible) continue;

            bool fallsOnWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            // (설날·추석 연휴 겹침까지 엄밀히 처리하려면 별도 로직 필요)
            if (!fallsOnWeekend) continue;

            var next = NextNonHoliday(date, cells);
            if (cells.TryGetValue(next, out var target) && !target.IsHoliday)
                target.Specials.Add(new SpecialDay("대체공휴일", SpecialDayType.Holiday));
        }
    }

    private static DateOnly NextNonHoliday(DateOnly d, IReadOnlyDictionary<DateOnly, DayCell> cells)
    {
        var cur = d.AddDays(1);
        for (int i = 0; i < 10; i++)
        {
            bool weekend = cur.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool holiday = cells.TryGetValue(cur, out var c) && c.IsHoliday;
            if (!weekend && !holiday) return cur;
            cur = cur.AddDays(1);
        }
        return cur;
    }

    public static void ApplyManual(IReadOnlyDictionary<DateOnly, DayCell> cells, IEnumerable<ManualSpecialDay> manual)
    {
        foreach (var m in manual)
        {
            if (!DateOnly.TryParse(m.Date, out var date)) continue;
            if (!cells.TryGetValue(date, out var cell)) continue;
            if (!Enum.TryParse<SpecialDayType>(m.Type, true, out var type))
                type = SpecialDayType.Marker;
            cell.Specials.Add(new SpecialDay(m.Name, type));
        }
    }
}
