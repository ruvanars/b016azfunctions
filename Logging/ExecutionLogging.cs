using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WorkflowNavigationFunction.Logging;

public interface IBlobLogWriter
{
    Task<string> WriteAsync(string correlationId, string text, CancellationToken ct);
}

public sealed class BlobLogWriter : IBlobLogWriter
{
    private readonly BlobContainerClient container;
    private readonly string prefix;

    public BlobLogWriter(IConfiguration configuration)
    {
        var account = configuration["Logging:StorageAccountName"] ?? "connectdataversesample01";
        var containerName = configuration["Logging:ContainerName"] ?? "filecontainer-logs";
        prefix = (configuration["Logging:BlobPrefix"] ?? "B016-logs").Trim('/');
        var connectionString = configuration["Logging:StorageConnectionString"];
        container = !string.IsNullOrWhiteSpace(connectionString)
            ? new BlobContainerClient(connectionString, containerName)
            : new BlobContainerClient(new Uri($"https://{account}.blob.core.windows.net/{containerName}"), new DefaultAzureCredential());
    }

    public async Task<string> WriteAsync(string correlationId, string text, CancellationToken ct)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var now = DateTimeOffset.UtcNow;
        var blobName = $"{prefix}/{now:yyyy/MM/dd}/B016_{now:yyyyMMdd_HHmmss_fff}_{correlationId}.txt";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(BinaryData.FromString(text), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "text/plain; charset=utf-8" }
        }, ct);
        return blob.Uri.ToString();
    }
}

public interface IExecutionTrace
{
    string CorrelationId { get; }
    void Start(string operation, object? input);
    void Step(string action, object? input = null, object? output = null);
    void Success(object? output);
    void Failure(Exception exception, object? context = null);
    Task<string> FlushAsync(CancellationToken ct);
}

public sealed class ExecutionTrace(ILogger<ExecutionTrace> logger, IBlobLogWriter blobWriter) : IExecutionTrace
{
    private readonly ConcurrentQueue<string> lines = new();
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");

    public void Start(string operation, object? input) => Add("START", operation, input, null, null);
    public void Step(string action, object? input = null, object? output = null) => Add("TRACE", action, input, output, null);
    public void Success(object? output) => Add("SUCCESS", "Processing completed", null, output, null);
    public void Failure(Exception exception, object? context = null) => Add("EXCEPTION", "Processing failed", context, null, exception);

    private void Add(string level, string action, object? input, object? output, Exception? exception)
    {
        var line = $"{DateTimeOffset.UtcNow:O} | {level} | CorrelationId={CorrelationId} | Action={action} | Input={Serialize(input)} | Output={Serialize(output)}";
        if (exception is not null)
            line += $" | ExceptionType={exception.GetType().FullName} | Message={exception.Message} | StackTrace={exception.StackTrace}";
        lines.Enqueue(line);
        if (exception is null) logger.LogInformation("{TraceLine}", line); else logger.LogError(exception, "{TraceLine}", line);
    }

    public async Task<string> FlushAsync(CancellationToken ct)
    {
        Add("TRACE", "Upload text log to Blob Storage", new { container = "filecontainer-logs", prefix = "B016-logs" }, null, null);
        var text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        try
        {
            var uri = await blobWriter.WriteAsync(CorrelationId, text, ct);
            logger.LogInformation("Trace log uploaded. CorrelationId={CorrelationId}; Blob={BlobUri}", CorrelationId, uri);
            return uri;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while uploading trace log. CorrelationId={CorrelationId}", CorrelationId);
            throw;
        }
    }

    private static string Serialize(object? value)
    {
        if (value is null) return "null";
        try { return JsonSerializer.Serialize(value); }
        catch { return value.ToString() ?? "null"; }
    }
}
