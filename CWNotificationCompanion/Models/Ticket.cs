using System.Text.Json.Serialization;

namespace CWNotificationCompanion.Models;

public class Ticket
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("status")]
    public TicketStatus? Status { get; set; }

    [JsonPropertyName("company")]
    public TicketCompany? Company { get; set; }

    [JsonPropertyName("contactName")]
    public string? ContactName { get; set; }

    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }

    public string IdDisplay => $"#{Id}";
    public string CompanyDisplay => Company?.Name ?? "Unknown";
    public string StatusDisplay => Status?.Name ?? "";

    public bool IsSecondResponse =>
        Status?.Name?.Contains("- 2", StringComparison.OrdinalIgnoreCase) == true ||
        Status?.Name?.EndsWith("2", StringComparison.OrdinalIgnoreCase) == true;
}

public class TicketStatus
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class TicketCompany
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
