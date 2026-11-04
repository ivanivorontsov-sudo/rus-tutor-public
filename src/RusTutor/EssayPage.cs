using System.Windows;
using System.Windows.Controls;
using RusTutor.Core;
namespace RusTutor;

sealed class EssayPage : ContentControl, IRefreshable
{
    readonly ComboBox _kind = Ui.Combo("ЕГЭ, задание 27 — сочинение", "ОГЭ, задание 13 — сочинение-рассуждение", "ОГЭ, задание 1 — сжатое изложение");
    readonly TextBox _source = Ui.Box(true, 160), _topic = Ui.Box(), _essay = Ui.Box(true, 320);
    readonly TextBlock _count = Ui.T("", 13), _crit = Ui.T("", 12);
    readonly StackPanel _result = new();
    readonly Button _check;
    static readonly string[] Kinds = { "ЕГЭ27", "ОГЭ13", "ОГЭ1" };

    public EssayPage()
    {
        _check = Ui.B("Проверить по критериям ФИПИ (GigaChat)", async () => await Check(), true);
        _essay.TextChanged += (_, _) => UpdateCount();
        _kind.SelectionChanged += (_, _) => { UpdateCount(); _crit.Text = CritText(); };
        _crit.Foreground = Ui.Muted; _crit.Text = CritText();
        Content = Ui.Scroll(Ui.V(
            Ui.H("Сочинение и изложение"), Ui.Row(Ui.T("Тип работы: "), _kind), _crit,
            Ui.T("Исходный текст (вставьте текст из варианта; для изложения — текст аудиозаписи):"), Ui.InputWithTools(_source),
            Ui.T("Формулировка задания / проблема (необязательно):"), _topic,
            Ui.T("Ваша работа (клавиатура, 🎤 голос или ✍ рукописный ввод):"), Ui.InputWithTools(_essay), _count,
            Ui.Row(_check, Ui.B("Теория по критериям", () => App.Main.OpenTheory(Kind == "ЕГЭ27" ? "ege27" : Kind == "ОГЭ13" ? "oge13" : "izlozhenie"))),
            _result));
        UpdateCount();
    }
    public void Refresh() { }
    string Kind => Kinds[_kind.SelectedIndex];
    int MinWords => Kind == "ЕГЭ27" ? 150 : 70;
    string CritText() => Kind switch { "ЕГЭ27" => Prompts.EgeCriteria, "ОГЭ13" => Prompts.OgeEssayCriteria, _ => Prompts.IzlCriteria };

    void UpdateCount()
    {
        var n = AnswerChecker.WordCount(_essay.Text);
        _count.Text = $"Слов: {n} (минимум {MinWords})";
        _count.Foreground = n < MinWords ? Ui.Bad : Ui.Good;
    }

    async Task Check()
    {
        if (AnswerChecker.WordCount(_essay.Text) < 20) { MessageBox.Show("Сначала напишите работу."); return; }
        _check.IsEnabled = false; _result.Children.Clear(); _result.Children.Add(Ui.T("Проверяю… это может занять до пары минут."));
        try
        {
            var g = await AiHub.Grade(Prompts.Essay(Kind, _source.Text, _topic.Text, _essay.Text));
            _result.Children.Clear();
            _result.Children.Add(Ui.H($"Итог: {g.Score} из {g.Max}", 18));
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = g.Criteria, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 4, 0, 8) };
            grid.Columns.Add(new DataGridTextColumn { Header = "Критерий", Binding = new System.Windows.Data.Binding("Id") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Балл", Binding = new System.Windows.Data.Binding("Score") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Макс.", Binding = new System.Windows.Data.Binding("Max") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Комментарий", Binding = new System.Windows.Data.Binding("Comment"), Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } } });
            _result.Children.Add(grid);
            if (g.Errors.Count > 0)
            {
                _result.Children.Add(Ui.H("Ошибки", 16));
                foreach (var e in g.Errors) _result.Children.Add(Ui.T($"• [{e.Type}] «{e.Fragment}» → {e.Fix}" + (string.IsNullOrEmpty(e.Rule) ? "" : $" — {e.Rule}")));
            }
            _result.Children.Add(Ui.H("Отзыв", 16)); _result.Children.Add(Ui.T(g.Explanation));
            var warn = Ui.T("Оценка ИИ ориентировочная и не заменяет эксперта.", 12); warn.Foreground = Ui.Muted; _result.Children.Add(warn);
            App.Progress.Essays.Add(new EssayResult { Kind = Kind, At = DateTime.Now, Score = g.Score, Max = g.Max, Summary = g.Explanation });
            App.SaveProgress();
        }
        catch (Exception e) { _result.Children.Clear(); _result.Children.Add(Ui.T("Ошибка проверки: " + e.Message)); }
        finally { _check.IsEnabled = true; }
    }
}
