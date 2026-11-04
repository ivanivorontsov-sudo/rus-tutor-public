using RusTutor.Core;
using Xunit;

public class CoreTests
{
    static readonly Content C = Content.Load();

    [Fact] public void BankLoads()
    {
        Assert.True(C.Tasks.Count >= 60);
        Assert.Equal(27, C.Numbers.Count(n => n.Exam == "ЕГЭ"));
        Assert.Equal(13, C.Numbers.Count(n => n.Exam == "ОГЭ"));
        for (int i = 1; i <= 26; i++) Assert.NotEmpty(C.For("ЕГЭ", i));
        for (int i = 2; i <= 12; i++) Assert.NotEmpty(C.For("ОГЭ", i));
        Assert.Equal(C.Tasks.Count, C.Tasks.Select(t => t.Id).Distinct().Count());
        foreach (var t in C.Tasks)
        {
            Assert.NotEmpty(t.Answers);
            Assert.True(C.Theory.Any(x => x.Id == t.Theory), "нет теории " + t.Theory);
            Assert.True(AnswerChecker.Check(t, t.Answers[0]), t.Id);
            if (t.Text != null) Assert.True(C.Texts.ContainsKey(t.Text));
        }
    }

    [Fact] public void Checker()
    {
        var d = new TaskItem { Kind = "digits", Answers = { "235" } };
        Assert.True(AnswerChecker.Check(d, "5 3 2")); Assert.True(AnswerChecker.Check(d, "3,2,5"));
        Assert.False(AnswerChecker.Check(d, "23")); Assert.False(AnswerChecker.Check(d, "2345"));
        var o = new TaskItem { Kind = "ordered", Answers = { "134" } };
        Assert.True(AnswerChecker.Check(o, "134")); Assert.False(AnswerChecker.Check(o, "431"));
        var w = new TaskItem { Kind = "word", Answers = { "тем не менее" } };
        Assert.True(AnswerChecker.Check(w, "Тем не менее")); Assert.True(AnswerChecker.Check(w, "темнеменее"));
        var e = new TaskItem { Kind = "word", Answers = { "свёкла" } };
        Assert.True(AnswerChecker.Check(e, "свекла"));
        Assert.Equal(5, AnswerChecker.WordCount("Мама мыла раму, кто-то пришёл."));
    }

    [Fact] public void ParsesWebAnswerWithLinksAndText()
    {
        var ans = "Вот проверка [1](https://example.com/a):\n```json\n{\"score\": 15, \"max\": 22, \"criteria\":[{\"id\":\"К1\",\"score\":1,\"max\":1,\"comment\":\"ок [2]\"}],\n \"errors\":[{\"type\":\"пунктуация\",\"fragment\":\"однако он\",\"fix\":\"однако, он\",\"rule\":\"вводное слово\"}], \"explanation\":\"Хорошо, см. https://ru.wikipedia.org/wiki/X\",}\n```\nИсточники: [1] сайт";
        var g = AiParser.Parse(ans);
        Assert.Equal(15, g.Score); Assert.Equal(22, g.Max);
        Assert.Single(g.Criteria); Assert.Equal("ок ", g.Criteria[0].Comment);
        Assert.Equal("однако, он", g.Errors[0].Fix);
        Assert.DoesNotContain("http", g.Explanation);
    }

    [Fact] public void TakesLastBlockAndBareJson()
    {
        Assert.Equal("{\"b\":2}", AiParser.ExtractLastBlock("```\n{\"a\":1}\n```\nтекст\n```json\n{\"b\":2}\n```"));
        var g = AiParser.Parse("Оценка: {\"score\": 1, \"max\": 1, \"explanation\": \"верно {в скобках}\"} конец");
        Assert.Equal(1, g.Score);
        Assert.Equal("распознано", AiParser.Parse("```json\n{\"text\":\"распознано\"}\n```").Recognized);
        Assert.Throws<FormatException>(() => AiParser.Parse("нет json"));
    }

    [Fact] public void GeneratedTasks()
    {
        var items = AiParser.ParseGeneratedTasks("```json\n[{\"kind\":\"digits\",\"prompt\":\"1) а 2) б\",\"answers\":[\"12\"],\"explain\":\"x\"},{\"prompt\":\"\",\"answers\":[]}]\n```", "ЕГЭ", 4);
        Assert.Single(items); Assert.Equal(4, items[0].Num); Assert.True(items[0].Generated);
    }

    [Fact] public void SrsAndAdaptive()
    {
        var now = new DateTime(2026, 10, 13, 9, 0, 0);
        var p = new Progress();
        var t = C.For("ЕГЭ", 4).First();
        p.Record(t, false, "x", now);
        Assert.Single(p.DueCards(now));
        p.Record(t, true, t.Answers[0], now);
        var c = p.Cards[t.Id];
        Assert.Equal(1, c.IntervalDays); Assert.Empty(p.DueCards(now));
        Srs.Review(c, 4, now); Assert.Equal(3, c.IntervalDays);
        Srs.Review(c, 4, now); Assert.True(c.IntervalDays >= 7);
        Srs.Review(c, 1, now); Assert.Equal(1, c.IntervalDays); Assert.Equal(1, c.Lapses);
        Assert.Equal(0.5, p.Mastery("ЕГЭ", 4));
        var pick = Adaptive.Pick(C, p, "ЕГЭ", 10, new Random(1));
        Assert.Equal(10, pick.Count); Assert.Equal(10, pick.Distinct().Count());
        var v = Adaptive.MockVariant(C, "ЕГЭ", new Random(2));
        Assert.Equal(26, v.Count);
        Assert.Equal(11, Adaptive.MockVariant(C, "ОГЭ", new Random(2)).Count);
    }

    [Fact] public void PromptsContainCriteria()
    {
        var p = Prompts.Essay("ЕГЭ27", "Текст", "Проблема", "Сочинение ученика");
        Assert.Contains("К10", p); Assert.Contains("\"max\":22", p); Assert.Contains("```json", p);
        Assert.Contains("СК4", Prompts.Essay("ОГЭ13", "", "", "x"));
    }

    [Fact] public void FlacEncodes()
    {
        var f = FlacEncoder.Encode(new short[16000]);
        Assert.Equal((byte)'f', f[0]); Assert.True(f.Length > 32000);
    }
}
