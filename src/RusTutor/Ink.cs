using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RusTutor.Core;
using WinInk = Windows.UI.Input.Inking;
using WinPoint = Windows.Foundation.Point;
namespace RusTutor;

/// <summary>Окно рукописного ввода: InkCanvas (перо, сенсор, мышь, тачпад с зажатой кнопкой) + распознавание.</summary>
sealed class InkWindow : Window
{
    public string ResultText = "";
    readonly InkCanvas _ink = new() { Background = Brushes.White };
    readonly TextBox _out = Ui.Box(true, 90);
    readonly TextBlock _status = Ui.T("", 12);

    public InkWindow()
    {
        Title = "Рукописный ввод"; Width = 900; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _ink.DefaultDrawingAttributes = new DrawingAttributes { Width = 3, Height = 3, Color = Colors.DarkBlue, FitToCurve = true };
        var lines = new DrawingBrush { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 40, 40), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xF5)), 1), new LineGeometry(new Point(0, 39.5), new Point(40, 39.5))) };
        _ink.Background = lines;
        var dock = new DockPanel { Margin = new Thickness(10) };
        var top = Ui.Row(
            Ui.B("Распознать (Windows)", async () => await Recognize("windows"), true),
            Ui.B("Распознать через GigaChat", async () => await Recognize("gigachat")),
            Ui.B("Стереть последнее", () => { if (_ink.Strokes.Count > 0) _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1); }),
            Ui.B("Очистить", () => _ink.Strokes.Clear()),
            Ui.B("Ластик", () => _ink.EditingMode = _ink.EditingMode == InkCanvasEditingMode.EraseByStroke ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.EraseByStroke));
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        var hint = Ui.T("Пишите пером, пальцем или тачпадом (удерживая нажатие/левую кнопку). Ниже можно поправить распознанный текст.", 12); hint.Foreground = Ui.Muted;
        DockPanel.SetDock(hint, Dock.Top); dock.Children.Add(hint);
        var bottom = Ui.V(_out, _status, Ui.Row(Ui.B("Вставить текст", () => { ResultText = _out.Text.Trim(); DialogResult = true; }, true), Ui.B("Отмена", () => DialogResult = false)));
        DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        dock.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = _ink });
        Content = dock;
    }

    async Task Recognize(string how)
    {
        if (_ink.Strokes.Count == 0) { _status.Text = "Нечего распознавать."; return; }
        try
        {
            if (how == "windows")
            {
                _status.Text = "Распознаю средствами Windows…";
                var (text, name) = await WindowsInk.Recognize(_ink.Strokes);
                if (!string.IsNullOrWhiteSpace(text)) { _out.Text = text; _status.Text = "Распознавание Windows: " + name; return; }
                _status.Text = "Windows не распознала текст — пробую GigaChat…";
            }
            _status.Text = "Отправляю изображение в GigaChat…";
            var png = RenderPng();
            var ans = await AiHub.AskImage(Prompts.Handwriting(), png);
            var g = AiParser.Parse(ans);
            _out.Text = string.IsNullOrWhiteSpace(g.Recognized) ? g.Explanation : g.Recognized;
            _status.Text = "Распознано GigaChat";
        }
        catch (Exception e) { _status.Text = "Ошибка распознавания: " + e.Message; }
    }

    byte[] RenderPng()
    {
        var b = _ink.Strokes.GetBounds(); if (b.IsEmpty) b = new Rect(0, 0, 10, 10);
        b.Inflate(20, 20);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, b.Width, b.Height));
            dc.PushTransform(new TranslateTransform(-b.X, -b.Y));
            _ink.Strokes.Draw(dc);
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(b.Width), (int)Math.Ceiling(b.Height), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
    }
}

/// <summary>Распознавание рукописи через WinRT Windows.UI.Input.Inking (нужен языковой пакет «Рукописный ввод» для русского языка).</summary>
static class WindowsInk
{
    public static async Task<(string text, string recognizer)> Recognize(StrokeCollection strokes)
    {
        var builder = new WinInk.InkStrokeBuilder();
        var container = new WinInk.InkStrokeContainer();
        foreach (var s in strokes)
        {
            var pts = s.StylusPoints.Select(p => new WinPoint(p.X, p.Y)).ToList();
            if (pts.Count < 2) pts.Add(new WinPoint(pts[0].X + 0.5, pts[0].Y + 0.5));
            container.AddStroke(builder.CreateStroke(pts));
        }
        var rc = new WinInk.InkRecognizerContainer();
        var all = rc.GetRecognizers();
        var ru = all.FirstOrDefault(r => r.Name.Contains("Русск", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Russian", StringComparison.OrdinalIgnoreCase));
        if (ru == null) throw new InvalidOperationException("в Windows не установлен распознаватель рукописного русского текста (Параметры → Время и язык → Язык → Русский → Рукописный ввод). Доступны: " + string.Join(", ", all.Select(r => r.Name)));
        rc.SetDefaultRecognizer(ru);
        var res = await rc.RecognizeAsync(container, WinInk.InkRecognitionTarget.All);
        return (string.Join(" ", res.Select(r => r.GetTextCandidates().FirstOrDefault() ?? "")).Trim(), ru.Name);
    }
}
