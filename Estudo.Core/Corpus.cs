using System.Globalization;
using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;

namespace Estudo.Core;

public sealed record SourceDocument(string Id, string Exam, int Year, string Stage, string Title,
    string Type, string Version, string Language, string Day, string Application, string KeyStatus,
    string RelativePath, string Sha256, string Url)
{
    public string Label => $"{Exam} {Year} · {Title}";
    public string DisplayTitle => Title.StartsWith($"{Exam} {Year}", StringComparison.CurrentCultureIgnoreCase) ? Title : $"{Exam} {Year} · {Title}";
    public string Details => string.Join(" · ", new[] { Stage.Length > 0 ? $"Etapa {Stage}" : "", Human(Version), Human(Language), Human(Application), Human(KeyStatus),
        RelativePath.Contains("CONFERIR_VERSAO", StringComparison.OrdinalIgnoreCase) ? "VERSÃO A CONFERIR" : "" }.Where(s => s.Length > 0).Distinct());
    private static string Human(string value) => value switch
    {
        "" or "nao_se_aplica" => "", "ingles" => "Inglês", "espanhol" => "Espanhol", "frances" => "Francês",
        "regular" => "Regular", "anual" => "Anual", "ledor" => "LEDOR", "adaptada" => "ADAPTADA",
        "preliminar" => "Preliminar", "definitivo" => "Definitivo", _ => value.Replace('_', ' ')
    };
    public override string ToString() => Title;
}

public sealed record Question(string Id, int Year, int Day, int Number, string Language, string Area,
    string OfficialAnswer, string Pdf, string KeyPdf, int Page, string PdfHash, string KeyHash);

public sealed class Corpus
{
    public string Root { get; }
    public List<SourceDocument> Documents { get; }
    public List<Question> Questions { get; }
    public Corpus(string root, string? catalogDirectory = null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        catalogDirectory ??= Path.Combine(root, "Dados");
        Documents = ReadCsv(Path.Combine(catalogDirectory, "PROVAS_MANIFEST.csv"))
            .Select(r => new SourceDocument(r["id"], r["exame"], int.Parse(r["ano_edicao"], CultureInfo.InvariantCulture),
                r["etapa"], r["titulo"], r["tipo"], r["versao"], r["idioma"], r["dia"], r["aplicacao"],
                r["situacao_gabarito"], r["arquivo_local"], r["sha256"], First(r["url_final"], r["url_download"])))
            .DistinctBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        Questions = ReadCsv(Path.Combine(catalogDirectory, "ENEM_QUESTOES_INDEX.csv"))
            .Select(r => new Question(r["id_item"], int.Parse(r["ano"]), int.Parse(r["dia"]),
                int.Parse(r["numero_questao"]), r["idioma_opcao"], r["area"], r["resposta_oficial"],
                r["pdf_original"], r["pdf_gabarito"], int.Parse(r["pagina_pdf"]),
                r["sha256_pdf_original"], r["sha256_pdf_gabarito"])).ToList();
    }
    private static string First(string a, string b) => a.Length > 0 ? a : b;
    public string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("O catálogo deve conter caminhos relativos.");
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Arquivo fora da pasta do projeto.");
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo do acervo não encontrado.", path);
        return path;
    }
    public string Verify(string relative, string expected)
    {
        var path = Resolve(relative);
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (expected.Length != 64 || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O SHA-256 diverge do catálogo: {relative}. Confira o arquivo antes de usar.");
        return path;
    }
    public List<Question> ForExam(int year, int day, string language)
    {
        if (language is not ("ingles" or "espanhol")) throw new ArgumentException("Idioma inválido.");
        var rows = Questions.Where(q => q.Year == year && q.Day == day &&
            (q.Language == "nao_se_aplica" || q.Language == language)).OrderBy(q => q.Number).ToList();
        if (rows.Count != 90 || rows.Select(q => q.Number).Distinct().Count() != 90 ||
            rows.Any(q => q.OfficialAnswer is not ("A" or "B" or "C" or "D" or "E" or "ANULADA")))
            throw new InvalidDataException("Esta prova ainda não tem 90 posições com gabarito validado.");
        if (rows.Select(q => q.Pdf).Distinct().Count() != 1 || rows.Select(q => q.KeyPdf).Distinct().Count() != 1)
            throw new InvalidDataException("Cadernos inconsistentes no índice.");
        Verify(rows[0].Pdf, rows[0].PdfHash);
        Verify(rows[0].KeyPdf, rows[0].KeyHash);
        return rows;
    }
    public static IEnumerable<Dictionary<string, string>> ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, System.Text.Encoding.UTF8);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields() ?? throw new InvalidDataException("CSV vazio.");
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            if (fields.Length != headers.Length) throw new InvalidDataException($"Linha inválida em {path}.");
            yield return headers.Select((h, i) => (h, fields[i])).ToDictionary(x => x.h, x => x.Item2);
        }
    }
}
