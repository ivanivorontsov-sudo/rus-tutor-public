using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace RusTutor.Core;

public sealed class CriterionScore { public string Id { get; set; } = ""; public int Score { get; set; } public int Max { get; set; } public string Comment { get; set; } = ""; }
public sealed class AiError { public string Type { get; set; } = ""; public string Fragment { get; set; } = ""; public string Fix { get; set; } = ""; public string Rule { get; set; } = ""; }
public sealed class AiGrade
{
    public int Score { get; set; }
    public int Max { get; set; }
    public List<CriterionScore> Criteria { get; set; } = new();
    public List<AiError> Errors { get; set; } = new();
    public string Explanation { get; set; } = "";
    public string Recognized { get; set; } = "";   // для распознавания рукописи
    public string Raw { get; set; } = "";
}

/// <summary>Способ общения с GigaChat: веб-чат во встроенном браузере или официальный API.</summary>
public interface IAiBackend
{
    string Name { get; }
    Task<string> AskAsync(string prompt, CancellationToken ct);
    Task<string> AskWithImageAsync(string prompt, byte[] png, CancellationToken ct);
}

public static class AiParser
{
    static readonly Regex CodeBlock = new(@"```[a-zA-Z]*\s*\n?(.*?)```", RegexOptions.Singleline);
    static readonly Regex Links = new(@"\[(\d+|[^\]]{0,40})\]\((https?://[^)]+)\)|\[\d+\]|【[^】]*】|https?://[^\s""\\,}\]]+", RegexOptions.Compiled);

    /// <summary>Последний блок кода; если блоков нет — последний сбалансированный {...}.</summary>
    public static string? ExtractLastBlock(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var m = CodeBlock.Matches(answer);
        if (m.Count > 0) return m[^1].Groups[1].Value.Trim();
        int end = answer.LastIndexOf('}');
        while (end >= 0)
        {
            int depth = 0;
            for (int i = end; i >= 0; i--)
            {
                if (answer[i] == '}') depth++;
                else if (answer[i] == '{' && --depth == 0) return answer.Substring(i, end - i + 1);
            }
            end = answer.LastIndexOf('}', end - 1);
        }
        return null;
    }

    /// <summary>Убирает ссылки и сноски поиска, которые веб-чат вставляет в ответ.</summary>
    public static string StripSearchLinks(string s) => Links.Replace(s, m => m.Groups[1].Success && !Regex.IsMatch(m.Groups[1].Value, @"^\d+$") ? m.Groups[1].Value : "");

    public static AiGrade Parse(string answer)
    {
        var block = ExtractLastBlock(answer) ?? throw new FormatException("В ответе нет JSON-блока");
        block = StripSearchLinks(block);
        block = Regex.Replace(block, @",\s*([}\]])", "$1"); // висячие запятые
        JsonNode node;
        try { node = JsonNode.Parse(block, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })!; }
        catch (JsonException e) { throw new FormatException("Не удалось разобрать JSON: " + e.Message); }
        var g = new AiGrade { Raw = answer };
        g.Score = Int(node["score"]); g.Max = Int(node["max"]);
        g.Explanation = Str(node["explanation"]); g.Recognized = Str(node["text"]);
        if (node["criteria"] is JsonArray ca)
            foreach (var c in ca) if (c != null) g.Criteria.Add(new CriterionScore { Id = Str(c["id"]), Score = Int(c["score"]), Max = Int(c["max"]), Comment = Str(c["comment"]) });
        if (node["errors"] is JsonArray ea)
            foreach (var e in ea) if (e != null)
                g.Errors.Add(e is JsonValue ? new AiError { Fragment = e.ToString() } : new AiError { Type = Str(e["type"]), Fragment = Str(e["fragment"]), Fix = Str(e["fix"]), Rule = Str(e["rule"]) });
        if (g.Criteria.Count > 0 && g.Score == 0) g.Score = g.Criteria.Sum(c => c.Score);
        if (g.Criteria.Count > 0 && g.Max == 0) g.Max = g.Criteria.Sum(c => c.Max);
        return g;
    }

    public static List<TaskItem> ParseGeneratedTasks(string answer, string exam, int num)
    {
        var block = ExtractLastBlock(answer.Replace("```json", "```")) ?? throw new FormatException("Нет JSON");
        block = StripSearchLinks(block).Trim();
        var arr = JsonNode.Parse(block.StartsWith("[") ? block : (ExtractArray(answer) ?? block));
        var list = new List<TaskItem>();
        var items = arr is JsonArray a ? a : arr?["tasks"] as JsonArray;
        if (items == null) throw new FormatException("Ожидался массив заданий");
        foreach (var n in items)
        {
            if (n == null) continue;
            var ans = n["answers"] is JsonArray aa ? aa.Select(x => x?.ToString() ?? "").ToList() : new List<string> { Str(n["answer"]) };
            var t = new TaskItem { Id = $"g-{exam}-{num}-{Guid.NewGuid():N}"[..24], Exam = exam, Num = num, Kind = Str(n["kind"]) is var k && k.Length > 0 ? k : "word",
                Prompt = Str(n["prompt"]), Answers = ans.Where(x => x.Length > 0).ToList(), Explain = Str(n["explain"]), Generated = true };
            if (t.Prompt.Length > 0 && t.Answers.Count > 0) list.Add(t);
        }
        return list;
    }
    static string? ExtractArray(string s) { int i = s.IndexOf('['), j = s.LastIndexOf(']'); return i >= 0 && j > i ? s[i..(j + 1)] : null; }

    static int Int(JsonNode? n) { try { if (n == null) return 0; if (n is JsonValue v && v.TryGetValue<double>(out var d)) return (int)Math.Round(d); return int.TryParse(n.ToString(), out var i) ? i : 0; } catch { return 0; } }
    static string Str(JsonNode? n) => n == null ? "" : n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString();
}

