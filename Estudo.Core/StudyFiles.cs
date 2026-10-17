using System.Text.Json;

namespace Estudo.Core;

public sealed class ScratchSurface
{
    public double Width { get; set; } = 1600;
    public double Height { get; set; } = 1200;
    public double Zoom { get; set; } = 1;
    public string ZoomMode { get; set; } = "width";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public void Validate()
    {
        if (new[] { Width, Height, Zoom, OffsetX, OffsetY }.Any(v => !double.IsFinite(v)) ||
            Width is < 400 or > 4800 || Height is < 400 or > 4800 || Zoom is < .1 or > 4 ||
            OffsetX < 0 || OffsetY < 0 || ZoomMode is not ("width" or "window" or "custom"))
            throw new InvalidDataException("As dimensões ou o zoom do papel são inválidos. O rascunho original foi preservado.");
    }
}

public sealed class Preferences
{
    public string Theme { get; set; } = "light";
    public string? CorpusPath { get; set; }
    public double AnswerWidth { get; set; } = 308;
    public bool ScratchOnLeft { get; set; }
    public string Paper { get; set; } = "Quadriculado";
    public static Preferences Load(string directory)
    {
        var path = Path.Combine(directory, "preferencias.json");
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new(); }
        catch (JsonException) { File.Copy(path, path + ".invalido-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), false); return new(); }
    }
    public void Save(string directory) => StudyFiles.AtomicWrite(Path.Combine(directory, "preferencias.json"),
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, AttemptStore.JsonOptions)));
}

public sealed class StudyFiles(string directory)
{
    public string Root { get; } = Path.Combine(directory, "Questoes");
    public string Folder(string attemptId, string questionId)
    {
        if (attemptId.Length == 0 || questionId.Length == 0 || attemptId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            questionId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Identificador inválido.");
        return Path.Combine(Root, attemptId, questionId);
    }
    public string ReviewNotes(string attemptId, string questionId)
    {
        var path = Path.Combine(Folder(attemptId, questionId), "revisao.txt");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
    public void SaveReview(Attempt attempt, Question question, string text)
    {
        if (!attempt.IsFinished || !attempt.Questions.Any(q => q.Id == question.Id))
            throw new InvalidOperationException("As observações de correção pertencem a uma tentativa finalizada.");
        AtomicWrite(Path.Combine(Folder(attempt.Id, question.Id), "revisao.txt"), System.Text.Encoding.UTF8.GetBytes(text));
    }
    public static void AtomicWrite(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(data); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}
