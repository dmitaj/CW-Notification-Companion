using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;
using CWNotificationCompanion.Models;
using CWNotificationCompanion.Services;

namespace CWNotificationCompanion;

public partial class App : System.Windows.Application
{
    private const string AppId = "CWNotificationCompanion.App";

    private WinForms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private System.Threading.Timer? _pollTimer;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly ConnectWiseService _cwService = new();
    private readonly SettingsService _settingsService = new();
    private readonly HashSet<int> _knownTicketIds = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = _settingsService.Load();
        ApplyTheme(settings.DarkMode);
        RegisterAumid();

        InitializeTrayIcon();
        StartPolling();

        if (!settings.IsConfigured)
            ShowSettings();
    }

    public static void ApplyTheme(bool dark)
    {
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceDictionary
        {
            Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        });
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "CW Notification Companion"
        };

        // WPF ContextMenu — avoids WinForms multi-monitor positioning bugs entirely.
        // PlacementMode.MousePoint reads WPF's logical mouse position which correctly
        // maps to whichever physical screen the cursor is on, including non-primary monitors.
        var trayMenu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open Tickets" };
        openItem.Click += (_, _) => ShowMainWindow(null);
        trayMenu.Items.Add(openItem);

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();
        trayMenu.Items.Add(settingsItem);

        trayMenu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        trayMenu.Items.Add(exitItem);

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button != WinForms.MouseButtons.Right) return;
            Dispatcher.Invoke(() => OpenTrayMenu(trayMenu));
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => ShowMainWindow(null));
    }

    private static void OpenTrayMenu(System.Windows.Controls.ContextMenu menu)
    {
        if (menu.IsOpen) return;

        // A proxy window is required so the ContextMenu has an owner for keyboard
        // capture and auto-close on focus loss. It is kept off-screen until closed.
        var proxy = new Window
        {
            Width = 1, Height = 1,
            Left = -32000, Top = -32000,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true
        };
        proxy.Show();

        var helper = new System.Windows.Interop.WindowInteropHelper(proxy);
        NativeMethods.SetForegroundWindow(helper.Handle);

        void OnClosed(object? s, RoutedEventArgs _)
        {
            menu.Closed -= OnClosed;
            proxy.Close();
        }
        menu.Closed += OnClosed;

        menu.PlacementTarget = proxy;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/CWNotify.ico"))?.Stream;
            if (stream != null) return new Drawing.Icon(stream);
        }
        catch { }

        // Fallback: draw a simple icon programmatically
        var bmp = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);
        g.FillEllipse(new Drawing.SolidBrush(Drawing.Color.FromArgb(8, 145, 178)), 1, 1, 30, 30);
        using var font = new Drawing.Font("Segoe UI", 9f, Drawing.FontStyle.Bold);
        var sf = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
        g.DrawString("CW", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 0, 32, 32), sf);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    public void StartPolling()
    {
        _pollTimer?.Dispose();
        var settings = _settingsService.Load();
        int intervalMs = Math.Max(settings.PollIntervalMinutes, 1) * 60_000;
        _pollTimer = new System.Threading.Timer(
            _ => _ = PollAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
    }

    private async Task PollAsync()
    {
        if (!await _pollLock.WaitAsync(0))
            return;

        try
        {
            var settings = _settingsService.Load();
            if (!settings.IsConfigured)
                return;

            var tickets = await _cwService.GetClientRespondedTicketsAsync(settings);

            Dispatcher.Invoke(() =>
            {
                if (tickets.Count > 0)
                {
                    var newTickets     = tickets.Where(t => !_knownTicketIds.Contains(t.Id)).ToList();
                    bool hasNewTickets = newTickets.Count > 0;

                    if (_mainWindow == null || !_mainWindow.IsLoaded)
                        _mainWindow = new MainWindow(_settingsService, _cwService);

                    _mainWindow.UpdateTickets(tickets);

                    if (!_mainWindow.IsVisible)
                        _mainWindow.Show();

                    if (hasNewTickets)
                    {
                        // SW_RESTORE unminimizes if needed, activates otherwise.
                        // SetForegroundWindow forces the window to the front even from
                        // a background process, which Activate() cannot reliably do.
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(hwnd);
                        ShowNewTicketNotification(newTickets);
                    }

                    _knownTicketIds.Clear();
                    foreach (var t in tickets) _knownTicketIds.Add(t.Id);

                    FlashTaskbar();
                }
                else
                {
                    _knownTicketIds.Clear();
                    _mainWindow?.UpdateTickets([]);
                }

                if (_trayIcon != null)
                    _trayIcon.Text = tickets.Count > 0
                        ? $"CW Notification Companion — {tickets.Count} pending"
                        : "CW Notification Companion";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                if (_trayIcon != null)
                    _trayIcon.Text = "CW Notification Companion — Error polling";
                _mainWindow?.SetError(ex.Message);
            });
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private void FlashTaskbar()
    {
        if (_mainWindow != null && _mainWindow.IsVisible)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
            NativeMethods.FlashWindow(helper.Handle, true);
        }
    }

    private static void RegisterAumid()
    {
        // Unpackaged Win32 apps must register an AppUserModelId in the registry
        // before Windows will accept and store their toast notifications.
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            $@"SOFTWARE\Classes\AppUserModelId\{AppId}");
        key.SetValue("DisplayName", "CW Notification Companion");
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (!string.IsNullOrEmpty(exe))
            key.SetValue("IconUri", exe);
    }

    private void ShowNewTicketNotification(List<Ticket> newTickets)
    {
        if (newTickets.Count == 0) return;

        string title, body;
        if (newTickets.Count == 1)
        {
            var t = newTickets[0];
            title = $"#{t.Id} — {t.CompanyDisplay}";
            body  = t.Summary.Length > 100 ? t.Summary[..100] + "…" : t.Summary;
        }
        else
        {
            title = $"{newTickets.Count} new client responses";
            var companies = newTickets.Take(3).Select(t => t.CompanyDisplay);
            body  = string.Join(", ", companies) + (newTickets.Count > 3 ? " …" : "");
        }

        var xml = $"""
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>{System.Security.SecurityElement.Escape(title)}</text>
                  <text>{System.Security.SecurityElement.Escape(body)}</text>
                </binding>
              </visual>
            </toast>
            """;

        try
        {
            var doc = new Windows.Data.Xml.Dom.XmlDocument();
            doc.LoadXml(xml);
            var toast = new Windows.UI.Notifications.ToastNotification(doc);
            toast.Activated += (_, _) => Dispatcher.Invoke(() => ShowMainWindow(null));
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier(AppId).Show(toast);
        }
        catch { /* notification failure is non-fatal */ }
    }

    public void ShowMainWindow(List<Ticket>? tickets)
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
            _mainWindow = new MainWindow(_settingsService, _cwService);

        if (tickets != null)
            _mainWindow.UpdateTickets(tickets);

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void ShowSettings()
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsService, this);
        _settingsWindow.Show();
    }

    public void RefreshMainWindowAnchor()
    {
        _mainWindow?.RefreshAnchor();
    }

    private void SaveWindowState()
    {
        if (_mainWindow == null) return;
        var settings = _settingsService.Load();
        settings.SavedLeft  = _mainWindow.Left;
        settings.SavedTop   = _mainWindow.Top;
        settings.SavedWidth = _mainWindow.Width;
        _settingsService.Save(settings);
    }

    private void ExitApp()
    {
        SaveWindowState();
        _pollTimer?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pollTimer?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}

