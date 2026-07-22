using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using CWNotificationCompanion.Models;
using CWNotificationCompanion.Services;

namespace CWNotificationCompanion;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly ConnectWiseService _cwService;
    private HashSet<int>? _knownTicketIds;

    private AnchorCorner _anchorCorner;
    private double _anchorX, _anchorY;
    private bool _isRepositioning;
    private bool _isLoaded;

    private const int SnapThreshold = 20; // physical pixels

    // Approximate rendered height of one ticket row (border padding + the tallest
    // row content, the "Open in CW" button). Only used as a cap on the ScrollViewer's
    // growth — Window.SizeToContent measures the real content, so this doesn't need
    // to be pixel-perfect, just not an underestimate (that would clip the last row).
    private const double RowH = 56;

    public MainWindow(SettingsService settingsService, ConnectWiseService cwService)
    {
        _settingsService = settingsService;
        _cwService = cwService;
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyMaxVisibleTicketsSetting();
        UpdateTickets([]);
    }

    private void ApplyMaxVisibleTicketsSetting()
    {
        var maxVisible = Math.Max(1, _settingsService.Load().MaxVisibleTickets);
        TicketScrollViewer.MaxHeight = maxVisible * RowH;
    }

    // ── Positioning & anchoring ───────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        _anchorCorner = settings.AnchorCorner;

        if (settings.SavedLeft.HasValue && settings.SavedTop.HasValue)
        {
            _isRepositioning = true;
            Left  = settings.SavedLeft.Value;
            Top   = settings.SavedTop.Value;
            if (settings.SavedWidth.HasValue)
                Width = settings.SavedWidth.Value;
            _isRepositioning = false;
            UpdateAnchorFromCurrentPosition();
        }
        else
        {
            PositionToAnchorCorner();
        }

        _isLoaded = true;

        // Lock height to its natural size immediately on first display too, not just
        // starting from the next poll's UpdateTickets call - see that method for why.
        MinHeight = ActualHeight;
        MaxHeight = ActualHeight;
    }

    private void PositionToAnchorCorner()
    {
        var wa = SystemParameters.WorkArea;
        const double margin = 16;

        _isRepositioning = true;
        try
        {
            switch (_anchorCorner)
            {
                case AnchorCorner.TopLeft:
                    Left = wa.Left   + margin;
                    Top  = wa.Top    + margin;
                    break;
                case AnchorCorner.TopRight:
                    Left = wa.Right  - Width - margin;
                    Top  = wa.Top    + margin;
                    break;
                case AnchorCorner.BottomLeft:
                    Left = wa.Left   + margin;
                    Top  = wa.Bottom - ActualHeight - margin;
                    break;
                default: // BottomRight
                    Left = wa.Right  - Width - margin;
                    Top  = wa.Bottom - ActualHeight - margin;
                    break;
            }
        }
        finally
        {
            _isRepositioning = false;
        }

        UpdateAnchorFromCurrentPosition();
    }

    private void UpdateAnchorFromCurrentPosition()
    {
        _anchorX = _anchorCorner is AnchorCorner.TopRight  or AnchorCorner.BottomRight
            ? Left + ActualWidth
            : Left;
        _anchorY = _anchorCorner is AnchorCorner.BottomLeft or AnchorCorner.BottomRight
            ? Top + ActualHeight
            : Top;
    }

    private void ApplyAnchor()
    {
        _isRepositioning = true;
        try
        {
            if (_anchorCorner is AnchorCorner.TopRight or AnchorCorner.BottomRight)
                Left = _anchorX - ActualWidth;
            if (_anchorCorner is AnchorCorner.BottomLeft or AnchorCorner.BottomRight)
                Top = _anchorY - ActualHeight;
        }
        finally
        {
            _isRepositioning = false;
        }
    }

    public void RefreshAnchor()
    {
        _anchorCorner = _settingsService.Load().AnchorCorner;
        ApplyMaxVisibleTicketsSetting();
        if (_isLoaded)
            PositionToAnchorCorner();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_isRepositioning) return;
        UpdateAnchorFromCurrentPosition();
        SnapToScreenCorners();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_isLoaded && !_isRepositioning)
            ApplyAnchor();
    }

    private void SnapToScreenCorners()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!NativeMethods.GetWindowRect(hwnd, out var win)) return;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = NativeMethods.MONITORINFO.Create();
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi)) return;

        var wa   = mi.rcWork;
        var winW = win.Right  - win.Left;
        var winH = win.Bottom - win.Top;

        int newX = win.Left, newY = win.Top;
        bool snap = false;

        if      (Math.Abs(win.Left  - wa.Left)   <= SnapThreshold) { newX = wa.Left;          snap = true; }
        else if (Math.Abs(win.Right - wa.Right)   <= SnapThreshold) { newX = wa.Right  - winW; snap = true; }

        if      (Math.Abs(win.Top    - wa.Top)    <= SnapThreshold) { newY = wa.Top;            snap = true; }
        else if (Math.Abs(win.Bottom - wa.Bottom) <= SnapThreshold) { newY = wa.Bottom - winH;  snap = true; }

        if (snap && (newX != win.Left || newY != win.Top))
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, newX, newY, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    // ── Ticket updates ────────────────────────────────────────────────────────

    public void UpdateTickets(List<Ticket> tickets)
    {
        Ticket? newestNew = null;
        if (_knownTicketIds != null)
        {
            var newTickets = tickets.Where(t => !_knownTicketIds.Contains(t.Id)).ToList();
            if (newTickets.Count > 0)
                newestNew = newTickets
                    .OrderByDescending(t => DateTime.TryParse(t.LastUpdated, out var dt) ? dt : DateTime.MinValue)
                    .First();
        }

        _knownTicketIds = tickets.Select(t => t.Id).ToHashSet();
        TicketList.ItemsSource = tickets;

        var hasTickets = tickets.Count > 0;
        TicketScrollViewer.Visibility = hasTickets ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility         = hasTickets ? Visibility.Collapsed : Visibility.Visible;
        ErrorBar.Visibility           = Visibility.Collapsed;

        HeaderText.Text = hasTickets
            ? $"Client Responses — {tickets.Count} ticket{(tickets.Count == 1 ? "" : "s")} awaiting response"
            : "Client Responses — All caught up";

        // Release any previous lock so SizeToContent can freely recompute the natural
        // height for the new ticket count, then force that resize to settle synchronously
        // (before the caller, App.PollAsync, can call ShowWindow(SW_RESTORE) on this window -
        // otherwise that Win32 call can land before the layout pass runs and bake in a stale size).
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        UpdateLayout();

        // Lock height to exactly that natural size so the resize grip can't drag the
        // window taller or shorter than what the current ticket count needs - taller
        // would just leave the ticket list floating, centered, in dead space below it.
        // Only once actually loaded/rendered - before that, ActualHeight is still 0
        // (this method also runs pre-Show, from the constructor and the first poll).
        if (_isLoaded)
        {
            MinHeight = ActualHeight;
            MaxHeight = ActualHeight;
        }

        if (newestNew != null)
            Dispatcher.InvokeAsync(
                () => FlashTicketRow(newestNew),
                System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FlashTicketRow(Ticket ticket)
    {
        if (TicketList.ItemContainerGenerator.ContainerFromItem(ticket) is not ContentPresenter container)
            return;

        var border = FindVisualChild<Border>(container);
        if (border == null) return;

        var flashBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199));
        border.Background = flashBrush;

        var animation = new ColorAnimation
        {
            To = Colors.White,
            Duration = TimeSpan.FromMilliseconds(300),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) => border.ClearValue(Border.BackgroundProperty);
        flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    public void SetError(string message)
    {
        ErrorBar.Visibility = Visibility.Visible;
        ErrorText.Text = $"Poll error: {message}";
        HeaderText.Text = "Client Responses — Error polling";
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OpenInCW_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Ticket ticket })
        {
            var settings = _settingsService.Load();
            var url = ConnectWiseService.BuildTicketUrl(settings, ticket.Id);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            var settings = _settingsService.Load();
            if (!settings.IsConfigured)
            {
                MessageBox.Show("Please configure your ConnectWise settings first.",
                    "Not Configured", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var tickets = await _cwService.GetClientRespondedTicketsAsync(settings);
            UpdateTickets(tickets);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ShowSettings();
    }
}

public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSecond = value is bool b && b;
        bool isText   = parameter is string s && s == "text";

        if (isText)
            return isSecond
                ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
                : new SolidColorBrush(Color.FromRgb(146, 64, 14));

        return isSecond
            ? new SolidColorBrush(Color.FromRgb(254, 226, 226))
            : new SolidColorBrush(Color.FromRgb(255, 237, 213));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusBorderColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isSecond = value is bool b && b;
        return isSecond
            ? new SolidColorBrush(Color.FromRgb(252, 165, 165))
            : new SolidColorBrush(Color.FromRgb(253, 186, 116));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
