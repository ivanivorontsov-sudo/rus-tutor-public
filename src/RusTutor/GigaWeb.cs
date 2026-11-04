using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RusTutor.Core;
namespace RusTutor;

/// <summary>
/// Шаблон «Тирпроц»: внутри приложения открыт веб-чат GigaChat. Программа вставляет промпт, отправляет,
/// ждёт, пока ответ перестанет меняться, берёт последний блок кода, вычищает ссылки поиска и разбирает JSON.
/// </summary>
sealed class GigaWebPage : DockPanel, IAiBackend
{
    string IAiBackend.Name => "GigaChat (веб-чат)";
    readonly WebView2 _web = new();
    readonly TextBlock _info = Ui.T("", 12);
    readonly SemaphoreSlim _gate = new(1, 1);
    Task _init;

    public GigaWebPage()
    {
        var bar = Ui.Row(
            Ui.B("Открыть GigaChat", () => Nav()),
            Ui.B("Обновить", () => _web.CoreWebView2?.Reload()),
            Ui.B("Проверить автоматизацию", async () => await Test()),
            _info);
        DockPanel.SetDock(bar, Dock.Top); Children.Add(bar);
        var hint = Ui.T("Войдите в свой аккаунт GigaChat (Сбер ID) один раз — вход сохранится. Приложение будет само отправлять сюда запросы на проверку.", 12);
        hint.Foreground = Ui.Muted; DockPanel.SetDock(hint, Dock.Top); Children.Add(hint);
        Children.Add(_web);
        Loaded += (_, _) => _ = EnsureReady();
    }

    void Nav() { if (_web.CoreWebView2 != null) _web.CoreWebView2.Navigate(App.Settings.GigaUrl); }

