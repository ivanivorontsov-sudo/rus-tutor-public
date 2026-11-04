using System.Text.Json;
namespace RusTutor.Core;

public sealed class Attempt { public string TaskId { get; set; } = ""; public string Exam { get; set; } = ""; public int Num { get; set; } public bool Ok { get; set; } public DateTime At { get; set; } public string Answer { get; set; } = ""; }

/// <summary>Карточка интервального повторения (алгоритм SM-2).</summary>
public sealed class SrsCard
{
    public string TaskId { get; set; } = "";
    public double Ease { get; set; } = 2.5;
    public int IntervalDays { get; set; }
    public int Reps { get; set; }
    public DateTime Due { get; set; }
    public int Lapses { get; set; }
}

public sealed class ExamResult { public string Exam { get; set; } = ""; public DateTime At { get; set; } public int Correct { get; set; } public int Total { get; set; } public int Primary { get; set; } public int MaxPrimary { get; set; } public TimeSpan Spent { get; set; } }
public sealed class EssayResult { public string Kind { get; set; } = ""; public DateTime At { get; set; } public int Score { get; set; } public int Max { get; set; } public string Summary { get; set; } = ""; }

public sealed class Progress
{
    public List<Attempt> Attempts { get; set; } = new();
    public Dictionary<string, SrsCard> Cards { get; set; } = new();
    public List<ExamResult> Exams { get; set; } = new();
    public List<EssayResult> Essays { get; set; } = new();

    public static Progress Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<Progress>(File.ReadAllText(path), Content.Json) ?? new(); } catch { }
        return new();
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, Content.Json)); File.Move(tmp, path, true);
    }

    public void Record(TaskItem t, bool ok, string answer, DateTime now)
    {
        Attempts.Add(new Attempt { TaskId = t.Id, Exam = t.Exam, Num = t.Num, Ok = ok, At = now, Answer = answer });
        if (Cards.TryGetValue(t.Id, out var c)) Srs.Review(c, ok ? 4 : 1, now);
        else if (!ok) Cards[t.Id] = Srs.NewCard(t.Id, now);
    }

    public IEnumerable<SrsCard> DueCards(DateTime now) => Cards.Values.Where(c => c.Due <= now).OrderBy(c => c.Due);

    /// <summary>Доля верных по номеру (с последних 20 попыток), null если попыток нет.</summary>
    public double? Mastery(string exam, int num)
    {
        var a = Attempts.Where(x => x.Exam == exam && x.Num == num).TakeLast(20).ToList();
        return a.Count == 0 ? null : a.Count(x => x.Ok) / (double)a.Count;
    }

    public int Streak(DateTime today)
    {
        var days = Attempts.Select(a => a.At.Date).ToHashSet(); int n = 0;
        for (var d = today.Date; days.Contains(d); d = d.AddDays(-1)) n++;
        return n;
    }
}

public static class Srs
{
    public static SrsCard NewCard(string id, DateTime now) => new() { TaskId = id, Due = now, IntervalDays = 0, Reps = 0 };

    /// <summary>q: 0..5 (SM-2). q &lt; 3 — сброс на повтор завтра.</summary>
    public static void Review(SrsCard c, int q, DateTime now)
    {
        if (q < 3) { c.Reps = 0; c.IntervalDays = 1; c.Lapses++; }
        else
        {
            c.Reps++;
            c.IntervalDays = c.Reps switch { 1 => 1, 2 => 3, _ => (int)Math.Round(Math.Max(1, c.IntervalDays) * c.Ease) };
        }
        c.Ease = Math.Max(1.3, c.Ease + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02)));
        c.Due = now.Date.AddDays(c.IntervalDays);
    }
}

/// <summary>Адаптивный подбор заданий: слабые номера и непройденные задания — чаще.</summary>
public static class Adaptive
{
    public static List<TaskItem> Pick(Content c, Progress p, string exam, int count, Random rnd)
    {
        var pool = c.Tasks.Where(t => t.Exam == exam).ToList();
        if (pool.Count == 0) return new();
        var weights = pool.Select(t =>
        {
            var m = p.Mastery(t.Exam, t.Num) ?? 0.3;           // неизвестный номер — считаем слабым
            var seen = p.Attempts.Where(a => a.TaskId == t.Id).ToList();
            double w = 0.15 + (1 - m) * 2;
            if (seen.Count == 0) w += 0.5;
            else if (!seen[^1].Ok) w += 1.0;
            return w;
        }).ToArray();
        var res = new List<TaskItem>();
        var used = new HashSet<int>();
        while (res.Count < Math.Min(count, pool.Count))
        {
            double total = 0; for (int i = 0; i < pool.Count; i++) if (!used.Contains(i)) total += weights[i];
            double r = rnd.NextDouble() * total;
            for (int i = 0; i < pool.Count; i++)
            {
                if (used.Contains(i)) continue;
                r -= weights[i];
                if (r <= 0) { used.Add(i); res.Add(pool[i]); break; }
            }
        }
        return res;
    }

    /// <summary>Пробный вариант: одно задание на каждый номер с кратким ответом.</summary>
    public static List<TaskItem> MockVariant(Content c, string exam, Random rnd) =>
        c.Tasks.Where(t => t.Exam == exam).GroupBy(t => t.Num).OrderBy(g => g.Key)
            .Select(g => g.ElementAt(rnd.Next(g.Count()))).ToList();

    public static TimeSpan ExamDuration(string exam) => TimeSpan.FromMinutes(235); // ЕГЭ и ОГЭ по русскому: 3 ч 55 мин
}
