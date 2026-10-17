using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Estudo.Core;

public static class Exporter
{
    public static string Export(Corpus corpus, AttemptStore store, Attempt a)
    {
        if (!a.IsFinished) throw new InvalidOperationException("Finalize antes de exportar a correção.");
        var source = a.Questions[0];
        var pdf = corpus.Verify(source.Pdf, source.PdfHash);
        var key = corpus.Verify(source.KeyPdf, source.KeyHash);
        var directory = Path.Combine(store.DirectoryPath, "Exportacoes", $"ENEM_{a.Year}_D{a.Day}_{a.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}");
        Directory.CreateDirectory(directory);
        File.Copy(pdf, Path.Combine(directory, Path.GetFileName(pdf)));
        File.Copy(key, Path.Combine(directory, Path.GetFileName(key)));
        var files = new StudyFiles(store.DirectoryPath);
        var attachments = new Dictionary<string, string[]>();
        foreach (var q in a.Questions)
        {
            var folder = files.Folder(a.Id, q.Id);
            var copied = new List<string>();
            foreach (var name in new[] { "rascunho.png", "rascunho.isf", "papel.json" })
            {
                var original = Path.Combine(folder, name);
                if (!File.Exists(original)) continue;
                var relative = $"rascunhos/{q.Id}/{name}";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory, relative))!);
                File.Copy(original, Path.Combine(directory, relative));
                copied.Add(relative);
            }
            attachments[q.Id] = copied.ToArray();
        }
        var events = store.Events(a.Id);
        var payload = new
        {
            schema_version = 2, created_utc = DateTimeOffset.UtcNow,
            purpose = "Revisão assistida de uma tentativa real. Não inferir fraquezas sem evidência repetida. Não estimar TRI.",
            attempt = new { a.Id, a.Title, a.Language, a.StartedUtc, a.FinishedUtc, a.ActiveSeconds },
            official_sources = corpus.Documents.Where(d => d.RelativePath == source.Pdf || d.RelativePath == source.KeyPdf),
            questions = a.Questions.Select(q => new
            {
                official = q,
                document_reference = new { pdf = Path.GetFileName(q.Pdf), page = q.Page, key_pdf = Path.GetFileName(q.KeyPdf) },
                personal = a.Response(q),
                post_correction_notes = files.ReviewNotes(a.Id, q.Id),
                attachments = attachments[q.Id],
                outcome = Outcome(q, a.Response(q)),
                classification = new { discipline = "indeterminado", topic = "indeterminado", skill = "indeterminado", confidence = "nao_avaliada" }
            }),
            scores_by_area = Score.Calculate(a), events,
            limitations = new[] { "Tempo ativo estimado: não prova atenção nem tempo de raciocínio.",
                "Páginas são físicas, a partir de 1. Enunciados podem continuar na página seguinte.",
                "Os dois PDFs anexos preservam enunciados, imagens e gabarito. Não foram feitos recortes automáticos.",
                "Acertos por área não correspondem a nota TRI. Anuladas são separadas e excluídas do denominador.",
                "Redação não é corrigida nesta versão. Classificações temáticas e diagnósticos por IA precisam de revisão." }
        };
        File.WriteAllText(Path.Combine(directory, "tentativa.json"), JsonSerializer.Serialize(payload, AttemptStore.JsonOptions), Encoding.UTF8);
        var md = new StringBuilder($"# {a.Title} — revisão da tentativa\n\nLíngua estrangeira: {LanguageName(a.Language)}. Tempo ativo estimado: {TimeSpan.FromSeconds(a.ActiveSeconds):hh\\:mm\\:ss}.\n\n");
        md.AppendLine("Abra os PDFs desta pasta junto com este relatório ou com `tentativa.json`. A exportação é local; nenhum dado foi enviado a uma IA. Os PDFs completos incluem as imagens e a continuação dos enunciados.\n");
        md.AppendLine("Avalie meu raciocínio e separe fato observado, hipótese e informação insuficiente. Não transforme um erro isolado em fraqueza. Use o gabarito oficial; não atribua nota TRI.\n");
        md.AppendLine("| Questão | Área | Minha resposta | Oficial | Resultado | Confiança | Tempo ativo | Página PDF |\n|---|---|---|---|---|---|---|---|");
        foreach (var q in a.Questions)
        {
            var r = a.Response(q);
            md.AppendLine($"| {q.Number} | {AreaName(q.Area)} | {r.Answer ?? "—"} | {q.OfficialAnswer} | {Outcome(q, r)} | {r.Confidence} | {r.ActiveSeconds:F0}s | {q.Page} |");
        }
        md.AppendLine("\n## Anotações e marcações\n");
        foreach (var q in a.Questions.Where(q => a.Response(q).Notes.Length > 0 || a.Response(q).Review))
            md.AppendLine($"### Questão {q.Number}\n\nRevisar: {a.Response(q).Review}.\n\n{a.Response(q).Notes}\n");
        md.AppendLine("## Fontes\n");
        foreach (var q in a.Questions.Where(q => attachments[q.Id].Length > 0 || files.ReviewNotes(a.Id, q.Id).Length > 0))
        {
            md.AppendLine($"### Revisão da questão {q.Number}\n\n{files.ReviewNotes(a.Id, q.Id)}\n");
            foreach (var image in attachments[q.Id].Where(p => p.EndsWith(".png"))) md.AppendLine($"![Rascunho da questão {q.Number}]({image})\n");
        }
        foreach (var d in corpus.Documents.Where(d => d.RelativePath == source.Pdf || d.RelativePath == source.KeyPdf))
            md.AppendLine($"- {d.Title}: {d.Url}\n  - Arquivo: {Path.GetFileName(d.RelativePath)}\n  - SHA-256: `{d.Sha256}`");
        md.AppendLine("\nOs eventos completos e os metadados originais estão no JSON. Notas e respostas são dados pessoais; classificações de tema/habilidade ainda não foram realizadas.");
        File.WriteAllText(Path.Combine(directory, "LEIA_COM_A_IA.md"), md.ToString(), Encoding.UTF8);
        var hashes = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order().Select(p => $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))}  {Path.GetRelativePath(directory, p).Replace('\\', '/')}");
        File.WriteAllLines(Path.Combine(directory, "SHA256SUMS.txt"), hashes);
        return directory;
    }
    public static string Outcome(Question q, Response r) => q.OfficialAnswer == "ANULADA" ? "anulada" : r.Answer == null ? "em_branco" : r.Answer == q.OfficialAnswer ? "acerto" : "erro";
    public static string LanguageName(string language) => language switch { "ingles" => "Inglês", "espanhol" => "Espanhol", _ => "Sem itens de língua estrangeira neste dia" };
    public static string AreaName(string area) => area.StartsWith("Ciencias da Natureza") ? "Natureza" : area.StartsWith("Ciencias Humanas") ? "Humanas" : area.StartsWith("Linguagens") ? "Linguagens" : "Matemática";
}
