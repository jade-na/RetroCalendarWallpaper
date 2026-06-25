using System.Globalization;
using System.Xml.Linq;

namespace RetroCalendarWallpaper.Data;

/// <summary>한국천문연구원(KASI) 공공 API 호출 + XML 파싱.</summary>
public sealed class KasiClient
{
    private readonly HttpClient _http;
    private readonly string _serviceKey;
    private readonly string _lunarBase;
    private readonly string _spcdeBase;

    /// <summary>재시도가 발생할 때마다 호출되는 콜백(진행 상황 표시용). 메시지에 민감정보는 없다.</summary>
    public Action<string>? OnRetry { get; set; }

    public KasiClient(HttpClient http, string serviceKey, string lunarBaseUrl, string spcdeBaseUrl)
    {
        _http = http;
        _serviceKey = serviceKey;
        _lunarBase = lunarBaseUrl.TrimEnd('/');
        _spcdeBase = spcdeBaseUrl.TrimEnd('/');
    }

    public sealed record LunarInfo(int LunYear, int LunMonth, int LunDay, bool Leap, string Iljin, string Secha);

    /// <summary>양력 1일 → 음력/간지 (getSolCalInfo). 하루 1콜.</summary>
    public async Task<LunarInfo?> GetLunarAsync(DateOnly d, CancellationToken ct = default)
    {
        var url = $"{_lunarBase}/getSolCalInfo?ServiceKey={_serviceKey}" +
                  $"&solYear={d.Year}&solMonth={d.Month:D2}&solDay={d.Day:D2}";
        var xml = await GetXmlAsync(url, "getSolCalInfo", ct);
        var item = xml?.Descendants("item").FirstOrDefault();
        if (item is null) return null;

        return new LunarInfo(
            LunYear:  ParseInt(item, "lunYear"),
            LunMonth: ParseInt(item, "lunMonth"),
            LunDay:   ParseInt(item, "lunDay"),
            Leap:     (string?)item.Element("lunLeapmonth") == "윤",
            Iljin:    (string?)item.Element("lunIljin") ?? "",
            Secha:    (string?)item.Element("lunSecha") ?? "");
    }

    public sealed record SpecialEntry(DateOnly Date, string Name, bool IsHoliday);

    /// <summary>공휴일(getRestDeInfo): 대체공휴일 포함. 월 1콜.</summary>
    public Task<List<SpecialEntry>> GetHolidaysAsync(int year, int month, CancellationToken ct = default)
        => GetSpcdeAsync("getRestDeInfo", year, month, ct);

    /// <summary>24절기(get24DivisionsInfo). 월 1콜.</summary>
    public Task<List<SpecialEntry>> GetSolarTermsAsync(int year, int month, CancellationToken ct = default)
        => GetSpcdeAsync("get24DivisionsInfo", year, month, ct);

    /// <summary>기념일(getAnniversaryInfo): 노동절 등. 월 1콜.</summary>
    public Task<List<SpecialEntry>> GetAnniversariesAsync(int year, int month, CancellationToken ct = default)
        => GetSpcdeAsync("getAnniversaryInfo", year, month, ct);

