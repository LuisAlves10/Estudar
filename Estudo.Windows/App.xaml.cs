using System.IO;
using System.Windows;
using Estudo.Core;

namespace Estudo.Windows;

public partial class App : Application
{
    private System.Threading.Mutex? instance;
    private bool ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var root = FindRoot(e.Args.Contains("--portable"));
            var qaIndex = Array.IndexOf(e.Args, "--qa-data");
            var data = qaIndex >= 0 && qaIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[qaIndex + 1]) : Path.Combine(root, "Dados", "Estudos");
            if (!data.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Os registros devem ficar dentro do projeto.");
            var instanceId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data.ToUpperInvariant())))[..24];
            instance = new System.Threading.Mutex(true, "Local\\Estudar_" + instanceId, out ownsInstance);
            if (!ownsInstance)
            {
                MessageBox.Show("O Estudar já está aberto para esta pasta de registros.", "Estudar", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            var preferences = Preferences.Load(data);
            var local = File.Exists(Path.Combine(root, "Dados", "PROVAS_MANIFEST.csv"));
            var catalog = local ? Path.Combine(root, "Dados") : Path.Combine(root, "catalogo");
            var corpusPath = preferences.CorpusPath ?? (local ? root : Path.Combine(root, "Acervo"));
            var applicationRoot = local ? Path.Combine(root, "Aplicativo") : root;
            var window = new MainWindow(new Corpus(corpusPath, catalog), new AttemptStore(data), preferences, catalog, applicationRoot, qaIndex >= 0);
            var sizeIndex = Array.IndexOf(e.Args, "--qa-size");
            if (sizeIndex >= 0 && sizeIndex + 1 < e.Args.Length) { var dimensions = e.Args[sizeIndex + 1].Split('x'); window.Width = int.Parse(dimensions[0]); window.Height = int.Parse(dimensions[1]); }
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o app.\n\n{ex.Message}\n\nMantenha o catálogo junto do aplicativo.", "Estudar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
    private static string FindRoot(bool preferPortable)
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        string? portable = null;
        while (cursor != null)
        {
            if (preferPortable && File.Exists(Path.Combine(cursor.FullName, "catalogo", "PROVAS_MANIFEST.csv"))) return cursor.FullName;
            if (File.Exists(Path.Combine(cursor.FullName, "Dados", "PROVAS_MANIFEST.csv"))) return cursor.FullName;
            if (portable == null && File.Exists(Path.Combine(cursor.FullName, "catalogo", "PROVAS_MANIFEST.csv"))) portable = cursor.FullName;
            cursor = cursor.Parent;
        }
        return portable ?? throw new DirectoryNotFoundException("Catálogo oficial não encontrado.");
    }
}
