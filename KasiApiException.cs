namespace RetroCalendarWallpaper.Data;

/// <summary>
/// KASI(공공데이터포털) API 호출 실패. HTTP 상태/결과코드별로 사람이 읽을 수 있는
/// 원인·조치 힌트를 메시지에 담는다. (ServiceKey 등 민감정보는 메시지에 포함하지 않는다.)
/// </summary>
public sealed class KasiApiException : Exception
{
    /// <summary>실패한 오퍼레이션 이름(예: getSolCalInfo, getRestDeInfo).</summary>
    public string Operation { get; }

    /// <summary>HTTP 상태코드(있을 경우). 네트워크 예외 등에서는 null.</summary>
    public int? HttpStatus { get; }

    /// <summary>data.go.kr 결과코드(있을 경우, 예: "30").</summary>
    public string? ResultCode { get; }

    /// <summary>총 시도 횟수.</summary>
    public int Attempts { get; }

    public KasiApiException(
        string operation, int? httpStatus, string? resultCode, int attempts, string detail, Exception? inner = null)
        : base(Compose(operation, httpStatus, resultCode, attempts, detail), inner)
    {
        Operation = operation;
        HttpStatus = httpStatus;
        ResultCode = resultCode;
        Attempts = attempts;
    }

    private static string Compose(string op, int? http, string? code, int attempts, string detail)
    {
        var what = http is not null ? $"HTTP {http}"
                 : code is not null ? $"결과코드 {code}"
                 : "응답 없음";
        return $"KASI '{op}' 호출 실패 — {what} ({attempts}회 시도). {detail}";
    }

    /// <summary>HTTP 상태코드별 원인·조치 힌트.</summary>
    public static string HttpHint(int status) => status switch
    {
        401 => "인증 실패: 인증키가 아직 활성화 전파 중이거나(발급/승인 직후 수십 분~최대 1일 소요) " +
               "Encoding 키를 넣었을 수 있습니다. Decoding 키인지 확인하세요.",
        403 => "접근 거부: 이 API의 '활용신청'이 승인되지 않았을 수 있습니다. " +
               "공공데이터포털 마이페이지 > 활용신청 현황에서 해당 API 승인 상태를 확인하세요.",
        404 => "잘못된 엔드포인트(URL)입니다. appsettings.json 의 Kasi BaseUrl 을 확인하세요.",
        408 => "요청 시간 초과입니다. 네트워크 상태를 확인하세요.",
        429 => "요청 한도 초과입니다(개발계정 일 10,000건). 한도를 확인하거나 잠시 후 재시도하세요.",
        >= 500 and < 600 => "KASI 서버 측 오류입니다. 잠시 후 다시 시도하세요.",
        _ => "예기치 않은 HTTP 오류입니다."
    };

    /// <summary>data.go.kr 표준 결과코드별 힌트.</summary>
    public static string ResultHint(string code) => code switch
    {
        "30" => "등록되지 않은 서비스키입니다. appsettings.json 의 Kasi:ServiceKey 값을 확인하세요.",
        "31" => "활용 기간이 만료된 서비스키입니다. 키를 갱신하세요.",
        "22" => "요청 제한 횟수를 초과했습니다(일 한도 초과).",
        "20" or "21" => "서비스 접근이 거부되었습니다. 활용신청/승인 상태를 확인하세요.",
        _ => "API가 오류 코드를 반환했습니다."
    };
}
