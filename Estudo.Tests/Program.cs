using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Estudo.Core;
using Estudo.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "Estudar-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Dados"));
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidDataException) { checks.Add(label); return; } throw new Exception(label); }
        try
        {
            // Generated fixture bytes are never used as official examination documents.
            foreach (var file in new[] { "prova.pdf", "gabarito.pdf" }) File.WriteAllText(Path.Combine(root, file), "%PDF-1.7\nSynthetic fixture: " + file);
            string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file))));
            File.WriteAllText(Path.Combine(root, "Dados", "PROVAS_MANIFEST.csv"), "id,exame,ano_edicao,etapa,titulo,tipo,versao,idioma,dia,aplicacao,situacao_gabarito,arquivo_local,sha256,url_final,url_download\n" +
                $"fixture,ENEM,2025,,Prova sintética,prova,Azul,,1,regular,definitivo,prova.pdf,{Hash("prova.pdf")},https://example.invalid,\n");
            var csv = new StringBuilder("id_item,ano,dia,numero_questao,idioma_opcao,area,resposta_oficial,pdf_original,pdf_gabarito,pagina_pdf,sha256_pdf_original,sha256_pdf_gabarito\n");
            for (var i = 1; i <= 90; i++) csv.AppendLine($"Q{i:000},2025,1,{i},nao_se_aplica,Matematica e suas Tecnologias,{(i == 90 ? "ANULADA" : "ABCDE"[(i-1)%5].ToString())},prova.pdf,gabarito.pdf,1,{Hash("prova.pdf")},{Hash("gabarito.pdf")}");
            File.WriteAllText(Path.Combine(root, "Dados", "ENEM_QUESTOES_INDEX.csv"), csv.ToString());
            var corpus = new Corpus(root); var store = new AttemptStore(Path.Combine(root, "registros"));
            Check(corpus.ForExam(2025, 1, "ingles").Count == 90, "Canonical question positions and hashes");
            Check(new Corpus(root + Path.DirectorySeparatorChar).Resolve("prova.pdf") == corpus.Resolve("prova.pdf"), "Portable catalogue root accepts trailing directory separator");
            Reject(() => corpus.Resolve("../outside.pdf"), "Corpus traversal rejected");
            Reject(() => corpus.Verify("prova.pdf", new string('0', 64)), "Corrupt source rejected");
            var attempt = AttemptEngine.Create(corpus, 2025, 1, "ingles", store); var clock = new FakeClock(); var engine = new AttemptEngine(attempt, store, clock);
            Reject(() => engine.Update("A", "certeza", false, ""), "Paused answer rejected");
            Reject(() => Score.Calculate(attempt), "No grading before finish");
            Reject(() => Exporter.Export(corpus, store, attempt), "No answer key in unfinished export");
            engine.Resume(); clock.Advance(12); engine.Update("A", "chute", true, "Anotação original\nsegunda linha"); engine.Navigate(1); clock.Advance(8); engine.Update("A", "certeza", false, "Erro sintético"); engine.Pause(); clock.Advance(3600); engine.Touch();
            Check(attempt.ActiveSeconds == 20 && attempt.Response(attempt.Questions[0]).ActiveSeconds == 12, "Monotonic time excludes pause and belongs to item");
            var restored = store.All().Single(); Check(restored.CurrentIndex == 1 && restored.Response(restored.Questions[0]).Notes.Contains("segunda linha"), "Legacy schema 1 round-trip with Unicode notes");
            engine.Resume();
            Reject(() => engine.Update("F", "certeza", false, ""), "Invalid response rejected");
            Reject(() => engine.Update("A", "unknown", false, ""), "Invalid confidence rejected");
            engine.Pause();
            var files = new StudyFiles(store.DirectoryPath); Reject(() => files.Folder("../", "Q1"), "Attachment traversal rejected");
            Reject(() => files.SaveReview(attempt, attempt.Questions[0], "too early"), "Review notes cannot precede correction");
            var prefs = new Preferences { Theme = "dark", Paper = "Pontilhado", ScratchOnLeft = true }; prefs.Save(store.DirectoryPath);
            Check(Preferences.Load(store.DirectoryPath).Theme == "dark" && Preferences.Load(store.DirectoryPath).ScratchOnLeft, "Theme and layout preferences persist");
            store.Backup(Path.Combine(root, "backup.sqlite3")); Check(File.Exists(Path.Combine(root, "backup.sqlite3")), "SQLite online backup");
            TestInk(files.Folder(attempt.Id, attempt.Questions[0].Id), Check);
            var previous = JsonSerializer.Serialize(attempt.Responses);
            engine.Finish(); Check(Score.Calculate(attempt).Sum(s => s.Correct) == 1 && Score.Calculate(attempt).Sum(s => s.Incorrect) == 1 && Score.Calculate(attempt).Sum(s => s.Annulled) == 1, "Correct / incorrect / blank / annulled remain distinct");
            Reject(() => store.Save(attempt, "tamper"), "Finished snapshot immutable in database"); Reject(() => engine.Update("B", "certeza", false, ""), "Finished snapshot immutable in engine");
            files.SaveReview(attempt, attempt.Questions[0], "Observação após corrigir");
            Check(JsonSerializer.Serialize(attempt.Responses) == previous, "Post-correction notes do not overwrite original response");
            Check(files.ReviewNotes(attempt.Id, attempt.Questions[0].Id).Contains("corrigir"), "Post-correction notes persist separately");
            var exported = Exporter.Export(corpus, store, attempt); using var payload = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "tentativa.json")));
            var item = payload.RootElement.GetProperty("questions")[0];
            Check(payload.RootElement.GetProperty("schema_version").GetInt32() == 2 && item.GetProperty("attachments").GetArrayLength() == 3, "Export schema 2 includes editable ink, PNG and paper");
            Check(item.GetProperty("post_correction_notes").GetString()!.Contains("corrigir"), "Export includes post-correction notes");
            Check(File.ReadAllText(Path.Combine(exported, "LEIA_COM_A_IA.md")).Contains("![Rascunho"), "Markdown links scratch images");
            foreach (var line in File.ReadAllLines(Path.Combine(exported, "SHA256SUMS.txt"))) { var relative = line[66..]; Check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(exported, relative)))) == line[..64], "Export file integrity: " + relative); }
            Check(ResultsView.Matches(attempt, attempt.Questions[0], "Acertos com baixa confiança") && ResultsView.Matches(attempt, attempt.Questions[1], "Erros com certeza"), "Confidence review filters use actual outcomes");
            Check(AttemptEngine.Create(corpus, 2025, 1, "ingles", store).Answered == 0, "Retake has independent blank responses");
            Check(store.Events(attempt.Id).Any(e => e.Kind == "finished"), "Transactional event log preserved");
            Console.WriteLine($"PASS: {checks.Count} checks");
            foreach (var check in checks) Console.WriteLine("  " + check);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); Console.Error.WriteLine("Fixture retained at " + root); return 1; }
    }
    private static void TestInk(string folder, Action<bool, string> check)
    {
        var app = new App(); app.InitializeComponent();
        var pad = new ScratchPad(folder, false, "Quadriculado", () => { }, () => { });
        var canvas = Descendants(pad).OfType<InkCanvas>().Single();
        var points = new StylusPointCollection { new StylusPoint(30, 40, .2f), new StylusPoint(80, 70, .8f), new StylusPoint(160, 110, .5f) }; var stroke = new Stroke(points);
        canvas.Strokes.Add(stroke); canvas.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent }); pad.Flush();
        check(File.Exists(Path.Combine(folder, "rascunho.png")) && File.Exists(Path.Combine(folder, "rascunho.isf")), "Ink autosave persists PNG and ISF");
        using (var stream = File.OpenRead(Path.Combine(folder, "rascunho.isf"))) { var loaded = new StrokeCollection(stream); check(loaded.Count == 1 && Math.Abs(loaded[0].StylusPoints[1].PressureFactor - .8f) < .01, "Editable stroke and pressure round-trip"); }
        var reopened = new ScratchPad(folder, false, "Branco", () => { }, () => { });
        check(Descendants(reopened).OfType<InkCanvas>().Single().Strokes.Count == 1, "Reopening recovers question ink");
        var undo = Descendants(pad).OfType<Button>().Single(b => (string?)b.Content == "Desfazer"); undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(canvas.Strokes.Count == 0, "Ink undo");
        var redo = Descendants(pad).OfType<Button>().Single(b => (string?)b.Content == "Refazer"); redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(canvas.Strokes.Count == 1, "Ink redo"); pad.Flush();
        var frozen = new ScratchPad(folder, true, "Branco", () => { }, () => { });
        check(Descendants(frozen).OfType<InkCanvas>().Single().EditingMode == InkCanvasEditingMode.None, "Finished ink is read-only");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private sealed class FakeClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += (long)(seconds * TimestampFrequency);
    }
}
