namespace CWNotificationCompanion.Models;

public class AppSettings
{
    public string ServerUrl { get; set; } = "https://na.myconnectwise.net/v4_6_release/apis/3.0";
    public string CompanyId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string CompanySlug { get; set; } = "";
    public int PollIntervalMinutes { get; set; } = 5;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerUrl) &&
        !string.IsNullOrWhiteSpace(CompanyId) &&
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(CompanySlug);
}
