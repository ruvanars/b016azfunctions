using System.Security;
using System.Text.Json;
using WorkflowNavigationFunction.Logging;
using WorkflowNavigationFunction.Models;

namespace WorkflowNavigationFunction.Repositories;

public sealed class DataverseWorkflowRepository(IDataverseClient client, IExecutionTrace trace) : IWorkflowRepository
{
    private const string Cases = "tps_ankenkanri_trn_ibms";
    private const string Activities = "tps_akuteibiteikanri_trn_ibms";
    private const string Screens = "tps_akuteibitei_sukurin_mst_ibms";
    private const string Environments = "tps_app_env_mst_ibms";
    private const string Transactions = "tps_toranzakushonkanri_trn_ibms";

    public async Task<CaseRecord?> GetCaseAsync(string requestId, CancellationToken ct)
    {
        trace.Step("Get case", new { requestId });
        using var doc = await client.GetAsync($"{Cases}({GuidValue(requestId)})?$select=tps_ankensutetasu,statecode", ct);
        var r = doc.RootElement;
        var result = new CaseRecord(requestId, String(r, "tps_ankensutetasu"), Int(r, "statecode"));
        trace.Step("Get case completed", new { requestId }, result);
        return result;
    }

    public Task<IReadOnlyList<ActivityRecord>> GetPowerAppsActivitiesAsync(string requestId, string routingSetId, string loginUserId, CancellationToken ct) =>
        GetActivitiesAsync($"_tps_shinseibango_value eq {GuidLiteral(requestId)} and _tps_kaifusakisettoid_value eq {GuidLiteral(routingSetId)} and _tps_shoninsha_value eq {GuidLiteral(loginUserId)} and tps_sutetasu eq 1 and statecode eq 0", ct);

    public Task<IReadOnlyList<ActivityRecord>> GetPortalActivitiesAsync(string requestId, string routingSetId, string loginUserId, CancellationToken ct) =>
        GetActivitiesAsync($"_tps_shinseibango_value eq {GuidLiteral(requestId)} and _tps_kaifusakisettoid_value eq {GuidLiteral(routingSetId)} and _tps_gaibukaifusha_value eq {GuidLiteral(loginUserId)} and tps_sutetasu eq 1 and statecode eq 0", ct);

