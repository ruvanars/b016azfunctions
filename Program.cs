using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkflowNavigationFunction.Logging;
using WorkflowNavigationFunction.Repositories;
using WorkflowNavigationFunction.Services;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.Services.AddApplicationInsightsTelemetryWorkerService();
//builder.Services.ConfigureFunctionsApplicationInsights();
builder.Services.AddHttpClient<IDataverseClient, DataverseClient>();
builder.Services.AddSingleton<IBlobLogWriter, BlobLogWriter>();
builder.Services.AddScoped<IExecutionTrace, ExecutionTrace>();
builder.Services.AddScoped<IWorkflowRepository, DataverseWorkflowRepository>();
builder.Services.AddScoped<IWorkflowNavigationService, WorkflowNavigationService>();
await builder.Build().RunAsync();
