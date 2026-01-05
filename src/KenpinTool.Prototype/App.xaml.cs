using System.Windows;
using KenpinTool.Prototype.Services;
using KenpinTool.Prototype.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuestPDF.Infrastructure;

namespace KenpinTool.Prototype;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        QuestPDF.Settings.License = LicenseType.Community;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Services
                services.AddSingleton<ImageLoaderService>();
                services.AddSingleton<CaseLoader>();
                services.AddSingleton<DummyDetectionService>();
                services.AddSingleton<QualityDetectionService>();
                services.AddSingleton<StructureDetectionService>();
                services.AddSingleton<ReportGenerator>();

                // ViewModels
                services.AddTransient<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
