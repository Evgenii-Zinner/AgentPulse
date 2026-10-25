using System;
using AgentPulse.Providers;
using AgentPulse.Services;
using AgentPulse.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace AgentPulse;

public partial class App : Application
{
    private Window? _mainWindow;
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<GoogleAuthService>();
        services.AddSingleton<GoogleQuotaProvider>();
        services.AddSingleton<AntigravityQuotaProvider>();
        services.AddSingleton<LockScreenUpdater>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();
    }
}
