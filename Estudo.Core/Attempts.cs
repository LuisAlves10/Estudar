using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Estudo.Core;

public sealed class Response
{
    public string? Answer { get; set; }
    public string Confidence { get; set; } = "nao_informada";
    public bool Review { get; set; }
    public string Notes { get; set; } = "";
    public double ActiveSeconds { get; set; }
}

public sealed class Attempt
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Year { get; set; }
    public int Day { get; set; }
    public string Language { get; set; } = "ingles";
    public string Status { get; set; } = "paused";
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public int CurrentIndex { get; set; }
    public double ActiveSeconds { get; set; }
    public List<Question> Questions { get; set; } = [];
    public Dictionary<string, Response> Responses { get; set; } = [];
    public Response Response(Question q) => Responses[q.Id];
    public string Title => $"ENEM {Year} · Dia {Day}";
    public int Answered => Responses.Values.Count(r => r.Answer != null);
    public bool IsFinished => Status == "finished";
}

public sealed record EventRecord(long Sequence, DateTimeOffset Utc, string Kind, string? QuestionId, JsonElement Data);
public sealed record Score(string Area, int Correct, int Incorrect, int Blank, int Annulled)
{
    public int Valid => Correct + Incorrect + Blank;
    public static List<Score> Calculate(Attempt a)
    {
        if (!a.IsFinished) throw new InvalidOperationException("Finalize a tentativa para corrigir.");
        return a.Questions.GroupBy(q => q.Area).Select(g => new Score(g.Key,
            g.Count(q => q.OfficialAnswer != "ANULADA" && a.Response(q).Answer == q.OfficialAnswer),
            g.Count(q => q.OfficialAnswer != "ANULADA" && a.Response(q).Answer != null && a.Response(q).Answer != q.OfficialAnswer),
            g.Count(q => q.OfficialAnswer != "ANULADA" && a.Response(q).Answer == null),
            g.Count(q => q.OfficialAnswer == "ANULADA"))).ToList();
    }
}

public sealed partial class AttemptStore
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string connectionString;
    public string DirectoryPath { get; }
    public AttemptStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        var database = Path.Combine(DirectoryPath, "estudos.sqlite3");
        var existed = File.Exists(database);
        connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var db = Open();
        using var version = db.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema > 2) throw new InvalidDataException("Este banco foi atualizado por uma versão mais recente do Estudar.");
        if (schema < 2 && existed)
        {
            var backups = Path.Combine(DirectoryPath, "Backups");
            Directory.CreateDirectory(backups);
            Backup(Path.Combine(backups, $"antes_migracao_v2_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.sqlite3"));
        }
        using var journal = db.CreateCommand(); journal.CommandText = "PRAGMA journal_mode=WAL;"; journal.ExecuteNonQuery();
        using var migration = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = migration;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS attempts (id TEXT PRIMARY KEY, updated_utc TEXT NOT NULL, snapshot TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS events (sequence INTEGER PRIMARY KEY AUTOINCREMENT, attempt_id TEXT NOT NULL REFERENCES attempts(id), utc TEXT NOT NULL, kind TEXT NOT NULL, question_id TEXT, data TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS events_attempt ON events(attempt_id, sequence);
            CREATE TABLE IF NOT EXISTS question_study (question_id TEXT NOT NULL, pdf_hash TEXT NOT NULL, metadata TEXT NOT NULL,
                PRIMARY KEY(question_id, pdf_hash));
            """;
        cmd.ExecuteNonQuery();
        if (schema < 2) { version.Transaction = migration; version.CommandText = "PRAGMA user_version=2"; version.ExecuteNonQuery(); }
        migration.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
        cmd.ExecuteNonQuery();
        return db;
    }
    public void Save(Attempt attempt, string kind, string? questionId = null, object? data = null)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var read = db.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT snapshot FROM attempts WHERE id=$id";
        read.Parameters.AddWithValue("$id", attempt.Id);
        if (read.ExecuteScalar() is string existing && JsonSerializer.Deserialize<Attempt>(existing)!.IsFinished)
            throw new InvalidOperationException("A tentativa finalizada é imutável. Inicie outra para refazer.");
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO attempts(id,updated_utc,snapshot) VALUES($id,$utc,$snapshot)
            ON CONFLICT(id) DO UPDATE SET updated_utc=excluded.updated_utc,snapshot=excluded.snapshot;
            INSERT INTO events(attempt_id,utc,kind,question_id,data) VALUES($id,$utc,$kind,$question,$data);
            """;
        cmd.Parameters.AddWithValue("$id", attempt.Id);
        cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(attempt, JsonOptions));
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$question", (object?)questionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(data ?? new { }));
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
    public List<Attempt> All()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT snapshot FROM attempts ORDER BY updated_utc DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<Attempt>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<Attempt>(reader.GetString(0))!);
        return result;
    }
    public List<EventRecord> Events(string attemptId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sequence,utc,kind,question_id,data FROM events WHERE attempt_id=$id ORDER BY sequence";
        cmd.Parameters.AddWithValue("$id", attemptId);
        using var reader = cmd.ExecuteReader();
        var result = new List<EventRecord>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), JsonSerializer.Deserialize<JsonElement>(reader.GetString(4))));
        return result;
    }
    public void Backup(string destination)
    {
        using var source = Open();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination }.ToString());
        target.Open();
        source.BackupDatabase(target);
    }
}