public static class Prompts
{
    const string JsonRule = "Ответь СТРОГО одним блоком кода ```json ... ``` без текста после него. Не используй поиск в интернете и ссылки.";

    public const string EgeCriteria = "К1 позиция автора (0–1); К2 комментарий: 2 примера-иллюстрации с пояснениями, смысловая связь и её пояснение (0–3); К3 собственное отношение с обоснованием (0–2); К4 фактическая точность (0–1); К5 логичность, 1 балл при 1–2 логических ошибках (0–2); К6 этические нормы (0–1); К7 орфография (0–3); К8 пунктуация (0–3); К9 грамматика (0–3); К10 речевые нормы (0–3). Для К7–К10: нет ошибок — 3, 1–2 — 2, 3–4 — 1, 5 и более — 0. Если в сочинении менее 150 слов — 0 баллов за всё. Максимум 22.";
    public const string OgeEssayCriteria = "СК1 ответ на вопрос (0–1); СК2 примеры из текста/опыта (0–3); СК3 логичность, 1 балл при 1–2 логических ошибках (0–2); СК4 композиция (0–1). Грамотность: ГК1 орфография (0–3), ГК2 пунктуация (0–3), ГК3 грамматика (0–3), ГК4 речь (0–3), ФК1 фактическая точность (0–1). Если менее 70 слов, пересказ или не по тексту — СК1–СК4 = 0.";
    public const string IzlCriteria = "ИК1 содержание: переданы все микротемы (0–2); ИК2 сжатие: применены приёмы сжатия (0–3); ИК3 смысловая цельность, связность, последовательность (0–1). Если менее 70 слов — 0. Максимум 6.";

    public static string Essay(string kind, string sourceText, string topic, string essay)
    {
        var (crit, max) = kind switch { "ЕГЭ27" => (EgeCriteria, 22), "ОГЭ13" => (OgeEssayCriteria, 20), _ => (IzlCriteria, 6) };
        var title = kind switch { "ЕГЭ27" => "сочинение ЕГЭ по русскому языку (задание 27)", "ОГЭ13" => "сочинение-рассуждение ОГЭ (задание 13)", _ => "сжатое изложение ОГЭ (задание 1)" };
        return $"Ты эксперт предметной комиссии ФИПИ. Проверь {title} по критериям ФИПИ 2026 года.\nКритерии: {crit}\n" +
               (string.IsNullOrWhiteSpace(sourceText) ? "" : $"\nИсходный текст:\n\"\"\"\n{sourceText.Trim()}\n\"\"\"\n") +
               (string.IsNullOrWhiteSpace(topic) ? "" : $"\nФормулировка задания/проблема: {topic.Trim()}\n") +
               $"\nРабота ученика ({AnswerChecker.WordCount(essay)} слов):\n\"\"\"\n{essay.Trim()}\n\"\"\"\n\n" +
               "Верни JSON вида {\"score\":число,\"max\":" + max + ",\"criteria\":[{\"id\":\"К1\",\"score\":0,\"max\":1,\"comment\":\"...\"}],\"errors\":[{\"type\":\"орфография|пунктуация|грамматика|речь|логика|факт\",\"fragment\":\"цитата\",\"fix\":\"как правильно\",\"rule\":\"правило\"}],\"explanation\":\"общий отзыв и советы\"}.\n" + JsonRule;
    }

