using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RusTutor.Core;
namespace RusTutor;

sealed class DashboardPage : ContentControl, IRefreshable
{
    public DashboardPage() { Loaded += (_, _) => Refresh(); }
    public void Refresh()
    {
        var p = App.Progress; var now = DateTime.Now;
        var root = Ui.V(Ui.H("Добро пожаловать! Подготовка к ОГЭ и ЕГЭ по русскому языку", 22));
        int total = p.Attempts.Count, ok = p.Attempts.Count(a => a.Ok);
        root.Children.Add(Ui.T($"Решено заданий: {total}, верно: {ok} ({(total == 0 ? 0 : ok * 100 / total)}%). Дней подряд: {p.Streak(now)}. " +
                               $"На повторение сегодня: {p.DueCards(now).Count()} карточек.", 15));
        root.Children.Add(Ui.Row(
            Ui.B("Умная тренировка ЕГЭ", () => App.Main.SelectedTab = FindTab<TrainerPage>(t => t.StartSmart("ЕГЭ")), true),
            Ui.B("Умная тренировка ОГЭ", () => App.Main.SelectedTab = FindTab<TrainerPage>(t => t.StartSmart("ОГЭ")), true),
            Ui.B("Повторить ошибки", () => App.Main.SelectedTab = FindTab<ReviewPage>(_ => { }))));
        foreach (var exam in new[] { "ЕГЭ", "ОГЭ" })
        {
            root.Children.Add(Ui.H($"Освоение номеров {exam}", 17));
            var grid = new UniformGrid4();
            foreach (var n in App.Content.Numbers.Where(n => n.Exam == exam))
            {
                var m = p.Mastery(exam, n.Num);
                var cnt = App.Content.For(exam, n.Num).Count();
                var bar = new ProgressBar { Value = (m ?? 0) * 100, Height = 10, Width = 120, Margin = new Thickness(0, 2, 0, 0), Foreground = m >= 0.8 ? Ui.Good : m < 0.5 ? Ui.Bad : Ui.Accent };
                var cell = Ui.V(Ui.T($"№{n.Num}. {n.Title}", 12), bar, Ui.T(m == null ? (cnt == 0 ? "письменная часть" : "не начато") : $"{m:P0}", 11));
                cell.Margin = new Thickness(0, 4, 16, 8); cell.Width = 250;
                grid.Children.Add(cell);
            }
            root.Children.Add(grid);
        }
        if (p.Exams.Count > 0)
        {
            root.Children.Add(Ui.H("Пробные экзамены", 17));
            foreach (var e in p.Exams.TakeLast(10).Reverse()) root.Children.Add(Ui.T($"{e.At:dd.MM.yyyy HH:mm} — {e.Exam}: верно {e.Correct} из {e.Total}, баллы {e.Primary}/{e.MaxPrimary}, время {e.Spent:hh\\:mm\\:ss}"));
        }
        if (p.Essays.Count > 0)
        {
            root.Children.Add(Ui.H("Сочинения и изложения", 17));
            foreach (var e in p.Essays.TakeLast(10).Reverse()) root.Children.Add(Ui.T($"{e.At:dd.MM.yyyy HH:mm} — {e.Kind}: {e.Score}/{e.Max}"));
        }
        var src = Ui.T("Структура и критерии — по демоверсиям и спецификациям ФИПИ 2026 (fipi.ru). Задания в банке авторские, составлены по образцу формата ФИПИ.", 12); src.Foreground = Ui.Muted;
        root.Children.Add(src);
        Content = Ui.Scroll(root);
    }
    static object FindTab<T>(Action<T> act) where T : class
    {
        foreach (TabItem t in ((TabControl)((DockPanel)App.Main.Content).Children[1]).Items) if (t.Content is T page) { act(page); return t; }
        return null;
    }
}

sealed class UniformGrid4 : WrapPanel { }

sealed class TrainerPage : Grid, IRefreshable
{
    readonly ComboBox _exam = Ui.Combo("ЕГЭ", "ОГЭ");
    readonly ListBox _nums = new() { FontSize = 13 };
    readonly TaskView _view = new();
    readonly TextBlock _session = Ui.T("", 12);
    Queue<TaskItem> _queue = new(); int _sOk, _sAll;
    readonly Random _rnd = new();

