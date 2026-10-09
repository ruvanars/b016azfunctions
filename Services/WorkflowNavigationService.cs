using WorkflowNavigationFunction.Logging;
using WorkflowNavigationFunction.Models;
using WorkflowNavigationFunction.Repositories;

namespace WorkflowNavigationFunction.Services;

public sealed class WorkflowNavigationService(IWorkflowRepository repository, IExecutionTrace trace) : IWorkflowNavigationService
{
    public Task<CaseRecord?> ExecuteAsyncTest(string request, CancellationToken ct) => repository.GetCaseAsync(request, ct);

    public async Task<WorkflowNavigationResponse> ExecuteAsync(WorkflowNavigationRequest request, CancellationToken ct)
    {
        trace.Step("B016 validate request", request);
        Validate(request);
        var caseRecord = await repository.GetCaseAsync(request.RequestId, ct);
        if (caseRecord is null || IsUnavailable(caseRecord)) return Empty(WorkflowConstants.RequestUnavailable);

        var activity = request.IsPortal ? await FindPortalActivityAsync(request, ct) : await FindPowerAppsActivityAsync(request, ct);
        if (activity is null) return Empty(WorkflowConstants.RequestUnavailable);
        trace.Step("Authorized activity selected", null, activity);

        var mapping = await repository.GetScreenMappingAsync(activity, ct);
        if (mapping is null) return Create(activity, "", "", WorkflowConstants.RequestUnavailable);
        var screenName = request.IsPortal ? mapping.PortalPageName : mapping.AppScreenName;
        var url = await BuildUrlAsync(request, mapping, ct);
        if (string.IsNullOrWhiteSpace(url)) return Create(activity, "", screenName, WorkflowConstants.RequestUnavailable);
        trace.Step("Navigation URL generated", new { request.IsPortal, screenName }, new { url });

        if (await repository.TransactionExistsAsync(activity, ct)) return Create(activity, url, screenName, WorkflowConstants.ActivityUnavailable);
        var pending = await repository.PendingActivityExistsAsync(activity, ct);
        return Create(activity, url, screenName, pending ? WorkflowConstants.Success : WorkflowConstants.ActivityUnavailable);
    }

    private async Task<ActivityRecord?> FindPowerAppsActivityAsync(WorkflowNavigationRequest r, CancellationToken ct)
    {
        /* var records = (await repository.GetPowerAppsActivitiesAsync(r.RequestId, r.RoutingSetId, r.LoginUserId, ct)).Where(IsPending).ToList();
         trace.Step("Power Apps activity candidates", new { r.RequestId, r.LoginUserId }, new { count = records.Count });
         if (records.Count == 0) return null;
         var teamRecords = records.Where(x => !string.IsNullOrWhiteSpace(x.TeamId)).ToList();
         if (teamRecords.Count == 0) return records.First();
         var teams = await repository.GetUserTeamIdsAsync(r.LoginUserId, ct); // B029
         return teamRecords.FirstOrDefault(x => x.TeamId is not null && teams.Contains(x.TeamId));
        */


        // 1. Get Activity Management records
        var records = (await repository.GetPowerAppsActivitiesAsync(
            r.RequestId,
            r.RoutingSetId,
            r.LoginUserId,
            ct))
            .Where(IsPending)
            .ToList();

        trace.Step(
            "B016 Power Apps - Activity Management records",
            new
            {
                r.RequestId,
                r.RoutingSetId,
                r.LoginUserId
            },
            new
            {
                count = records.Count
            });

        // No Activity Management records
        if (records.Count == 0)
        {
            trace.Step(
                "B016 Power Apps - No Activity Management records");

            return null;
        }

        // 2. Filter records where Team is not null
        var teamRecords = records
            .Where(x => !string.IsNullOrWhiteSpace(x.TeamId))
            .ToList();

        trace.Step(
            "B016 Power Apps - Filter Team records",
            null,
            new
            {
                totalRecords = records.Count,
                teamRecords = teamRecords.Count
            });

        // 3. Team 判断
        // Power Automate NO branch:
        // If there are no records with a Team,
        // the first Activity Management record is selected.
        if (teamRecords.Count == 0)
        {
            var selected = records[0];

            trace.Step(
                "B016 Power Apps - No Team records",
                null,
                new
                {
                    selectedActivityId = selected.Id,
                    selectedTeamId = selected.TeamId
                });

            return selected;
        }

        // 4. Get the teams assigned to the logged-in user.
        // This corresponds to the B029 child flow.
        var userTeamIds = await repository.GetUserTeamIdsAsync(
            r.LoginUserId,
            ct);

        trace.Step(
            "B016 Power Apps - B029 user team lookup",
            new
            {
                loginUserId = r.LoginUserId
            },
            new
            {
                userTeamCount = userTeamIds.Count
            });

        // 5. Intersect Activity Management Teams
        // with the logged-in user's Teams.
        var matchingActivity = teamRecords.FirstOrDefault(activity =>
        !string.IsNullOrWhiteSpace(activity.TeamId) &&
        userTeamIds.Contains(activity.TeamId));

        trace.Step(
            "B016 Power Apps - Team intersection",
            null,
            new
            {
                matched = matchingActivity is not null,
                activityId = matchingActivity?.Id,
                teamId = matchingActivity?.TeamId
            });

        if (matchingActivity is null)
            return null;
        // Power Automate Condition 5 = length(intersection(...)) != 0
        // YES branch:
        // first(outputs('行を一覧にする_アクティビティ管理取得')?['body/value'])
        return records[0];
    }
    

