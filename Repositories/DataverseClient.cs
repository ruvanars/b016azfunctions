using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using WorkflowNavigationFunction.Logging;

namespace WorkflowNavigationFunction.Repositories;

public sealed class DataverseClient : IDataverseClient
{
    private readonly HttpClient http;
    private readonly string baseUrl;
    private readonly TokenCredential credential;
    private readonly IExecutionTrace trace;

    public DataverseClient(HttpClient http, IConfiguration configuration, IExecutionTrace trace)
    {
        this.http = http;
        this.trace = trace;
        baseUrl = (configuration["Dataverse:Url"] ?? throw new InvalidOperationException("Dataverse:Url is missing.")).TrimEnd('/');
        var tenantId = configuration["Dataverse:TenantId"];
        var clientId = configuration["Dataverse:ClientId"];
        var clientSecret = configuration["Dataverse:ClientSecret"];
        credential = !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret)
            ? new ClientSecretCredential(tenantId, clientId, clientSecret)
            : new DefaultAzureCredential();
    }

    public async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken ct)
    {
        trace.Step("Dataverse GET", new { relativeUrl });
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([$"{baseUrl}/.default"]), ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/data/v9.2/{relativeUrl.TrimStart('/')}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("Prefer", "odata.include-annotations=OData.Community.Display.V1.FormattedValue");
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            trace.Step("Dataverse response", new { relativeUrl }, new { statusCode = (int)response.StatusCode, responseLength = body.Length });
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            trace.Failure(ex, new { action = "Dataverse GET", relativeUrl });
            throw;
        }
    }

    public async Task<IReadOnlyList<JsonElement>> FetchAsync(string entitySet, string fetchXml, CancellationToken ct)
    {
        trace.Step("Dataverse FetchXML", new { entitySet, fetchXml });
        using var document = await GetAsync($"{entitySet}?fetchXml={Uri.EscapeDataString(fetchXml)}", ct);
        var rows = document.RootElement.GetProperty("value").EnumerateArray().Select(x => x.Clone()).ToList();
        trace.Step("FetchXML completed", new { entitySet }, new { count = rows.Count });
        return rows;
    }
}
