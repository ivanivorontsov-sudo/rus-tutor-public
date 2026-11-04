using System.Net.Http;
using System.Threading.Channels;
using Google.Cloud.Speech.V1;
using Google.Protobuf;
using RusTutor.Core;
namespace RusTutor;

interface IRecognizer : IDisposable
{
    event Action<string> Interim;   // промежуточный текст (может прийти из фонового потока)
    event Action<string> Final;     // окончательная фраза
    event Action<string> Error;
    Task StartAsync();
    void Feed(byte[] buf, int count);  // PCM16 16 кГц моно
    Task StopAsync();
}

/// <summary>
/// Бесплатный режим: тот же неофициальный endpoint Google, что и recognize_google в Python SpeechRecognition.
/// Без ключа, без промежуточных результатов, без пунктуации; Google может ограничить частоту запросов.
/// </summary>
sealed class GoogleFreeRecognizer : IRecognizer
{
    static string PublicKey => Environment.GetEnvironmentVariable("RUSTUTOR_GOOGLE_KEY") ?? ""; // ключ не хранится в коде: задайте переменную окружения RUSTUTOR_GOOGLE_KEY
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly string _lang; readonly VadSegmenter _vad;
    Task _chain = Task.CompletedTask; readonly object _lock = new();
    public event Action<string> Interim, Final, Error;

    public GoogleFreeRecognizer(string lang, int silenceMs, int threshold)
    {
        _lang = lang;
        _vad = new VadSegmenter { SilenceMs = silenceMs, Threshold = threshold, MaxSegmentMs = 15000 };
        _vad.Segment += OnSegment;
    }

    public Task StartAsync() => Task.CompletedTask;

    bool _wasSpeech;
    public void Feed(byte[] buf, int count)
    {
        var s = new short[count / 2]; Buffer.BlockCopy(buf, 0, s, 0, s.Length * 2);
        _vad.Feed(s);
        if (_vad.InSpeech != _wasSpeech) { _wasSpeech = _vad.InSpeech; if (_wasSpeech) Interim?.Invoke("🎙 говорите…"); }
    }

    void OnSegment(short[] pcm)
    {
        if (string.IsNullOrWhiteSpace(PublicKey)) { Error?.Invoke("Бесплатный голосовой режим: задайте ключ в переменной окружения RUSTUTOR_GOOGLE_KEY или выберите облачный режим в «Настройках»."); return; }
        Interim?.Invoke("⏳ распознаю…");
        lock (_lock) _chain = _chain.ContinueWith(_ => Recognize(pcm)).Unwrap(); // строго по порядку
    }

    async Task Recognize(short[] pcm)
    {
        try
        {
            var flac = FlacEncoder.Encode(pcm);
            var content = new ByteArrayContent(flac);
            content.Headers.TryAddWithoutValidation("Content-Type", "audio/x-flac; rate=16000");
            var url = $"https://www.google.com/speech-api/v2/recognize?client=chromium&lang={_lang}&key={PublicKey}&output=json&pFilter=0";
            using var r = await Http.PostAsync(url, content);
            var body = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode) { Error?.Invoke($"Google: HTTP {(int)r.StatusCode}"); return; }
            var text = GoogleFreeParser.Parse(body);
            if (!string.IsNullOrWhiteSpace(text)) Final?.Invoke(text); else Interim?.Invoke("");
        }
        catch (Exception e) { Error?.Invoke("Сеть: " + e.Message); }
    }

    public async Task StopAsync() { _vad.Flush(); Task c; lock (_lock) c = _chain; await c; }
    public void Dispose() { }
}

/// <summary>Официальный Google Cloud Speech‑to‑Text, потоковое распознавание с промежуточными результатами.</summary>
sealed class GoogleCloudRecognizer : IRecognizer
{
    readonly AppSettings _s;
    readonly Channel<ByteString> _audio = Channel.CreateUnbounded<ByteString>();
    CancellationTokenSource _cts; Task _loop;
    public event Action<string> Interim, Final, Error;

