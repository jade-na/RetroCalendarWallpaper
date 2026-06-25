using SkiaSharp;
using RetroCalendarWallpaper.Models;

namespace RetroCalendarWallpaper.Rendering;

/// <summary>월 패널들을 가로로 배치해 한 장의 배경화면 비트맵을 생성한다.</summary>
public sealed class CalendarRenderer
{
    private readonly Theme _t;
    private readonly string _assetsDir;
    private readonly Dictionary<char, SKBitmap?> _iconCache = new();

    public CalendarRenderer(Theme theme, string assetsDir)
    {
        _t = theme;
        _assetsDir = assetsDir;
    }

    public SKBitmap RenderWallpaper(int width, int height, IReadOnlyList<MonthPanel> panels)
    {
        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(_t.Background);

        float margin = width * 0.025f;
        float gap = width * 0.02f;
        int n = Math.Max(1, panels.Count);
        float panelW = (width - margin * 2 - gap * (n - 1)) / n;
        float panelH = height - margin * 2;

        for (int i = 0; i < panels.Count; i++)
        {
            var rect = new SKRect(margin + i * (panelW + gap), margin,
                                  margin + i * (panelW + gap) + panelW, margin + panelH);
            DrawPanel(canvas, rect, panels[i]);
        }
        return bmp;
    }

    private void DrawPanel(SKCanvas canvas, SKRect r, MonthPanel panel)
    {
        using var border = new SKPaint { Color = _t.PanelBorder, IsStroke = true, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawRect(r, border);

        float headerH = r.Height * 0.14f;
        float weekdayH = r.Height * 0.07f;
        float gridTop = r.Top + headerH + weekdayH;
        float gridH = r.Bottom - gridTop;
        float cellW = r.Width / 7f;
        float rowH = gridH / 6f;

        DrawHeader(canvas, new SKRect(r.Left, r.Top, r.Right, r.Top + headerH), panel);
        DrawWeekdayRow(canvas, new SKRect(r.Left, r.Top + headerH, r.Right, gridTop));

        var cells = panel.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            int col = i % 7, row = i / 7;
            var cellRect = new SKRect(r.Left + col * cellW, gridTop + row * rowH,
                                      r.Left + (col + 1) * cellW, gridTop + (row + 1) * rowH);
            DrawCell(canvas, cellRect, cells[i], col);
        }
    }

    private void DrawHeader(SKCanvas canvas, SKRect r, MonthPanel panel)
    {
        // 큰 월 숫자
        using var big = new SKPaint
        {
            Color = _t.TextHoliday, Typeface = _t.Bold, IsAntialias = true,
            TextSize = r.Height * 0.95f
        };
        float numX = r.Left + r.Width * 0.06f;
        float baseY = r.Bottom - r.Height * 0.10f;
        canvas.DrawText(panel.Month.ToString(), numX, baseY, big);

        float infoX = numX + big.MeasureText(panel.Month.ToString()) + r.Width * 0.04f;
        using var yearP = new SKPaint { Color = _t.TextDefault, Typeface = _t.Regular, IsAntialias = true, TextSize = r.Height * 0.18f };
        using var monP  = new SKPaint { Color = _t.TextDefault, Typeface = _t.Bold,    IsAntialias = true, TextSize = r.Height * 0.26f };
        using var ganjiP= new SKPaint { Color = _t.TextGanji,   Typeface = _t.Regular, IsAntialias = true, TextSize = r.Height * 0.18f };

        canvas.DrawText(panel.Year.ToString(), infoX, r.Top + r.Height * 0.32f, yearP);
        canvas.DrawText(Theme.MonthEnglish[panel.Month - 1], infoX, r.Top + r.Height * 0.62f, monP);
        if (!string.IsNullOrEmpty(panel.YearGanji))
            canvas.DrawText(panel.YearGanji, infoX, r.Top + r.Height * 0.86f, ganjiP);

        // TODO(다음 단계): 헤더 우측 상단에 전월/익월 미니 캘린더 그리기.
    }

    private void DrawWeekdayRow(SKCanvas canvas, SKRect r)
    {
        float cellW = r.Width / 7f;
        using var hanja = new SKPaint { Typeface = _t.Bold, IsAntialias = true, TextAlign = SKTextAlign.Center, TextSize = r.Height * 0.55f };
        using var eng   = new SKPaint { Typeface = _t.Regular, IsAntialias = true, TextAlign = SKTextAlign.Center, TextSize = r.Height * 0.22f };

        for (int c = 0; c < 7; c++)
        {
            float cx = r.Left + c * cellW + cellW / 2;
            hanja.Color = eng.Color = _t.WeekdayColor(c);
            canvas.DrawText(Theme.WeekdayHanja[c], cx, r.Top + r.Height * 0.55f, hanja);
            canvas.DrawText(Theme.WeekdayEnglish[c], cx, r.Bottom - r.Height * 0.12f, eng);
        }
    }

