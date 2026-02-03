using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner
{
    public sealed class RepairPlannerAgent(
        AIProjectClient projectClient,
        CosmosDbService cosmosDb,
        IFaultMappingService faultMapping,
        string modelDeploymentName,
        ILogger<RepairPlannerAgent> logger)
    {
        private const string AgentName = "RepairPlannerAgent";
        private const string AgentInstructions = """
You are a Repair Planner Agent for tire manufacturing equipment.
Generate a repair plan with tasks, timeline, and resource allocation.
Return the response as valid JSON matching the WorkOrder schema.

Output JSON with these fields:
- workOrderNumber, machineId, title, description
- type: "corrective" | "preventive" | "emergency"
- priority: "critical" | "high" | "medium" | "low"
- status, assignedTo (technician id or null), notes
- estimatedDuration: integer (minutes, e.g. 60 not "60 minutes")
- partsUsed: [{ partId, partNumber, quantity }]
- tasks: [{ sequence, title, description, estimatedDurationMinutes (integer), requiredSkills, safetyNotes }]

IMPORTANT: All duration fields must be integers representing minutes (e.g. 90), not strings.

Rules:
- Assign the most qualified available technician
- Include only relevant parts; empty array if none needed
- Tasks must be ordered and actionable
""";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
        {
            try
            {
                var definition = new PromptAgentDefinition(model: modelDeploymentName) { Instructions = AgentInstructions };
                await projectClient.Agents.CreateAgentVersionAsync(AgentName, new AgentVersionCreationOptions(definition), ct).ConfigureAwait(false);
                logger.LogInformation("Ensured agent version for {AgentName}.", AgentName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create or ensure agent version.");
                throw;
            }
        }

        public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
        {
            if (fault == null) throw new ArgumentNullException(nameof(fault));

            try
            {
                // 1. Get required skills and parts from mapping
                var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType).ToList();
                var requiredParts = faultMapping.GetRequiredParts(fault.FaultType).ToList();

                logger.LogInformation("Fault {FaultType} requires {SkillsCount} skills and {PartsCount} parts.", fault.FaultType, requiredSkills.Count, requiredParts.Count);

                // 2. Query technicians and parts from Cosmos DB
                var technicians = await cosmosDb.GetAvailableTechniciansWithSkillsAsync(requiredSkills, ct).ConfigureAwait(false);
                var parts = await cosmosDb.GetPartsInventoryAsync(requiredParts, ct).ConfigureAwait(false);

                // 3. Build prompt and invoke agent
                // Provide context: fault, required skills, parts inventory, technicians list
                var prompt = BuildPrompt(fault, requiredSkills, requiredParts, technicians, parts);

                var agent = projectClient.GetAIAgent(name: AgentName);
                var response = await agent.RunAsync(prompt, thread: null, options: null, cancellationToken: ct).ConfigureAwait(false);
                var text = response.Text ?? string.Empty;

                logger.LogInformation("Agent response: {ResponseSnippet}", text.Length > 200 ? text.Substring(0, 200) + "..." : text);

                // 4. Parse response and apply defaults
                var workOrder = JsonSerializer.Deserialize<WorkOrder>(text, JsonOptions) ?? new WorkOrder();

                // Apply safe defaults
                workOrder.Id ??= Guid.NewGuid().ToString();
                workOrder.WorkOrderNumber ??= $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}";
                workOrder.MachineId ??= fault.MachineId;
                workOrder.Status ??= "open";

                // Ensure estimated durations are integers (JSON options allow reading from strings)
                // Validate assignedTo is in available technicians; otherwise pick top technician
                if (!string.IsNullOrWhiteSpace(workOrder.AssignedTo))
                {
                    var assigned = technicians.FirstOrDefault(t => string.Equals(t.Id, workOrder.AssignedTo, StringComparison.OrdinalIgnoreCase));
                    if (assigned == null)
                    {
                        logger.LogWarning("Assigned technician {Assigned} not found among available technicians; picking best candidate.", workOrder.AssignedTo);
                        workOrder.AssignedTo = technicians.FirstOrDefault()?.Id;
                    }
                }
                else
                {
                    workOrder.AssignedTo = technicians.FirstOrDefault()?.Id;
                }

                // Ensure partsUsed are valid (filter missing parts)
                if (workOrder.PartsUsed != null && workOrder.PartsUsed.Count > 0)
                {
                    var availablePartNumbers = parts.Select(p => p.PartNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    workOrder.PartsUsed = workOrder.PartsUsed.Where(pu => availablePartNumbers.Contains(pu.PartNumber)).ToList();
                }
                else
                {
                    workOrder.PartsUsed = workOrder.PartsUsed ?? new System.Collections.Generic.List<WorkOrderPartUsage>();
                }

                // Tasks ordering and integer durations ensured by JSON parsing; ensure sequence ordering
                if (workOrder.Tasks != null && workOrder.Tasks.Count > 0)
                {
                    workOrder.Tasks = workOrder.Tasks.OrderBy(t => t.Sequence).ToList();
                }
                else
                {
                    workOrder.Tasks = workOrder.Tasks ?? new System.Collections.Generic.List<RepairTask>();
                }

                // 5. Save to Cosmos DB
                var saved = await cosmosDb.CreateWorkOrderAsync(workOrder, ct).ConfigureAwait(false);
                logger.LogInformation("Work order {WO} saved with id {Id}.", saved.WorkOrderNumber, saved.Id);

                return saved;
            }
            catch (JsonException jex)
            {
                logger.LogError(jex, "Failed to parse agent response into WorkOrder JSON.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while planning and creating work order.");
                throw;
            }
        }

        private static string BuildPrompt(DiagnosedFault fault, System.Collections.Generic.List<string> skills, System.Collections.Generic.List<string> parts, System.Collections.Generic.List<Technician> technicians, System.Collections.Generic.List<Part> partsInventory)
        {
            // Keep prompt concise but include relevant context
            var techSummary = technicians.Select(t => $"{{id: \"{t.Id}\", name: \"{t.Name}\", skills: [{string.Join(", ", t.Skills.Select(s => $"\"{s}\""))}]}}").ToList();
            var partSummary = partsInventory.Select(p => $"{{partNumber: \"{p.PartNumber}\", description: \"{p.Description}\", qty: {p.QuantityAvailable}}}").ToList();

            var prompt = $"{AgentInstructions}\n\nFault: {fault.FaultType}\nMachineId: {fault.MachineId}\nDescription: {fault.Description}\n\nRequiredSkills: [{string.Join(", ", skills.Select(s => $"\"{s}\""))}]\nRequiredParts (requested): [{string.Join(", ", parts.Select(p => $"\"{p}\""))}]\n\nAvailableTechnicians: [{string.Join(", ", techSummary)}]\nAvailableParts: [{string.Join(", ", partSummary)}]\n\nReturn only the JSON object representing the WorkOrder as described above.";

            return prompt;
        }
    }
}
