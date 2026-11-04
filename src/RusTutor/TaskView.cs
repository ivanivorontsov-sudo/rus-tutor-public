using System.Windows;
using System.Windows.Controls;
using RusTutor.Core;
namespace RusTutor;

/// <summary>Показ одного задания: условие, ответ (клавиатура/голос/рукопись), проверка, объяснение, разбор через GigaChat.</summary>
sealed class TaskView : StackPanel
{
    public event Action<TaskItem, bool> Answered;
    public event Action Next;
    TaskItem _t; bool _done;
    readonly TextBlock _head = Ui.H("", 16), _prompt = Ui.T("", 15), _fb = Ui.T("", 14), _ai = Ui.T("", 14);
    readonly TextBox _ans = Ui.Box();
    readonly Button _check, _next, _explain, _theory;

    public TaskView()
    {
        _check = Ui.B("Проверить", Check, true);
        _next = Ui.B("Следующее →", () => Next?.Invoke());
        _explain = Ui.B("Объяснить через GigaChat", async () => await Explain());
        _theory = Ui.B("Теория", () => { if (_t != null) App.Main.OpenTheory(_t.Theory); });
        _ans.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { if (_done) Next?.Invoke(); else Check(); } };
        _prompt.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        Children.Add(_head); Children.Add(_prompt);
        Children.Add(Ui.T("Ответ:", 13)); Children.Add(Ui.InputWithTools(_ans));
        Children.Add(Ui.Row(_check, _next, _explain, _theory));
        Children.Add(_fb); Children.Add(_ai);
        Show(null);
    }

    public void Show(TaskItem t)
    {
        _t = t; _done = false; _ans.Text = ""; _fb.Text = ""; _ai.Text = "";
        _check.IsEnabled = t != null; _explain.IsEnabled = false; _theory.IsEnabled = t != null && !string.IsNullOrEmpty(t.Theory);
        if (t == null) { _head.Text = "Заданий нет"; _prompt.Text = "Выберите номер или запустите тренировку."; return; }
        var n = App.Content.Number(t.Exam, t.Num);
        _head.Text = $"{t.Exam}, задание {t.Num}. {n?.Title}" + (t.Generated ? " (сгенерировано)" : "");
        _prompt.Text = App.Content.FullPrompt(t);
        _ans.Focus();
    }

    void Check()
    {
        if (_t == null || _done) return;
        var ok = AnswerChecker.Check(_t, _ans.Text);
        _done = true; _explain.IsEnabled = !ok;
        _fb.Foreground = ok ? Ui.Good : Ui.Bad;
        _fb.Text = (ok ? "✔ Верно! " : $"✘ Неверно. Правильный ответ: {string.Join(" / ", _t.Answers)}. ") + "\n" + _t.Explain;
        App.Progress.Record(_t, ok, _ans.Text, DateTime.Now);
        App.SaveProgress();
        Answered?.Invoke(_t, ok);
    }

    async Task Explain()
    {
        _explain.IsEnabled = false; _ai.Text = "Спрашиваю GigaChat…";
        try
        {
            var g = await AiHub.Grade(Prompts.ExplainMistake(App.Content.FullPrompt(_t), _ans.Text, string.Join(" / ", _t.Answers)));
            _ai.Text = "🤖 " + g.Explanation + string.Concat(g.Errors.Select(e => $"\n• {e.Fragment} → {e.Fix} ({e.Rule})"));
        }
        catch (Exception e) { _ai.Text = "GigaChat: " + e.Message; _explain.IsEnabled = true; }
    }
}
