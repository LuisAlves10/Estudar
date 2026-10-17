using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Estudo.Core;
using Estudo.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class InterfaceVerification
{
    public static int Run(string projectRoot, string output)
    {
        AppContext.SetSwitch("Estudar.NonInteractiveVerification", true);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var frame = new DispatcherFrame(); var exit = 1;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Verify(Path.GetFullPath(projectRoot), Path.GetFullPath(output)); exit = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame); app.Shutdown(); return exit;
    }
    private static async Task Verify(string root, string output)
    {
        Directory.CreateDirectory(output);
        var documents = Path.Combine(output, "documentos-sinteticos"); Directory.CreateDirectory(documents);
        var file = Path.Combine(documents, "demonstracao.pdf"); WritePdf(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        var relative = Path.GetRelativePath(root, file);
        var corpus = new Corpus(root);
        var store = new AttemptStore(Path.Combine(output, "registros-sinteticos"));
        var questions = Enumerable.Range(91, 90).Select(n => new Question($"DEMO{n}", 2025, 2, n, "nao_se_aplica",
            n < 136 ? "Ciencias da Natureza e suas Tecnologias" : "Matematica e suas Tecnologias", "ABCDE"[(n - 91) % 5].ToString(), relative, relative, 1, hash, hash)).ToList();
        Attempt Fixture(bool finished, int correct)
        {
            var a = new Attempt { Year = 2025, Day = 2, Language = "nao_se_aplica", Status = finished ? "finished" : "paused", Questions = questions, CurrentIndex = 45, ActiveSeconds = 3140, FinishedUtc = finished ? DateTimeOffset.UtcNow : null };
            foreach (var q in questions) a.Responses[q.Id] = new() { Answer = q.Number - 91 < correct ? q.OfficialAnswer : q.Number < 161 ? (q.OfficialAnswer == "A" ? "B" : "A") : null,
                Confidence = q.Number % 3 == 0 ? "certeza" : "duvida", Review = q.Number % 7 == 0, ActiveSeconds = 35 };
            store.Save(a, "synthetic_fixture"); return a;
        }
        var running = Fixture(false, 37); var finished = Fixture(true, 42); _ = Fixture(true, 35);
        var current = questions[45]; var study = store.GetQuestionStudy(current);
        study.Regions = [new(1, .05, .05, .9, .5), new(2, .05, .05, .9, .35)];
        study.Topics.Add(new("Matemática", "Matemática", "Funções", "Função quadrática", ["demonstração"])); store.SaveQuestionStudy(current, study);
        var checks = new List<string>();
        void Check(bool value, string description) { if (!value) throw new Exception(description); checks.Add(description); Console.WriteLine("PASS " + description); }
        var shell = new MainWindow(corpus, store, new Preferences(), Path.Combine(root, "Dados"), Path.Combine(root, "Aplicativo"), true)
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1366, Height = 768,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
        try
        {
            shell.Show(); await Layout(shell); Check(!shell.IsActive, "Verification window never activated");
            shell.ShowPage("Biblioteca"); await Layout(shell); await Capture(shell, Path.Combine(output, "biblioteca.png"));
            shell.OpenAttempt(running); await Layout(shell);
            var exam = Descendants(shell).OfType<ExamView>().Single();
            var browser = Descendants(shell).OfType<WebView2>().Single();
            await WaitFor(async () => browser.CoreWebView2 != null && await browser.ExecuteScriptAsync("document.querySelectorAll('.page-card').length===2 && document.getElementById('status').textContent.includes('recorte')") == "true");
            Check(await browser.ExecuteScriptAsync("document.querySelectorAll('.caption').length") == "2", "Native WebView2 renders both pages of a question");
            var editor = QuestionStudyEditor.Create(shell, store, current, () => { });
            editor.WindowStartupLocation = WindowStartupLocation.Manual; editor.Left = -20000; editor.Top = -20000; editor.ShowActivated = false; editor.ShowInTaskbar = false;
            editor.Show(); await Layout(editor);
            var fields = Descendants(editor).OfType<TextBox>().ToArray();
            fields[1].Text = "Matemática"; fields[2].Text = "Geometria"; fields[3].Text = "Área"; fields[4].Text = "plana, desenho";
            Click(editor, "Salvar classificação");
            Check(store.GetQuestionStudy(current).Topics.Count == 2, "Native classification editor saves an additional theme");
            Descendants(editor).OfType<ListBox>().Single().SelectedIndex = 1; fields[3].Text = "Área do triângulo"; Click(editor, "Salvar classificação");
            Check(store.GetQuestionStudy(current).Topics[1].Subsubject == "Área do triângulo", "Native classification editor updates a saved theme");
            Descendants(editor).OfType<ListBox>().Single().SelectedIndex = 1; Click(editor, "Remover selecionada");
            Check(store.GetQuestionStudy(current).Topics.Count == 1 && !editor.IsActive, "Native classification removal preserves other themes without activating the window");
            editor.Close();
            await browser.ExecuteScriptAsync("document.getElementById('full').click()");
            await WaitFor(async () => await browser.ExecuteScriptAsync("document.querySelectorAll('.textLayer span').length>0") == "true");
            Check(await browser.ExecuteScriptAsync("document.querySelectorAll('.page-card').length") == "1", "Full original page remains accessible with selectable text");
            await browser.ExecuteScriptAsync("document.getElementById('region-tools').open=true;document.getElementById('add').click()");
            await WaitFor(async () => await browser.ExecuteScriptAsync("!!document.querySelector('.selection-layer')") == "true");
            var boundsJson = await browser.ExecuteScriptAsync("JSON.stringify((()=>{const r=document.querySelector('.selection-layer').getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height}})())");
            var bounds = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Deserialize<string>(boundsJson)!);
            var x = bounds.GetProperty("x").GetDouble(); var y = bounds.GetProperty("y").GetDouble(); var w = bounds.GetProperty("w").GetDouble(); var h = bounds.GetProperty("h").GetDouble();
            async Task Mouse(string type, double px, double py, int buttons) => await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                JsonSerializer.Serialize(new { type, x = px, y = py, button = "left", buttons, clickCount = 1 }));
            await Mouse("mousePressed", x + w * .1, y + h * .1, 1);
            await Mouse("mouseMoved", x + w * .85, y + h * .3, 1);
            await Mouse("mouseReleased", x + w * .85, y + h * .3, 0);
            Check(await browser.ExecuteScriptAsync("!document.getElementById('save').disabled") == "true", "Manual rectangle selection enables explicit save");
            await browser.ExecuteScriptAsync("document.getElementById('save').click()");
            await WaitFor(() => Task.FromResult(store.GetQuestionStudy(current).Regions.Count == 3));
            Check(Math.Abs(store.GetQuestionStudy(current).Regions[^1].Width - .75) < .01, "Browser selection persists normalized coordinates through Core");
            // Keep two demonstration regions in the documentation screenshot.
            store.SaveQuestionStudy(current, study); await Descendants(shell).OfType<PdfPane>().Single().UpdateRegions(study);
            Descendants(exam).OfType<Expander>().Single(e => (string?)e.Header == "Anotações da questão").IsExpanded = true;
            await Layout(shell);
            var note = Descendants(exam).OfType<TextBox>().Single(t => t.MaxHeight == 260); note.Text = "Demonstração: conferir o sinal do coeficiente antes de calcular.";
            Click(exam, "B"); Click(exam, "Rascunho"); await Layout(shell);
            var pad = Descendants(exam).OfType<ScratchPad>().Single(); var ink = Descendants(pad).OfType<InkCanvas>().Single();
            ink.Strokes.Add(new Stroke(new StylusPointCollection { new StylusPoint(100, 100, .2f), new StylusPoint(250, 180, .8f), new StylusPoint(410, 100, .5f) }));
            ink.Strokes.Add(new Stroke(new StylusPointCollection { new StylusPoint(100, 280), new StylusPoint(100, 460), new StylusPoint(500, 460) }));
            pad.Flush(); Click(exam, "Próxima →"); await Layout(shell); Click(exam, "← Anterior"); await Layout(shell);
            pad = Descendants(exam).OfType<ScratchPad>().Single(); ink = Descendants(pad).OfType<InkCanvas>().Single();
            Check(ink.Strokes.Count == 2, "Question navigation reloads the correct editable scratchpad");
            var restored = store.All().Single(a => a.Id == running.Id);
            Check(restored.Response(current).Answer == "B" && restored.Response(current).Notes.Contains("coeficiente"), "Native response and notes survive question navigation");
            ClickContaining(exam, "Pausar"); Check(ink.EditingMode == InkCanvasEditingMode.None, "Native pause locks the scratchpad");
            ClickContaining(exam, "Retomar"); Check(ink.EditingMode == InkCanvasEditingMode.Ink && ink.Strokes.Count == 2, "Native resume preserves strokes");
            await WaitFor(async () => await browser.ExecuteScriptAsync("document.querySelectorAll('.caption').length===2") == "true");
            await Capture(shell, Path.Combine(output, "simulado.png"));
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                shell.Width = 1366 / scale; shell.Height = 768 / scale; await Layout(shell);
                var next = Descendants(exam).OfType<Button>().Single(b => (string?)b.Content == "Próxima →");
                var rect = next.TransformToAncestor((Visual)shell.Content).TransformBounds(new Rect(next.RenderSize));
                Check(rect.Bottom <= ((FrameworkElement)shell.Content).ActualHeight + 1 && rect.Right <= ((FrameworkElement)shell.Content).ActualWidth + 1, $"Next question remains visible at equivalent {scale:P0} layout");
                await WaitFor(async () => await browser.ExecuteScriptAsync("document.body.getAttribute('aria-busy')!=='true'") == "true");
                var readingHeight = double.Parse(await browser.ExecuteScriptAsync("document.getElementById('scroll').clientHeight"), System.Globalization.CultureInfo.InvariantCulture);
                Console.WriteLine($"Layout {scale:P0}: PDF viewport {readingHeight}px; scratchpad {Descendants(pad).OfType<ScrollViewer>().First().ViewportHeight:0} DIP");
                await Capture(shell, Path.Combine(output, $"layout-{(int)(scale * 100)}.png"));
                Check(readingHeight >= 120, $"PDF reading viewport stays usable at equivalent {scale:P0} layout");
                Check(Descendants(pad).OfType<ScrollViewer>().First().ViewportHeight >= 100, $"Scratch paper viewport stays usable at equivalent {scale:P0} layout");
            }
            shell.Width = 1366; shell.Height = 768; shell.ShowResults(finished); await Layout(shell);
            Check(Descendants(shell).OfType<TextBlock>().Any(t => t.Text == "Tentativas desta mesma prova"), "Same-exam comparison appears for real synthetic records");
            await Capture(shell, Path.Combine(output, "resultados.png"));
            shell.ShowPage("Revisão"); await Layout(shell); Click(shell, "Revisão por conteúdo"); await Layout(shell);
            Check(Descendants(shell).OfType<ContentReportView>().Any(), "Content report opens from native review navigation");
            await Capture(shell, Path.Combine(output, "conteudos.png"));
            shell.ShowPage("Configurações"); await Layout(shell);
            var theme = Descendants(shell).OfType<ComboBox>().Single(c => c.Items.Contains("Escuro")); theme.SelectedItem = "Escuro";
            shell.ShowPage("Biblioteca"); await Layout(shell); await Capture(shell, Path.Combine(output, "biblioteca-escuro.png"));
            Check(!shell.IsActive, "Background verification did not take desktop focus");
            File.WriteAllText(Path.Combine(output, "interface-verificada.json"), JsonSerializer.Serialize(new { checks, limitations = new[] { "DPI layout equivalence is not a physical monitor/driver DPI test.", "Pen pressure values are synthetic; physical tablet was not operated." }, screenshots = "Native WPF render plus native WebView2 CapturePreview, same live layout; synthetic exam document." }, AttemptStore.JsonOptions));
        }
        finally { shell.Close(); }
    }
    private static void Click(DependencyObject root, string label) => Descendants(root).OfType<Button>().First(b => (string?)b.Content == label && b.IsVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void ClickContaining(DependencyObject root, string label) => Descendants(root).OfType<Button>().First(b => b.Content is string s && s.Contains(label) && b.IsVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Layout(Window window) { window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Render); await Task.Delay(180); window.UpdateLayout(); }
    private static async Task WaitFor(Func<Task<bool>> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until) { if (await predicate()) return; await Task.Delay(100); }
        throw new TimeoutException("The native viewer did not reach the expected state.");
    }
    private static async Task Capture(Window window, string path)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawImage(bitmap, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            foreach (var browser in Descendants(root).OfType<WebView2>().Where(b => b.IsVisible && b.CoreWebView2 != null))
            {
                await WaitFor(async () => await browser.ExecuteScriptAsync("document.body.getAttribute('aria-busy')!=='true'") == "true");
                using var stream = new MemoryStream(); await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); stream.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
                var location = browser.TranslatePoint(new Point(0, 0), root); dc.DrawImage(image, new Rect(location, browser.RenderSize));
            }
        }
        var final = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32); final.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(final)); using var file = File.Create(path); encoder.Save(file);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is not Visual) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void WritePdf(string path)
    {
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [4 0 R 6 0 R 8 0 R] /Count 3 >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        for (var p = 1; p <= 3; p++)
        {
            var text = p == 1 ? new[] { "ESTUDAR / DOCUMENTO SINTETICO", "Demonstracao de leitura por questao", "Questao 136 - Funcoes", "Considere a funcao f(x) = x^2 - 6x + 8.", "Determine os zeros e interprete o grafico.", "A resolucao continua na pagina seguinte." }
                : new[] { "DEMONSTRACAO / CONTINUACAO", $"Pagina {p} de 3 - nenhum enunciado oficial", "Use o rascunho para fatorar a expressao.", "Depois, confira o sinal dos coeficientes.", "A) 1 e 8     B) 2 e 4     C) -2 e -4", "D) 0 e 6     E) 3 e 5" };
            var content = "BT /F1 17 Tf 45 740 Td " + string.Join(" 0 -38 Td ", text.Select(t => $"({t.Replace("(", "\\(").Replace(")", "\\)")}) Tj")) + " ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (p - 1) * 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }
        using var stream = File.Create(path); void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n"); var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { offsets.Add(stream.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = stream.Position; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
