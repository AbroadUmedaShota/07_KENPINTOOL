using System.IO;
using System.Windows;
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
                services.AddSingleton<SimpleValidationService>(); // Added SimpleValidationService
                services.AddSingleton<QualityDetectionService>();
                services.AddSingleton<StructureDetectionService>();
                services.AddSingleton<ReportGenerator>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<DashboardViewModel>();

                // Views
                services.AddTransient<MainWindow>();
                services.AddSingleton<DashboardWindow>();

                // Database
                services.AddSingleton<DatabaseService>(sp => 
                {
                    var appData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "KenpinTool.Prototype");
                    var dbPath = Path.Combine(appData, "kenpin_master.db");
                    
                    // Fallback is same for now, or use MyDocuments if needed
                    return new DatabaseService(dbPath, dbPath);
                });
            })
            .Build();

        await _host.StartAsync();

        var dashboard = _host.Services.GetRequiredService<DashboardWindow>();
        if (dashboard.DataContext is DashboardViewModel vm)
        {
            vm.RequestOpenInspection += (sender, caseRecord) =>
            {
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                if (mainWindow.DataContext is MainViewModel mainVm)
                {
                    mainVm.Initialize(caseRecord);
                }
                
                mainWindow.Show();
                dashboard.Hide(); // ダッシュボードを隠す（閉じるとアプリ終了してしまう場合があるため）
                
                mainWindow.Closed += (s, e) => 
                {
                    // メインウィンドウが閉じたらダッシュボードを再表示してリストを更新
                    dashboard.Show();
                    vm.Initialize(); 
                };
            };
        }
        
        dashboard.Show();
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
