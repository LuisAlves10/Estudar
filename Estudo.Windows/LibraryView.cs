using System.IO;
using System.Windows;
using System.Windows.Controls;
using Estudo.Core;

namespace Estudo.Windows;

public sealed class LibraryView : UserControl
{
    private readonly MainWindow shell;
    private readonly StackPanel cards = UI.Stack();
    private readonly TextBlock count = UI.Text("", 12, "Muted");
    private readonly TextBox search = new() { ToolTip = "Buscar prova, caderno ou documento", MinWidth = 250, Margin = new Thickness(0, 0, 8, 8) };
    private readonly ComboBox year, type, stage, version, application, order;
    private readonly Dictionary<string, Button> segments = [];
    private string exam = "ENEM";
    private bool ready;
    private int limit = 24;
    public static bool CanStart(SourceDocument d) => d.Exam == "ENEM" && d.Type == "prova" && int.TryParse(d.Day, out _) && d.Year is >= 2009 and <= 2025;
    public LibraryView(MainWindow shell)
    {
        this.shell = shell;
        year = UI.Choice(new[] { "Todos os anos" }, Refresh, 140); type = UI.Choice(new[] { "Provas", "Gabaritos", "Documentos", "Todos os tipos" }, Refresh, 142);
        stage = UI.Choice(new[] { "Todas as etapas" }, Refresh, 146); version = UI.Choice(new[] { "Todos os cadernos" }, Refresh, 180); application = UI.Choice(new[] { "Todas as aplicações" }, Refresh, 180);
        order = UI.Choice(new[] { "Mais recentes", "Mais antigas", "Relevância" }, Refresh, 145);
        var tabs = UI.Row();
        foreach (var label in new[] { "ENEM", "PAS", "UnB" }) { var b = UI.Button(label, () => { exam = label; UpdateFilters(); }); b.Width = 94; segments[label] = b; tabs.Children.Add(b); }
        search.TextChanged += (_, _) => Refresh();
        var filters = UI.Card(UI.Stack(UI.Row(UI.Stack(UI.Text("Buscar no acervo", 11, "Muted"), search), UI.Stack(UI.Text("Ano", 11, "Muted"), year), UI.Stack(UI.Text("Tipo", 11, "Muted"), type), UI.Stack(UI.Text("Ordenar", 11, "Muted"), order)), UI.Row(stage, version, application)), 16);
        var top = UI.Stack(UI.Text("Biblioteca", 30, bold: true), UI.Text("Provas e documentos oficiais. Escolha o que estudar ou consultar.", color: "Muted"), tabs, filters, count);
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.Children.Add(top); var scroll = UI.Scroll(cards); Grid.SetRow(scroll, 1); grid.Children.Add(scroll); Content = grid;
        UpdateFilters();
    }
    private void UpdateFilters()
    {
        ready = false; var docs = shell.Corpus.Documents.Where(d => d.Exam == exam).ToList();
        year.ItemsSource = new[] { "Todos os anos" }.Concat(docs.Select(d => d.Year).Distinct().OrderDescending().Select(y => y.ToString())); year.SelectedItem = docs.Any(d => d.Year == 2025) ? "2025" : "Todos os anos";
        stage.ItemsSource = new[] { "Todas as etapas" }.Concat(docs.Select(d => d.Stage).Where(s => s.Length > 0 && s != "nao_se_aplica").Distinct().Order()); stage.SelectedIndex = 0;
        version.ItemsSource = new[] { "Todos os cadernos" }.Concat(docs.Select(d => d.Version).Where(s => s.Length > 0 && s != "nao_se_aplica").Distinct().Order()); version.SelectedIndex = 0;
        application.ItemsSource = new[] { "Todas as aplicações" }.Concat(docs.Select(d => d.Application).Where(s => s.Length > 0 && s != "nao_se_aplica").Distinct().Order()); application.SelectedIndex = 0;
        foreach (var (label, b) in segments) b.SetResourceReference(BackgroundProperty, label == exam ? "AccentSoft" : "Surface");
        ready = true; Refresh();
    }
    private void Refresh() { limit = 24; Render(); }
    private void Render()
    {
        if (!ready) return; cards.Children.Clear();
        var attempts = shell.Store.All();
        var docs = shell.Corpus.Documents.Where(d => d.Exam == exam && (year.SelectedIndex == 0 || d.Year.ToString() == (string)year.SelectedItem) &&
            (stage.SelectedIndex == 0 || d.Stage == (string)stage.SelectedItem) && (version.SelectedIndex == 0 || d.Version == (string)version.SelectedItem) &&
            (application.SelectedIndex == 0 || d.Application == (string)application.SelectedItem) &&
            (type.SelectedIndex == 3 || type.SelectedIndex == 0 && d.Type == "prova" || type.SelectedIndex == 1 && d.Type == "gabarito" || type.SelectedIndex == 2 && d.Type is not ("prova" or "gabarito")) &&
            (search.Text.Length == 0 || (d.DisplayTitle + " " + d.Details).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)));
        docs = order.SelectedIndex == 1 ? docs.OrderBy(d => d.Year).ThenBy(d => d.Title) : order.SelectedIndex == 2 ? docs.OrderByDescending(d => d.Type == "prova").ThenByDescending(d => d.Year).ThenBy(d => d.Title) : docs.OrderByDescending(d => d.Year).ThenBy(d => d.Title);
        var rows = docs.ToList(); count.Text = $"{rows.Count} documentos · {(exam == "ENEM" ? "Correção disponível para as provas regulares" : "Consulta de PDFs; correção de itens ainda não estruturada")}";
        foreach (var d in rows.Take(limit))
        {
            var local = File.Exists(Path.Combine(shell.Corpus.Root, d.RelativePath));
            var attempt = CanStart(d) ? attempts.FirstOrDefault(a => a.Year == d.Year && a.Day.ToString() == d.Day) : null;
            var label = !local ? "ARQUIVO NÃO BAIXADO" : attempt == null ? "DISPONÍVEL OFFLINE" : attempt.IsFinished ? "JÁ REALIZADA" : "EM ANDAMENTO";
            var header = UI.Text($"{label}   ·   {d.Type.Replace('_', ' ').ToUpperInvariant()}", 10, attempt != null ? "Accent" : "Muted", true);
            var actions = UI.Row();
            var preview = UI.Button("Visualizar PDF", () => shell.Preview(d)); preview.IsEnabled = local; actions.Children.Add(preview);
            if (CanStart(d))
            {
                var language = UI.Choice(new[] { "Inglês", "Espanhol" }, width: 120);
                if (shell.Corpus.Questions.Any(q => q.Year == d.Year && q.Day.ToString() == d.Day && q.Language != "nao_se_aplica")) actions.Children.Add(language);
                var start = UI.Button(attempt is { IsFinished: false } ? "Continuar →" : "Iniciar simulado →", () => { if (attempt is { IsFinished: false }) shell.OpenAttempt(attempt); else shell.NewAttempt(d.Year, int.Parse(d.Day), language.SelectedIndex == 1 ? "espanhol" : "ingles"); }, true); start.IsEnabled = local; actions.Children.Add(start);
            }
            if (!local) actions.Children.Add(UI.Button("Configurar acervo", () => shell.ShowPage("Configurações")));
            cards.Children.Add(UI.Card(UI.Stack(header, UI.Text(d.DisplayTitle, 19, bold: true), UI.Text(d.Details, 12, "Muted"), actions), 17));
        }
        if (rows.Count == 0) cards.Children.Add(UI.Card(UI.Stack(UI.Text("Nenhum documento encontrado", 20, bold: true), UI.Text("Ajuste o ano, os filtros ou a busca.", color: "Muted"))));
        if (rows.Count > limit) cards.Children.Add(UI.Button($"Mostrar mais ({rows.Count - limit} restantes)", () => { limit += 24; Render(); }));
    }
}
