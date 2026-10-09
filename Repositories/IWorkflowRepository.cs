using WorkflowNavigationFunction.Models;
namespace WorkflowNavigationFunction.Repositories;
public interface IWorkflowRepository
{
    Task<CaseRecord?> GetCaseAsync(string requestId, CancellationToken ct);
    Task<IReadOnlyList<ActivityRecord>> GetPowerAppsActivitiesAsync(string requestId, string routingSetId, string loginUserId, CancellationToken ct);
    Task<IReadOnlyList<ActivityRecord>> GetPortalActivitiesAsync(string requestId, string routingSetId, string loginUserId, CancellationToken ct);
    Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string? teamIds, string? userIds, CancellationToken ct);
    Task<IReadOnlyList<WebRoleUser>> GetWebRoleUsersAsync(string? userIds, string? roleIds, string? accountId, CancellationToken ct);
    Task<IReadOnlySet<string>> GetUserTeamIdsAsync(string loginUserId, CancellationToken ct);
    Task<IReadOnlySet<string>> GetUserWebRoleIdsAsync(string loginUserId, CancellationToken ct);
    Task<ScreenMapping?> GetScreenMappingAsync(ActivityRecord activity, CancellationToken ct);
    Task<string?> GetApplicationUrlAsync(string applicationId, bool isPortal, CancellationToken ct);

    Task<bool> TransactionExistsAsync(ActivityRecord activity, CancellationToken ct);
    Task<bool> PendingActivityExistsAsync(ActivityRecord activity, CancellationToken ct);
}
