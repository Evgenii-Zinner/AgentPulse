using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using AgentPulse.Models;
using AgentPulse.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AgentPulse;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_RESTORE = 9;

    public MainViewModel ViewModel { get; }
    private AppWindow _appWindow;
    private OverlappedPresenter? _presenter;
    private bool _isExplicitExit;
    private bool _isPinned;
    private bool _isWindowVisible = true;
    private System.Drawing.Icon? _currentTrayIcon;

    public IRelayCommand ToggleWindowVisibilityCommand { get; }
    public IRelayCommand ShowWindowCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ExitApplicationCommand { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;

        // Initialize Commands for Tray and Window interaction
        ToggleWindowVisibilityCommand = new RelayCommand(ToggleWindow);
        ShowWindowCommand = new RelayCommand(ShowWindow);
        RefreshCommand = new AsyncRelayCommand(async () =>
        {
            await ViewModel.RefreshAsync();
            UpdateTrayIcon(ViewModel.AntigravityGemini.Percent);
        });
        ExitApplicationCommand = new RelayCommand(ExitApplication);

        this.InitializeComponent();

        _appWindow = GetAppWindowForCurrentWindow();
        ConfigureWindow();

        // Initial Tray Icon rendering with current Gemini 5-hour percentage
        UpdateTrayIcon(ViewModel.AntigravityGemini.Percent);

        // Listen for live quota updates to dynamically update the tray number
        ViewModel.AntigravityGemini.PropertyChanged += OnGeminiPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void ConfigureWindow()
    {
        Title = "Agent Pulse";

        // Extend content into titlebar for custom PC Manager titlebar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Sized comfortably to fit all contents with zero scroll: 380 x 510
        _appWindow.Resize(new SizeInt32(380, 510));

        // Use OverlappedPresenter with resizing enabled
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            _presenter = presenter;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        // Position at bottom-right corner of the work area (docked above system tray)
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
        if (displayArea != null)
        {
            var posX = displayArea.WorkArea.X + displayArea.WorkArea.Width - 395;
            var posY = displayArea.WorkArea.Y + displayArea.WorkArea.Height - 525;
            _appWindow.Move(new PointInt32(posX, posY));
        }

        // Set App Icon
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        // Intercept Close event (e.g. Alt+F4) -> Hide to system tray instead of exiting
        _appWindow.Closing += (sender, args) =>
        {
            if (!_isExplicitExit)
            {
                args.Cancel = true;
                HideWindow();
            }
        };
    }

    private void OnGeminiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuotaGroup.Percent))
        {
            DispatcherQueue.TryEnqueue(() => UpdateTrayIcon(ViewModel.AntigravityGemini.Percent));
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AntigravityGemini))
        {
            DispatcherQueue.TryEnqueue(() => UpdateTrayIcon(ViewModel.AntigravityGemini.Percent));
        }
        else if (e.PropertyName == nameof(MainViewModel.TrayTooltip))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrayIcon.ToolTipText = ViewModel.TrayTooltip;
            });
        }
    }

    private void UpdateTrayIcon(int percent)
    {
        try
        {
            var newIcon = CreateDynamicQuotaIcon(percent);
            var oldIcon = _currentTrayIcon;

            _currentTrayIcon = newIcon;
            TrayIcon.Icon = newIcon;

            oldIcon?.Dispose();
        }
        catch
        {
            // Fallback to static app icon
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                TrayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
        }
    }

    private static System.Drawing.Icon CreateDynamicQuotaIcon(int percent)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(System.Drawing.Color.Transparent);

        System.Drawing.Color bgColor;
        System.Drawing.Color textColor;
        System.Drawing.Color borderColor;
        string text;

        if (percent >= 100)
        {
            // OK when 100%
            bgColor = System.Drawing.Color.FromArgb(255, 16, 185, 129); // Vibrant Green
            textColor = System.Drawing.Color.White;
            borderColor = System.Drawing.Color.FromArgb(255, 52, 211, 153);
            text = "OK";
        }
        else if (percent <= 15)
        {
            bgColor = System.Drawing.Color.FromArgb(255, 239, 68, 68); // Red
            textColor = System.Drawing.Color.White;
            borderColor = System.Drawing.Color.FromArgb(255, 248, 113, 113);
            text = percent.ToString();
        }
        else if (percent <= 35)
        {
            bgColor = System.Drawing.Color.FromArgb(255, 245, 158, 11); // Amber
            textColor = System.Drawing.Color.Black;
            borderColor = System.Drawing.Color.FromArgb(255, 251, 191, 36);
            text = percent.ToString();
        }
        else
        {
            // Healthy active usage (< 100% and > 35%)
            bgColor = System.Drawing.Color.FromArgb(255, 20, 24, 32); // Dark slate (#141820)
            textColor = System.Drawing.Color.FromArgb(255, 76, 194, 255); // Cyan (#4CC2FF)
            borderColor = System.Drawing.Color.FromArgb(255, 76, 194, 255);
            text = percent.ToString();
        }

        // Background pill
        using var bgBrush = new SolidBrush(bgColor);
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Border ring
        using var borderPen = new System.Drawing.Pen(borderColor, 1.5f);
        g.DrawEllipse(borderPen, 1, 1, 30, 30);

        // Center text
        float fontSize = text.Length >= 3 ? 12f : 14.5f;
        using var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(textColor);

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(text, font, textBrush, new RectangleF(0, 0, 32, 32), sf);

        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private AppWindow GetAppWindowForCurrentWindow()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    public void ShowWindow()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hWnd, SW_RESTORE);
        _appWindow.Show();
        this.Activate();
        SetForegroundWindow(hWnd);
        _isWindowVisible = true;
    }

    public void HideWindow()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hWnd, SW_HIDE);
        _appWindow.Hide();
        _isWindowVisible = false;
    }

    private void ToggleWindow()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_isWindowVisible && IsWindowVisible(hWnd))
        {
            HideWindow();
        }
        else
        {
            ShowWindow();
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        HideWindow();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        HideWindow();
    }

    private void ExitApplication()
    {
        _isExplicitExit = true;
        try
        {
            _currentTrayIcon?.Dispose();
            TrayIcon?.Dispose();
            ViewModel.Dispose();
        }
        catch { }
        Environment.Exit(0);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
        UpdateTrayIcon(ViewModel.AntigravityGemini.Percent);
    }

    private void OnTogglePinClicked(object sender, RoutedEventArgs e)
    {
        if (_presenter == null) return;

        _isPinned = !_isPinned;
        _presenter.IsAlwaysOnTop = _isPinned;

        PinIcon.Foreground = _isPinned
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 194, 255)) // #4CC2FF cyan
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 115, 122, 140)); // #737A8C grey
    }

    #region XAML Binding Helpers

    public Visibility GetCooldownVisibility(bool isOnCooldown) =>
        isOnCooldown ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetLoadingVisibility(bool isLoading) =>
        isLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetInvertedVisibility(bool isLoading) =>
        isLoading ? Visibility.Collapsed : Visibility.Visible;

    public string GetButtonText(bool isLoading) =>
        isLoading ? "Refreshing..." : "Refresh Quotas";

    #endregion
}
