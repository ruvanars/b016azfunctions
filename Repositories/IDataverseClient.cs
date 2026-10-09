using System.Text.Json;
namespace WorkflowNavigationFunction.Repositories;
public interface IDataverseClient
{
    Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken ct);
    Task<IReadOnlyList<JsonElement>> FetchAsync(string entitySet, string fetchXml, CancellationToken ct);
}
