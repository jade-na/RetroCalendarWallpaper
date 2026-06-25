using System.Collections.Concurrent;
using System.Text.Json;
using RetroCalendarWallpaper.Models;

namespace RetroCalendarWallpaper.Data;

/// <summary>
/// 42칸(6주) 그리드를 만들고 KASI 음양력/특일 데이터를 병합한다.
/// 음력은 날짜별로 영구 캐시(lunar.json)하여 재호출을 최소화한다.
/// </summary>
public sealed class CalendarDataService
{
    private readonly KasiClient _kasi;
    private readonly string _cacheDir;
    private readonly bool _useCache;
    private readonly IReadOnlyList<ManualSpecialDay> _manual;

    // 음력 호출은 최대 N개까지 동시 실행한다(체감 + 실제 속도 단축). KASI 개발계정 일 10,000건이라
    // 8 동시성은 충분히 여유롭다. 동시 접근 때문에 캐시는 ConcurrentDictionary 로 둔다.
    private const int LunarConcurrency = 8;

    private readonly ConcurrentDictionary<string, KasiClient.LunarInfo> _lunarCache = new();
    private volatile bool _lunarDirty;

    // 회로 차단기: 한 서비스가 (재시도 소진 후) 죽으면 같은 실행에서 더는 호출하지 않는다.
    // 덕분에 403이 지속되는 특일 API에 매번 5회씩 재시도하느라 시간을 낭비하지 않는다.
    private volatile bool _lunarDown;
    private volatile bool _specialsDown;

    // 병렬 음력 호출에서 진행률 출력/회로차단 로그를 직렬화하기 위한 락.
    private readonly object _progressLock = new();
    private readonly object _downLock = new();

    /// <summary>이번 실행에서 음양력 정보를 가져오지 못했는지(부분 렌더링됨).</summary>
    public bool LunarUnavailable => _lunarDown;

    /// <summary>이번 실행에서 특일(공휴일/절기/기념일) 정보를 가져오지 못했는지(공휴일 없이 렌더링됨).</summary>
    public bool SpecialsUnavailable => _specialsDown;

    public CalendarDataService(KasiClient kasi, string cacheDir, bool useCache, IReadOnlyList<ManualSpecialDay> manual)
    {
        _kasi = kasi;
        _cacheDir = cacheDir;
        _useCache = useCache;
        _manual = manual;
        Directory.CreateDirectory(_cacheDir);
        LoadLunarCache();
    }

    public async Task<MonthPanel> BuildPanelAsync(int year, int month, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);   // 일요일 시작
        var dates = Enumerable.Range(0, 42).Select(start.AddDays).ToList();

        // 1) 음력/간지 (날짜별 캐시) — 최대 LunarConcurrency 개를 동시 호출해 가속한다.
        //    완료 순서는 뒤섞이므로 결과는 날짜 순서(인덱스)대로 배열에 채운다.
        int total = dates.Count, done = 0, hitCache = 0, hitNet = 0, lunarFilled = 0;
        var cellArr = new DayCell[total];
        using (var gate = new SemaphoreSlim(LunarConcurrency))
        {
            var tasks = dates.Select(async (d, idx) =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    bool wasCached = _lunarCache.ContainsKey(d.ToString("yyyy-MM-dd"));
                    var li = await GetLunarCachedAsync(d, ct);

                    cellArr[idx] = new DayCell
                    {
                        Date = d,
                        IsInCurrentMonth = d.Month == month && d.Year == year,
                        LunarMonth = li?.LunMonth ?? 0,
                        LunarDay = li?.LunDay ?? 0,
                        IsLeapMonth = li?.Leap ?? false,
                        Iljin = li?.Iljin,
                        Secha = li?.Secha,
                    };

                    lock (_progressLock)
                    {
                        done++;
                        if (wasCached) hitCache++; else if (!_lunarDown) hitNet++;
                        if (li is not null) lunarFilled++;
                        Console.Write($"\r        음력/간지 {done,2}/{total}일  (캐시 {hitCache}, 네트워크 {hitNet})   ");
                    }
                }
                finally { gate.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);
        }
        Console.WriteLine();

        // 음양력 API가 오류 없이 응답했는데도 음력 데이터가 전부 비어 있으면(흔히 활용신청 미승인 시
        // 200 + totalCount=0 으로 옴) 무음 실패를 경고하고, 이후 호출은 생략한다(회로 차단).
        if (lunarFilled == 0 && hitNet > 0 && !_lunarDown)
        {
            lock (_downLock)
            {
                if (!_lunarDown)
                {
                    _lunarDown = true;
                    Console.WriteLine("[경고] 음양력 API가 정상 응답(200)했지만 모든 날짜의 음력 데이터가 비어 있습니다(totalCount=0).");
                    Console.WriteLine("        → 음양력 정보(LrsrCldInfoService) 활용신청이 승인/활성화되지 않았을 가능성이 높습니다.");
                    Console.WriteLine("        달력은 음력/간지 없이 렌더링됩니다.");
                }
            }
        }

        var cells = cellArr.ToList();
        var byDate = cells.ToDictionary(c => c.Date);

        // 2) 특일(공휴일/절기/기념일) — 패널이 걸친 모든 달에 대해 1콜씩
        var spcMonths = dates.Select(d => (d.Year, d.Month)).Distinct().ToList();
        for (int i = 0; i < spcMonths.Count; i++)
        {
            var (y, m) = spcMonths[i];
            Console.Write($"\r        특일 조회 {i + 1}/{spcMonths.Count}  ({y}-{m:D2}) ...   ");
            await MergeSpecialsAsync(byDate, y, m, ct);
        }
        Console.WriteLine();

