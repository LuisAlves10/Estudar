using System.Windows;
using System.Windows.Controls;
using Estudo.Core;

namespace Estudo.Windows;

public sealed class ContentReportView : UserControl
{
    public ContentReportView(MainWindow shell)
    {
        var attempts = shell.Store.All().Where(a => a.IsFinished).ToList();
        var grouping = UI.Choice(new[] { "Por assunto", "Por disciplina" }, width: 180);
        var onlyReview = new CheckBox { Content = "Mostrar somente erros ou marcações", Margin = new Thickness(0, 6, 0, 12) };
        var rows = UI.Stack();
        void Render()
        {
            rows.Children.Clear();
            var summaries = ContentReports.Calculate(attempts, shell.Store.GetQuestionStudy, grouping.SelectedIndex == 0);
            foreach (var summary in summaries)
            {
                var observations = summary.Observations.Where(x => onlyReview.IsChecked != true || x.Outcome == "erro" || x.Review).ToList();
                if (observations.Count == 0) continue;
                var rate = summary.Accuracy is double accuracy ? $"{accuracy:P0} de acerto nas respondidas" : "Amostra pequena · percentual omitido";
                var detail = UI.Stack(UI.Text(summary.Label, 18, bold: true),
                    UI.Text($"{summary.Correct} acertos · {summary.Incorrect} erros · {summary.Blank} em branco · {summary.Annulled} anuladas", 13),
                    UI.Text($"{summary.Answered} respostas em {summary.DistinctAnswered} {(summary.DistinctAnswered == 1 ? "questão distinta" : "questões distintas")} · {rate}", 12, "Muted"));
                var items = UI.Stack();
                foreach (var item in observations)
                {
                    var button = UI.Button($"{item.Attempt.Title} · Q{item.Question.Number} · {ResultsView.OutcomeLabel(item.Outcome)}" + (item.Review ? " · revisar" : "") + $" · {item.Attempt.StartedUtc.ToLocalTime():dd/MM HH:mm}",
                        () => shell.ReviewQuestion(item.Attempt, item.Attempt.Questions.IndexOf(item.Question)));
                    button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 6); items.Children.Add(button);
                }
                detail.Children.Add(new Expander { Header = $"Abrir questões ({observations.Count})", Content = items });
                rows.Children.Add(UI.Card(detail));
            }
            if (rows.Children.Count == 0) rows.Children.Add(UI.Card(UI.Stack(UI.Text("Ainda não há conteúdos neste filtro", 21, bold: true),
                UI.Text("Classifique uma questão na resolução ou na revisão. Somente tentativas finalizadas entram neste relatório.", color: "Muted"))));
        }
        grouping.SelectionChanged += (_, _) => Render(); onlyReview.Click += (_, _) => Render(); Render();
        var unclassified = attempts.Sum(a => a.Questions.Count(q => shell.Store.GetQuestionStudy(q).Topics.Count == 0));
        Content = UI.Scroll(UI.Stack(UI.Text("Revisão por conteúdo", 30, bold: true),
            UI.Text($"Classificações manuais · {unclassified} registros de questões sem classificação. Questões com vários assuntos aparecem em cada grupo; não some os grupos.", 13, "Muted"),
            UI.Text("Repetições contam como novas respostas e também são identificadas como a mesma questão. Menos de cinco respostas ou cinco questões distintas: sem percentual. Os resultados não provam evolução nem estimam TRI.", 12, "Muted"),
            UI.Row(grouping, UI.Button("Voltar à revisão", () => shell.ShowPage("Revisão"))), onlyReview, rows));
    }
}
