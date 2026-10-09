<<<<<<< HEAD
# b016azfunctions
=======
# WorkflowNavigationFunction updated solution

This solution is based on the individually uploaded original B016 source files. It preserves their project name, namespaces, classes, repository pattern, Application Insights setup and HTTP route.

## Incorporated logic
- B016 workflow navigation and transaction validation.
- B029 team membership: team ID takes precedence over user ID; non-default owner teams; enabled system users; duplicate removal.
- B040 custom Web Role membership: role ID takes precedence; optional account filtering; active roles; approved active contacts; duplicate removal.

B016 invokes B029 logic through `GetUserTeamIdsAsync` and B040 logic through `GetUserWebRoleIdsAsync` in `DataverseWorkflowRepository`.

## Logging
Each request writes action, input, output, success and exception details to the Functions console. One text log per invocation is uploaded to:

`connectdataversesample01/filecontainer-logs/B016-logs/yyyy/MM/dd/`

Use `Logging__StorageConnectionString` or leave it empty to use `DefaultAzureCredential`. The Azure identity needs permission to upload blobs.

## Local run
1. Copy `local.settings.json.example` to `local.settings.json`.
2. Add a newly issued Dataverse client secret and Blob Storage configuration.
3. Run `dotnet restore`, `dotnet build`, and `func start`.
4. Use `test.http` in VS Code.

## Security
The attached local settings contained a client secret. It is not included in this solution. Revoke/rotate that secret before running the application.

## Environment verification
Confirm custom Dataverse entity-set names, lookup logical names, intersect relationship name, and Choice values against target metadata.
>>>>>>> origin/master
