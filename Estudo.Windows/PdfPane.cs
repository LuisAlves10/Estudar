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
                case "rendered": message.Text = $"Página {root.GetProperty("page")} de {root.GetProperty("total")} · o enunciado pode continuar na próxima página"; break;
                case "error": message.Text = "Leitor indisponível. Use Abrir original."; System.Diagnostics.Trace.WriteLine(root.GetProperty("message").GetString()); break;
                case "shortcut": Shortcut?.Invoke(root.GetProperty("action").GetString()!); break;
            }
        };
        core.Navigate("https://leitor.estudar/index.html");
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    public async Task Show(string relative, string hash, int page)
    {
        var ticket = ++request;
        try
        {
            var verified = await Task.Run(() => corpus.Verify(relative, hash));
            initialization ??= Initialize(); await initialization;
            if (disposed || ticket != request) return;
            path = verified;
            var url = "https://arquivo.estudar/document.pdf?id=" + Uri.EscapeDataString(relative);
            var dark = UI.Brush("Bg").ToString() == "#FF151D1A";
            await browser.ExecuteScriptAsync($"window.openDocument({JsonSerializer.Serialize(url)},{page},{dark.ToString().ToLowerInvariant()})");
        }
        catch (Exception ex) { if (!disposed) { message.Text = "Não foi possível exibir o PDF. Confira o acervo em Configurações ou use Abrir original."; System.Diagnostics.Trace.WriteLine(ex); } }
    }
    public async void Theme(bool dark) { if (browser.CoreWebView2 != null) await browser.ExecuteScriptAsync($"window.setTheme({dark.ToString().ToLowerInvariant()})"); }
    public void Dispose() { disposed = true; request++; browser.Dispose(); }
}
