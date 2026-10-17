using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Estudo.Windows;

internal static class UI
{
    public static Brush Brush(string name) => (Brush)Application.Current.Resources[name];
    public static TextBlock Text(string text, double size = 14, string color = "Ink", bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 6) };
        block.SetResourceReference(TextBlock.ForegroundProperty, color == "Accent" ? "AccentText" : color); return block;
    }
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        button.Click += (_, _) => Safe(action); return button;
    }
    public static void Safe(Action action) { try { action(); } catch (Exception ex) { Alert("Não foi possível concluir", ex.Message); } }
    public static StackPanel Stack(params UIElement[] children) { var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    public static WrapPanel Row(params UIElement[] children) { var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) }; foreach (var child in children) panel.Children.Add(child); return panel; }
    public static Border Card(UIElement child, double padding = 20)
    {
        var border = new Border { Child = child, CornerRadius = new CornerRadius(10), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); return border;
    }
    public static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public static ComboBox Choice(IEnumerable<string> values, Action? changed = null, double width = 160)
    {
        var box = new ComboBox { ItemsSource = values, Width = width, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 8), SelectedIndex = 0 };
        box.SelectionChanged += (_, _) => changed?.Invoke(); return box;
    }
    public static string Duration(double seconds) { var t = TimeSpan.FromSeconds(seconds); return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"; }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public static bool Confirm(string title, string text, string confirm = "Confirmar") => Dialog(title, text, confirm, true);
    public static void Alert(string title, string text) => Dialog(title, text, "Entendi", false);
    private static bool Dialog(string title, string text, string label, bool cancel)
    {
        if (AppContext.TryGetSwitch("Estudar.NonInteractiveVerification", out var verifying) && verifying)
            throw new InvalidOperationException($"{title}: {text}");
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, ShowInTaskbar = false };
        var accept = Button(label, () => dialog.DialogResult = true, true); accept.IsDefault = true;
        var buttons = Row(); if (cancel) { var no = Button("Cancelar", () => dialog.DialogResult = false); no.IsCancel = true; buttons.Children.Add(no); } buttons.Children.Add(accept);
        dialog.Content = new Border { Padding = new Thickness(26), Child = Stack(Text(title, 23, bold: true), Text(text, 14, "Muted"), buttons) };
        return dialog.ShowDialog() == true;
    }
    public static void ApplyTheme(string theme)
    {
        var dark = theme == "dark";
        var colors = new Dictionary<string, string> {
            ["Bg"] = dark ? "#151D1A" : "#F3F5F4", ["Surface"] = dark ? "#202B26" : "#FFFFFF", ["Soft"] = dark ? "#293830" : "#EAF0ED",
            ["Ink"] = dark ? "#EDF4F0" : "#1D302B", ["Muted"] = dark ? "#A9BDB2" : "#586D64", ["Line"] = dark ? "#3E5146" : "#D6E0DA",
            ["Accent"] = dark ? "#247E62" : "#126C54", ["AccentSoft"] = dark ? "#264D3D" : "#D8EEE3",
            ["AccentText"] = dark ? "#80D9AE" : "#126C54",
            ["Good"] = dark ? "#78D4A3" : "#207450", ["Bad"] = dark ? "#F69898" : "#B44242", ["Warn"] = dark ? "#EFC477" : "#996016" };
        foreach (var (key, color) in colors) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