    private async Task<ActivityRecord?> FindPortalActivityAsync(WorkflowNavigationRequest r, CancellationToken ct)
    {
        var records = (await repository.GetPortalActivitiesAsync(r.RequestId, r.RoutingSetId, r.LoginUserId, ct)).Where(IsPending).ToList();
        trace.Step("Portal activity candidates", new { r.RequestId, r.LoginUserId }, new { count = records.Count });
        if (records.Count == 0) return null;
        var roleRecords = records.Where(x => !string.IsNullOrWhiteSpace(x.WebRoleId)).ToList();
        if (roleRecords.Count == 0) return records.First();
        var roles = await repository.GetUserWebRoleIdsAsync(r.LoginUserId, ct);

        var matchingActivity = roleRecords.FirstOrDefault(
            x => x.WebRoleId is not null &&
                 roles.Contains(x.WebRoleId));

        trace.Step(
            "B016 Portal - Web Role intersection",
            null,
            new
            {
                matched = matchingActivity is not null,
                activityId = matchingActivity?.Id,
                webRoleId = matchingActivity?.WebRoleId
            });

        if (matchingActivity is null)
            return null;

        // Power Automate Condition 3 = length(intersection(...)) != 0
        // YES branch sets Activity Management Record to:
        // first(outputs('行を一覧にする_アクティビティ管理web')?['body/value'])
        return records[0];
    }

    private async Task<string?> BuildUrlAsync(WorkflowNavigationRequest r, ScreenMapping m, CancellationToken ct)
    {
        var baseUrl = r.ApplicationIdOrUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var id = r.IsPortal ? m.PortalId : m.AppId;
            if (string.IsNullOrWhiteSpace(id)) return null;
            //baseUrl = await repository.GetApplicationUrlAsync(id, ct);
            baseUrl = await repository.GetApplicationUrlAsync(id, r.IsPortal, ct);
        }
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        return r.IsPortal
            ? $"{baseUrl.TrimEnd('/')}/{m.PortalPageName.Trim('/')}/?reqid={Uri.EscapeDataString(r.RequestId)}&transid={Uri.EscapeDataString(r.RoutingSetId)}"
            : $"{baseUrl}&gbl_RequestID={Uri.EscapeDataString(r.RequestId)}&gbl_KaifusakiID={Uri.EscapeDataString(r.RoutingSetId)}";
    }

    //private static bool IsUnavailable(CaseRecord x) => x.StateCode == 1 || x.CaseStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase) || x.CaseStatus.Contains("Under Investigation", StringComparison.OrdinalIgnoreCase) || x.CaseStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnavailable(CaseRecord x) => x.StateCode == 1 || x.CaseStatus.Equals("完了", StringComparison.Ordinal) || x.CaseStatus.Contains("調査中", StringComparison.Ordinal) ||  x.CaseStatus.Equals("取消", StringComparison.Ordinal);
    private static bool IsPending(ActivityRecord x) => x.Status == WorkflowConstants.Pending && x.StateCode == 0;
    private static WorkflowNavigationResponse Create(ActivityRecord a, string url, string screen, string message) => new(url, a.BlockName, a.BlockAlias, a.ActivityName, screen, "true", a.PhaseName, message, a.IsFinalApprover.ToString().ToLowerInvariant(), a.BusinessName, a.RoleHolder);
    private static WorkflowNavigationResponse Empty(string message) => new("", "", "", "", "", "false", "", message, "false", "", "");
    private static void Validate(WorkflowNavigationRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (string.IsNullOrWhiteSpace(r.RequestId)) throw new ArgumentException("RequestId is required.");
        if (string.IsNullOrWhiteSpace(r.LoginUserId)) throw new ArgumentException("LoginUserId is required.");
        if (string.IsNullOrWhiteSpace(r.RoutingSetId)) throw new ArgumentException("RoutingSetId is required.");
    }
}
