using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace RusTutor;

public sealed class MainWindow : Window
{
    readonly TabControl _tabs = new() { FontSize = 14 };
    readonly TextBlock _status = new() { Margin = new Thickness(8, 2, 8, 2) };
    internal GigaWebPage GigaWeb { get; }
    readonly TabItem _gigaTab;
    readonly DashboardPage _dash;

    public MainWindow()
    {
        Title = "Репетитор по русскому языку — ОГЭ и ЕГЭ"; Width = 1200; Height = 820; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _dash = new DashboardPage();
        GigaWeb = new GigaWebPage();
        Add("🏠 Главная", _dash);
        Add("📝 Тренажёр", new TrainerPage());
        Add("🔁 Работа над ошибками", new ReviewPage());
        Add("⏱ Пробный экзамен", new ExamPage());
        Add("✒ Сочинение и изложение", new EssayPage());
        Add("📚 Теория", new TheoryPage());
        _gigaTab = Add("🤖 GigaChat", GigaWeb);
        Add("⚙ Настройки", new SettingsPage());
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs && _tabs.SelectedItem is TabItem { Content: IRefreshable r }) r.Refresh(); };
        var dock = new DockPanel();
        var sb = new StatusBar(); sb.Items.Add(_status); DockPanel.SetDock(sb, Dock.Bottom);
        dock.Children.Add(sb); dock.Children.Add(_tabs);
        Content = dock;
        Status("Готово. Данные сохраняются в " + AppSettings.Dir);
    }

    TabItem Add(string header, UIElement page) { var t = new TabItem { Header = header, Content = page }; _tabs.Items.Add(t); return t; }
    public void Status(string s) => _status.Text = s;
    public void ShowGigaTab() => _tabs.SelectedItem = _gigaTab;
    public object SelectedTab { get => _tabs.SelectedItem; set => _tabs.SelectedItem = value; }
    public void OpenTheory(string id) { foreach (TabItem t in _tabs.Items) if (t.Content is TheoryPage tp) { _tabs.SelectedItem = t; tp.Show(id); } }
}

interface IRefreshable { void Refresh(); }