        // 3) 대체공휴일 폴백 + 수동 특일
        HolidayResolver.ApplySubstituteFallback(byDate);
        HolidayResolver.ApplyManual(byDate, _manual);

        SaveLunarCache();

        // 연 간지(세차) — 주 월의 대표일에서 한자 추출
        var rep = cells.First(c => c.IsInCurrentMonth);
        var ganji = ExtractHanja(rep.Secha);

        return new MonthPanel
        {
            Year = year,
            Month = month,
            Cells = cells,
            YearGanji = string.IsNullOrEmpty(ganji) ? "" : ganji + "年"
        };
    }

    private async Task MergeSpecialsAsync(Dictionary<DateOnly, DayCell> byDate, int y, int m, CancellationToken ct)
    {
        var specials = await GetMonthSpecialsAsync(y, m, ct);
        foreach (var (date, name, type) in specials)
            if (byDate.TryGetValue(date, out var cell))
                cell.Specials.Add(new SpecialDay(name, type));
    }

    private async Task<List<(DateOnly Date, string Name, SpecialDayType Type)>> GetMonthSpecialsAsync(
        int year, int month, CancellationToken ct)
    {
        var file = Path.Combine(_cacheDir, $"specials-{year}-{month:D2}.json");
        if (_useCache && File.Exists(file))
        {
            var cached = JsonSerializer.Deserialize<List<SpecialDto>>(await File.ReadAllTextAsync(file, ct));
            if (cached is not null)
                return cached.Select(c => (DateOnly.Parse(c.Date), c.Name, Enum.Parse<SpecialDayType>(c.Type))).ToList();
        }

        // 특일 서비스가 이미 죽었으면(회로 차단) 더 호출하지 않고 빈 목록 반환.
        if (_specialsDown) return new();

        var list = new List<(DateOnly, string, SpecialDayType)>();
        try
        {
            foreach (var e in await _kasi.GetHolidaysAsync(year, month, ct))
                list.Add((e.Date, e.Name, SpecialDayType.Holiday));
            foreach (var e in await _kasi.GetSolarTermsAsync(year, month, ct))
                list.Add((e.Date, e.Name, SpecialDayType.SolarTerm));
            foreach (var e in await _kasi.GetAnniversariesAsync(year, month, ct))
                list.Add((e.Date, e.Name, e.IsHoliday ? SpecialDayType.Holiday : SpecialDayType.Anniversary));
        }
        catch (KasiApiException ex)
        {
            // 부분 성공: 특일 정보 없이 공휴일을 비운 채로 렌더링한다.
            // 실패한 월은 캐시에 쓰지 않는다(승인/복구 후 다음 실행에서 다시 시도하도록).
            _specialsDown = true;
            Console.WriteLine("\n[경고] 특일(공휴일/절기/기념일) 정보를 가져오지 못했습니다 → 공휴일 없이 렌더링합니다.");
            Console.WriteLine($"        원인: {ex.Message}");
            return new();
        }

        if (_useCache)
        {
            var dto = list.Select(x => new SpecialDto(x.Item1.ToString("yyyy-MM-dd"), x.Item2, x.Item3.ToString())).ToList();
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(dto), ct);
        }
        return list;
    }

    private async Task<KasiClient.LunarInfo?> GetLunarCachedAsync(DateOnly d, CancellationToken ct)
    {
        var key = d.ToString("yyyy-MM-dd");
        if (_lunarCache.TryGetValue(key, out var cached)) return cached;

        // 음양력 서비스가 이미 죽었으면(회로 차단) 더 호출하지 않고 음력 없이 진행.
        if (_lunarDown) return null;

        try
        {
            var info = await _kasi.GetLunarAsync(d, ct);
            if (info is not null) { _lunarCache[key] = info; _lunarDirty = true; }
            return info;
        }
        catch (KasiApiException ex)
        {
            // 부분 성공: 음력/간지 없이(양력 날짜만으로) 렌더링한다.
            // 병렬 호출이 동시에 실패할 수 있으므로 경고는 락으로 한 번만 출력한다.
            lock (_downLock)
            {
                if (!_lunarDown)
                {
                    _lunarDown = true;
                    Console.WriteLine("\n[경고] 음양력 정보를 가져오지 못했습니다 → 음력/간지 없이 렌더링합니다.");
                    Console.WriteLine($"        원인: {ex.Message}");
                }
            }
            return null;
        }
    }

    private void LoadLunarCache()
    {
        var file = Path.Combine(_cacheDir, "lunar.json");
        if (!_useCache || !File.Exists(file)) return;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, KasiClient.LunarInfo>>(File.ReadAllText(file));
            if (data is not null) foreach (var kv in data) _lunarCache[kv.Key] = kv.Value;
        }
        catch { /* 손상 시 무시하고 새로 채움 */ }
    }

    private void SaveLunarCache()
    {
        if (!_useCache || !_lunarDirty) return;
        var file = Path.Combine(_cacheDir, "lunar.json");
        File.WriteAllText(file, JsonSerializer.Serialize(_lunarCache));
        _lunarDirty = false;
    }

    private static string ExtractHanja(string? secha)
    {
        if (string.IsNullOrEmpty(secha)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(secha, @"\(([\u4E00-\u9FFF]{2})\)");
        return m.Success ? m.Groups[1].Value : "";
    }

    private sealed record SpecialDto(string Date, string Name, string Type);
}