public sealed class AttemptEngine
{
    public Attempt Attempt { get; }
    private readonly AttemptStore store;
    private readonly TimeProvider clock;
    private long last;
    public bool Running { get; private set; }
    public AttemptEngine(Attempt attempt, AttemptStore store, TimeProvider? clock = null)
    {
        Attempt = attempt;
        this.store = store;
        this.clock = clock ?? TimeProvider.System;
    }
    public static Attempt Create(Corpus corpus, int year, int day, string language, AttemptStore store)
    {
        var questions = corpus.ForExam(year, day, language);
        var a = new Attempt { Year = year, Day = day, Language = questions.Any(q => q.Language != "nao_se_aplica") ? language : "nao_se_aplica", Questions = questions };
        foreach (var q in questions) a.Responses[q.Id] = new();
        store.Save(a, "created", data: new { language, source = questions[0].Pdf, questions = questions.Count });
        return a;
    }
    private void Editable()
    {
        if (Attempt.IsFinished) throw new InvalidOperationException("Tentativa finalizada.");
    }
    public void Touch()
    {
        if (!Running) return;
        var now = clock.GetTimestamp();
        var seconds = Math.Max(0, clock.GetElapsedTime(last, now).TotalSeconds);
        Attempt.ActiveSeconds += seconds;
        Attempt.Response(Attempt.Questions[Attempt.CurrentIndex]).ActiveSeconds += seconds;
        last = now;
    }
    public void Resume()
    {
        Editable();
        if (Running) return;
        last = clock.GetTimestamp();
        Running = true;
        Attempt.Status = "running";
        store.Save(Attempt, "resumed");
    }
    public void Pause(string reason = "manual")
    {
        if (!Running || Attempt.IsFinished) return;
        Touch();
        Running = false;
        Attempt.Status = "paused";
        store.Save(Attempt, "paused", data: new { reason });
    }
    public void Navigate(int index)
    {
        if (index < 0 || index >= Attempt.Questions.Count) throw new ArgumentOutOfRangeException(nameof(index));
        Touch();
        Attempt.CurrentIndex = index;
        if (!Attempt.IsFinished) store.Save(Attempt, "question_opened", Attempt.Questions[index].Id);
    }
    public void Update(string? answer, string confidence, bool review, string notes)
    {
        Editable();
        if (!Running) throw new InvalidOperationException("Retome o simulado antes de responder.");
        if (answer is not (null or "A" or "B" or "C" or "D" or "E")) throw new ArgumentException("Resposta inválida.");
        if (confidence is not ("nao_informada" or "certeza" or "duvida" or "chute")) throw new ArgumentException("Confiança inválida.");
        Touch();
        var q = Attempt.Questions[Attempt.CurrentIndex];
        var r = Attempt.Response(q);
        var previous = new { r.Answer, r.Confidence, r.Review, r.Notes };
        r.Answer = answer;
        r.Confidence = confidence;
        r.Review = review;
        r.Notes = notes;
        store.Save(Attempt, "response_updated", q.Id, new { previous, current = new { answer, confidence, review, notes } });
    }
    public void Checkpoint()
    {
        if (!Running) return;
        Touch();
        store.Save(Attempt, "checkpoint");
    }
    public void Finish()
    {
        Editable();
        Touch();
        Running = false;
        Attempt.Status = "finished";
        Attempt.FinishedUtc = DateTimeOffset.UtcNow;
        store.Save(Attempt, "finished", data: new { Attempt.Answered, Attempt.ActiveSeconds });
    }
}
