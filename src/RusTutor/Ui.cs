using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace RusTutor;

static class Ui
{
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x2B, 0x5C, 0xB8));
    public static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E));
    public static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    public static TextBlock H(string s, double size = 20) => new() { Text = s, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = Accent };
    public static TextBlock T(string s = "", double size = 14) => new() { Text = s, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
    public static Button B(string s, Action a, bool primary = false)
    {
        var b = new Button { Content = s, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 4, 8, 4), FontSize = 14 };
        if (primary) { b.Background = Accent; b.Foreground = Brushes.White; }
        b.Click += (_, _) => a(); return b;
    }
    public static StackPanel V(params UIElement[] items) { var p = new StackPanel(); foreach (var i in items) p.Children.Add(i); return p; }
    public static WrapPanel Row(params UIElement[] items) { var p = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) }; foreach (var i in items) p.Children.Add(i); return p; }
    public static TextBox Box(bool multi = false, double height = double.NaN) => new()
    {
        FontSize = 15, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = height, Padding = new Thickness(4),
        VerticalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled, SpellCheck = { IsEnabled = false }
    };
    public static ScrollViewer Scroll(UIElement c) => new() { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
    public static Grid Split(UIElement left, UIElement right, double leftWidth = 300)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1); g.Children.Add(left); g.Children.Add(right); return g;
    }
    public static ComboBox Combo(params string[] items) { var c = new ComboBox { FontSize = 14, Margin = new Thickness(0, 4, 8, 4), MinWidth = 120 }; foreach (var i in items) c.Items.Add(i); c.SelectedIndex = 0; return c; }

    /// <summary>Поле ввода с кнопками голосового и рукописного ввода.</summary>
    public static Grid InputWithTools(TextBox box)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        tools.Children.Add(new VoiceButton(box));
        var ink = new Button { Content = "✍", ToolTip = "Рукописный ввод (тачпад, перо, мышь)", Width = 40, Height = 34, Margin = new Thickness(4, 0, 0, 0), FontSize = 16 };
        ink.Click += (_, _) => { var w = new InkWindow { Owner = App.Main }; if (w.ShowDialog() == true) Insert(box, w.ResultText); };
        tools.Children.Add(ink);
        Grid.SetColumn(tools, 1); g.Children.Add(box); g.Children.Add(tools); return g;
    }

    public static void Insert(TextBox box, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int i = box.CaretIndex;
        var pre = i > 0 && box.Text.Length > 0 && !char.IsWhiteSpace(box.Text[i - 1]) && box.AcceptsReturn ? " " : "";
        box.Text = box.Text.Insert(i, pre + text);
        box.CaretIndex = i + pre.Length + text.Length;
        box.Focus();
    }
}