    // B029: teamId takes precedence. If empty, userId is used. Only non-default owner teams and enabled users are returned.
    public async Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string? teamIds, string? userIds, CancellationToken ct)
    {
        var teams = ParseIds(teamIds);
        var users = ParseIds(userIds);
        if (teams.Count == 0 && users.Count == 0) return [];
        var criterion = teams.Count > 0
            ? $"<condition attribute='teamid' operator='in'>{ValuesXml(teams)}</condition>"
            : $"<condition entityname='membership' attribute='systemuserid' operator='in'>{ValuesXml(users)}</condition>";
        var fetch = $"<fetch distinct='true'><entity name='team'><attribute name='teamid'/><attribute name='name'/><filter><condition attribute='teamtype' operator='eq' value='0'/><condition attribute='isdefault' operator='eq' value='0'/>{criterion}</filter><link-entity name='teammembership' from='teamid' to='teamid' alias='membership'><link-entity name='systemuser' from='systemuserid' to='systemuserid' alias='member'><attribute name='systemuserid'/><attribute name='fullname'/><attribute name='internalemailaddress'/><filter><condition attribute='isdisabled' operator='eq' value='0'/></filter></link-entity></link-entity></entity></fetch>";
        var rows = await client.FetchAsync("teams", fetch, ct);
        var result = rows.Select(x => new TeamMember(String(x, "member.systemuserid"), String(x, "teamid"), String(x, "member.fullname"), String(x, "member.internalemailaddress"), String(x, "name"))).Distinct().ToList();
        trace.Step("B029 team membership completed", new { teamIds, userIds }, new { count = result.Count, result });
        return result;
    }

    // B040: roleId takes precedence. Optional accountId narrows approved active contacts. If roleId is empty, userId drives role membership.
    public async Task<IReadOnlyList<WebRoleUser>> GetWebRoleUsersAsync(string? userIds, string? roleIds, string? accountId, CancellationToken ct)
    {
        var roles = ParseIds(roleIds);
        var users = ParseIds(userIds);
        if (roles.Count == 0 && users.Count == 0) return [];
        var roleFilter = roles.Count > 0 ? $"<condition attribute='tps_pageswebroru_mst_ibmid' operator='in'>{ValuesXml(roles)}</condition>" : "";
        var userFilter = users.Count > 0 ? $"<condition attribute='contactid' operator='in'>{ValuesXml(users)}</condition>" : "";
        var accountFilter = string.IsNullOrWhiteSpace(accountId) ? "" : $"<condition attribute='parentcustomerid' operator='eq' value='{GuidValue(accountId)}'/>";
        var fetch = $"<fetch distinct='true'><entity name='tps_pageswebroru_mst_ibm'><attribute name='tps_pageswebroru_mst_ibmid'/><attribute name='tps_webrorumei'/><filter>{roleFilter}<condition attribute='statecode' operator='eq' value='0'/></filter><link-entity name='tps_pageswebroru_mst_ibm_contact' from='tps_pageswebroru_mst_ibmid' to='tps_pageswebroru_mst_ibmid' intersect='true'><link-entity name='contact' from='contactid' to='contactid' alias='contact'><attribute name='contactid'/><attribute name='fullname'/><attribute name='emailaddress1'/><attribute name='parentcustomerid'/><filter>{userFilter}{accountFilter}<condition attribute='tps_shoninsutetasu' operator='eq' value='2'/><condition attribute='statecode' operator='eq' value='0'/></filter></link-entity></link-entity></entity></fetch>";
        var rows = await client.FetchAsync("tps_pageswebroru_mst_ibms", fetch, ct);
        var result = rows.Select(x => new WebRoleUser(String(x, "tps_pageswebroru_mst_ibmid"), String(x, "tps_webrorumei"), String(x, "contact.contactid"), String(x, "contact.fullname"), String(x, "contact.emailaddress1"), NullableString(x, "contact.parentcustomerid"))).Distinct().ToList();
        trace.Step("B040 Web Role membership completed", new { userIds, roleIds, accountId }, new { count = result.Count, result });
        return result;
    }

    public async Task<IReadOnlySet<string>> GetUserTeamIdsAsync(string loginUserId, CancellationToken ct)
    {
        var members = await GetTeamMembersAsync(null, loginUserId, ct);
        return members.Select(x => x.TeamId).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlySet<string>> GetUserWebRoleIdsAsync(string loginUserId, CancellationToken ct)
    {
        var members = await GetWebRoleUsersAsync(loginUserId, null, null, ct);
        return members.Select(x => x.WebRoleId).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ScreenMapping?> GetScreenMappingAsync(ActivityRecord a, CancellationToken ct)
    {
        var filter = $"_tps_gyomumei_value eq {GuidLiteral(a.BusinessId)} and _tps_fuezumei_value eq {GuidLiteral(a.PhaseId)} and _tps_burokubetsumei_value eq {GuidLiteral(a.BlockAliasId)} and _tps_akuteibiteimei_value eq {GuidLiteral(a.ActivityId)} and statecode eq 0";
        using var doc = await client.GetAsync($"{Screens}?$select=tps_name,tps_pejimei,_tps_apurimei_value,_tps_potarumei_value&$filter={Uri.EscapeDataString(filter)}&$top=1", ct);
        var r = Values(doc).FirstOrDefault();
        var result = r.ValueKind == JsonValueKind.Undefined ? null : new ScreenMapping(String(r, "tps_name"), String(r, "tps_pejimei"), NullableString(r, "_tps_apurimei_value"), NullableString(r, "_tps_potarumei_value"));
        trace.Step("Resolve screen mapping", new { a.ActivityId }, result);
        return result;
    }

    /*public async Task<string?> GetApplicationUrlAsync(string applicationId, CancellationToken ct)
    {
        var filter = $"_tps_apurimei_value eq {GuidLiteral(applicationId)} and statecode eq 0";
        using var doc = await client.GetAsync($"{Environments}?$select=tps_apuriid&$filter={Uri.EscapeDataString(filter)}&$top=1", ct);
        var r = Values(doc).FirstOrDefault();
        var result = r.ValueKind == JsonValueKind.Undefined ? null : NullableString(r, "tps_apuriid");
        trace.Step("Resolve application URL", new { applicationId }, new { found = result is not null });
        return result;
    }
    */
    public async Task<string?> GetApplicationUrlAsync(
    string applicationId,
    bool isPortal,
    CancellationToken ct)
    {
        var lookupField = isPortal
            ? "_tps_potarumei_value"
            : "_tps_apurimei_value";

        var filter =
            $"{lookupField} eq {GuidLiteral(applicationId)} and statecode eq 0";

        using var doc = await client.GetAsync(
            $"{Environments}?$select=tps_apuriid&$filter={Uri.EscapeDataString(filter)}&$top=1",
            ct);

        var r = Values(doc).FirstOrDefault();

        var result = r.ValueKind == JsonValueKind.Undefined
            ? null
            : NullableString(r, "tps_apuriid");

        trace.Step(
            "Resolve application URL",
            new { applicationId, isPortal, lookupField },
            new { found = result is not null });

        return result;
    }
    public Task<bool> TransactionExistsAsync(ActivityRecord a, CancellationToken ct) => ExistsAsync(Transactions, a, false, ct);
    public Task<bool> PendingActivityExistsAsync(ActivityRecord a, CancellationToken ct) => ExistsAsync(Activities, a, true, ct);

    private async Task<IReadOnlyList<ActivityRecord>> GetActivitiesAsync(string filter, CancellationToken ct)
    {
        const string select = "tps_akuteibiteikanri_trn_ibmid,_tps_shinseibango_value,_tps_kaifusakisettoid_value,_tps_shoninsha_value,_tps_gaibukaifusha_value,_tps_chimu_value,_tps_webroru_value,tps_sutetasu,statecode,_tps_fuezumei_value,_tps_burokkumei_value,_tps_burokkubetsumei_value,_tps_akuteibiteimei_value,_tps_gyomumeisho_value,tps_saishukaifusha,tps_yakuwarisha";
        using var doc = await client.GetAsync($"{Activities}?$select={select}&$filter={Uri.EscapeDataString(filter)}", ct);
        var result = Values(doc).Select(MapActivity).ToList();
        trace.Step("Get activities completed", new { filter }, new { count = result.Count });
        return result;
    }

    private async Task<bool> ExistsAsync(string entitySet, ActivityRecord a, bool pending, CancellationToken ct)
    {
        var filter = $"_tps_shinseibango_value eq {GuidLiteral(a.RequestId)} and _tps_fuezumei_value eq {GuidLiteral(a.PhaseId)} and _tps_burokkubetsumei_value eq {GuidLiteral(a.BlockAliasId)} and _tps_akuteibiteimei_value eq {GuidLiteral(a.ActivityId)} and statecode eq 0" + (pending ? " and tps_sutetasu eq 1" : "");
        using var doc = await client.GetAsync($"{entitySet}?$select=statecode&$filter={Uri.EscapeDataString(filter)}&$top=1", ct);
        var result = Values(doc).Any();
        trace.Step(pending ? "Check pending activity" : "Check transaction", new { a.RequestId, a.ActivityId }, new { exists = result });
        return result;
    }

    private static ActivityRecord MapActivity(JsonElement r) => new(String(r,"tps_akuteibiteikanri_trn_ibmid"), String(r,"_tps_shinseibango_value"), String(r,"_tps_kaifusakisettoid_value"), NullableString(r,"_tps_shoninsha_value"), NullableString(r,"_tps_gaibukaifusha_value"), NullableString(r,"_tps_chimu_value"), NullableString(r,"_tps_webroru_value"), Int(r,"tps_sutetasu"), Int(r,"statecode"), String(r,"_tps_fuezumei_value"), Formatted(r,"_tps_fuezumei_value"), String(r,"_tps_burokkumei_value"), Formatted(r,"_tps_burokkumei_value"), String(r,"_tps_burokkubetsumei_value"), Formatted(r,"_tps_burokkubetsumei_value"), String(r,"_tps_akuteibiteimei_value"), Formatted(r,"_tps_akuteibiteimei_value"), String(r,"_tps_gyomumeisho_value"), Formatted(r,"_tps_gyomumeisho_value"), Bool(r,"tps_saishukaifusha"), String(r,"tps_yakuwarisha"));
    private static IEnumerable<JsonElement> Values(JsonDocument d) => d.RootElement.TryGetProperty("value", out var v) ? v.EnumerateArray() : [];
    private static string String(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
    private static string? NullableString(JsonElement e, string n) { var x = String(e, n); return x.Length == 0 ? null : x; }
    private static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
    private static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var x) && x));
    private static string Formatted(JsonElement e, string n) => String(e, n + "@OData.Community.Display.V1.FormattedValue");
    private static string GuidValue(string value) => Guid.Parse(value).ToString("D");
    private static string GuidLiteral(string value) => Guid.Parse(value).ToString("D");
    private static List<string> ParseIds(string? ids) => string.IsNullOrWhiteSpace(ids) ? [] : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).Select(x => x.ToString("D")).ToList();
    private static string ValuesXml(IEnumerable<string> ids) => string.Join("", ids.Select(x => $"<value>{SecurityElement.Escape(x)}</value>"));
}
