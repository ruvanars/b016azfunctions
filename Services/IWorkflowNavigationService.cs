using WorkflowNavigationFunction.Models;
namespace WorkflowNavigationFunction.Services;
public interface IWorkflowNavigationService
{
    Task<WorkflowNavigationResponse> ExecuteAsync(WorkflowNavigationRequest request, CancellationToken ct);
    Task<CaseRecord?> ExecuteAsyncTest(string request, CancellationToken ct);
}
