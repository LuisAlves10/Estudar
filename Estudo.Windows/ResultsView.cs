using System.Windows;
using System.Windows.Controls;
using Estudo.Core;

namespace Estudo.Windows;

public sealed class ResultsView : UserControl
{
    public static string OutcomeLabel(string value) => value switch { "acerto" => "Acerto", "erro" => "Erro", "anulada" => "Anulada", _ => "Em branco" };
    public static bool Matches(Attempt a, Question q, string filter)
    {
        var r = a.Response(q); var outcome = Exporter.Outcome(q, r);
        return filter switch { "Erros" => outcome == "erro", "Acertos" => outcome == "acerto", "Em branco" => outcome == "em_branco", "Marcadas" => r.Review,
            "Chutes" => r.Confidence == "chute", "Erros com certeza" => outcome == "erro" && r.Confidence == "certeza", "Acertos com baixa confiança" => outcome == "acerto" && r.Confidence is "duvida" or "chute", _ => true };
    }
    public ResultsView(MainWindow shell, Attempt a)
    {
        if (!a.IsFinished) throw new InvalidOperationException("Finalize para ver os resultados.");
        var scores = Score.Calculate(a); var page = UI.Stack(UI.Text("Resultados", 30, bold: true), UI.Text($"{a.Title} · {a.FinishedUtc?.ToLocalTime():dd/MM/yyyy HH:mm} · {UI.Duration(a.ActiveSeconds)} de tempo ativo", 14, "Muted"));
        Border Metric(string value, string label, string color) { var c = UI.Card(UI.Stack(UI.Text(value, 30, color, true), UI.Text(label, 12, "Muted")), 18); c.MinWidth = 170; c.Margin = new Thickness(0, 0, 10, 12); return c; }
        page.Children.Add(UI.Row(Metric(scores.Sum(s => s.Correct).ToString(), "acertos", "Good"), Metric(scores.Sum(s => s.Incorrect).ToString(), "erros", "Bad"), Metric(scores.Sum(s => s.Blank).ToString(), "em branco", "Muted"), Metric(scores.Sum(s => s.Annulled).ToString(), "anuladas", "Muted")));
        page.Children.Add(UI.Text("Acertos brutos não são nota TRI. Anuladas são separadas; a redação não é corrigida.", 12, "Muted"));
        var areas = UI.Stack(UI.Text("Desempenho por área", 20, bold: true));
        foreach (var s in scores)
        {
            var bar = new ProgressBar { Value = s.Correct, Maximum = Math.Max(1, s.Valid), Height = 7, Margin = new Thickness(0, 4, 0, 14), Foreground = UI.Brush("Accent"), Background = UI.Brush("Soft") };
            areas.Children.Add(UI.Text($"{Exporter.AreaName(s.Area)}   ·   {s.Correct}/{s.Valid} acertos   ·   {s.Incorrect} erros   ·   {s.Blank} em branco", 14)); areas.Children.Add(bar);
        }
        page.Children.Add(UI.Card(areas));
        page.Children.Add(UI.Card(UI.Stack(UI.Text("O que merece uma segunda leitura", 20, bold: true), UI.Text($"{a.Questions.Count(q => Matches(a, q, "Erros com certeza"))} erros com alta confiança   ·   {a.Questions.Count(q => Matches(a, q, "Acertos com baixa confiança"))} acertos com baixa confiança   ·   {a.Responses.Values.Count(r => r.Review)} marcadas", color: "Muted"), UI.Text("Distribuição das respostas: " + string.Join("   ·   ", "ABCDE".Select(c => $"{c}: {a.Responses.Values.Count(r => r.Answer == c.ToString())}")), 12, "Muted"))));
        page.Children.Add(UI.Row(UI.Button("Exportar para IA", () => shell.Export(a), true), UI.Button("Refazer esta prova", () => shell.NewAttempt(a.Year, a.Day, a.Language == "espanhol" ? "espanhol" : "ingles")), UI.Button("Voltar ao histórico", () => shell.ShowPage("Histórico"))));
        var comparisons = shell.Store.All().Where(x => x.IsFinished && x.Id != a.Id && ContentReports.SameExam(a, x)).OrderByDescending(x => x.FinishedUtc).Take(4).ToList();
        if (comparisons.Count > 0)
        {
            var comparison = UI.Stack(UI.Text("Tentativas desta mesma prova", 20, bold: true), UI.Text("Contagens descritivas. Repetir a prova pode envolver memória das questões; variação de acertos não prova evolução.", 12, "Muted"));
            foreach (var other in comparisons.Prepend(a))
            {
                var otherScores = Score.Calculate(other);
                comparison.Children.Add(UI.Text($"{other.FinishedUtc?.ToLocalTime():dd/MM/yyyy HH:mm} · {otherScores.Sum(s => s.Correct)} acertos · {other.Answered} respondidas · {UI.Duration(other.ActiveSeconds)}" + (other.Id == a.Id ? " · esta tentativa" : ""), 13));
            }
            page.Children.Add(UI.Card(comparison));
        }
        var filter = UI.Choice(new[] { "Todas", "Erros", "Acertos", "Em branco", "Marcadas", "Chutes", "Erros com certeza", "Acertos com baixa confiança" }, width: 260);
        var labels = a.Questions.ToDictionary(q => q.Id, q => shell.Store.GetQuestionStudy(q).Topics);
        var topicFilter = UI.Choice(new[] { "Todos os conteúdos", "Sem classificação" }.Concat(labels.Values.SelectMany(x => x).Select(t => t.Display).Distinct().Order()), width: 330);
        var rows = UI.Stack();
        void Render()
        {
            rows.Children.Clear();
            foreach (var q in a.Questions.Where(q => Matches(a, q, (string)filter.SelectedItem) &&
                (topicFilter.SelectedIndex == 0 || topicFilter.SelectedIndex == 1 && labels[q.Id].Count == 0 || labels[q.Id].Any(t => t.Display == (string)topicFilter.SelectedItem))))
            {
                var r = a.Response(q); var outcome = Exporter.Outcome(q, r); var index = a.Questions.IndexOf(q);
                var button = UI.Button($"Questão {q.Number}    ·    {OutcomeLabel(outcome)}    ·    Sua: {r.Answer ?? "—"}    ·    Oficial: {q.OfficialAnswer}    ·    {Exporter.AreaName(q.Area)}" + (r.Review ? "    • Revisar" : ""), () => shell.ReviewQuestion(a, index));
                button.Content = UI.Text((string)button.Content + (labels[q.Id].Count > 0 ? "\n" + string.Join(" · ", labels[q.Id].Select(t => t.Display)) : ""), 13);
                button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 7); rows.Children.Add(button);
            }
            if (rows.Children.Count == 0) rows.Children.Add(UI.Text("Nenhuma questão neste filtro.", color: "Muted"));
        }
        filter.SelectionChanged += (_, _) => Render(); topicFilter.SelectionChanged += (_, _) => Render(); Render();
        page.Children.Add(UI.Text("Revisar questões", 21, bold: true)); page.Children.Add(UI.Row(filter, topicFilter)); page.Children.Add(rows); Content = UI.Scroll(page);
    }
}