    Task EnsureReady() => _init ??= InitCore();
    async Task InitCore()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RusTutor", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dir);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Navigate(App.Settings.GigaUrl);
            _info.Text = "WebView2 готов";
        }
        catch (Exception e)
        {
            _info.Text = "WebView2 недоступен: " + e.Message + " (установите Microsoft Edge WebView2 Runtime)";
            _init = null; throw;
        }
    }

    async Task Test()
    {
        try
        {
            var ans = await AskAsync("Ответь одним блоком кода ```json {\"score\":1,\"max\":1,\"explanation\":\"связь есть\"} ``` и больше ничего.", CancellationToken.None);
            var g = AiParser.Parse(ans);
            MessageBox.Show($"Автоматизация работает. score={g.Score}, explanation={g.Explanation}");
        }
        catch (Exception e) { MessageBox.Show("Не получилось: " + e.Message + "\n\nПроверьте вход в аккаунт и CSS-селекторы в «Настройках»."); }
    }

    static string J(string s) => JsonSerializer.Serialize(s);

    async Task<string> Eval(string js)
    {
        var r = await _web.CoreWebView2.ExecuteScriptAsync(js);
        try { return JsonSerializer.Deserialize<string>(r) ?? ""; } catch { return r; }
    }

    string ProbeJs => @"(function(){
  var pres = document.querySelectorAll('pre');
  var last = pres.length ? (pres[pres.length-1].innerText || pres[pres.length-1].textContent) : '';
  var stop = document.querySelector(" + J(App.Settings.StopSelector) + @");
  var body = (document.body && document.body.innerText) || '';
  return JSON.stringify({n: pres.length, last: last, busy: !!stop, len: body.length, tail: body.slice(-30000)});
})()";

    sealed record Probe(int n, string last, bool busy, int len, string tail);

    async Task<Probe> GetProbe() => JsonSerializer.Deserialize<Probe>(await Eval(ProbeJs))!;

    async Task InsertAndSend(string text)
    {
        var s = App.Settings;
        var r = await Eval(@"(function(text){
  var el = document.querySelector(" + J(s.InputSelector) + @");
  if(!el) return 'noinput';
  el.focus();
  if(el.tagName==='TEXTAREA'||el.tagName==='INPUT'){
    var proto = Object.getPrototypeOf(el); var d = Object.getOwnPropertyDescriptor(proto,'value');
    d.set.call(el, text); el.dispatchEvent(new Event('input',{bubbles:true}));
  } else { document.execCommand('selectAll',false,null); document.execCommand('insertText',false,text); }
  return 'ok';
})(" + J(text) + ")");
        if (r == "noinput") throw new InvalidOperationException("Не найдено поле ввода чата. Войдите в GigaChat или поправьте селектор поля в «Настройках».");
        await Task.Delay(400);
        await Eval(@"(function(){
  var b = document.querySelector(" + J(s.SendSelector) + @");
  if(b && !b.disabled){ b.click(); return 'click'; }
  var el = document.querySelector(" + J(s.InputSelector) + @");
  ['keydown','keypress','keyup'].forEach(function(t){ el.dispatchEvent(new KeyboardEvent(t,{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true})); });
  return 'enter';
})()");
    }

    async Task PasteImage(byte[] png)
    {
        var b64 = Convert.ToBase64String(png);
        var r = await Eval(@"(function(b64){
  var el = document.querySelector(" + J(App.Settings.InputSelector) + @"); if(!el) return 'noinput';
  var bin = atob(b64); var arr = new Uint8Array(bin.length); for(var i=0;i<bin.length;i++) arr[i]=bin.charCodeAt(i);
  var file = new File([arr], 'handwriting.png', {type:'image/png'});
  var dt = new DataTransfer(); dt.items.add(file);
  el.focus();
  el.dispatchEvent(new ClipboardEvent('paste', {clipboardData: dt, bubbles: true, cancelable: true}));
  var inp = document.querySelector('input[type=file]');
  if(inp){ try{ inp.files = dt.files; inp.dispatchEvent(new Event('change',{bubbles:true})); }catch(e){} }
  return 'ok';
})(" + J(b64) + ")");
        if (r == "noinput") throw new InvalidOperationException("Не найдено поле ввода чата.");
        await Task.Delay(3000); // ждём загрузку изображения
    }

    public Task<string> AskAsync(string prompt, CancellationToken ct) => Run(prompt, null, ct);
    public Task<string> AskWithImageAsync(string prompt, byte[] png, CancellationToken ct) => Run(prompt, png, ct);

    async Task<string> Run(string prompt, byte[] png, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var prevTab = App.Main.SelectedTab;
        try
        {
            App.Main.ShowGigaTab();                       // WebView2 должен быть в визуальном дереве
            await Task.Delay(200, ct);
            await EnsureReady();
            var before = await GetProbe();
            if (png != null) await PasteImage(png);
            await InsertAndSend(prompt);
            var deadline = DateTime.UtcNow.AddSeconds(App.Settings.WebTimeoutSec);
            string lastSeen = null; int stable = 0; Probe p = before;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000, ct);
                p = await GetProbe();
                var cur = p.n > before.n && !string.IsNullOrWhiteSpace(p.last) && !prompt.Contains(p.last.Trim()) ? p.last : null;
                if (cur != null && cur == lastSeen && !p.busy) { if (++stable >= 3) return "```json\n" + cur + "\n```"; }
                else stable = 0;
                lastSeen = cur;
                _info.Text = cur == null ? "Жду ответ GigaChat…" : "Ответ печатается…";
            }
            // запасной путь: ответ без оформленного блока кода — ищем JSON в тексте страницы после промпта
            var tail = p.tail; var marker = prompt.Length > 60 ? prompt[^60..] : prompt;
            int idx = tail.LastIndexOf(marker.Trim(), StringComparison.Ordinal);
            var after = idx >= 0 ? tail[(idx + marker.Trim().Length)..] : tail;
            if (AiParser.ExtractLastBlock(after) is string blk) return blk;
            throw new TimeoutException("GigaChat не прислал ответ с JSON вовремя.");
        }
        finally { _info.Text = ""; App.Main.SelectedTab = prevTab; _gate.Release(); }
    }
}
