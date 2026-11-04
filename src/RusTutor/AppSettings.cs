using System.IO;
using System.Text.Json;
using RusTutor.Core;
namespace RusTutor;

public sealed class AppSettings
{
    // ИИ
    public string AiMode { get; set; } = "web";              // web | api
    public string GigaUrl { get; set; } = "https://giga.chat/";
    public string InputSelector { get; set; } = "textarea, div[contenteditable='true']";
    public string SendSelector { get; set; } = "button[type='submit'], button[aria-label*='тправ'], button[data-testid*='send']";
    public string StopSelector { get; set; } = "button[aria-label*='станов'], button[aria-label*='Stop'], button[data-testid*='stop']";
    public int WebTimeoutSec { get; set; } = 180;
    public string GigaAuthKey { get; set; } = "";
    public string GigaScope { get; set; } = "GIGACHAT_API_PERS";
    public string GigaModel { get; set; } = "GigaChat-Max";
    public bool GigaTrustAllCerts { get; set; }
    // Голос
    public string VoiceBackend { get; set; } = "free";        // free | cloud
    public string VoiceLanguage { get; set; } = "ru-RU";
    public string CloudApiKey { get; set; } = "";
    public string CloudCredentialsPath { get; set; } = "";
    public bool AutoPunctuation { get; set; } = true;
    public int SilenceMs { get; set; } = 800;
    public int VadThreshold { get; set; } = 500;
    public int MicDevice { get; set; }
    // Рукопись
    public string InkMode { get; set; } = "auto";              // auto | windows | gigachat

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RusTutor");
    static string File_ => Path.Combine(Dir, "settings.json");
    public static string ProgressPath => Path.Combine(Dir, "progress.json");
    public static string ExtraTasksPath => Path.Combine(Dir, "my-tasks.json");

    public static AppSettings Load()
    {
        try { if (File.Exists(File_)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(File_), Content.Json) ?? new(); } catch { }
        return new();
    }
    public void Save() { Directory.CreateDirectory(Dir); File.WriteAllText(File_, JsonSerializer.Serialize(this, Content.Json)); }
}
