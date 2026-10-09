namespace WorkflowNavigationFunction.Models;

public sealed record WorkflowNavigationRequest(string RequestId, string LoginUserId, string RoutingSetId, string? ApplicationIdOrUrl, bool IsPortal);
public sealed record WorkflowNavigationResponse(string Url, string BlockName, string BlockAlias, string ActivityName, string ScreenName, string EditMode, string PhaseName, string Message, string IsApprover, string BusinessName, string RoleHolder);
public sealed record TeamMember(string MemberId, string TeamId, string MemberName, string MemberMail, string TeamName);
public sealed record WebRoleUser(string WebRoleId, string WebRoleName, string ContactId, string FullName, string EmailAddress, string? AccountId);
public sealed record CaseRecord(string Id, string CaseStatus, int StateCode);
public sealed record ActivityRecord(string Id, string RequestId, string RoutingSetId, string? ApproverId, string? ExternalApproverId, string? TeamId, string? WebRoleId, int Status, int StateCode, string PhaseId, string PhaseName, string BlockId, string BlockName, string BlockAliasId, string BlockAlias, string ActivityId, string ActivityName, string BusinessId, string BusinessName, bool IsFinalApprover, string RoleHolder);
public sealed record ScreenMapping(string AppScreenName, string PortalPageName, string? AppId, string? PortalId);
