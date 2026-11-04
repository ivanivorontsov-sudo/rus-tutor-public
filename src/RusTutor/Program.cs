using System.Windows;
using RusTutor.Core;
namespace RusTutor;

public static class App
{
    public static AppSettings Settings = AppSettings.Load();
    public static Content Content;
    public static Progress Progress;
    public static MainWindow Main;
    public static void SaveProgress() { try { Progress.Save(AppSettings.ProgressPath); } catch (Exception e) { MessageBox.Show("Не удалось сохранить прогресс: " + e.Message); } }
    public static void ReloadContent() => Content = Content.Load(AppSettings.ExtraTasksPath);
}

static class Program
{
    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
        App.ReloadContent();
        App.Progress = Progress.Load(AppSettings.ProgressPath);
        App.Main = new MainWindow();
        app.Run(App.Main);
    }
}
