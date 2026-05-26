using System;
using System.Windows;
using System.Windows.Media;
using CWNotificationCompanion.Models;
using CWNotificationCompanion.Services;

namespace CWNotificationCompanion;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly App _app;

    public SettingsWindow(SettingsService settingsService, App app)
    {
        _settingsService = settingsService;
        _app = app;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        ServerUrlBox.Text = s.ServerUrl;
        CompanyIdBox.Text = s.CompanyId;
        PublicKeyBox.Text = s.PublicKey;
        PrivateKeyBox.Password = s.PrivateKey;
        ClientIdBox.Text = s.ClientId;
        CompanySlugBox.Text = s.CompanySlug;
        ResourceFilterBox.Text = s.ResourceFilter;
        AnchorCornerBox.SelectedIndex = (int)s.AnchorCorner;
        PollIntervalBox.Text = s.PollIntervalMinutes.ToString();
    }

    private AppSettings ReadForm()
    {
        if (!int.TryParse(PollIntervalBox.Text.Trim(), out int interval) || interval < 1)
            interval = 5;

        return new AppSettings
        {
            ServerUrl = ServerUrlBox.Text.Trim(),
            CompanyId = CompanyIdBox.Text.Trim(),
            PublicKey = PublicKeyBox.Text.Trim(),
            PrivateKey = PrivateKeyBox.Password,
            ClientId = ClientIdBox.Text.Trim(),
            CompanySlug = CompanySlugBox.Text.Trim(),
            ResourceFilter = ResourceFilterBox.Text.Trim(),
            AnchorCorner = (AnchorCorner)(AnchorCornerBox.SelectedIndex < 0 ? 3 : AnchorCornerBox.SelectedIndex),
            PollIntervalMinutes = interval
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadForm();
        _settingsService.Save(settings);
        _app.StartPolling();
        _app.RefreshMainWindowAnchor();
        ShowStatus("Settings saved successfully.", success: true);
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadForm();

        if (!settings.IsConfigured)
        {
            ShowStatus("Please fill in all required fields before testing.", success: false);
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = "Testing…";
        HideStatus();

        try
        {
            var cwService = new Services.ConnectWiseService();
            var (success, message) = await cwService.TestConnectionAsync(settings);
            ShowStatus(message, success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", success: false);
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = "Test Connection";
        }
    }

    private void ShowStatus(string message, bool success)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = message;
        StatusBorder.Background = success
            ? new SolidColorBrush(Color.FromRgb(240, 253, 244))
            : new SolidColorBrush(Color.FromRgb(254, 242, 242));
        StatusBorder.BorderBrush = success
            ? new SolidColorBrush(Color.FromRgb(187, 247, 208))
            : new SolidColorBrush(Color.FromRgb(254, 202, 202));
        StatusBorder.BorderThickness = new Thickness(1);
        StatusText.Foreground = success
            ? new SolidColorBrush(Color.FromRgb(22, 101, 52))
            : new SolidColorBrush(Color.FromRgb(153, 27, 27));
    }

    private void HideStatus()
    {
        StatusBorder.Visibility = Visibility.Collapsed;
    }
}