    public TrainerPage()
    {
        _exam.SelectionChanged += (_, _) => Refresh();
        _nums.SelectionChanged += (_, _) => { if (_nums.SelectedItem is ListBoxItem { Tag: ExamNumber n }) StartNumber(n); };
        var left = new DockPanel { Margin = new Thickness(8) };
        var top = Ui.V(Ui.Row(Ui.T("Экзамен: "), _exam),
            Ui.B("🎯 Умная тренировка (10)", () => StartSmart(Exam), true),
            Ui.B("✨ Сгенерировать задания (GigaChat)", async () => await Generate()),
            _session);
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top); left.Children.Add(_nums);
        _view.Answered += (_, ok) => { _sAll++; if (ok) _sOk++; _session.Text = $"Сессия: {_sOk} из {_sAll} верно"; };
        _view.Next += () => _view.Show(_queue.Count > 0 ? _queue.Dequeue() : null);
        Children.Add(Ui.Split(left, Ui.Scroll(_view), 330));
        Refresh();
    }
    string Exam => (string)_exam.SelectedItem;

    public void Refresh()
    {
        var sel = (_nums.SelectedItem as ListBoxItem)?.Tag as ExamNumber;
        _nums.Items.Clear();
        foreach (var n in App.Content.Numbers.Where(n => n.Exam == Exam))
        {
            var cnt = App.Content.For(Exam, n.Num).Count(); if (cnt == 0) continue;
            var m = App.Progress.Mastery(Exam, n.Num);
            _nums.Items.Add(new ListBoxItem { Content = $"№{n.Num}. {n.Title}  [{cnt}]  {(m == null ? "" : $"{m:P0}")}", Tag = n });
        }
    }

    void StartNumber(ExamNumber n)
    {
        _queue = new Queue<TaskItem>(App.Content.For(n.Exam, n.Num).OrderBy(_ => _rnd.Next()));
        _view.Show(_queue.Count > 0 ? _queue.Dequeue() : null);
    }

    public void StartSmart(string exam)
    {
        _exam.SelectedItem = exam;
        _queue = new Queue<TaskItem>(Adaptive.Pick(App.Content, App.Progress, exam, 10, _rnd));
        _sOk = _sAll = 0; _session.Text = "Умная тренировка: слабые номера идут первыми";
        _view.Show(_queue.Count > 0 ? _queue.Dequeue() : null);
    }

    async Task Generate()
    {
        if (_nums.SelectedItem is not ListBoxItem { Tag: ExamNumber n }) { MessageBox.Show("Сначала выберите номер задания слева."); return; }
        var sample = App.Content.For(n.Exam, n.Num).FirstOrDefault();
        try
        {
            var ans = await AiHub.Ask(Prompts.Generate(n.Exam, n.Num, n.Title, 5, sample == null ? "" : App.Content.FullPrompt(sample) + "\nОтвет: " + string.Join("/", sample.Answers)));
            var items = AiParser.ParseGeneratedTasks(ans, n.Exam, n.Num);
            foreach (var t in items) t.Theory = sample?.Theory ?? "";
            if (items.Count == 0) { MessageBox.Show("GigaChat не вернул заданий."); return; }
            Content.AppendExtra(AppSettings.ExtraTasksPath, items);
            App.ReloadContent(); Refresh();
            _queue = new Queue<TaskItem>(items); _view.Show(_queue.Dequeue());
            App.Main.Status($"Добавлено заданий: {items.Count}. Проверяйте ответы ИИ — он может ошибаться.");
        }
        catch (Exception e) { MessageBox.Show("Генерация: " + e.Message); }
    }
}

sealed class ReviewPage : Grid, IRefreshable
{
    readonly TaskView _view = new();
    readonly TextBlock _info = Ui.T("", 14);
    readonly ListBox _hist = new() { FontSize = 12 };
    Queue<TaskItem> _q = new();
    public ReviewPage()
    {
        var left = new DockPanel { Margin = new Thickness(8) };
        var top = Ui.V(Ui.H("Интервальное повторение", 16), _info, Ui.B("Начать повторение", Start, true), Ui.T("Последние ошибки:", 12));
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top); left.Children.Add(_hist);
        _view.Next += () => { _view.Show(_q.Count > 0 ? _q.Dequeue() : null); Refresh(); };
        Children.Add(Ui.Split(left, Ui.Scroll(_view), 360));
        Refresh();
    }
    public void Refresh()
    {
        var due = App.Progress.DueCards(DateTime.Now).Count();
        var next = App.Progress.Cards.Values.Where(c => c.Due > DateTime.Now).OrderBy(c => c.Due).FirstOrDefault();
        _info.Text = $"К повторению сейчас: {due}. Всего карточек: {App.Progress.Cards.Count}." + (next != null ? $" Следующая: {next.Due:dd.MM}" : "");
        _hist.Items.Clear();
        foreach (var a in App.Progress.Attempts.Where(a => !a.Ok).TakeLast(50).Reverse())
            _hist.Items.Add($"{a.At:dd.MM HH:mm} {a.Exam} №{a.Num}: «{a.Answer}»");
    }
    void Start()
    {
        var ids = App.Progress.DueCards(DateTime.Now).Select(c => c.TaskId).ToList();
        _q = new Queue<TaskItem>(ids.Select(id => App.Content.Tasks.FirstOrDefault(t => t.Id == id)).Where(t => t != null));
        if (_q.Count == 0) { MessageBox.Show("Сегодня повторять нечего — отличная работа! Ошибки из тренажёра появятся здесь."); return; }
        _view.Show(_q.Dequeue());
    }
}

