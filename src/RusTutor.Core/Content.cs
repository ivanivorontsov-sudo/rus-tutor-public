using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RusTutor.Core;

public sealed class ExamNumber { public string Exam { get; set; } = ""; public int Num { get; set; } public string Title { get; set; } = ""; public int Max { get; set; } = 1; }

public sealed class TaskItem
{
    public string Id { get; set; } = "";
    public string Exam { get; set; } = "";
    public int Num { get; set; }
    /// <summary>digits — набор цифр в любом порядке; ordered — цифры строго по порядку; word — слово/слова.</summary>
    public string Kind { get; set; } = "word";
    public string Prompt { get; set; } = "";
    public string? Text { get; set; }
    public List<string> Answers { get; set; } = new();
    public string Explain { get; set; } = "";
    public string Theory { get; set; } = "";
    public bool Generated { get; set; }
}

public sealed class TheoryItem { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Body { get; set; } = ""; }

public sealed class Content
{
    public Dictionary<string, string> Texts { get; set; } = new();
    public List<ExamNumber> Numbers { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();
    [JsonIgnore] public List<TheoryItem> Theory { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string Res(string name)
    {
        using var s = typeof(Content).Assembly.GetManifestResourceStream("RusTutor.Data." + name) ?? throw new FileNotFoundException(name);
        using var r = new StreamReader(s); return r.ReadToEnd();
    }

    /// <summary>Встроенный банк + пользовательские/сгенерированные задания из extraTasksPath.</summary>
    public static Content Load(string? extraTasksPath = null)
    {
        var c = JsonSerializer.Deserialize<Content>(Res("tasks.json"), Json)!;
        c.Theory = JsonSerializer.Deserialize<List<TheoryItem>>(Res("theory.json"), Json)!;
        if (extraTasksPath != null && File.Exists(extraTasksPath))
        {
            try
            {
                var extra = JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(extraTasksPath), Json) ?? new();
                foreach (var t in extra) if (!c.Tasks.Any(x => x.Id == t.Id)) c.Tasks.Add(t);
            }
            catch (JsonException) { }
        }
        return c;
    }

    public static void AppendExtra(string path, IEnumerable<TaskItem> items)
    {
        var list = File.Exists(path) ? (JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(path), Json) ?? new()) : new();
        list.AddRange(items);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(list, Json));
    }

    public IEnumerable<TaskItem> For(string exam, int num) => Tasks.Where(t => t.Exam == exam && t.Num == num);
    public ExamNumber? Number(string exam, int num) => Numbers.FirstOrDefault(n => n.Exam == exam && n.Num == num);
    public string FullPrompt(TaskItem t) => t.Text != null && Texts.TryGetValue(t.Text, out var tx) ? $"Текст:\n{tx}\n\n{t.Prompt}" : t.Prompt;
    public TheoryItem? TheoryFor(TaskItem t) => Theory.FirstOrDefault(x => x.Id == t.Theory);
}

public static class AnswerChecker
{
    public static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Replace('ё', 'е').Where(ch => char.IsLetterOrDigit(ch)).ToArray());

    public static bool Check(TaskItem t, string answer)
    {
        var a = Normalize(answer ?? "");
        if (a.Length == 0) return false;
        foreach (var exp in t.Answers)
        {
            var e = Normalize(exp);
            if (t.Kind == "digits")
            {
                if (a.All(char.IsDigit) && string.Concat(a.Distinct().OrderBy(c => c)) == string.Concat(e.Distinct().OrderBy(c => c))) return true;
            }
            else if (a == e) return true;
        }
        return false;
    }

    public static int WordCount(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text ?? "", @"[\p{L}\d]+(?:-[\p{L}\d]+)*").Count;
}