    public GoogleCloudRecognizer(AppSettings s) { _s = s; }

    SpeechClient CreateClient()
    {
        var b = new SpeechClientBuilder();
        if (!string.IsNullOrWhiteSpace(_s.CloudCredentialsPath)) b.CredentialsPath = _s.CloudCredentialsPath;
        else if (!string.IsNullOrWhiteSpace(_s.CloudApiKey)) {
            var key = _s.CloudApiKey.Trim();
            var call = Grpc.Core.CallCredentials.FromInterceptor((ctx, md) => { md.Add("x-goog-api-key", key); return Task.CompletedTask; });
            b.ChannelCredentials = Grpc.Core.ChannelCredentials.Create(Grpc.Core.ChannelCredentials.SecureSsl, call);
        }
        else throw new InvalidOperationException("Не задан API‑ключ или JSON сервисного аккаунта (Настройки → Google Cloud)");
        return b.Build();
    }

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        var client = CreateClient();
        _loop = Task.Run(() => Loop(client, _cts.Token));
        return Task.CompletedTask;
    }

    async Task Loop(SpeechClient client, CancellationToken ct)
    {
        // Google ограничивает поток ~5 минутами — переоткрываем каждые 4,5 минуты
        while (!ct.IsCancellationRequested && !_audio.Reader.Completion.IsCompleted)
        {
            try { await OneStream(client, ct); }
            catch (Exception e) when (!ct.IsCancellationRequested) { Error?.Invoke("Google Cloud: " + Short(e)); await Task.Delay(1500, ct).ContinueWith(_ => { }); }
            catch { }
        }
    }

    static string Short(Exception e) => e is Grpc.Core.RpcException r ? $"{r.StatusCode}: {r.Status.Detail}" : e.Message;

    async Task OneStream(SpeechClient client, CancellationToken ct)
    {
        using var call = client.StreamingRecognize();
        await call.WriteAsync(new StreamingRecognizeRequest
        {
            StreamingConfig = new StreamingRecognitionConfig
            {
                InterimResults = true,
                Config = new RecognitionConfig
                {
                    Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                    SampleRateHertz = 16000,
                    LanguageCode = _s.VoiceLanguage,
                    EnableAutomaticPunctuation = _s.AutoPunctuation,
                    Model = "latest_long",
                }
            }
        });
        var reader = Task.Run(async () =>
        {
            await foreach (var resp in call.GetResponseStream())
                foreach (var res in resp.Results)
                {
                    var t = res.Alternatives.FirstOrDefault()?.Transcript ?? "";
                    if (res.IsFinal) { if (t.Trim().Length > 0) Final?.Invoke(t.Trim()); Interim?.Invoke(""); }
                    else Interim?.Invoke(t);
                }
        });
        var until = DateTime.UtcNow.AddSeconds(270);
        try
        {
            while (DateTime.UtcNow < until)
            {
                using var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);
                tcs.CancelAfter(until - DateTime.UtcNow);
                ByteString chunk;
                try { chunk = await _audio.Reader.ReadAsync(tcs.Token); } catch (OperationCanceledException) { break; }
                catch (ChannelClosedException) { break; }
                await call.WriteAsync(new StreamingRecognizeRequest { AudioContent = chunk });
            }
        }
        finally
        {
            try { await call.WriteCompleteAsync(); } catch { }
            await reader;
        }
    }

    public void Feed(byte[] buf, int count) => _audio.Writer.TryWrite(ByteString.CopyFrom(buf, 0, count));

    public async Task StopAsync()
    {
        _audio.Writer.TryComplete();  // дослать накопленный звук и дождаться последних итоговых результатов
        if (_loop != null) await Task.WhenAny(_loop, Task.Delay(6000));
        _cts?.Cancel();
    }
    public void Dispose() { _cts?.Cancel(); }
}
