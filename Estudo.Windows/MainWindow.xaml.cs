using System.IO;
using System.Windows;
using System.Windows.Controls;
using Estudo.Core;

namespace Estudo.Windows;

public partial class MainWindow : Window
{
    public Corpus Corpus { get; private set; }
    public AttemptStore Store { get; }
    public Preferences Preferences { get; }
    private readonly string catalog, applicationRoot;
    private IDisposable? current;
    private readonly Dictionary<string, Button> navigation = [];
    private readonly bool qa;
    public MainWindow(Corpus corpus, AttemptStore store, Preferences preferences, string catalog, string applicationRoot, bool qa)
    {
        Corpus = corpus; Store = store; Preferences = preferences; this.catalog = catalog; this.applicationRoot = applicationRoot; this.qa = qa;
        InitializeComponent(); UI.ApplyTheme(preferences.Theme);
        if (qa) { Title += " · Teste isolado"; LocalInfo.Text = "Dados demonstrativos isolados"; }
        foreach (var (label, symbol) in new[] { ("Início", "⌂"), ("Biblioteca", "▤"), ("Simulados", "▷"), ("Histórico", "◷"), ("Revisão", "✓"), ("Configurações", "⚙") })
        {
            var b = UI.Button($"{symbol}    {label}", () => ShowPage(label)); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 7); b.Padding = new Thickness(12); navigation[label] = b; Navigation.Children.Add(b);
        }
        Closing += (_, e) => { try { current?.Dispose(); SavePreferences(); } catch (Exception ex) { e.Cancel = true; UI.Alert("Não foi possível salvar", ex.Message); } };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() => (current as ExamView)?.PauseWhenInactive(), System.Windows.Threading.DispatcherPriority.Background);
        ShowPage("Início");
    }
    public void Notify(string text) => Toast.Text = text;
    public void SavePreferences() => Preferences.Save(Store.DirectoryPath);
    private void SetContent(UIElement content, string section, IDisposable? disposable = null)
    {
        current?.Dispose(); current = disposable; PageHost.Content = content;
        foreach (var (label, button) in navigation) button.SetResourceReference(BackgroundProperty, label == section ? "AccentSoft" : "Surface");
        PageHost.Margin = section == "Simulados" && content is ExamView ? new Thickness(18, 18, 18, 0) : new Thickness(28, 26, 28, 0);
    }
    public void ShowPage(string page)
    {
        if (current is ExamView exam) exam.PauseForNavigation();
        switch (page)
        {
            case "Início": Home(); break;
            case "Biblioteca": SetContent(new LibraryView(this), page); break;
            case "Simulados": History(false, true); break;
            case "Histórico": History(false, false); break;
            case "Revisão": History(true, false); break;
            case "Configurações": Settings(); break;
        }
    }
    private void Home()
    {
        var attempts = Store.All(); var last = attempts.FirstOrDefault(a => !a.IsFinished); var finished = attempts.Count(a => a.IsFinished);
        var page = UI.Stack(UI.Text("Bom estudo.", 32, bold: true), UI.Text("Uma questão de cada vez. Continue de onde parou ou escolha uma nova prova.", 15, "Muted"));
        page.Children.Add(UI.Card(last != null ? UI.Stack(UI.Text("CONTINUAR ESTUDANDO", 11, "Accent", true), UI.Text(last.Title, 24, bold: true), UI.Text($"{last.Answered}/90 respondidas · questão {last.Questions[last.CurrentIndex].Number} · {UI.Duration(last.ActiveSeconds)} de prática", color: "Muted"), UI.Row(UI.Button("Retomar simulado →", () => OpenAttempt(last), true), UI.Button("Ver simulados", () => ShowPage("Simulados")))) : UI.Stack(UI.Text("Seu próximo simulado começa aqui", 23, bold: true), UI.Text("Escolha uma prova oficial. As respostas são salvas enquanto você estuda.", color: "Muted"), UI.Row(UI.Button("Explorar biblioteca →", () => ShowPage("Biblioteca"), true)))));
        page.Children.Add(UI.Row(Stat(finished.ToString(), "simulados finalizados"), Stat(attempts.Count(a => !a.IsFinished).ToString(), "sessões em andamento"), Stat(UI.Duration(attempts.Sum(a => a.ActiveSeconds)), "tempo ativo registrado")));
        var downloaded = Corpus.Documents.Count(d => File.Exists(Path.Combine(Corpus.Root, d.RelativePath)));
        page.Children.Add(UI.Card(UI.Stack(UI.Text("Seu acervo oficial", 20, bold: true), UI.Text($"{downloaded} de {Corpus.Documents.Count} documentos disponíveis · ENEM, PAS e UnB", color: "Muted"), UI.Row(UI.Button("Abrir biblioteca", () => ShowPage("Biblioteca")), UI.Button(downloaded == 0 ? "Baixar provas oficiais" : "Gerenciar arquivos", () => ShowPage("Configurações"))))));
        page.Children.Add(UI.Text("Provas recentes", 20, bold: true));
        foreach (var a in attempts.Take(3)) page.Children.Add(AttemptCard(a));
        if (attempts.Count == 0) page.Children.Add(UI.Text("Ainda não há tentativas. ENEM 2009–2025 tem correção estruturada. PAS e UnB estão disponíveis para leitura.", color: "Muted"));
        SetContent(UI.Scroll(page), "Início");
    }
    private static Border Stat(string value, string label) { var card = UI.Card(UI.Stack(UI.Text(value, 25, "Accent", true), UI.Text(label, 12, "Muted")), 18); card.MinWidth = 210; card.Margin = new Thickness(0, 0, 12, 12); return card; }
    private Border AttemptCard(Attempt a)
    {
        var scores = a.IsFinished ? Score.Calculate(a) : null;
        var info = $"{a.StartedUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {UI.Duration(a.ActiveSeconds)} · {a.Answered}/90 respondidas · {a.Responses.Values.Count(r => r.Review)} para revisar";
        var state = a.IsFinished ? $"Finalizado · {scores!.Sum(s => s.Correct)} acertos brutos" : "Em andamento · salvo";
        var buttons = UI.Row(UI.Button(a.IsFinished ? "Ver resultados" : "Continuar →", () => OpenAttempt(a), true));
        if (a.IsFinished)
        {
            buttons.Children.Add(UI.Button("Revisar erros", () => OpenAttempt(a, "Erros")));
            buttons.Children.Add(UI.Button("Refazer", () => NewAttempt(a.Year, a.Day, a.Language == "espanhol" ? "espanhol" : "ingles")));
            buttons.Children.Add(UI.Button("Exportar para IA", () => Export(a)));
        }
        return UI.Card(UI.Stack(UI.Text(state, 11, a.IsFinished ? "Good" : "Accent", true), UI.Text(a.Title, 21, bold: true), UI.Text(info, 13, "Muted"), buttons));
    }
    private void History(bool review, bool running)
    {
        var title = review ? "Revisão" : running ? "Simulados" : "Histórico";
        var rows = Store.All().Where(a => review ? a.IsFinished : !running || !a.IsFinished).ToList();
        var page = UI.Stack(UI.Text(title, 30, bold: true), UI.Text(review ? "Erros, dúvidas e marcações das tentativas finalizadas." : running ? "Retome suas sessões. Cada nova tentativa mantém um registro independente." : "Todas as suas tentativas, com respostas e anotações preservadas.", color: "Muted"));
        page.Children.Add(UI.Row(UI.Button("Escolher uma prova", () => ShowPage("Biblioteca"), true)));
        foreach (var a in rows) page.Children.Add(AttemptCard(a));
        if (rows.Count == 0) page.Children.Add(UI.Card(UI.Stack(UI.Text(review ? "Sua revisão começa depois do primeiro simulado" : "Nenhuma tentativa por aqui", 21, bold: true), UI.Text("Abra a biblioteca para começar. Seus dados serão registrados apenas quando você responder.", color: "Muted"))));
        SetContent(UI.Scroll(page), title);
    }
    public void NewAttempt(int year, int day, string language) => OpenAttempt(AttemptEngine.Create(Corpus, year, day, language, Store));
    public void OpenAttempt(Attempt attempt, string? filter = null)
    {
        if (attempt.IsFinished && filter == null) { ShowResults(attempt); return; }
        Corpus.Verify(attempt.Questions[0].Pdf, attempt.Questions[0].PdfHash);
        if (filter != null) { var index = attempt.Questions.FindIndex(q => ResultsView.Matches(attempt, q, filter)); if (index >= 0) attempt.CurrentIndex = index; }
        current?.Dispose(); current = null;
        var exam = new ExamView(this, attempt); SetContent(exam, "Simulados", exam);
    }
    public void ShowResults(Attempt a) => SetContent(new ResultsView(this, a), "Histórico");
    public void ReviewQuestion(Attempt a, int index) { a.CurrentIndex = index; OpenAttempt(a, "Todas"); }
    public void Export(Attempt a)
    {
        var path = Exporter.Export(Corpus, Store, a); Notify("✓ Exportação criada: JSON, Markdown, PDFs e rascunhos. Nenhum dado foi enviado.");
        UI.Alert("Exportação pronta", "Os arquivos foram salvos na pasta Exportacoes. O pacote inclui seus dados pessoais e os documentos originais; escolha o que compartilhar."); UI.Open(path);
    }
    public void Preview(SourceDocument doc)
    {
        var pdf = new PdfPane(Corpus, Store.DirectoryPath);
        var controls = UI.Row(UI.Button("← Biblioteca", () => ShowPage("Biblioteca")), UI.Button("Abrir original", () => UI.Open(Corpus.Resolve(doc.RelativePath))));
        var language = UI.Choice(new[] { "Inglês", "Espanhol" }, width: 140);
        if (LibraryView.CanStart(doc)) { controls.Children.Add(language); controls.Children.Add(UI.Button("Iniciar nova tentativa", () => NewAttempt(doc.Year, int.Parse(doc.Day), language.SelectedIndex == 1 ? "espanhol" : "ingles"), true)); }
        var top = UI.Stack(UI.Text(doc.DisplayTitle, 23, bold: true), UI.Text(doc.Details, 12, "Muted"), controls);
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.Children.Add(top); Grid.SetRow(pdf, 1); grid.Children.Add(pdf);
        SetContent(grid, "Biblioteca", pdf); _ = pdf.Show(doc.RelativePath, doc.Sha256, 1);
    }
    private void Settings()
    {
        var theme = UI.Choice(new[] { "Claro", "Escuro" }, width: 170); theme.SelectedIndex = Preferences.Theme == "dark" ? 1 : 0;
        theme.SelectionChanged += (_, _) => { Preferences.Theme = theme.SelectedIndex == 1 ? "dark" : "light"; UI.ApplyTheme(Preferences.Theme); SavePreferences(); Notify("✓ Aparência salva"); };
        var paper = UI.Choice(new[] { "Branco", "Quadriculado", "Pontilhado" }, width: 180); paper.SelectedItem = Preferences.Paper;
        paper.SelectionChanged += (_, _) => { Preferences.Paper = (string)paper.SelectedItem; SavePreferences(); };
        var count = Corpus.Documents.Count(d => File.Exists(Path.Combine(Corpus.Root, d.RelativePath)));
        var page = UI.Stack(UI.Text("Configurações", 30, bold: true), UI.Text("Preferências simples. Funcionamento local, sem conta ou conexão obrigatória.", color: "Muted"),
            UI.Card(UI.Stack(UI.Text("Aparência", 20, bold: true), UI.Text("Tema da interface", 12, "Muted"), theme, UI.Text("Fundo padrão do rascunho", 12, "Muted"), paper)),
            UI.Card(UI.Stack(UI.Text("Acervo e registros", 20, bold: true), UI.Text($"{count}/{Corpus.Documents.Count} documentos encontrados. O catálogo verifica SHA-256 antes de iniciar provas.", color: "Muted"),
                UI.Row(UI.Button("Baixar provas oficiais", Download), UI.Button("Selecionar pasta do acervo", SelectCorpus), UI.Button("Abrir pasta de registros", () => UI.Open(Store.DirectoryPath))),
                UI.Row(UI.Button("Fazer backup do banco", () => { var folder = Path.Combine(Store.DirectoryPath, "Backups"); Directory.CreateDirectory(folder); Store.Backup(Path.Combine(folder, "estudos_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".sqlite3")); Notify("✓ Backup do banco concluído. Rascunhos permanecem na pasta Questoes."); })))),
            UI.Card(UI.Stack(UI.Text("Atalhos", 20, bold: true), UI.Text("Ctrl + Alt + A–E   ·   Selecionar alternativa\nCtrl + Alt + ← / →   ·   Questão anterior / próxima\nTab / Shift + Tab   ·   Navegar entre controles\nNo leitor: seleção de texto, scroll e controles próprios de página e zoom.\nNo rascunho: ferramenta Mover ou botão do meio para arrastar.", color: "Muted"))),
            UI.Card(UI.Stack(UI.Text("Estudar 2.0", 20, bold: true), UI.Text("ENEM: acertos brutos por área, sem estimativa TRI. Redação, PAS e UnB ainda não têm correção automática.\nPágina inicial da questão mapeada; recortes e habilidades não são inferidos.\nRascunhos usam tinta do Windows; a pressão depende do dispositivo e do driver.", color: "Muted"))));
        SetContent(UI.Scroll(page), "Configurações");
    }
    private void Download()
    {
        var bat = Path.Combine(applicationRoot, "baixar-provas.bat");
        if (!File.Exists(bat)) throw new FileNotFoundException("Mantenha baixar-provas.bat junto do aplicativo.");
        var process = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-File", Path.Combine(applicationRoot, "scripts", "Download-Corpus.ps1"), "-Destination", Corpus.Root }) process.ArgumentList.Add(argument);
        System.Diagnostics.Process.Start(process); Notify("Download aberto em uma janela própria. O acervo pode ser retomado depois de uma interrupção.");
    }
    private void SelectCorpus()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Escolha a pasta que contém ENEM, PAS e UnB" };
        if (dialog.ShowDialog(this) != true) return;
        var replacement = new Corpus(dialog.FolderName, catalog); Corpus = replacement; Preferences.CorpusPath = dialog.FolderName; SavePreferences(); Settings(); Notify("✓ Pasta do acervo atualizada");
    }
}
