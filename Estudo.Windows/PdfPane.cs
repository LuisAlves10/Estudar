using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Estudo.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Estudo.Windows;

public sealed class PdfPane : UserControl, IDisposable
{
    private readonly WebView2 browser = new();
    private readonly TextBlock message = UI.Text("Abrindo leitor…", color: "Muted");
    private readonly Corpus corpus;
    private readonly string data;
    private Task? initialization;
    private readonly TaskCompletionSource ready = new();
    private string? path;
    private int request;
    private bool disposed;
    private string? questionId;
    public event Action<string, string, int, QuestionRegion?>? RegionEdited;
    public Action<string>? Shortcut { get; set; }
    public PdfPane(Corpus corpus, string data)
    {
        this.corpus = corpus; this.data = data;
        Content = new Grid { Children = { browser, new Border { Child = message, VerticalAlignment = VerticalAlignment.Bottom, Background = UI.Brush("Surface"), Padding = new Thickness(10) } } };
    }
    private async Task Initialize()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(data, "LeitorCache"));
        if (disposed) return;
        await browser.EnsureCoreWebView2Async(env);
        var core = browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.SetVirtualHostNameToFolderMapping("leitor.estudar", Path.Combine(AppContext.BaseDirectory, "Viewer"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://leitor.estudar/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) =>
        {
            var uri = new Uri(e.Request.Uri);
            if (uri.Host == "arquivo.estudar" && uri.AbsolutePath == "/document.pdf")
                e.Response = path != null ? env.CreateWebResourceResponse(File.OpenRead(path), 200, "OK", "Content-Type: application/pdf\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: https://leitor.estudar") : env.CreateWebResourceResponse(Stream.Null, 404, "Not found", "");
            else if (uri.Host != "leitor.estudar") e.Response = env.CreateWebResourceResponse(Stream.Null, 403, "Offline", "");
        };
        core.WebMessageReceived += (_, e) =>
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson); var root = json.RootElement;
            switch (root.GetProperty("kind").GetString())
            {
                case "ready": ready.TrySetResult(); break;
                case "rendered": message.Text = root.GetProperty("mode").GetString() == "question" ? "Recortes definidos manualmente · PDF original preservado" : $"Página {root.GetProperty("page")} de {root.GetProperty("total")} · PDF integral"; break;
                case "region":
                    var id = root.GetProperty("questionId").GetString();
                    if (id == questionId && id != null)
                    {
                        var region = root.TryGetProperty("region", out var value) && value.ValueKind == JsonValueKind.Object
                            ? JsonSerializer.Deserialize<QuestionRegion>(value.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
                        UI.Safe(() => RegionEdited?.Invoke(id, root.GetProperty("action").GetString()!, root.GetProperty("index").GetInt32(), region));
                    }
                    break;
                case "error": message.Text = "Leitor indisponível. Use Abrir original."; System.Diagnostics.Trace.WriteLine(root.GetProperty("message").GetString()); break;
                case "shortcut": Shortcut?.Invoke(root.GetProperty("action").GetString()!); break;
            }
        };
        core.Navigate("https://leitor.estudar/index.html");
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    public async Task Show(string relative, string hash, int page, QuestionStudyData? study = null)
    {
        var ticket = ++request;
        questionId = study?.QuestionId;
        try
        {
            var verified = await Task.Run(() => corpus.Verify(relative, hash));
            initialization ??= Initialize(); await initialization;
            if (disposed || ticket != request) return;
            path = verified;
            var url = "https://arquivo.estudar/document.pdf?id=" + Uri.EscapeDataString(relative);
            var dark = UI.Brush("Bg").ToString() == "#FF151D1A";
            var context = JsonSerializer.Serialize(new { id = study?.QuestionId, regions = study?.Regions ?? [] }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await browser.ExecuteScriptAsync($"window.openDocument({JsonSerializer.Serialize(url)},{page},{dark.ToString().ToLowerInvariant()},{context})");
        }
        catch (Exception ex) { if (!disposed) { message.Text = "Não foi possível exibir o PDF. Confira o acervo em Configurações ou use Abrir original."; System.Diagnostics.Trace.WriteLine(ex); } }
    }
    public async Task UpdateRegions(QuestionStudyData study)
    {
        if (disposed || study.QuestionId != questionId || browser.CoreWebView2 == null) return;
        var context = JsonSerializer.Serialize(new { id = study.QuestionId, regions = study.Regions }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        try { await browser.ExecuteScriptAsync($"window.updateQuestion({context})"); }
        catch (Exception ex) { if (!disposed) message.Text = "Recorte salvo. Reabra a questão para atualizar o leitor."; System.Diagnostics.Trace.WriteLine(ex); }
    }
    public async void Theme(bool dark) { if (browser.CoreWebView2 != null) await browser.ExecuteScriptAsync($"window.setTheme({dark.ToString().ToLowerInvariant()})"); }
    public void Dispose() { disposed = true; request++; browser.Dispose(); }
}