    public static string ExplainMistake(string fullPrompt, string studentAnswer, string correct) =>
        $"Ты репетитор по русскому языку. Ученик решал задание:\n\"\"\"\n{fullPrompt}\n\"\"\"\nОтвет ученика: {studentAnswer}\nПравильный ответ: {correct}\n" +
        "Объясни простыми словами, в чём ошибка и какое правило применить, приведи 2 похожих примера.\n" +
        "Верни JSON {\"score\":0 или 1,\"max\":1,\"errors\":[{\"type\":\"...\",\"fragment\":\"...\",\"fix\":\"...\",\"rule\":\"...\"}],\"explanation\":\"...\"}.\n" + JsonRule;

    public static string CheckFreeAnswer(string fullPrompt, string studentAnswer) =>
        $"Проверь ответ ученика на задание по русскому языку.\nЗадание:\n\"\"\"\n{fullPrompt}\n\"\"\"\nОтвет ученика: {studentAnswer}\n" +
        "Верни JSON {\"score\":0 или 1,\"max\":1,\"errors\":[...],\"explanation\":\"...\"}.\n" + JsonRule;

    public static string Generate(string exam, int num, string title, int count, string sample) =>
        $"Составь {count} новых оригинальных тренировочных заданий для {exam} по русскому языку, задание №{num} «{title}», в формате ФИПИ 2026. Не копируй задания из открытых банков, придумай свои.\n" +
        $"Пример формата задания:\n\"\"\"\n{sample}\n\"\"\"\n" +
        "Верни JSON-массив [{\"kind\":\"digits|ordered|word\",\"prompt\":\"полный текст задания с вариантами\",\"answers\":[\"правильный ответ\"],\"explain\":\"краткое объяснение\"}]. " +
        "kind=digits — ответ набор цифр в любом порядке, ordered — цифры по порядку, word — слово. " + JsonRule;

    public static string Handwriting() =>
        "На изображении рукописный текст на русском языке. Распознай его дословно, сохраняя орфографию и пунктуацию автора (ошибки НЕ исправляй). " +
        "Верни JSON {\"text\":\"распознанный текст\"}. " + JsonRule;
}

/// <summary>Официальный GigaChat API (developers.sber.ru): OAuth по ключу авторизации → access token (30 мин) → chat/completions.</summary>
public sealed class GigaChatApi : IAiBackend
{
    public string Name => "GigaChat API";
    readonly string _authKey, _scope, _model; readonly HttpClient _http;
    string? _token; DateTime _tokenExp;
    public const string OAuthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    public const string ApiBase = "https://gigachat.devices.sberbank.ru/api/v1/";

    /// <param name="trustAllCerts">Серверы GigaChat используют сертификат НУЦ Минцифры; если он не установлен в Windows, можно разрешить его без проверки (небезопасно).</param>
    public GigaChatApi(string authKey, string scope = "GIGACHAT_API_PERS", string model = "GigaChat-Max", bool trustAllCerts = false)
    {
        _authKey = authKey.Trim(); _scope = scope; _model = model;
        var h = new HttpClientHandler();
        if (trustAllCerts) h.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(120) };
    }

    async Task<string> Token(CancellationToken ct)
    {
        if (_token != null && DateTime.UtcNow < _tokenExp) return _token;
        using var req = new HttpRequestMessage(HttpMethod.Post, OAuthUrl) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["scope"] = _scope }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authKey);
        req.Headers.Add("RqUID", Guid.NewGuid().ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var r = await _http.SendAsync(req, ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"GigaChat OAuth: HTTP {(int)r.StatusCode} {body}");
        using var d = JsonDocument.Parse(body);
        _token = d.RootElement.GetProperty("access_token").GetString();
        _tokenExp = DateTime.UtcNow.AddMinutes(25);
        return _token!;
    }

    public Task<string> AskAsync(string prompt, CancellationToken ct) => Chat(prompt, null, ct);

    public async Task<string> AskWithImageAsync(string prompt, byte[] png, CancellationToken ct)
    {
        var tok = await Token(ct);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "handwriting.png"); form.Add(new StringContent("general"), "purpose");
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + "files") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);
        using var r = await _http.SendAsync(req, ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"GigaChat files: HTTP {(int)r.StatusCode} {body}");
        using var d = JsonDocument.Parse(body);
        return await Chat(prompt, d.RootElement.GetProperty("id").GetString(), ct);
    }

    async Task<string> Chat(string prompt, string? fileId, CancellationToken ct)
    {
        var tok = await Token(ct);
        var msg = new JsonObject { ["role"] = "user", ["content"] = prompt };
        if (fileId != null) msg["attachments"] = new JsonArray(fileId);
        var payload = new JsonObject { ["model"] = _model, ["messages"] = new JsonArray(msg), ["temperature"] = 0.2 };
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + "chat/completions") { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);
        using var r = await _http.SendAsync(req, ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"GigaChat: HTTP {(int)r.StatusCode} {body}");
        using var d = JsonDocument.Parse(body);
        return d.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
