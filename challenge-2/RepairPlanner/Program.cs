using System;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using RepairPlanner.Services;
using RepairPlanner.Models;
using RepairPlanner;

// Simple console runner demonstrating the RepairPlannerAgent workflow.
class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSimpleConsole(options => {
            options.TimestampFormat = "[HH:mm:ss] ";
            options.SingleLine = true;
        }));

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Read configuration from environment variables
        var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? string.Empty;
        var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? string.Empty;
        var cosmosDbName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "factory-db";
        var aiEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT") ?? string.Empty;
        var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(aiEndpoint))
        {
            logger.LogWarning("Environment variable AZURE_AI_PROJECT_ENDPOINT is not set. Agent calls will fail without a valid endpoint.");
        }

        if (string.IsNullOrWhiteSpace(cosmosEndpoint) || string.IsNullOrWhiteSpace(cosmosKey))
        {
            logger.LogWarning("COSMOS_ENDPOINT or COSMOS_KEY not set; Cosmos DB calls will fail without valid credentials.");
        }

        // Register configuration and services
        services.AddSingleton(new CosmosDbOptions { Endpoint = cosmosEndpoint, Key = cosmosKey, DatabaseName = cosmosDbName });
        services.AddSingleton<IFaultMappingService, FaultMappingService>();

        // AIProjectClient uses DefaultAzureCredential (ensure environment or managed identity is configured)
        if (!string.IsNullOrWhiteSpace(aiEndpoint))
        {
            var projectClient = new AIProjectClient(new Uri(aiEndpoint), new DefaultAzureCredential());
            services.AddSingleton(projectClient);
        }

        services.AddSingleton<CosmosDbService>(sp =>
        {
            var opts = sp.GetRequiredService<CosmosDbOptions>();
            var log = sp.GetRequiredService<ILogger<CosmosDbService>>();
            return new CosmosDbService(opts, log);
        });

        // Register the RepairPlannerAgent using the model deployment name
        services.AddSingleton(sp =>
        {
            var projectClient = sp.GetService<AIProjectClient>();
            var cosmos = sp.GetRequiredService<CosmosDbService>();
            var mapping = sp.GetRequiredService<IFaultMappingService>();
            var log = sp.GetRequiredService<ILogger<RepairPlannerAgent>>();
            return new RepairPlannerAgent(projectClient!, cosmos, mapping, modelDeploymentName, log);
        });

        serviceProvider = services.BuildServiceProvider();
        logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var agent = serviceProvider.GetRequiredService<RepairPlannerAgent>();

            // Create a sample fault to demonstrate the workflow
            var sampleFault = new DiagnosedFault
            {
                FaultType = "curing_temperature_excessive",
                MachineId = "machine-001",
                Description = "Curing press temperature exceeding setpoint intermittently",
                DetectedAt = DateTime.UtcNow
            };

            logger.LogInformation("Ensuring agent version exists...");
            await agent.EnsureAgentVersionAsync();

            logger.LogInformation("Planning work order for sample fault...");
            var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);

            logger.LogInformation("Work order created: {WorkOrderNumber} (id: {Id})", workOrder.WorkOrderNumber, workOrder.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running repair planning workflow.");
            return 1;
        }

        return 0;
    }
}
