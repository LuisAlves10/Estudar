using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Estudo.Core;

namespace Estudo.Windows;

public sealed class ExamView : UserControl, IDisposable
{
    private readonly MainWindow shell;
    private readonly TextBlock sessionTitle = UI.Text("", 21, bold: true);
    private readonly AttemptEngine engine;
    private readonly PdfPane pdf;
    private readonly TextBlock clock = UI.Text("00:00:00", 17), progress = UI.Text("", 12, "Muted"), questionTitle = UI.Text("", 28, bold: true), meta = UI.Text("", 12, "Muted"), saved = UI.Text("✓ Salvo", 12, "Good"), result = UI.Text("", 16, "Accent", true);
    private readonly ProgressBar bar = new() { Maximum = 90, Height = 4, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBox notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox postNotes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly CheckBox review = new() { Content = "Marcar para revisar" };
    private readonly ComboBox confidence;
    private readonly Button pause, previous, next, finish, blank;
    private readonly List<Button> answers = [];
    private readonly Grid body = new();
    private readonly Border answerPanel;
    private readonly ContentControl scratchHost = new();
    private readonly ColumnDefinition side = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) }, debounce = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly StudyFiles files;
    private ScratchPad? scratch;
    private Window? scratchWindow;
    private bool painting, dirty, scratchVisible, disposed, confirmingFinish;
    private int ticks;
    public Attempt Attempt => engine.Attempt;
    private Question Current => Attempt.Questions[Attempt.CurrentIndex];
    public ExamView(MainWindow shell, Attempt attempt)
    {
        this.shell = shell; engine = new(attempt, shell.Store); files = new(shell.Store.DirectoryPath);
        pdf = new(shell.Corpus, shell.Store.DirectoryPath) { Shortcut = Shortcut };
        confidence = UI.Choice(new[] { "Não informada", "Tenho certeza", "Acho que sei", "Chutei" }, SaveAnswer, 250);
        pause = UI.Button("Pausar", TogglePause); previous = UI.Button("← Anterior", () => Navigate(Attempt.CurrentIndex - 1)); next = UI.Button("Próxima →", () => Navigate(Attempt.CurrentIndex + 1), true);
        finish = UI.Button(attempt.IsFinished ? "Ver resultados" : "Finalizar e corrigir", Finish);
        blank = UI.Button("Deixar em branco", () => SaveAnswer(null, true));
        var top = new Grid(); top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.Children.Add(UI.Stack(sessionTitle, progress, bar));
        var controls = UI.Row(clock, pause); controls.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(controls, 1); top.Children.Add(controls);
        var answerRow = UI.Row();
        for (var i = 0; i < 5; i++) { var label = ((char)('A' + i)).ToString(); var b = UI.Button(label, () => SaveAnswer(label, true)); b.Width = 44; b.Height = 46; b.FontSize = 18; b.Margin = new Thickness(0, 0, 6, 0); answers.Add(b); answerRow.Children.Add(b); }
        review.Click += (_, _) => SaveAnswer();
        notes.TextChanged += (_, _) => NoteChanged(); postNotes.TextChanged += (_, _) => NoteChanged();
        var notesExpander = new Expander { Header = "Anotações da questão", Content = notes };
        var postExpander = new Expander { Header = "Observações após a correção", Content = postNotes, IsExpanded = attempt.IsFinished, Visibility = attempt.IsFinished ? Visibility.Visible : Visibility.Collapsed };
        var sideContent = UI.Stack(questionTitle, meta, result, UI.Text("SUA RESPOSTA", 11, "Muted", true), answerRow, blank,
            UI.Text("Confiança na resposta", 12, "Muted"), confidence, review, notesExpander, postExpander,
            UI.Row(UI.Button("Abrir rascunho", ToggleScratch), UI.Button("Cartão de respostas", Card)),
            UI.Text("Ctrl + Alt + A–E: responder\nCtrl + Alt + ← / →: navegar", 11, "Muted"));
        var answerDock = new DockPanel(); var sideFooter = UI.Stack(saved, finish); DockPanel.SetDock(sideFooter, Dock.Bottom); answerDock.Children.Add(sideFooter); answerDock.Children.Add(UI.Scroll(sideContent));
        answerPanel = UI.Card(answerDock, 18); answerPanel.Margin = new Thickness(0);
        body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new GridLength(8) }); side.Width = new GridLength(shell.Preferences.AnswerWidth); side.MinWidth = 292; body.ColumnDefinitions.Add(side);
        body.Children.Add(pdf); Grid.SetColumn(answerPanel, 2); body.Children.Add(answerPanel); Grid.SetColumn(scratchHost, 2); body.Children.Add(scratchHost); scratchHost.Visibility = Visibility.Collapsed;
        var splitter = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent }; Grid.SetColumn(splitter, 1); body.Children.Add(splitter);
        splitter.DragCompleted += (_, _) => { if (!scratchVisible) { shell.Preferences.AnswerWidth = side.ActualWidth; shell.SavePreferences(); } };
        var bottom = UI.Row(previous, next, UI.Button("Cartão de respostas", Card), UI.Button("Rascunho", ToggleScratch), UI.Button("Recolher painel", TogglePanel), UI.Button("Abrir original", () => UI.Open(shell.Corpus.Resolve(Current.Pdf))));
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); top.Margin = new Thickness(0, 0, 0, 18); grid.Children.Add(top); Grid.SetRow(body, 1); grid.Children.Add(body); Grid.SetRow(bottom, 2); grid.Children.Add(bottom); Content = grid;
        debounce.Tick += (_, _) => UI.Safe(Flush);
        timer.Tick += (_, _) => UI.Safe(() => { engine.Touch(); PaintClock(); if (++ticks % 5 == 0) { engine.Checkpoint(); saved.Text = "✓ Salvo neste dispositivo"; } });
        PreviewKeyDown += KeyDownHandler;
        if (!attempt.IsFinished) engine.Resume();
        Paint(); ShowPdf(); timer.Start();
    }
    private void ShowPdf() => _ = pdf.Show(Current.Pdf, Current.PdfHash, Current.Page);
    private void PaintClock() { clock.Text = UI.Duration(Attempt.ActiveSeconds); progress.Text = $"{Attempt.Answered} de 90 respondidas · {Attempt.Responses.Values.Count(r => r.Review)} para revisar"; bar.Value = Attempt.Answered; }
    private void Paint()
    {
        painting = true;
        try
        {
            var r = Attempt.Response(Current); var editable = engine.Running && !Attempt.IsFinished;
            sessionTitle.Text = $"{Attempt.Title} · questão {Current.Number}";
            questionTitle.Text = $"Questão {Current.Number}"; meta.Text = Exporter.AreaName(Current.Area) + " · caderno azul" + (Current.Language == "nao_se_aplica" ? "" : " · " + Exporter.LanguageName(Current.Language));
            result.Text = Attempt.IsFinished ? $"{ResultsView.OutcomeLabel(Exporter.Outcome(Current, r))}\nSua resposta: {r.Answer ?? "em branco"} · oficial: {Current.OfficialAnswer}" : "";
            result.Visibility = Attempt.IsFinished ? Visibility.Visible : Visibility.Collapsed;
            result.SetResourceReference(ForegroundProperty, Exporter.Outcome(Current, r) == "erro" ? "Bad" : Exporter.Outcome(Current, r) == "acerto" ? "Good" : "Muted");
            foreach (var b in answers) { var selected = (string)b.Content == r.Answer; b.SetResourceReference(BackgroundProperty, selected ? "Accent" : "Surface"); if (selected) b.Foreground = Brushes.White; else b.SetResourceReference(ForegroundProperty, "Ink"); b.IsEnabled = editable; }
            blank.IsEnabled = confidence.IsEnabled = review.IsEnabled = editable; notes.IsReadOnly = !editable;
            confidence.SelectedIndex = r.Confidence switch { "certeza" => 1, "duvida" => 2, "chute" => 3, _ => 0 };
            review.IsChecked = r.Review; notes.Text = r.Notes; postNotes.Text = files.ReviewNotes(Attempt.Id, Current.Id);
            pause.Content = engine.Running ? "Ⅱ  Pausar" : "▶  Retomar"; pause.Visibility = Attempt.IsFinished ? Visibility.Collapsed : Visibility.Visible;
            previous.IsEnabled = Attempt.CurrentIndex > 0; next.IsEnabled = Attempt.CurrentIndex < 89;
            saved.Text = Attempt.IsFinished ? "Tentativa finalizada · respostas preservadas" : engine.Running ? "✓ Salvo neste dispositivo" : "Ⅱ Pausado · retome para responder";
            scratch?.SetPaused(!engine.Running); PaintClock();
        }
        finally { painting = false; }
    }
    private void SaveAnswer() => SaveAnswer(null, false);
    private void SaveAnswer(string? answer, bool replace)
    {
        if (painting || Attempt.IsFinished || !engine.Running) return;
        Flush(); var r = Attempt.Response(Current);
        engine.Update(replace ? answer : r.Answer, new[] { "nao_informada", "certeza", "duvida", "chute" }[confidence.SelectedIndex], review.IsChecked == true, notes.Text);
        Paint();
    }
    private void NoteChanged() { if (painting) return; dirty = true; saved.Text = "Salvando…"; debounce.Stop(); debounce.Start(); }
    public void Flush()
    {
        debounce.Stop(); scratch?.Flush(); if (!dirty) return;
        if (Attempt.IsFinished) files.SaveReview(Attempt, Current, postNotes.Text);
        else if (engine.Running) { var r = Attempt.Response(Current); engine.Update(r.Answer, r.Confidence, r.Review, notes.Text); }
        dirty = false; saved.Text = "✓ Salvo neste dispositivo";
    }
    private void Navigate(int index)
    {
        if (index < 0 || index >= Attempt.Questions.Count) return;
        Flush(); engine.Navigate(index); if (scratch != null) LoadScratch(); Paint(); ShowPdf();
    }
    private void TogglePause() { Flush(); if (engine.Running) engine.Pause(); else engine.Resume(); Paint(); }
    public void PauseForNavigation() { Flush(); engine.Pause("left_session"); Paint(); }
    private void TogglePanel() { if (scratchVisible) ToggleScratch(); if (side.Width.Value == 0) { side.MinWidth = 292; side.Width = new GridLength(shell.Preferences.AnswerWidth); } else { side.MinWidth = 0; side.Width = new GridLength(0); } }
    public void PauseWhenInactive()
    {
        if (disposed || Application.Current.Windows.OfType<Window>().Any(w => w.IsActive)) return;
        Flush(); engine.Pause("window_deactivated"); Paint();
    }
    private void Finish()
    {
        if (confirmingFinish || disposed) return;
        Flush(); if (Attempt.IsFinished) { shell.ShowResults(Attempt); return; }
        var wasRunning = engine.Running; engine.Pause("finish_confirmation"); Paint();
        bool confirmed;
        confirmingFinish = true;
        try { confirmed = UI.Confirm("Finalizar e corrigir?", $"Você respondeu {Attempt.Answered} de 90 questões. Após finalizar, as respostas, anotações e rascunhos desta tentativa ficam preservados. Você poderá acrescentar observações de revisão.", "Finalizar e corrigir"); }
        finally { confirmingFinish = false; }
        if (disposed || Attempt.IsFinished) return;
        if (!confirmed) { if (wasRunning) engine.Resume(); Paint(); return; }
        engine.Finish(); shell.ShowResults(Attempt);
    }
    private void Card()
    {
        var dialog = new Window { Title = "Cartão de respostas", Width = 670, Height = 700, MinWidth = 530, MinHeight = 450, Owner = shell, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new UniformGrid { Columns = 10 }; var count = UI.Text($"{Attempt.Answered} respondidas · {90 - Attempt.Answered} pendentes", 15, "Muted");
        var filter = UI.Choice(new[] { "Todas", "Não respondidas", "Para revisar" }, null, 200);
        void Render()
        {
            grid.Children.Clear();
            for (var i = 0; i < Attempt.Questions.Count; i++)
            {
                var index = i; var q = Attempt.Questions[i]; var r = Attempt.Response(q);
                if ((string)filter.SelectedItem == "Não respondidas" && r.Answer != null || (string)filter.SelectedItem == "Para revisar" && !r.Review) continue;
                var b = UI.Button(q.Number + (r.Review ? " •" : ""), () => { Navigate(index); Render(); }); b.Margin = new Thickness(3); b.Height = 44; b.Padding = new Thickness(0);
                b.SetResourceReference(BackgroundProperty, r.Answer == null ? "Surface" : "AccentSoft"); b.SetResourceReference(ForegroundProperty, r.Review ? "Warn" : "Ink");
                if (index == Attempt.CurrentIndex) { b.BorderBrush = UI.Brush("Accent"); b.BorderThickness = new Thickness(3); }
                grid.Children.Add(b);
            }
        }
        filter.SelectionChanged += (_, _) => Render(); Render();
        dialog.Content = new Border { Padding = new Thickness(24), Child = UI.Scroll(UI.Stack(UI.Text("Cartão de respostas", 25, bold: true), count, filter, UI.Text("Verde: respondida   ·   Neutro: em branco   ·   Âmbar e •: revisar   ·   Borda: atual", 12, "Muted"), grid, UI.Row(UI.Button("Fechar cartão", dialog.Close)))) };
        dialog.ShowDialog();
    }
    private void ToggleScratch()
    {
        if (scratchWindow != null) { scratchWindow.Activate(); return; }
        if (scratchVisible) { scratch!.Flush(); scratchVisible = false; scratchHost.Visibility = Visibility.Collapsed; answerPanel.Visibility = Visibility.Visible; side.MinWidth = 292; side.Width = new GridLength(shell.Preferences.AnswerWidth); ApplyPosition(false); return; }
        scratchVisible = true; side.MinWidth = 380; side.Width = new GridLength(440); answerPanel.Visibility = Visibility.Collapsed; scratchHost.Visibility = Visibility.Visible; LoadScratch(); ApplyPosition(shell.Preferences.ScratchOnLeft);
    }
    private void LoadScratch()
    {
        scratch?.Flush(); scratchHost.Content = null;
        if (scratchWindow != null) scratchWindow.Content = null;
        scratch = new ScratchPad(files.Folder(Attempt.Id, Current.Id), Attempt.IsFinished, shell.Preferences.Paper, ToggleDetach, () => { shell.Preferences.ScratchOnLeft = !shell.Preferences.ScratchOnLeft; shell.SavePreferences(); ApplyPosition(shell.Preferences.ScratchOnLeft); });
        scratch.SetPaused(!engine.Running);
        scratch.SetDetached(scratchWindow != null);
        if (scratchWindow != null) { scratchWindow.Title = $"Rascunho · questão {Current.Number}"; scratchWindow.Content = new Border { Padding = new Thickness(14), Child = scratch }; }
        else scratchHost.Content = scratch;
    }
    private void ApplyPosition(bool left)
    {
        Grid.SetColumn(pdf, left ? 2 : 0); Grid.SetColumn(scratchHost, left ? 0 : 2);
        body.ColumnDefinitions[0].Width = left ? new GridLength(440) : new GridLength(1, GridUnitType.Star);
        side.Width = left ? new GridLength(1, GridUnitType.Star) : new GridLength(scratchVisible ? 440 : shell.Preferences.AnswerWidth);
    }
    private void DetachScratch()
    {
        if (scratchWindow != null) return;
        scratchHost.Content = null; scratchHost.Visibility = Visibility.Collapsed; scratchVisible = false; answerPanel.Visibility = Visibility.Visible; side.MinWidth = 292; ApplyPosition(false);
        scratch!.SetDetached(true);
        var detached = new Window { Title = $"Rascunho · questão {Current.Number}", Width = 900, Height = 680, MinWidth = 540, MinHeight = 400, Owner = shell, Content = new Border { Padding = new Thickness(14), Child = scratch } };
        scratchWindow = detached;
        scratchWindow.Closing += (_, e) => { try { scratch?.Flush(); } catch (Exception ex) { e.Cancel = true; UI.Alert("Não foi possível salvar o rascunho", ex.Message); } };
        scratchWindow.Closed += (_, _) => { if (ReferenceEquals(scratchWindow, detached)) { scratchWindow = null; scratch = null; } };
        scratchWindow.Deactivated += (_, _) => Dispatcher.BeginInvoke(PauseWhenInactive, DispatcherPriority.Background);
        scratchWindow.Show();
    }
    private void ToggleDetach()
    {
        if (scratchWindow == null) { DetachScratch(); return; }
        var window = scratchWindow; var pad = scratch!;
        pad.Flush();
        if (window.Content is Border container) container.Child = null;
        window.Content = null; scratchWindow = null; window.Close(); scratch = pad;
        pad.SetDetached(false); scratchHost.Content = pad; scratchHost.Visibility = Visibility.Visible;
        answerPanel.Visibility = Visibility.Collapsed; scratchVisible = true; side.MinWidth = 380;
        ApplyPosition(shell.Preferences.ScratchOnLeft);
    }
    private void KeyDownHandler(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Alt)) return;
        if (Keyboard.FocusedElement is TextBox) return;
        var action = e.Key == Key.Right ? "next" : e.Key == Key.Left ? "previous" : e.Key >= Key.A && e.Key <= Key.E ? e.Key.ToString() : "";
        if (action.Length == 0) return; e.Handled = true; UI.Safe(() => Shortcut(action));
    }
    private void Shortcut(string action) { if (action == "next") Navigate(Attempt.CurrentIndex + 1); else if (action == "previous") Navigate(Attempt.CurrentIndex - 1); else if (action is "A" or "B" or "C" or "D" or "E") SaveAnswer(action, true); }
    public void Dispose() { if (disposed) return; Flush(); engine.Pause("closed_session"); disposed = true; timer.Stop(); debounce.Stop(); scratchWindow?.Close(); pdf.Dispose(); }
}
