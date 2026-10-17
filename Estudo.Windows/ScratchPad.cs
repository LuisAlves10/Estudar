using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Estudo.Core;

namespace Estudo.Windows;

public sealed class ScratchPad : UserControl
{
    private readonly InkCanvas ink = new() { Width = 1600, Height = 1200, EditingMode = InkCanvasEditingMode.Ink };
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly List<byte[]> undo = [];
    private readonly Stack<byte[]> redo = new();
    private readonly ComboBox paper, tool;
    private readonly Slider width = new() { Minimum = 1, Maximum = 16, Value = 3, Width = 90, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Espessura da caneta" };
    private readonly TextBlock status = UI.Text("Rascunho por questão", 12, "Muted");
    private readonly Button undoButton, redoButton;
    private readonly string folder;
    private readonly bool readOnly;
    private bool changing, dirty;
    private Point? pan;
    public ScratchPad(string folder, bool readOnly, string defaultPaper, Action detach, Action position)
    {
        this.folder = folder; this.readOnly = readOnly;
        scroll = new ScrollViewer { Content = ink, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.None, Background = Brushes.White };
        tool = UI.Choice(new[] { "Caneta", "Marca-texto", "Borracha", "Mover" }, SetTool, 128);
        var color = UI.Choice(new[] { "Grafite", "Verde", "Azul", "Vermelho", "Âmbar" }, null, 112);
        color.SelectionChanged += (_, _) => ink.DefaultDrawingAttributes.Color = (string)color.SelectedItem switch { "Verde" => Colors.SeaGreen, "Azul" => Colors.RoyalBlue, "Vermelho" => Colors.Firebrick, "Âmbar" => Colors.Goldenrod, _ => Colors.Black };
        paper = UI.Choice(new[] { "Branco", "Quadriculado", "Pontilhado" }, PaperChanged, 144);
        var zoom = UI.Choice(new[] { "100%", "75%", "125%", "150%", "200%" }, null, 90);
        zoom.SelectionChanged += (_, _) => { var scale = double.Parse(((string)zoom.SelectedItem).TrimEnd('%')) / 100; ink.LayoutTransform = new ScaleTransform(scale, scale); };
        width.ValueChanged += (_, _) => { ink.DefaultDrawingAttributes.Width = width.Value; ink.DefaultDrawingAttributes.Height = width.Value; };
        undoButton = UI.Button("Desfazer", Undo); redoButton = UI.Button("Refazer", Redo);
        var clear = UI.Button("Limpar", () => { if (UI.Confirm("Limpar este rascunho?", "Os traços desta questão serão removidos. Você pode desfazer enquanto o rascunho estiver aberto.", "Limpar rascunho")) { ink.Strokes.Clear(); CaptureChange(); } });
        var toolbar = UI.Row(tool, color, width, undoButton, redoButton, clear, paper, zoom, UI.Button("Destacar ↗", detach), UI.Button("Trocar lado", position));
        foreach (var control in new Control[] { tool, color, width, undoButton, redoButton, clear }) control.IsEnabled = !readOnly;
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(toolbar); Grid.SetRow(scroll, 1); grid.Children.Add(scroll); Grid.SetRow(status, 2); grid.Children.Add(status); Content = grid;
        ink.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Black, Width = 3, Height = 3, FitToCurve = true, IgnorePressure = false };
        ink.StrokeCollected += (_, _) => CaptureChange();
        ink.StrokeErased += (_, _) => CaptureChange();
        saveTimer.Tick += (_, _) => UI.Safe(Flush);
        ink.PreviewMouseDown += (_, e) => { if ((string)tool.SelectedItem != "Mover" && e.ChangedButton != MouseButton.Middle) return; pan = e.GetPosition(scroll); ink.CaptureMouse(); e.Handled = true; };
        ink.PreviewMouseMove += (_, e) => { if (pan is not Point p) return; var now = e.GetPosition(scroll); scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + p.X - now.X); scroll.ScrollToVerticalOffset(scroll.VerticalOffset + p.Y - now.Y); pan = now; e.Handled = true; };
        ink.PreviewMouseUp += (_, e) => { if (pan == null) return; pan = null; ink.ReleaseMouseCapture(); e.Handled = true; };
        var isf = Path.Combine(folder, "rascunho.isf");
        if (File.Exists(isf)) { using var stream = File.OpenRead(isf); ink.Strokes = new StrokeCollection(stream); }
        var config = Path.Combine(folder, "papel.json");
        var background = File.Exists(config) ? JsonSerializer.Deserialize<string>(File.ReadAllText(config)) : defaultPaper;
        changing = true; paper.SelectedItem = background; changing = false; PaintPaper();
        undo.Add(Snapshot()); UpdateUndo(); SetTool();
        if (readOnly) status.Text = "Rascunho original · somente leitura após a finalização";
        Unloaded += (_, _) => UI.Safe(Flush);
    }
    private byte[] Snapshot() { using var stream = new MemoryStream(); ink.Strokes.Save(stream); return stream.ToArray(); }
    private void Restore(byte[] data) { changing = true; using var stream = new MemoryStream(data); ink.Strokes = new StrokeCollection(stream); changing = false; MarkDirty(); UpdateUndo(); }
    private void CaptureChange() { if (changing || readOnly) return; var data = Snapshot(); if (!undo[^1].SequenceEqual(data)) { undo.Add(data); if (undo.Count > 60) undo.RemoveAt(0); redo.Clear(); } MarkDirty(); UpdateUndo(); }
    private void Undo() { if (undo.Count < 2) return; redo.Push(undo[^1]); undo.RemoveAt(undo.Count - 1); Restore(undo[^1]); }
    private void Redo() { if (!redo.TryPop(out var data)) return; undo.Add(data); Restore(data); }
    private void UpdateUndo() { undoButton.IsEnabled = !readOnly && undo.Count > 1; redoButton.IsEnabled = !readOnly && redo.Count > 0; }
    private void MarkDirty() { if (readOnly) return; dirty = true; status.Text = "Salvando rascunho…"; saveTimer.Stop(); saveTimer.Start(); }
    private void SetTool()
    {
        if (tool == null) return;
        ink.EditingMode = readOnly || (string)tool.SelectedItem == "Mover" ? InkCanvasEditingMode.None : (string)tool.SelectedItem == "Borracha" ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
        ink.DefaultDrawingAttributes.IsHighlighter = (string)tool.SelectedItem == "Marca-texto";
    }
    private void PaperChanged() { if (paper == null) return; PaintPaper(); if (!changing) MarkDirty(); }
    private void PaintPaper()
    {
        var selected = paper.SelectedItem as string;
        if (selected == "Branco") { ink.Background = Brushes.White; return; }
        var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var line = new SolidColorBrush(Color.FromRgb(213, 225, 221));
        if (selected == "Pontilhado") group.Children.Add(new GeometryDrawing(line, null, new EllipseGeometry(new Point(12, 12), 1, 1)));
        else group.Children.Add(new GeometryDrawing(null, new Pen(line, .6), new RectangleGeometry(new Rect(0, 0, 24, 24))));
        ink.Background = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute };
    }
    public void Flush()
    {
        saveTimer.Stop(); if (!dirty || readOnly) return;
        StudyFiles.AtomicWrite(Path.Combine(folder, "rascunho.isf"), Snapshot());
        StudyFiles.AtomicWrite(Path.Combine(folder, "papel.json"), System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(paper.SelectedItem as string)));
        // Render the logical paper independently of window size and zoom.
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(ink.Background, null, new Rect(0, 0, 1600, 1200)); ink.Strokes.Draw(dc); }
        var bitmap = new RenderTargetBitmap(1600, 1200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var png = new MemoryStream(); encoder.Save(png);
        StudyFiles.AtomicWrite(Path.Combine(folder, "rascunho.png"), png.ToArray()); dirty = false; status.Text = "✓ Rascunho salvo neste dispositivo";
    }
    public void SetPaused(bool paused) { if (readOnly) return; ink.IsEnabled = !paused; }
}
