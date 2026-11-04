using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
namespace RusTutor;

/// <summary>Кнопка 🎤: запись с микрофона и распознавание Google (ru-RU) с вставкой текста в поле.</summary>
sealed class VoiceButton : Button
{
    readonly TextBox _target; WaveInEvent _wave; IRecognizer _rec; bool _on;
    public VoiceButton(TextBox target)
    {
        _target = target; Content = "🎤"; Width = 40; Height = 34; FontSize = 16; Margin = new Thickness(4, 0, 0, 0);
        ToolTip = "Голосовой ввод (Google, ru-RU). Нажмите ещё раз, чтобы остановить.";
        Click += async (_, _) => { if (_on) await Stop(); else await Start(); };
        Unloaded += async (_, _) => { if (_on) await Stop(); };
    }

    async Task Start()
    {
        var s = App.Settings;
        if (WaveInEvent.DeviceCount == 0) { MessageBox.Show("Микрофон не найден."); return; }
        try
        {
            _rec = s.VoiceBackend == "cloud" ? new GoogleCloudRecognizer(s) : new GoogleFreeRecognizer(s.VoiceLanguage, s.SilenceMs, s.VadThreshold);
            _rec.Final += t => Dispatcher.Invoke(() => Ui.Insert(_target, t));
            _rec.Interim += t => Dispatcher.Invoke(() => ToolTip = string.IsNullOrEmpty(t) ? "Слушаю…" : t);
            _rec.Error += e => Dispatcher.Invoke(() => App.Main.Status(e));
            await _rec.StartAsync();
            _wave = new WaveInEvent { DeviceNumber = Math.Max(0, Math.Min(s.MicDevice, WaveInEvent.DeviceCount - 1)), WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
            var rec = _rec;
            _wave.DataAvailable += (_, e) => rec.Feed(e.Buffer, e.BytesRecorded);
            _wave.StartRecording();
            _on = true; Background = Brushes.IndianRed; App.Main.Status("🎙 Говорите… (" + (s.VoiceBackend == "cloud" ? "Google Cloud" : "Google, бесплатный режим") + ")");
        }
        catch (Exception e) { MessageBox.Show("Голосовой ввод: " + e.Message); _rec?.Dispose(); _rec = null; }
    }

    async Task Stop()
    {
        _on = false; ClearValue(BackgroundProperty);
        try { _wave?.StopRecording(); _wave?.Dispose(); } catch { }
        _wave = null;
        if (_rec != null) { App.Main.Status("⏳ Дораспознаю…"); await _rec.StopAsync(); _rec.Dispose(); _rec = null; }
        App.Main.Status("Готово");
    }
}