sealed class ExamPage : ContentControl, IRefreshable
{
    readonly ComboBox _exam = Ui.Combo("ЕГЭ", "ОГЭ");
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock _clock = Ui.H("", 18);
    List<(TaskItem t, TextBox box, TextBlock fb)> _items = new();
    DateTime _start; TimeSpan _limit; bool _running;

    public ExamPage() { _timer.Tick += (_, _) => Tick(); Intro(); }
    public void Refresh() { }

    void Intro()
    {
        Content = Ui.Scroll(Ui.V(Ui.H("Пробный экзамен с таймером"),
            Ui.T("Вариант собирается из банка: по одному заданию на каждый номер с кратким ответом. Время — 3 ч 55 мин, как на экзамене. " +
                 "Сочинение (ЕГЭ №27, ОГЭ №13) и изложение (ОГЭ №1) пишите в разделе «Сочинение и изложение» — там их проверит GigaChat по критериям ФИПИ."),
            Ui.Row(Ui.T("Экзамен: "), _exam, Ui.B("Начать", Start, true))));
    }

    void Start()
    {
        var exam = (string)_exam.SelectedItem;
        var v = Adaptive.MockVariant(App.Content, exam, new Random());
        _items = new();
        var list = Ui.V(_clock);
        foreach (var t in v)
        {
            var box = Ui.Box(); var fb = Ui.T("", 13);
            list.Children.Add(Ui.H($"Задание {t.Num}", 15));
            list.Children.Add(Ui.T(App.Content.FullPrompt(t)));
            list.Children.Add(Ui.InputWithTools(box)); list.Children.Add(fb);
            _items.Add((t, box, fb));
        }
        list.Children.Add(Ui.B("Завершить и проверить", Finish, true));
        _start = DateTime.Now; _limit = Adaptive.ExamDuration(exam); _running = true; _timer.Start(); Tick();
        Content = Ui.Scroll(list);
    }

    void Tick()
    {
        var left = _limit - (DateTime.Now - _start);
        if (left <= TimeSpan.Zero) { _clock.Text = "Время вышло!"; Finish(); return; }
        _clock.Text = $"⏱ Осталось {left:hh\\:mm\\:ss}";
    }

    void Finish()
    {
        if (!_running) return;
        _running = false; _timer.Stop();
        int ok = 0, prim = 0, maxPrim = 0; var now = DateTime.Now;
        foreach (var (t, box, fb) in _items)
        {
            var max = App.Content.Number(t.Exam, t.Num)?.Max ?? 1; maxPrim += max;
            var good = AnswerChecker.Check(t, box.Text);
            if (good) { ok++; prim += max; }
            App.Progress.Record(t, good, box.Text, now);
            box.IsReadOnly = true;
            fb.Foreground = good ? Ui.Good : Ui.Bad;
            fb.Text = good ? "✔ Верно" : $"✘ Правильно: {string.Join(" / ", t.Answers)}. {t.Explain}";
        }
        var exam = _items.FirstOrDefault().t?.Exam ?? "";
        App.Progress.Exams.Add(new ExamResult { Exam = exam, At = now, Correct = ok, Total = _items.Count, Primary = prim, MaxPrimary = maxPrim, Spent = now - _start });
        App.SaveProgress();
        _clock.Text = $"Результат: верно {ok} из {_items.Count}, первичных баллов за краткие ответы {prim} из {maxPrim}. Ошибки добавлены в «Работу над ошибками».";
        if (Content is ScrollViewer sv) sv.ScrollToTop();
    }
}

sealed class TheoryPage : Grid, IRefreshable
{
    readonly ListBox _list = new() { FontSize = 13, Margin = new Thickness(8) };
    readonly TextBlock _title = Ui.H(""), _body = Ui.T("", 15);
    readonly StackPanel _links = new();
    public TheoryPage()
    {
        foreach (var t in App.Content.Theory) _list.Items.Add(new ListBoxItem { Content = t.Title, Tag = t });
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: TheoryItem t }) Render(t); };
        Children.Add(Ui.Split(_list, Ui.Scroll(Ui.V(_title, _body, _links)), 330));
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
    public void Refresh() { }
    public void Show(string id) { foreach (ListBoxItem i in _list.Items) if (((TheoryItem)i.Tag).Id == id) { _list.SelectedItem = i; i.BringIntoView(); } }
    void Render(TheoryItem t)
    {
        _title.Text = t.Title; _body.Text = t.Body; _links.Children.Clear();
        var nums = App.Content.Tasks.Where(x => x.Theory == t.Id).Select(x => (x.Exam, x.Num)).Distinct().ToList();
        if (nums.Count > 0) _links.Children.Add(Ui.T("Где встречается: " + string.Join(", ", nums.Select(n => $"{n.Exam} №{n.Num}")), 13));
    }
}
