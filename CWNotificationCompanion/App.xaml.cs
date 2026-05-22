using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using CWNotificationCompanion.Models;
using CWNotificationCompanion.Services;

namespace CWNotificationCompanion;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private Timer? _pollTimer;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly ConnectWiseService _cwService = new();
    private readonly SettingsService _settingsService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        InitializeTrayIcon();
        StartPolling();

        var settings = _settingsService.Load();
        if (!settings.IsConfigured)
            ShowSettings();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "CW Notification Companion"
        };

        var menu = new ContextMenuStrip();
        menu.Font = new Font("Segoe UI", 9f);

        var openItem = new ToolStripMenuItem("Open Tickets");
        openItem.Click += (_, _) => ShowMainWindow(null);
        menu.Items.Add(openItem);

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow(null);
    }

    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillEllipse(new SolidBrush(Color.FromArgb(37, 99, 235)), 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("CW", font, System.Drawing.Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void StartPolling()
    {
        _pollTimer?.Dispose();
        var settings = _settingsService.Load();
        int intervalMs = Math.Max(settings.PollIntervalMinutes, 1) * 60_000;
        _pollTimer = new Timer(_ => _ = PollAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
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
                    if (_mainWindow == null || !_mainWindow.IsLoaded)
                    {
                        _mainWindow = new MainWindow(_settingsService, _cwService);
                        _mainWindow.UpdateTickets(tickets);
                        _mainWindow.Show();
                    }
                    else
                    {
                        _mainWindow.UpdateTickets(tickets);
                        if (!_mainWindow.IsVisible)
                            _mainWindow.Show();
                    }

                    FlashTaskbar();
                }
                else
                {
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

    public void ShowMainWindow(List<Ticket>? tickets)
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow(_settingsService, _cwService);
        }

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

    private void ExitApp()
    {
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

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
}