    private async Task<List<SpecialEntry>> GetSpcdeAsync(string op, int year, int month, CancellationToken ct)
    {
        var url = $"{_spcdeBase}/{op}?ServiceKey={_serviceKey}" +
                  $"&solYear={year}&solMonth={month:D2}&numOfRows=50";
        var xml = await GetXmlAsync(url, op, ct);
        var result = new List<SpecialEntry>();
        if (xml is null) return result;

        foreach (var item in xml.Descendants("item"))
        {
            var loc = (string?)item.Element("locdate");
            var name = (string?)item.Element("dateName");
            if (loc is null || name is null) continue;
            if (!DateOnly.TryParseExact(loc, "yyyyMMdd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var date)) continue;
            bool isHoliday = (string?)item.Element("isHoliday") == "Y";
            result.Add(new SpecialEntry(date, name.Trim(), isHoliday));
        }
        return result;
    }

    // 재시도 설정: data.go.kr 게이트웨이는 키 전파 중/평시에도 간헐적 401·403·5xx 를 내므로
    // 한 호출이 삐끗해도 전체 실행이 죽지 않도록 지수 백오프 + 지터로 재시도한다.
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    private async Task<XDocument?> GetXmlAsync(string url, string operation, CancellationToken ct)
    {
        var content = await GetWithRetryAsync(url, operation, ct);

        // 에러 시 OpenAPI는 다른 XML(예: OpenAPI_ServiceResponse)을 반환하므로 방어적 파싱.
        XDocument doc;
        try { doc = XDocument.Parse(content); }
        catch { return null; }

        var resultCode = doc.Descendants("resultCode").FirstOrDefault()?.Value
                       ?? doc.Descendants("returnReasonCode").FirstOrDefault()?.Value;
        if (resultCode is not null && resultCode is not "00" and not "0000")
        {
            var msg = doc.Descendants("resultMsg").FirstOrDefault()?.Value
                    ?? doc.Descendants("returnAuthMsg").FirstOrDefault()?.Value;
            throw new KasiApiException(operation, null, resultCode, 1,
                $"{KasiApiException.ResultHint(resultCode)} (resultMsg: {msg})");
        }
        return doc;
    }

    /// <summary>
    /// GET 요청을 일시적 오류(401·403·408·429·5xx, 타임아웃/네트워크 예외)에 한해
    /// 지수 백오프 + 지터로 재시도하고 본문 문자열을 반환한다.
    /// 영구 오류(예: 404)나 모든 재시도 소진 시에는 상세 원인을 담은 KasiApiException 을 던진다.
    /// </summary>
    private async Task<string> GetWithRetryAsync(string url, string operation, CancellationToken ct)
    {
        int lastStatus = 0;
        Exception? lastNetworkError = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string retryReason;
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsStringAsync(ct);

                lastStatus = (int)resp.StatusCode;

                // 일시적이지 않은 상태코드(예: 404)는 재시도하지 않고 즉시 실패.
                if (!IsTransient(resp.StatusCode))
                    throw new KasiApiException(operation, lastStatus, null, attempt,
                        KasiApiException.HttpHint(lastStatus));
                retryReason = $"HTTP {lastStatus}";
            }
            catch (HttpRequestException ex) { lastNetworkError = ex; retryReason = "네트워크 오류"; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { lastNetworkError = ex; retryReason = "응답 시간 초과"; }

            if (attempt < MaxAttempts)
            {
                var delay = BackoffFor(attempt);
                OnRetry?.Invoke($"{operation}: {retryReason} → {attempt}/{MaxAttempts - 1} 재시도, {delay.TotalSeconds:0.#}s 대기");
                await Task.Delay(delay, ct);
            }
        }

        // 모든 재시도 소진.
        if (lastStatus != 0)
            throw new KasiApiException(operation, lastStatus, null, MaxAttempts,
                KasiApiException.HttpHint(lastStatus), lastNetworkError);

        throw new KasiApiException(operation, null, null, MaxAttempts,
            $"네트워크 오류로 응답을 받지 못했습니다: {lastNetworkError?.Message}", lastNetworkError);
    }

    private static bool IsTransient(System.Net.HttpStatusCode code) => code switch
    {
        System.Net.HttpStatusCode.Unauthorized        // 401: 키 전파 지연 시 간헐 발생
        or System.Net.HttpStatusCode.Forbidden        // 403: 게이트웨이 노드별 권한 전파 지연
        or System.Net.HttpStatusCode.RequestTimeout   // 408
        or (System.Net.HttpStatusCode)429             // Too Many Requests
            => true,
        _ => (int)code >= 500                          // 5xx
    };

    /// <summary>지수 백오프(0.5s, 1s, 2s, 4s ...) + 0~250ms 지터. (Random.Shared 는 스레드 안전)</summary>
    private static TimeSpan BackoffFor(int attempt)
    {
        var exp = BaseDelay * Math.Pow(2, attempt - 1);
        return exp + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
    }

    private static int ParseInt(XElement item, string name)
        => int.TryParse((string?)item.Element(name), out var v) ? v : 0;
}
