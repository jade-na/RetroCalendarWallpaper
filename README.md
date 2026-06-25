# RetroCalendarWallpaper

한국 전통 양식(음력·간지·24절기·공휴일·12지신)의 달력을 **배경화면 이미지로 자동 생성**하고
Windows 바탕화면에 적용하는 .NET 8 + SkiaSharp 콘솔 프로그램입니다.

> 이 단계는 **Phase 1(데이터) + Phase 2(렌더링) 골격**입니다. 컴파일·실행은 직접 환경에서 해 주세요
> (이 코드는 작성 단계라 NuGet 복원이 가능한 환경에서 한 번 빌드 후 디자인을 다듬는 흐름을 권장합니다).

## 1. API 키 발급

[공공데이터포털](https://www.data.go.kr)에서 회원가입 후 아래 두 API를 **활용신청**합니다(즉시 승인, 개발계정 일 10,000건).

- 한국천문연구원_음양력 정보 (`LrsrCldInfoService`) — 음력 변환·간지(일진/세차)
- 한국천문연구원_특일 정보 (`SpcdeInfoService`) — 공휴일·기념일·24절기

마이페이지에서 **일반 인증키(Decoding)** 를 복사합니다.
(URL 인코딩된 키를 쓰면 `+`, `=`, `%` 처리 문제가 생기니 Decoding 키 권장.)

> ⚠ **키는 `appsettings.json`에 넣지 마세요.** 이 파일은 git에 커밋됩니다.
> 키 주입은 아래 두 방법 중 하나를 쓰세요(둘 다 git에서 제외됨):
>
> **방법 A — 로컬 설정 파일(권장)**
> `appsettings.Local.json.example` 을 같은 폴더에 `appsettings.Local.json` 으로 복사하고 키를 채웁니다.
> ```json
> { "Kasi": { "ServiceKey": "발급받은-Decoding-키" } }
> ```
>
> **방법 B — 환경변수** (CI/작업 스케줄러용)
> ```powershell
> $env:Kasi__ServiceKey = "발급받은-Decoding-키"   # 구분자는 콜론이 아닌 이중 밑줄(__)
> ```
>
> 우선순위: `appsettings.json` < `appsettings.Local.json` < 환경변수.

## 2. 빌드 & 실행

```bash
dotnet restore
dotnet run
```

실행하면 이번 달 + 다음 달을 그려 `%USERPROFILE%\Pictures\RetroCalendar\wallpaper.png`로 저장하고
바탕화면에 적용합니다. 해상도/출력 경로/표시 개월 수는 `appsettings.json`에서 조정합니다.
(키만 `appsettings.Local.json`/환경변수로 분리됩니다 — 위 *API 키 발급* 참고.)

## 3. 매달 자동 실행 (작업 스케줄러)

`dotnet publish -c Release -r win-x64 --self-contained false` 로 exe를 만든 뒤,
Windows **작업 스케줄러**에 등록합니다.

- 트리거: 매월 1일 오전 9시 + (보조) 로그온 시
- 동작: 게시한 `RetroCalendarWallpaper.exe` 실행

월이 바뀌면 자동으로 새 달력 배경이 깔립니다.

## 4. 12지신 아이콘 (선택)

`Assets/zodiac/` 에 지지 한자명으로 PNG를 넣으면 이모지 대신 사용합니다:
`子.png 丑.png 寅.png 卯.png 辰.png 巳.png 午.png 未.png 申.png 酉.png 戌.png 亥.png`
(없으면 Segoe UI Emoji 폴백으로 그려집니다.)

## 5. 구조

```
Program.cs                  진입점 (설정→데이터→렌더→바탕화면)
Models/CalendarModels.cs    DayCell, MonthPanel, 12지신 매핑, 특일 타입
Data/KasiClient.cs          KASI REST 호출 + XML 파싱
Data/CalendarDataService.cs 42칸 그리드 구성 + 병합 + 영구 캐시(lunar.json)
Data/HolidayResolver.cs     대체공휴일 폴백 + 수동 특일(임시공휴일·마커)
Rendering/Theme.cs          색상·폰트·치수
Rendering/CalendarRenderer.cs  SkiaSharp 패널/셀 드로잉
Platform/WallpaperSetter.cs Win32 바탕화면 설정
```

## 6. 알려진 한계 / 다음 단계

- **미니 캘린더(전월/익월)**: 헤더 우측 상단 표시는 TODO. `DrawHeader`에 추가 예정.
- **설날·추석 연휴 대체공휴일**: 연휴 겹침 규칙은 단순화돼 있음. 최신 `getRestDeInfo`가
  대체공휴일을 직접 내려주므로 보통은 그 값을 신뢰. 누락 시에만 폴백 적용.
- **임시공휴일/선거일**: API 반영이 늦으므로 `appsettings.json > ManualSpecialDays`에 직접 추가.
- **노동절 명칭**: 기념일 API는 "근로자의 날"로 내려줄 수 있음. 표시명 변경은 매핑 테이블로 처리 가능.
- **택배(📦) 마커**: `ManualSpecialDays`에 `type: "Marker"`로 넣으면 칸 우측 상단에 표시. 데이터 소스는 추후 정의.
- **레이아웃 정밀도**: 첨부 이미지 100% 재현은 아니며, 폰트/여백/색상은 `Theme`와 `CalendarRenderer`에서 튜닝.

## 7. 캐시

- `lunar.json` : 날짜별 음력/간지 (한 번 받으면 재호출 안 함 — 영구 보존 권장)
- `specials-YYYY-MM.json` : 월별 공휴일/절기/기념일
캐시 위치는 `Behavior:CacheDir`. 데이터가 갱신되면(예: 공휴일 정정) 해당 파일 삭제 후 재실행.
