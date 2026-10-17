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
    private readonly InkCanvas ink = new() { EditingMode = InkCanvasEditingMode.Ink };
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly List<byte[]> undo = [];
    private readonly Stack<byte[]> redo = new();
    private readonly ComboBox paper, tool;
    private readonly TextBlock status = UI.Text("Rascunho por questão", 12, "Muted"), zoomLabel = UI.Text("", 12, "Muted");
    private readonly Button undoButton, redoButton, detachButton, positionButton;
    private readonly List<Control> editing = [];
    private readonly string folder;
    private readonly bool readOnly;
    private readonly ScratchSurface surface;
    private bool changing, dirty, previewDirty, paused, applyingZoom, restoringViewport = true;
    private Point? pan;
    public ScratchPad(string folder, bool readOnly, string defaultPaper, Action detach, Action position)
    {
        this.folder = folder; this.readOnly = readOnly;
        var config = Path.Combine(folder, "superficie.json");
        surface = File.Exists(config) ? JsonSerializer.Deserialize<ScratchSurface>(File.ReadAllText(config)) ?? new() : new();
        surface.Validate(); ink.Width = surface.Width; ink.Height = surface.Height;
        var savedX = surface.OffsetX; var savedY = surface.OffsetY;
        ink.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Black, Width = 3, Height = 3, FitToCurve = true, IgnorePressure = false };
        scroll = new ScrollViewer { Content = ink, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.None };
        scroll.SetResourceReference(BackgroundProperty, "Soft");
        tool = UI.Choice(new[] { "Caneta", "Marca-texto", "Borracha", "Mover" }, SetTool, 110);
        var color = UI.Choice(new[] { "Grafite", "Verde", "Azul", "Vermelho", "Âmbar" }, width: 105);
        color.SelectionChanged += (_, _) => ink.DefaultDrawingAttributes.Color = (string)color.SelectedItem switch { "Verde" => Colors.SeaGreen, "Azul" => Colors.RoyalBlue, "Vermelho" => Colors.Firebrick, "Âmbar" => Colors.Goldenrod, _ => Colors.Black };
        var width = new Slider { Minimum = 1, Maximum = 16, Value = 3, Width = 74, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Espessura da caneta (unidades do papel)" };
        width.ValueChanged += (_, _) => { ink.DefaultDrawingAttributes.Width = width.Value; ink.DefaultDrawingAttributes.Height = width.Value; };
        paper = UI.Choice(new[] { "Branco", "Quadriculado", "Pontilhado" }, PaperChanged, 144);
        undoButton = UI.Button("Desfazer", Undo); redoButton = UI.Button("Refazer", Redo);
        undoButton.Padding = redoButton.Padding = new Thickness(8, 7, 8, 7);
        var clear = UI.Button("Limpar", () => { if (UI.Confirm("Limpar este rascunho?", "Os traços desta questão serão removidos. Você pode desfazer enquanto o rascunho estiver aberto.", "Limpar rascunho")) ink.Strokes.Clear(); });
        var expand = UI.Button("Mais papel ↓", ExpandPaper);
        detachButton = UI.Button("Destacar ↗", detach); positionButton = UI.Button("Trocar lado", position);
        editing.AddRange([color, width, clear, paper, expand]);
        var toolbar = UI.Stack(UI.Row(tool, undoButton, redoButton),
            UI.Row(UI.Button("−", () => SetZoom(surface.Zoom / 1.25)), UI.Button("+", () => SetZoom(surface.Zoom * 1.25)), UI.Button("Ajustar", FitWidth), UI.Button("100%", ResetZoom)),
            new Expander { Header = "Papel e ferramentas", Content = UI.Stack(UI.Row(color, width), UI.Row(paper, expand, clear), UI.Row(UI.Button("Ajustar à janela", FitToWindow), detachButton, positionButton)) });
        foreach (var row in toolbar.Children.OfType<WrapPanel>()) row.Margin = new Thickness(0, 0, 0, 4);
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(toolbar); Grid.SetRow(scroll, 1); grid.Children.Add(scroll); var footer = UI.Stack(zoomLabel, status); Grid.SetRow(footer, 2); grid.Children.Add(footer); Content = grid;
        saveTimer.Tick += (_, _) => UI.Safe(() => Persist(false));
        ink.PreviewMouseDown += (_, e) => { if ((string)tool.SelectedItem != "Mover" && e.ChangedButton != MouseButton.Middle) return; pan = e.GetPosition(scroll); ink.CaptureMouse(); e.Handled = true; };
        ink.PreviewMouseMove += (_, e) => { if (pan is not Point p) return; var now = e.GetPosition(scroll); scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + p.X - now.X); scroll.ScrollToVerticalOffset(scroll.VerticalOffset + p.Y - now.Y); pan = now; e.Handled = true; };
        ink.PreviewMouseUp += (_, e) => { if (pan == null) return; pan = null; ink.ReleaseMouseCapture(); e.Handled = true; };
        scroll.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers != ModifierKeys.Control) return; SetZoom(surface.Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15)); e.Handled = true; };
        var isf = Path.Combine(folder, "rascunho.isf");
        if (File.Exists(isf)) { using var stream = File.OpenRead(isf); ink.Strokes = new StrokeCollection(stream); }
        var preview = Path.Combine(folder, "rascunho.png");
        previewDirty = File.Exists(isf) && (!File.Exists(preview) || File.GetLastWriteTimeUtc(isf) > File.GetLastWriteTimeUtc(preview));
        var paperPath = Path.Combine(folder, "papel.json");
        var background = File.Exists(paperPath) ? JsonSerializer.Deserialize<string>(File.ReadAllText(paperPath)) : defaultPaper;
        changing = true; paper.SelectedItem = background; changing = false; PaintPaper();
        undo.Add(Snapshot()); ink.Strokes.StrokesChanged += StrokesChanged; UpdateUndo(); SetTool();
        SetPaused(false);
        if (readOnly) status.Text = "Rascunho original · somente leitura após a finalização";
        Loaded += (_, _) => { ApplyZoom(false); scroll.ScrollToHorizontalOffset(savedX); scroll.ScrollToVerticalOffset(savedY); scroll.UpdateLayout(); restoringViewport = false; };
        scroll.SizeChanged += (_, _) => { if (surface.ZoomMode != "custom") ApplyZoom(true); };
        scroll.ScrollChanged += (_, _) => { if (!restoringViewport && !applyingZoom && (surface.OffsetX != scroll.HorizontalOffset || surface.OffsetY != scroll.VerticalOffset)) { surface.OffsetX = scroll.HorizontalOffset; surface.OffsetY = scroll.VerticalOffset; MarkDirty(); } };
        Unloaded += (_, _) => UI.Safe(Flush);
    }
    private byte[] Snapshot() { using var stream = new MemoryStream(); ink.Strokes.Save(stream); return stream.ToArray(); }
    private void StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (changing || readOnly || paused) return;
        var data = Snapshot();
        if (!undo[^1].SequenceEqual(data)) { undo.Add(data); while (undo.Count > 2 && (undo.Count > 60 || undo.Sum(x => (long)x.Length) > 64 * 1024 * 1024)) undo.RemoveAt(0); redo.Clear(); }
        previewDirty = true; MarkDirty(); UpdateUndo();
    }
    private void Restore(byte[] data)
    {
        changing = true; ink.Strokes.StrokesChanged -= StrokesChanged;
        try { using var stream = new MemoryStream(data); ink.Strokes = new StrokeCollection(stream); }
        finally { ink.Strokes.StrokesChanged += StrokesChanged; changing = false; }
        previewDirty = true; MarkDirty(); UpdateUndo();
    }
    private void Undo() { if (readOnly || paused || undo.Count < 2) return; redo.Push(undo[^1]); undo.RemoveAt(undo.Count - 1); Restore(undo[^1]); }
    private void Redo() { if (readOnly || paused || !redo.TryPop(out var data)) return; undo.Add(data); Restore(data); }
    private void UpdateUndo() { undoButton.IsEnabled = !readOnly && !paused && undo.Count > 1; redoButton.IsEnabled = !readOnly && !paused && redo.Count > 0; }
    private void MarkDirty() { if (readOnly || changing) return; dirty = true; status.Text = "Salvando rascunho…"; saveTimer.Stop(); saveTimer.Start(); }
    private void SetTool()
    {
        if (tool == null) return;
        ink.EditingMode = readOnly || paused || (string)tool.SelectedItem == "Mover" ? InkCanvasEditingMode.None : (string)tool.SelectedItem == "Borracha" ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
        ink.DefaultDrawingAttributes.IsHighlighter = (string)tool.SelectedItem == "Marca-texto";
    }
    public void SetZoom(double zoom) { surface.ZoomMode = "custom"; surface.Zoom = Math.Clamp(zoom, .1, 4); ApplyZoom(true); MarkDirty(); }
    public void ResetZoom() => SetZoom(1);
    public void FitWidth() { surface.ZoomMode = "width"; ApplyZoom(true); MarkDirty(); }
    public void FitToWindow() { surface.ZoomMode = "window"; ApplyZoom(true); MarkDirty(); }
    private void ApplyZoom(bool preserveCenter)
    {
        if (applyingZoom) return;
        applyingZoom = true;
        try
        {
            var old = (ink.LayoutTransform as ScaleTransform)?.ScaleX ?? 1;
            var center = new Point((scroll.HorizontalOffset + scroll.ViewportWidth / 2) / old, (scroll.VerticalOffset + scroll.ViewportHeight / 2) / old);
            if (surface.ZoomMode != "custom" && scroll.ActualWidth > 32 && scroll.ActualHeight > 32)
            {
                surface.Zoom = (scroll.ActualWidth - 20) / ink.Width;
                if (surface.ZoomMode == "window") surface.Zoom = Math.Min(surface.Zoom, (scroll.ActualHeight - 20) / ink.Height);
                surface.Zoom = Math.Clamp(surface.Zoom, .1, 4);
            }
            ink.LayoutTransform = new ScaleTransform(surface.Zoom, surface.Zoom); scroll.UpdateLayout();
            if (preserveCenter) { scroll.ScrollToHorizontalOffset(Math.Max(0, center.X * surface.Zoom - scroll.ViewportWidth / 2)); scroll.ScrollToVerticalOffset(Math.Max(0, center.Y * surface.Zoom - scroll.ViewportHeight / 2)); }
            zoomLabel.Text = $"{surface.Zoom:P0} · papel {ink.Width:0} × {ink.Height:0}";
            zoomLabel.ToolTip = "Ctrl + roda: zoom. Ferramenta Mover ou botão do meio: arrastar o papel.";
        }
        finally { applyingZoom = false; }
    }
    public void ExpandPaper()
    {
        if (readOnly || paused) return;
        if (surface.Height >= 4800) { status.Text = "Altura máxima de 4800 unidades atingida"; return; }
        surface.Height = Math.Min(4800, surface.Height + 800); ink.Height = surface.Height; previewDirty = true; ApplyZoom(true); MarkDirty();
    }
    private void PaperChanged() { if (paper == null) return; PaintPaper(); if (!changing) { previewDirty = true; MarkDirty(); } }
    private void PaintPaper()
    {
        if (paper.SelectedItem as string == "Branco") { ink.Background = Brushes.White; return; }
        var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var line = new SolidColorBrush(Color.FromRgb(213, 225, 221));
        if (paper.SelectedItem as string == "Pontilhado") group.Children.Add(new GeometryDrawing(line, null, new EllipseGeometry(new Point(12, 12), 1, 1)));
        else group.Children.Add(new GeometryDrawing(null, new Pen(line, .6), new RectangleGeometry(new Rect(0, 0, 24, 24))));
        ink.Background = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute };
    }
    public void Flush() => Persist(true);
    private void Persist(bool includePreview)
    {
        saveTimer.Stop();
        if (dirty && !readOnly)
        {
            surface.OffsetX = scroll.HorizontalOffset; surface.OffsetY = scroll.VerticalOffset; surface.Validate();
            StudyFiles.AtomicWrite(Path.Combine(folder, "rascunho.isf"), Snapshot());
            StudyFiles.AtomicWrite(Path.Combine(folder, "papel.json"), System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(paper.SelectedItem as string)));
            StudyFiles.AtomicWrite(Path.Combine(folder, "superficie.json"), System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(surface)));
            dirty = false; status.Text = "✓ Traços salvos neste dispositivo";
        }
        if (includePreview && previewDirty)
        {
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(ink.Background, null, new Rect(0, 0, ink.Width, ink.Height)); ink.Strokes.Draw(dc); }
            var bitmap = new RenderTargetBitmap((int)ink.Width, (int)ink.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var png = new MemoryStream(); encoder.Save(png);
            StudyFiles.AtomicWrite(Path.Combine(folder, "rascunho.png"), png.ToArray()); previewDirty = false;
        }
    }
    public void SetPaused(bool value)
    {
        var wasPaused = paused;
        if (value) Flush(); paused = value; SetTool(); foreach (var control in editing) control.IsEnabled = !readOnly && !paused; UpdateUndo();
        if (!readOnly && paused) status.Text = "Ⅱ Rascunho salvo · retome o simulado para escrever";
        else if (!readOnly && wasPaused) status.Text = "✓ Traços salvos neste dispositivo";
    }
    public void SetDetached(bool detached) { detachButton.Content = detached ? "Voltar ao painel" : "Destacar ↗"; positionButton.Visibility = detached ? Visibility.Collapsed : Visibility.Visible; }
}
