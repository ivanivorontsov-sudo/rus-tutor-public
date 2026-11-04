using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using RusTutor.Core;
namespace RusTutor;

sealed class SettingsPage : ContentControl, IRefreshable
{
    public SettingsPage() { Build(); }
    public void Refresh() { }
    void Build()
    {
        var s = App.Settings;
        var mode = Ui.Combo("Веб-чат GigaChat во встроенном браузере (нужен вход в аккаунт)", "Официальный GigaChat API (ключ авторизации)");
        mode.SelectedIndex = s.AiMode == "api" ? 1 : 0;
        var url = Ui.Box(); url.Text = s.GigaUrl;
        var inSel = Ui.Box(); inSel.Text = s.InputSelector;
        var sendSel = Ui.Box(); sendSel.Text = s.SendSelector;
        var stopSel = Ui.Box(); stopSel.Text = s.StopSelector;
        var timeout = Ui.Box(); timeout.Text = s.WebTimeoutSec.ToString();
        var key = new PasswordBox { Password = s.GigaAuthKey, FontSize = 14, Padding = new Thickness(4) };
        var scope = Ui.Combo("GIGACHAT_API_PERS", "GIGACHAT_API_B2B", "GIGACHAT_API_CORP"); scope.SelectedItem = s.GigaScope;
        var model = Ui.Box(); model.Text = s.GigaModel;
        var trust = new CheckBox { Content = "Не проверять сертификат серверов GigaChat (если не установлен сертификат НУЦ Минцифры) — небезопасно", IsChecked = s.GigaTrustAllCerts, Margin = new Thickness(0, 4, 0, 4) };
        var voice = Ui.Combo("Бесплатный режим (Google с ключом или офлайн Vosk без ключа)", "Google Cloud Speech-to-Text (API-ключ или JSON)"); voice.SelectedIndex = s.VoiceBackend == "cloud" ? 1 : 0;
        var lang = Ui.Box(); lang.Text = s.VoiceLanguage;
        var gkey = new PasswordBox { Password = s.CloudApiKey, FontSize = 14, Padding = new Thickness(4) };
        var fkey = new PasswordBox { Password = s.FreeVoiceKey, FontSize = 14, Padding = new Thickness(4) };
        var gjson = Ui.Box(); gjson.Text = s.CloudCredentialsPath;
        var mic = Ui.Combo(); mic.Items.Clear();
        try { for (int i = 0; i < WaveInEvent.DeviceCount; i++) mic.Items.Add(WaveInEvent.GetCapabilities(i).ProductName); } catch { }
        if (mic.Items.Count == 0) mic.Items.Add("(микрофон не найден)");
        mic.SelectedIndex = Math.Min(s.MicDevice, mic.Items.Count - 1);
        var reset = Ui.B("Сбросить весь прогресс", () =>
        {
            if (MessageBox.Show("Удалить всю статистику и карточки повторения?", "Сброс", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            App.Progress = new Progress(); App.SaveProgress();
        });
        var save = Ui.B("Сохранить", () =>
        {
            s.AiMode = mode.SelectedIndex == 1 ? "api" : "web"; s.GigaUrl = url.Text.Trim();
            s.InputSelector = inSel.Text; s.SendSelector = sendSel.Text; s.StopSelector = stopSel.Text;
            s.WebTimeoutSec = int.TryParse(timeout.Text, out var t) ? Math.Clamp(t, 30, 900) : 180;
            s.GigaAuthKey = key.Password.Trim(); s.GigaScope = (string)scope.SelectedItem; s.GigaModel = model.Text.Trim(); s.GigaTrustAllCerts = trust.IsChecked == true;
            s.VoiceBackend = voice.SelectedIndex == 1 ? "cloud" : "free"; s.VoiceLanguage = lang.Text.Trim(); s.CloudApiKey = gkey.Password.Trim(); s.FreeVoiceKey = fkey.Password.Trim(); s.CloudCredentialsPath = gjson.Text.Trim();
            s.MicDevice = Math.Max(0, mic.SelectedIndex);
            s.Save(); App.Main.Status("Настройки сохранены");
        }, true);
        Content = Ui.Scroll(Ui.V(
            Ui.H("GigaChat"), Ui.T("Способ проверки:"), mode,
            Ui.T("Адрес веб-чата:"), url,
            Ui.T("CSS-селектор поля ввода:"), inSel, Ui.T("CSS-селектор кнопки «Отправить»:"), sendSel, Ui.T("CSS-селектор кнопки «Остановить» (идёт генерация):"), stopSel,
            Ui.T("Таймаут ожидания ответа, сек:"), timeout,
            Ui.T("Ключ авторизации GigaChat API (Authorization key из личного кабинета developers.sber.ru):"), key,
            Ui.T("Scope:"), scope, Ui.T("Модель (для распознавания изображений нужна модель с поддержкой картинок, например GigaChat-Max):"), model, trust,
            Ui.H("Голосовой ввод"), voice, Ui.T("Язык:"), lang, Ui.T("Ключ бесплатного режима Google (необязательно; без ключа работает офлайн-распознавание Vosk):"), fkey, Ui.T("Google Cloud API-ключ:"), gkey, Ui.T("или путь к JSON сервисного аккаунта:"), gjson, Ui.T("Микрофон:"), mic,
            Ui.Row(save, reset),
            Ui.T("Данные: " + AppSettings.Dir, 12)));
    }
}
