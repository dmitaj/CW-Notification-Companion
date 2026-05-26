using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Threading.Tasks;
using CWNotificationCompanion.Models;
using CWNotificationCompanion.Services;

namespace CWNotificationCompanion;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly ConnectWiseService _cwService;

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
        TicketList.ItemsSource = tickets;
        LastCheckedText.Text = $"Last checked: {DateTime.Now:h:mm:ss tt}";

        var hasTickets = tickets.Count > 0;
        TicketScrollViewer.Visibility = hasTickets ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasTickets ? Visibility.Collapsed : Visibility.Visible;
        ErrorBar.Visibility = Visibility.Collapsed;

        SubtitleText.Text = hasTickets
            ? $"{tickets.Count} ticket{(tickets.Count == 1 ? "" : "s")} awaiting response"
            : "All caught up";
    }

    public void SetError(string message)
    {
        ErrorBar.Visibility = Visibility.Visible;
        ErrorText.Text = $"Poll error: {message}";
        SubtitleText.Text = "Error — see details below";
    }

    private void OpenInCW_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Ticket ticket })
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

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        Hide();
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
