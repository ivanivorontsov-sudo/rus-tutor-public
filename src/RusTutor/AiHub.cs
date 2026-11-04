using RusTutor.Core;
namespace RusTutor;

/// <summary>Выбор канала GigaChat по настройкам: встроенный веб-чат или официальный API.</summary>
static class AiHub
{
    static GigaChatApi _api; static string _apiKeySig;
    public static IAiBackend Current()
    {
        var s = App.Settings;
        if (s.AiMode == "api")
        {
            if (string.IsNullOrWhiteSpace(s.GigaAuthKey)) throw new InvalidOperationException("Укажите ключ авторизации GigaChat API в «Настройках» или переключитесь на веб-чат.");
            var sig = s.GigaAuthKey + s.GigaScope + s.GigaModel + s.GigaTrustAllCerts;
            if (_api == null || sig != _apiKeySig) { _api = new GigaChatApi(s.GigaAuthKey, s.GigaScope, s.GigaModel, s.GigaTrustAllCerts); _apiKeySig = sig; }
            return _api;
        }
        return App.Main.GigaWeb;
    }

    public static async Task<string> Ask(string prompt)
    {
        var b = Current(); App.Main.Status($"Спрашиваю {b.Name}…");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(60, App.Settings.WebTimeoutSec + 30)));
        try { return await b.AskAsync(prompt, cts.Token); } finally { App.Main.Status("Готово"); }
    }
    public static async Task<string> AskImage(string prompt, byte[] png)
    {
        var b = Current(); App.Main.Status($"Отправляю изображение в {b.Name}…");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(60, App.Settings.WebTimeoutSec + 30)));
        try { return await b.AskWithImageAsync(prompt, png, cts.Token); } finally { App.Main.Status("Готово"); }
    }
    public static async Task<AiGrade> Grade(string prompt) => AiParser.Parse(await Ask(prompt));
}
