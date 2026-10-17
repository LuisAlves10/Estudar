using System.Windows;
using System.Windows.Controls;
using Estudo.Core;

namespace Estudo.Windows;

public static class QuestionStudyEditor
{
    public static void Open(Window owner, AttemptStore store, Question question, Action updated)
        => Create(owner, store, question, updated).ShowDialog();

    public static Window Create(Window owner, AttemptStore store, Question question, Action updated)
    {
        var data = store.GetQuestionStudy(question);
        var dialog = new Window { Title = $"Classificar questão {question.Number}", Width = Math.Min(620, owner.ActualWidth - 24), Height = Math.Min(650, owner.ActualHeight - 24),
            MinWidth = 420, MinHeight = 360, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox { Height = 110, Margin = new Thickness(0, 8, 0, 12) };
        TextBox Field() => new() { MaxLength = 160, Margin = new Thickness(0, 0, 0, 10) };
        var area = Field(); var discipline = Field(); var subject = Field(); var subsubject = Field(); var tags = Field();
        var status = UI.Text("Classificações manuais, compartilhadas entre tentativas desta questão.", 12, "Muted");
        void Refresh()
        {
            list.ItemsSource = data.Topics.Select(t => t.Display).ToArray(); list.SelectedIndex = -1;
            area.Text = Exporter.AreaName(question.Area); discipline.Clear(); subject.Clear(); subsubject.Clear(); tags.Clear();
        }
        void Persist() { store.SaveQuestionStudy(question, data); updated(); Refresh(); status.Text = "✓ Classificação salva neste dispositivo"; }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= data.Topics.Count) return;
            var t = data.Topics[list.SelectedIndex]; area.Text = t.Area; discipline.Text = t.Discipline;
            subject.Text = t.Subject; subsubject.Text = t.Subsubject; tags.Text = string.Join(", ", t.Tags);
        };
        var save = UI.Button("Salvar classificação", () =>
        {
            var topic = new TopicLabel(area.Text.Trim(), discipline.Text.Trim(), subject.Text.Trim(), subsubject.Text.Trim(),
                tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            topic.Validate();
            if (list.SelectedIndex >= 0) data.Topics[list.SelectedIndex] = topic; else data.Topics.Add(topic);
            Persist();
        }, true);
        var remove = UI.Button("Remover selecionada", () => { if (list.SelectedIndex >= 0) { data.Topics.RemoveAt(list.SelectedIndex); Persist(); } });
        remove.IsEnabled = false; list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedIndex >= 0;
        var content = UI.Stack(UI.Text($"Questão {question.Number} · conteúdos", 24, bold: true),
            UI.Text("Opcional. Adicione mais de uma classificação quando a questão combinar conteúdos. Selecione uma existente para editar.", 13, "Muted"),
            list, UI.Text("Área", 12, "Muted"), area, UI.Text("Disciplina", 12, "Muted"), discipline,
            UI.Text("Assunto", 12, "Muted"), subject, UI.Text("Subassunto (opcional)", 12, "Muted"), subsubject,
            UI.Text("Etiquetas opcionais, separadas por vírgula", 12, "Muted"), tags,
            UI.Row(save, UI.Button("Nova classificação", Refresh), remove), status, UI.Row(UI.Button("Concluir", dialog.Close)));
        dialog.Content = new Border { Padding = new Thickness(24), Child = UI.Scroll(content) }; Refresh(); return dialog;
    }
}
