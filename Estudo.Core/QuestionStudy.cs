using System.Text.Json;

namespace Estudo.Core;

// Coordinates are fractions of the displayed PDF page, after its intrinsic rotation.
public sealed record QuestionRegion(int Page, double X, double Y, double Width, double Height)
{
    public void Validate()
    {
        if (Page < 1 || new[] { X, Y, Width, Height }.Any(v => !double.IsFinite(v)) ||
            X < 0 || Y < 0 || Width <= 0 || Height <= 0 || X + Width > 1.000001 || Y + Height > 1.000001)
            throw new ArgumentException("O recorte precisa estar dentro da página, em coordenadas relativas de 0 a 1.");
    }
}

public sealed record TopicLabel(string Area, string Discipline, string Subject, string Subsubject, string[] Tags)
{
    public string Source { get; init; } = "manual";
    public string Display => string.Join(" → ", new[] { Area, Discipline, Subject, Subsubject }.Where(s => s.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase));
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Discipline) || string.IsNullOrWhiteSpace(Subject))
            throw new ArgumentException("Preencha disciplina e assunto. Classificar é opcional para resolver a prova.");
        if (new[] { Area, Discipline, Subject, Subsubject }.Concat(Tags).Any(s => s.Length > 160) || Tags.Length > 20 || Source != "manual")
            throw new ArgumentException("Revise o tamanho dos campos e a origem manual da classificação.");
    }
}

public sealed class QuestionStudyData
{
    public string QuestionId { get; set; } = "";
    public string PdfHash { get; set; } = "";
    public List<QuestionRegion> Regions { get; set; } = [];
    public List<TopicLabel> Topics { get; set; } = [];
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed partial class AttemptStore
{
    public QuestionStudyData GetQuestionStudy(Question question)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT metadata FROM question_study WHERE question_id=$id AND pdf_hash=$hash";
        cmd.Parameters.AddWithValue("$id", question.Id); cmd.Parameters.AddWithValue("$hash", question.PdfHash);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<QuestionStudyData>(json)!
            : new() { QuestionId = question.Id, PdfHash = question.PdfHash };
    }
    public void SaveQuestionStudy(Question question, QuestionStudyData data)
    {
        if (data.QuestionId != question.Id || data.PdfHash != question.PdfHash)
            throw new ArgumentException("Os metadados não correspondem à questão e ao PDF original.");
        if (data.Regions.Count > 32 || data.Topics.Count > 30) throw new ArgumentException("Há regiões ou classificações demais nesta questão.");
        foreach (var region in data.Regions) region.Validate();
        foreach (var topic in data.Topics) topic.Validate();
        data.UpdatedUtc = DateTimeOffset.UtcNow;
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO question_study(question_id,pdf_hash,metadata) VALUES($id,$hash,$json)
            ON CONFLICT(question_id,pdf_hash) DO UPDATE SET metadata=excluded.metadata
            """;
        cmd.Parameters.AddWithValue("$id", question.Id); cmd.Parameters.AddWithValue("$hash", question.PdfHash);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(data, JsonOptions)); cmd.ExecuteNonQuery();
    }
}

public sealed record ContentObservation(Attempt Attempt, Question Question, string Outcome, bool Review);
public sealed record ContentSummary(string Area, string Discipline, string Subject, int Correct, int Incorrect,
    int Blank, int Annulled, int DistinctAnswered, IReadOnlyList<ContentObservation> Observations)
{
    public int Answered => Correct + Incorrect;
    public bool SmallSample => Answered < 5 || DistinctAnswered < 5;
    public double? Accuracy => SmallSample ? null : (double)Correct / Answered;
    public string Label => string.Join(" → ", new[] { Area, Discipline, Subject }.Where(s => s.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase));
}

public static class ContentReports
{
    public static List<ContentSummary> Calculate(IEnumerable<Attempt> attempts, Func<Question, QuestionStudyData> lookup, bool bySubject)
    {
        var groups = new Dictionary<(string Area, string Discipline, string Subject), List<ContentObservation>>();
        var cache = new Dictionary<(string, string), QuestionStudyData>();
        foreach (var a in attempts.Where(a => a.IsFinished).DistinctBy(a => a.Id))
        foreach (var q in a.Questions)
        {
            var key = (q.Id, q.PdfHash);
            if (!cache.TryGetValue(key, out var data)) cache[key] = data = lookup(q);
            var labels = data.Topics.Select(t => (t.Area, t.Discipline, Subject: bySubject ? t.Subject : ""))
                .DistinctBy(k => (k.Area.ToUpperInvariant(), k.Discipline.ToUpperInvariant(), k.Subject.ToUpperInvariant()));
            foreach (var label in labels)
            {
                var normalized = (label.Area.ToUpperInvariant(), label.Discipline.ToUpperInvariant(), label.Subject.ToUpperInvariant());
                var existing = groups.Keys.FirstOrDefault(k => (k.Area.ToUpperInvariant(), k.Discipline.ToUpperInvariant(), k.Subject.ToUpperInvariant()) == normalized);
                var target = existing == default ? label : existing;
                if (!groups.TryGetValue(target, out var items)) groups[target] = items = [];
                items.Add(new(a, q, Exporter.Outcome(q, a.Response(q)), a.Response(q).Review));
            }
        }
        return groups.Select(g => new ContentSummary(g.Key.Area, g.Key.Discipline, g.Key.Subject,
            g.Value.Count(x => x.Outcome == "acerto"), g.Value.Count(x => x.Outcome == "erro"),
            g.Value.Count(x => x.Outcome == "em_branco"), g.Value.Count(x => x.Outcome == "anulada"),
            g.Value.Where(x => x.Outcome is "acerto" or "erro").Select(x => (x.Question.Id, x.Question.PdfHash)).Distinct().Count(), g.Value))
            .OrderBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static bool SameExam(Attempt a, Attempt b) => a.Year == b.Year && a.Day == b.Day && a.Language == b.Language &&
        a.Questions.Select(q => (q.Id, q.PdfHash, q.KeyHash, q.OfficialAnswer)).ToHashSet().SetEquals(b.Questions.Select(q => (q.Id, q.PdfHash, q.KeyHash, q.OfficialAnswer)));
}
