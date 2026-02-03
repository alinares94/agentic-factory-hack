using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models
{
    public sealed class DiagnosedFault
    {
        [JsonPropertyName("faultType")]
        [JsonProperty("faultType")]
        public string FaultType { get; set; } = string.Empty;

        [JsonPropertyName("machineId")]
        [JsonProperty("machineId")]
        public string MachineId { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("detectedAt")]
        [JsonProperty("detectedAt")]
        public DateTime DetectedAt { get; set; }
    }

    public sealed class Technician
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("department")]
        [JsonProperty("department")]
        public string Department { get; set; } = string.Empty;

        [JsonPropertyName("skills")]
        [JsonProperty("skills")]
        public List<string> Skills { get; set; } = new List<string>();

        [JsonPropertyName("available")]
        [JsonProperty("available")]
        public bool Available { get; set; } = true;

        // Non-persisted convenience property for ranking
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public int MatchScore { get; set; }
    }

    public sealed class Part
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("partNumber")]
        [JsonProperty("partNumber")]
        public string PartNumber { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("quantityAvailable")]
        [JsonProperty("quantityAvailable")]
        public int QuantityAvailable { get; set; }
    }

    public sealed class WorkOrderPartUsage
    {
        [JsonPropertyName("partId")]
        [JsonProperty("partId")]
        public string PartId { get; set; } = string.Empty;

        [JsonPropertyName("partNumber")]
        [JsonProperty("partNumber")]
        public string PartNumber { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        [JsonProperty("quantity")]
        public int Quantity { get; set; }
    }

    public sealed class RepairTask
    {
        [JsonPropertyName("sequence")]
        [JsonProperty("sequence")]
        public int Sequence { get; set; }

        [JsonPropertyName("title")]
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("estimatedDurationMinutes")]
        [JsonProperty("estimatedDurationMinutes")]
        public int EstimatedDurationMinutes { get; set; }

        [JsonPropertyName("requiredSkills")]
        [JsonProperty("requiredSkills")]
        public List<string> RequiredSkills { get; set; } = new List<string>();

        [JsonPropertyName("safetyNotes")]
        [JsonProperty("safetyNotes")]
        public string? SafetyNotes { get; set; }
    }

    public sealed class WorkOrder
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("workOrderNumber")]
        [JsonProperty("workOrderNumber")]
        public string WorkOrderNumber { get; set; } = string.Empty;

        [JsonPropertyName("machineId")]
        [JsonProperty("machineId")]
        public string MachineId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        [JsonProperty("type")]
        public string Type { get; set; } = "corrective";

        [JsonPropertyName("priority")]
        [JsonProperty("priority")]
        public string Priority { get; set; } = "medium";

        [JsonPropertyName("status")]
        [JsonProperty("status")]
        public string Status { get; set; } = "open";

        [JsonPropertyName("assignedTo")]
        [JsonProperty("assignedTo")]
        public string? AssignedTo { get; set; }

        [JsonPropertyName("notes")]
        [JsonProperty("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("estimatedDuration")]
        [JsonProperty("estimatedDuration")]
        public int EstimatedDuration { get; set; }

        [JsonPropertyName("partsUsed")]
        [JsonProperty("partsUsed")]
        public List<WorkOrderPartUsage> PartsUsed { get; set; } = new List<WorkOrderPartUsage>();

        [JsonPropertyName("tasks")]
        [JsonProperty("tasks")]
        public List<RepairTask> Tasks { get; set; } = new List<RepairTask>();
    }
}
