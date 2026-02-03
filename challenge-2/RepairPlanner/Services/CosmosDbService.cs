using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services
{
    // Encapsulates Cosmos DB access for Technicians, PartsInventory, and WorkOrders.
    public sealed class CosmosDbService
    {
        private readonly CosmosClient _client;
        private readonly Container _techniciansContainer;
        private readonly Container _partsContainer;
        private readonly Container _workOrdersContainer;
        private readonly ILogger<CosmosDbService> _logger;

        // Note: containers/partition keys (from spec):
        // - Technicians (partition key: "department")
        // - PartsInventory (partition key: "category")
        // - WorkOrders (partition key: "status")
        public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (options == null) throw new ArgumentNullException(nameof(options));

            _client = new CosmosClient(options.Endpoint, options.Key);
            var database = _client.GetDatabase(options.DatabaseName);
            _techniciansContainer = database.GetContainer("Technicians");
            _partsContainer = database.GetContainer("PartsInventory");
            _workOrdersContainer = database.GetContainer("WorkOrders");
        }

        // Returns available technicians who match at least one of the required skills.
        // Results are ordered by number of matching skills (descending).
        public async Task<List<Technician>> GetAvailableTechniciansWithSkillsAsync(
            IReadOnlyList<string> requiredSkills,
            CancellationToken ct = default)
        {
            try
            {
                if (requiredSkills == null || requiredSkills.Count == 0)
                {
                    _logger.LogInformation("No required skills provided; returning empty technician list.");
                    return new List<Technician>();
                }

                var query = new QueryDefinition("SELECT * FROM c WHERE c.available = @available")
                    .WithParameter("@available", true);

                var results = new List<Technician>();
                using var feed = _techniciansContainer.GetItemQueryIterator<Technician>(query);
                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync(ct).ConfigureAwait(false);
                    foreach (var tech in response)
                    {
                        if (tech?.Skills == null) continue;

                        // count overlapping skills
                        var matchCount = tech.Skills.Intersect(requiredSkills, StringComparer.OrdinalIgnoreCase).Count();
                        if (matchCount > 0)
                        {
                            tech.MatchScore = matchCount; // optional convenience property for ordering
                            results.Add(tech);
                        }
                    }
                }

                // order by most matches
                var ordered = results.OrderByDescending(t => t.MatchScore).ThenBy(t => t.Name).ToList();
                _logger.LogInformation("Found {Count} available technicians matching skills.", ordered.Count);
                return ordered;
            }
            catch (CosmosException cex)
            {
                _logger.LogError(cex, "Cosmos DB error while querying technicians.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while querying technicians.");
                throw;
            }
        }

        // Fetch parts matching the provided part numbers. Missing parts are ignored.
        public async Task<List<Part>> GetPartsInventoryAsync(
            IReadOnlyList<string> partNumbers,
            CancellationToken ct = default)
        {
            try
            {
                if (partNumbers == null || partNumbers.Count == 0)
                {
                    _logger.LogInformation("No part numbers provided; returning empty parts list.");
                    return new List<Part>();
                }

                // Build a parameterized IN-clause
                var parameters = new List<string>();
                var queryText = "SELECT * FROM c WHERE c.partNumber IN (";
                for (var i = 0; i < partNumbers.Count; i++)
                {
                    var paramName = $"@p{i}";
                    if (i > 0) queryText += ", ";
                    queryText += paramName;
                    parameters.Add(paramName);
                }
                queryText += ")";

                var qd = new QueryDefinition(queryText);
                for (var i = 0; i < partNumbers.Count; i++) qd.WithParameter(parameters[i], partNumbers[i]);

                var results = new List<Part>();
                using var feed = _partsContainer.GetItemQueryIterator<Part>(qd);
                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync(ct).ConfigureAwait(false);
                    results.AddRange(response.Resource);
                }

                _logger.LogInformation("Fetched {Count} parts for requested part numbers.", results.Count);
                return results;
            }
            catch (CosmosException cex)
            {
                _logger.LogError(cex, "Cosmos DB error while fetching parts.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while fetching parts.");
                throw;
            }
        }

        // Create a work order; uses WorkOrder.Status as the partition key (per spec).
        public async Task<WorkOrder> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
        {
            if (workOrder == null) throw new ArgumentNullException(nameof(workOrder));

            try
            {
                if (string.IsNullOrWhiteSpace(workOrder.Id)) workOrder.Id = Guid.NewGuid().ToString();
                if (string.IsNullOrWhiteSpace(workOrder.Status)) workOrder.Status = "open";

                var response = await _workOrdersContainer.CreateItemAsync(workOrder, new PartitionKey(workOrder.Status), cancellationToken: ct).ConfigureAwait(false);
                _logger.LogInformation("Created work order {WorkOrderId} (RU: {RequestCharge}).", response.Resource.Id, response.RequestCharge);
                return response.Resource;
            }
            catch (CosmosException cex)
            {
                _logger.LogError(cex, "Cosmos DB error while creating work order.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating work order.");
                throw;
            }
        }
    }
}
