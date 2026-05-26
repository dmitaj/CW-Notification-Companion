using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CWNotificationCompanion.Models;

namespace CWNotificationCompanion.Services;

public class ConnectWiseService
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ConnectWiseService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.connectwise.com+json", 1.0));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json", 0.9));
    }

    public async Task<List<Ticket>> GetClientRespondedTicketsAsync(AppSettings settings)
    {
        SetAuth(settings);

        var statusFilter = "status/name=\"Client Responded\" OR status/name=\"Client Responded - 2\"";
        var conditionStr = string.IsNullOrWhiteSpace(settings.ResourceFilter)
            ? statusFilter
            : $"({statusFilter}) AND resources contains \"{settings.ResourceFilter}\"";

        var conditions = Uri.EscapeDataString(conditionStr);
        var fields = Uri.EscapeDataString("id,summary,status,company,contactName,lastUpdated");
        var url = $"{settings.ServerUrl.TrimEnd('/')}/service/tickets" +
                  $"?conditions={conditions}&fields={fields}&pageSize=100&orderBy=lastUpdated+asc";


        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<Ticket>>(json, _jsonOptions) ?? [];
    }

    public async Task<(bool success, string message)> TestConnectionAsync(AppSettings settings)
    {
        try
        {
            SetAuth(settings);
            var url = $"{settings.ServerUrl.TrimEnd('/')}/system/info";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
                return (true, "Connection successful!");

            return (false, $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private void SetAuth(AppSettings settings)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.CompanyId}+{settings.PublicKey}:{settings.PrivateKey}"));

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        _httpClient.DefaultRequestHeaders.Remove("clientId");
        _httpClient.DefaultRequestHeaders.Add("clientId", settings.ClientId);
    }
}
