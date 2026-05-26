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

    public MainWindow(SettingsService settingsService, ConnectWiseService cwService)
    {
        _settingsService = settingsService;
        _cwService = cwService;
        InitializeComponent();
        Loaded += (_, _) => PositionBottomRight();
        UpdateTickets([]);
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - ActualHeight - 16;
    }

    public void UpdateTickets(List<Ticket> tickets)
    {
        // Detect newly arrived tickets since the last refresh (skip on very first load)
        Ticket? newestNew = null;
        if (_knownTicketIds != null)
        {
            var newTickets = tickets.Where(t => !_knownTicketIds.Contains(t.Id)).ToList();
            if (newTickets.Count > 0)
            {
                newestNew = newTickets
                    .OrderByDescending(t => DateTime.TryParse(t.LastUpdated, out var dt) ? dt : DateTime.MinValue)
                    .First();
            }
        }

        _knownTicketIds = tickets.Select(t => t.Id).ToHashSet();

        TicketList.ItemsSource = tickets;

        var hasTickets = tickets.Count > 0;
        TicketScrollViewer.Visibility = hasTickets ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasTickets ? Visibility.Collapsed : Visibility.Visible;
        ErrorBar.Visibility = Visibility.Collapsed;

        HeaderText.Text = hasTickets
            ? $"Client Responses — {tickets.Count} ticket{(tickets.Count == 1 ? "" : "s")} awaiting response"
            : "Client Responses — All caught up";

        if (newestNew != null)
        {
            // Defer until after layout pass so the container exists
            Dispatcher.InvokeAsync(
                () => FlashTicketRow(newestNew),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void FlashTicketRow(Ticket ticket)
    {
        if (TicketList.ItemContainerGenerator.ContainerFromItem(ticket) is not ContentPresenter container)
            return;

        var border = FindVisualChild<Border>(container);
        if (border == null) return;

        // Set a local brush so we can animate its Color property
        var flashBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199)); // amber-100
        border.Background = flashBrush;

        var animation = new ColorAnimation
        {
            To = Colors.White,
            Duration = TimeSpan.FromMilliseconds(300),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop
        };

        // Restore Style control of Background after animation finishes
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

    private void OpenInCW_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Ticket ticket })
        {
            var settings = _settingsService.Load();
            var url = $"https://api-na.myconnectwise.net/v4_6_release/services/system_io/router/openrecord.rails" +
                      $"?locale=en_US&companyName={Uri.EscapeDataString(settings.CompanySlug)}" +
                      $"&recordType=ServiceFV&recid={ticket.Id}";
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
        bool isText = parameter is string s && s == "text";

        if (isText)
        {
            return isSecond
                ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
                : new SolidColorBrush(Color.FromRgb(146, 64, 14));
        }

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
