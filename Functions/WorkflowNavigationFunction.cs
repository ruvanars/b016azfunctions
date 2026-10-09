using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkflowNavigationFunction.Logging;
using WorkflowNavigationFunction.Models;
using WorkflowNavigationFunction.Services;

namespace WorkflowNavigationFunction.Functions;

public sealed class WorkflowNavigationFunction(IWorkflowNavigationService service, IExecutionTrace trace)
{
    [Function("WorkflowNavigation")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "workflow/navigation")] HttpRequestData req, CancellationToken ct)
    {
        WorkflowNavigationRequest? input = null;
        try
        {
            input = await req.ReadFromJsonAsync<WorkflowNavigationRequest>(ct);
            trace.Start("B016 WorkflowNavigation", input);
            if (input is null) throw new ArgumentException("Request body is required.");
            var result = await service.ExecuteAsync(input, ct);
            trace.Success(result);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result, ct);
            return response;
        }
        catch (Exception ex)
        {
            trace.Failure(ex, new { input });
            var response = req.CreateResponse(ex is ArgumentException ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = ex.Message, exceptionType = ex.GetType().FullName, correlationId = trace.CorrelationId }, ct);
            return response;
        }
        finally
        {
            try { await trace.FlushAsync(CancellationToken.None); }
            catch (Exception logException) { trace.Failure(logException, new { stage = "Blob log upload" }); }
        }
    }

    [Function("HealthCheck")]
    public async Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "healthy" });
        return response;
    }

    [Function("GetCaseTest")]
    public async Task<HttpResponseData> GetCaseTest([HttpTrigger(AuthorizationLevel.Function, "get", Route = "workflow/testcase")] HttpRequestData req, CancellationToken ct)
    {
        string? id = null;
        try
        {
            // simple query parsing for ?id=...
            var q = req.Url.Query;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pairs = q.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length > 0 && kv[0] == "id") id = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                }
            }

            trace.Start("B016 GetCaseTest", new { id });
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id query parameter is required.");
            var result = await service.ExecuteAsyncTest(id, ct);
            trace.Success(result);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result, ct);
            return response;
        }
        catch (Exception ex)
        {
            trace.Failure(ex, new { id });
            var response = req.CreateResponse(ex is ArgumentException ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = ex.Message, exceptionType = ex.GetType().FullName, correlationId = trace.CorrelationId }, ct);
            return response;
        }
        finally
        {
            try { await trace.FlushAsync(CancellationToken.None); }
            catch (Exception logException) { trace.Failure(logException, new { stage = "Blob log upload" }); }
        }
    }
}
