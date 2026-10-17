using System.IO;
using System.Text.Json;
using Estudo.Core;
using Microsoft.Data.Sqlite;

internal static class StudyTests
{
    public static void Run(string root, Attempt a, AttemptStore store, Action<bool, string> check, Action<Action, string> reject)
    {
        var q = a.Questions[0]; var original = JsonSerializer.Serialize(store.All().Single(x => x.Id == a.Id));
        var data = store.GetQuestionStudy(q);
        check(data.Topics.Count == 0 && data.Regions.Count == 0, "Unclassified questions remain usable");
        data.Regions.Add(new(1, .05, .1, .42, .7)); data.Regions.Add(new(2, .5, .05, .45, .4));
        var functions = new TopicLabel("Matemática", "Matemática", "Funções", "Função quadrática", ["gráfico"]);
        data.Topics.Add(functions); data.Topics.Add(new("Matemática", "Matemática", "Geometria", "", [])); store.SaveQuestionStudy(q, data);
        var reopened = new AttemptStore(store.DirectoryPath).GetQuestionStudy(q);
        check(reopened.Regions.Count == 2 && reopened.Regions[1].Page == 2 && reopened.Regions[0].Width == .42, "Multiple normalized PDF regions survive restart");
        check(reopened.Topics.Count == 2 && reopened.Topics[0].Tags.Single() == "gráfico", "Multiple manual themes and tags survive restart");
        reopened.Regions[0] = new(3, .1, .2, .8, .3); reopened.Regions.RemoveAt(1); store.SaveQuestionStudy(q, reopened);
        check(store.GetQuestionStudy(q).Regions.Single().Page == 3, "Manual regions can be corrected and removed");
        check(store.GetQuestionStudy(q with { PdfHash = new string('f', 64) }).Regions.Count == 0, "Coordinates are isolated by original PDF hash");
        reject(() => store.SaveQuestionStudy(q with { PdfHash = new string('f', 64) }, data), "Mismatched metadata source rejected");
        reject(() => new QuestionRegion(0, 0, 0, 1, 1).Validate(), "Zero PDF page rejected");
        reject(() => new QuestionRegion(1, .8, 0, .3, 1).Validate(), "Out-of-page crop rejected");
        reject(() => new QuestionRegion(1, double.NaN, 0, 1, 1).Validate(), "Nonfinite crop rejected");
        reject(() => new QuestionRegion(1, 0, 0, 0, 1).Validate(), "Empty crop rejected");
        reject(() => new TopicLabel("", "", "", "", []).Validate(), "Incomplete classification rejected without blocking exams");
        var second = store.GetQuestionStudy(a.Questions[1]); second.Topics.Add(functions); store.SaveQuestionStudy(a.Questions[1], second);
        var annulled = store.GetQuestionStudy(a.Questions[^1]); annulled.Topics.Add(functions); store.SaveQuestionStudy(a.Questions[^1], annulled);
        var subject = ContentReports.Calculate([a], store.GetQuestionStudy, true).Single(x => x.Subject == "Funções");
        check(subject.Correct == 1 && subject.Incorrect == 1 && subject.Annulled == 1 && subject.Answered == 2, "Content outcomes separate annulments and answered denominator");
        check(subject.SmallSample && subject.Accuracy == null && subject.DistinctAnswered == 2, "Small samples show counts instead of misleading percentages");
        var discipline = ContentReports.Calculate([a], store.GetQuestionStudy, false).Single();
        check(discipline.Answered == 2, "Multiple themes do not duplicate an item within a discipline");
        var repeat = JsonSerializer.Deserialize<Attempt>(JsonSerializer.Serialize(a))!; repeat.Id = Guid.NewGuid().ToString("N");
        var repeated = ContentReports.Calculate([a, repeat], store.GetQuestionStudy, true).Single(x => x.Subject == "Funções");
        check(repeated.Answered == 4 && repeated.DistinctAnswered == 2 && repeated.Accuracy == null, "Retakes distinguish responses from distinct questions");
        repeat.Status = "paused";
        check(ContentReports.Calculate([repeat], store.GetQuestionStudy, true).Count == 0, "Unfinished attempts never reveal content grading");
        repeat.Status = "finished";
        for (var i = 2; i < 5; i++)
        {
            var metadata = store.GetQuestionStudy(a.Questions[i]); metadata.Topics.Add(functions); store.SaveQuestionStudy(a.Questions[i], metadata);
            repeat.Response(repeat.Questions[i]).Answer = repeat.Questions[i].OfficialAnswer;
        }
        var enough = ContentReports.Calculate([repeat], store.GetQuestionStudy, true).Single(x => x.Subject == "Funções");
        check(enough.Answered == 5 && enough.DistinctAnswered == 5 && Math.Abs(enough.Accuracy!.Value - .8) < .001, "Accuracy uses actual classified answers once sample is sufficient");
        check(enough.Observations.Any(x => x.Outcome == "erro") && enough.Observations.Any(x => x.Review), "Content review retains error and marked item references");
        check(ContentReports.SameExam(a, repeat), "Comparable attempts require same item bank and language");
        repeat.Language = "espanhol"; check(!ContentReports.SameExam(a, repeat), "Different language retakes are not compared");
        repeat.Language = a.Language; repeat.Questions[0] = repeat.Questions[0] with { KeyHash = new string('e', 64) };
        check(!ContentReports.SameExam(a, repeat), "Different official answer-key versions are not compared");
        check(JsonSerializer.Serialize(store.All().Single(x => x.Id == a.Id)) == original, "Classification edits preserve immutable legacy attempt snapshots");
        TestMigration(root, a, check, reject);
    }
    private static void TestMigration(string root, Attempt a, Action<bool, string> check, Action<Action, string> reject)
    {
        var folder = Path.Combine(root, "legacy-v1"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "estudos.sqlite3"); var snapshot = JsonSerializer.Serialize(a);
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE attempts(id TEXT PRIMARY KEY,updated_utc TEXT NOT NULL,snapshot TEXT NOT NULL);
                CREATE TABLE events(sequence INTEGER PRIMARY KEY AUTOINCREMENT,attempt_id TEXT NOT NULL,utc TEXT NOT NULL,kind TEXT NOT NULL,question_id TEXT,data TEXT NOT NULL);
                INSERT INTO attempts VALUES($id,$utc,$snapshot);
                INSERT INTO events(attempt_id,utc,kind,data) VALUES($id,$utc,'finished','{}');
                PRAGMA user_version=1;
                """;
            cmd.Parameters.AddWithValue("$id", a.Id); cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$snapshot", snapshot); cmd.ExecuteNonQuery();
        }
        var migrated = new AttemptStore(folder);
        check(migrated.All().Single().Responses[qId(a)].Notes.Contains("segunda linha") && migrated.Events(a.Id).Count == 1, "Version 1 migration preserves Unicode notes, answers and event log");
        var backup = Directory.GetFiles(Path.Combine(folder, "Backups"), "*.sqlite3").Single();
        using (var db = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version";
            check(Convert.ToInt32(cmd.ExecuteScalar()) == 1, "Pre-migration backup preserves original schema");
            cmd.CommandText = "SELECT snapshot FROM attempts"; check((string)cmd.ExecuteScalar()! == snapshot, "Migration backup preserves exact original snapshot bytes");
        }
        _ = new AttemptStore(folder);
        check(Directory.GetFiles(Path.Combine(folder, "Backups"), "*.sqlite3").Length == 1, "Migration is idempotent and does not repeat backups on restart");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery(); }
        reject(() => new AttemptStore(folder), "Future database versions are refused instead of downgraded");
        static string qId(Attempt attempt) => attempt.Questions[0].Id;
    }
}