    private void DrawCell(SKCanvas canvas, SKRect r, DayCell cell, int col)
    {
        using var grid = new SKPaint { Color = _t.GridLine, IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(r, grid);

        float pad = r.Width * 0.07f;

        // 날짜 색상
        SKColor dateColor =
            !cell.IsInCurrentMonth ? _t.TextDim :
            cell.IsHoliday || cell.IsSunday ? _t.TextHoliday :
            cell.IsSaturday ? _t.TextSaturday : _t.TextDefault;

        using var dateP = new SKPaint { Color = dateColor, Typeface = _t.Bold, IsAntialias = true, TextSize = r.Height * 0.30f };
        canvas.DrawText(cell.Date.Day.ToString(), r.Left + pad, r.Top + r.Height * 0.32f, dateP);

        // 마커(택배 등) — 우측 상단 작은 박스
        if (cell.HasMarker)
        {
            using var mk = new SKPaint { Color = _t.MarkerColor, IsAntialias = true };
            float s = r.Height * 0.12f;
            var box = new SKRect(r.Right - pad - s, r.Top + pad, r.Right - pad, r.Top + pad + s);
            canvas.DrawRoundRect(box, 2, 2, mk);
        }

        // 공휴일/기념일 이름(빨강) + 절기(초록) — 최대 2줄
        float textY = r.Top + r.Height * 0.50f;
        using var nameP = new SKPaint { Color = _t.TextHoliday, Typeface = _t.Regular, IsAntialias = true, TextSize = r.Height * 0.115f };
        foreach (var name in cell.HolidayOrAnniversaryNames.Take(2))
        {
            canvas.DrawText(Ellipsize(name, nameP, r.Width - pad * 2), r.Left + pad, textY, nameP);
            textY += r.Height * 0.14f;
        }
        if (cell.SolarTermName is { } term)
        {
            using var termP = new SKPaint { Color = _t.TextSolarTerm, Typeface = _t.Regular, IsAntialias = true, TextSize = r.Height * 0.115f };
            canvas.DrawText(Ellipsize(term, termP, r.Width - pad * 2), r.Left + pad, textY, termP);
        }

        // 하단: [띠 아이콘] 음력  ......  간지(한자)
        float bottomY = r.Bottom - r.Height * 0.10f;
        float iconSize = r.Height * 0.16f;
        float cursorX = r.Left + pad;

        var branch = GanjiBranch(cell.Iljin);
        if (branch is { } b)
        {
            DrawZodiac(canvas, b, new SKRect(cursorX, bottomY - iconSize, cursorX + iconSize, bottomY));
            cursorX += iconSize + r.Width * 0.04f;
        }

        if (cell.LunarMonth > 0)
        {
            using var lunarP = new SKPaint { Color = _t.TextLunar, Typeface = _t.Regular, IsAntialias = true, TextSize = r.Height * 0.115f };
            canvas.DrawText(cell.LunarLabel, cursorX, bottomY - r.Height * 0.02f, lunarP);
        }

        if (!string.IsNullOrEmpty(cell.GanjiHanja))
        {
            using var ganjiP = new SKPaint { Color = _t.TextGanji, Typeface = _t.Regular, IsAntialias = true, TextAlign = SKTextAlign.Right, TextSize = r.Height * 0.115f };
            canvas.DrawText(cell.GanjiHanja, r.Right - pad, bottomY - r.Height * 0.02f, ganjiP);
        }
    }

    private static char? GanjiBranch(string? iljin)
    {
        if (string.IsNullOrEmpty(iljin)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(iljin, @"\(([\u4E00-\u9FFF]{2})\)");
        return m.Success ? m.Groups[1].Value[1] : null;
    }

    /// <summary>띠 아이콘: Assets/zodiac/{지지한자}.png 가 있으면 사용, 없으면 이모지로 폴백.</summary>
    private void DrawZodiac(SKCanvas canvas, char branch, SKRect dest)
    {
        if (!_iconCache.TryGetValue(branch, out var icon))
        {
            var path = Path.Combine(_assetsDir, "zodiac", $"{branch}.png");
            icon = File.Exists(path) ? SKBitmap.Decode(path) : null;
            _iconCache[branch] = icon;
        }

        if (icon is not null)
        {
            canvas.DrawBitmap(icon, dest);
            return;
        }

        // 폴백: 컬러 이모지 (Segoe UI Emoji). 미지원 환경이면 비워둠 — 아이콘 에셋 권장.
        var info = EarthlyBranch.FromIljin($"({branch}{branch})");
        string glyph = info?.Emoji ?? "";
        using var emoji = new SKPaint
        {
            Typeface = SKTypeface.FromFamilyName("Segoe UI Emoji") ?? _t.Regular,
            IsAntialias = true, TextSize = dest.Height
        };
        if (!string.IsNullOrEmpty(glyph))
            canvas.DrawText(glyph, dest.Left, dest.Bottom, emoji);
    }

    private static string Ellipsize(string text, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(text) <= maxWidth) return text;
        for (int len = text.Length - 1; len > 0; len--)
        {
            var s = text[..len] + "…";
            if (paint.MeasureText(s) <= maxWidth) return s;
        }
        return text;
    }
}
