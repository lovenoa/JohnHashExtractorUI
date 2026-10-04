using JohnHashExtractor.Services;
using JohnHashExtractor.Ui;

namespace JohnHashExtractor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var configStore = new AppConfigStore();
        var config = configStore.Load();
        var dependencyProbe = new DependencyProbe(config.RuntimePaths);
        var scanner = new ConverterScanner(dependencyProbe);
        var extractionService = new ExtractionService(scanner, dependencyProbe, new ProcessRunner());

        Application.Run(new MainForm(configStore, scanner, extractionService, config));
    }
}
