using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common.DataForge;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class DataForgeTool(
	IDataForgeReadClient readClient,
	IDataForgeMaintenanceClient maintenanceClient,
	IDataForgeContextService contextService,
	IToolCommandResolver commandResolver) {
	internal const string DataForgeStatusToolName = "dataforge-status";
	internal const string DataForgeFindTablesToolName = "dataforge-find-tables";
	internal const string DataForgeFindLookupsToolName = "dataforge-find-lookups";
	internal const string DataForgeGetRelationsToolName = "dataforge-get-relations";
	internal const string DataForgeGetTableColumnsToolName = "dataforge-get-table-columns";
	internal const string DataForgeContextToolName = "dataforge-context";
	internal const string DataForgeInitializeToolName = "dataforge-initialize";
	internal const string DataForgeUpdateToolName = "dataforge-update";

	private const string SourceName = "clio+dataforge-service";
	private const string PlatformRequirementDescription =
		"Requires Creatio platform version 10.0.0 or later; CrtDataForge is included in supported platform versions.";

	[McpServerTool(Name = DataForgeStatusToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Returns DataForge service health (liveness, readiness, data-structure and lookups readiness) and Creatio maintenance status in a single call via the Creatio DataForgeMaintenanceService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeStatusResponse GetStatus(
		[Description("Parameters: environment-name (required).")]
		[Required]
		DataForgeStatusArgs args) {
		try {
			(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = Execute(args, options => {
				IDataForgeMaintenanceClient maintenance = ResolveService(options, maintenanceClient);
				return maintenance.GetFullStatus();
			});
			return new DataForgeStatusResponse(true, SourceName, string.Empty, [], null, health, status);
		} catch (Exception ex) {
			return new DataForgeStatusResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("status_error", ex.Message),
				null,
				null);
		}
	}

	[McpServerTool(Name = DataForgeFindTablesToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Finds similar tables via the Creatio DataForgeSchemaReadService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeFindTablesResponse FindTables(
		[Description("Parameters: query (required), optional limit, environment-name (required).")]
		[Required]
		DataForgeFindTablesArgs args) {
		try {
			EnsureRequired(args.Query, "query");
			IReadOnlyList<SimilarTableResult> results = Execute(args, options => {
				IDataForgeReadClient client = ResolveService(options, readClient);
				return client.FindSimilarTables(args.Query!, args.Limit);
			});
			return new DataForgeFindTablesResponse(true, SourceName, string.Empty, [], null, results);
		} catch (Exception ex) {
			return new DataForgeFindTablesResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("find_tables_error", ex.Message),
				[]);
		}
	}

	[McpServerTool(Name = DataForgeFindLookupsToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Finds similar lookups via the Creatio DataForgeSchemaReadService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeFindLookupsResponse FindLookups(
		[Description("Parameters: query (required), optional schema-name, optional limit, environment-name (required).")]
		[Required]
		DataForgeFindLookupsArgs args) {
		try {
			EnsureRequired(args.Query, "query");
			IReadOnlyList<SimilarLookupResult> results = Execute(args, options => {
				IDataForgeReadClient client = ResolveService(options, readClient);
				return client.FindSimilarLookups(args.Query!, args.SchemaName, args.Limit);
			});
			return new DataForgeFindLookupsResponse(true, SourceName, string.Empty, [], null, results);
		} catch (Exception ex) {
			return new DataForgeFindLookupsResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("find_lookups_error", ex.Message),
				[]);
		}
	}

	[McpServerTool(Name = DataForgeGetRelationsToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Retrieves relationship paths between two tables via the Creatio DataForgeSchemaReadService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeRelationsResponse GetRelations(
		[Description("Parameters: source-table, target-table (required), optional limit, environment-name (required).")]
		[Required]
		DataForgeGetRelationsArgs args) {
		try {
			EnsureRequired(args.SourceTable, "source-table");
			EnsureRequired(args.TargetTable, "target-table");
			IReadOnlyList<string> results = Execute(args, options => {
				IDataForgeReadClient client = ResolveService(options, readClient);
				return client.GetTableRelationships(args.SourceTable!, args.TargetTable!, args.Limit);
			});
			return new DataForgeRelationsResponse(true, SourceName, string.Empty, [], null, results);
		} catch (Exception ex) {
			return new DataForgeRelationsResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("relations_error", ex.Message),
				[]);
		}
	}

	[McpServerTool(Name = DataForgeGetTableColumnsToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Reads table column details via the Creatio DataForgeSchemaReadService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeColumnsResponse GetTableColumns(
		[Description("Parameters: table-name (required), environment-name (required).")]
		[Required]
		DataForgeGetTableColumnsArgs args) {
		try {
			EnsureRequired(args.TableName, "table-name");
			IReadOnlyList<DataForgeColumnResult> results = Execute(args, options => {
				IDataForgeReadClient client = ResolveService(options, readClient);
				return client.GetTableColumnsDetails(args.TableName!);
			});
			return new DataForgeColumnsResponse(true, SourceName, string.Empty, [], null, results);
		} catch (Exception ex) {
			return new DataForgeColumnsResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("columns_error", ex.Message),
				[]);
		}
	}

	[McpServerTool(Name = DataForgeContextToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Aggregates tables, lookups, relations, columns, and service status via the Creatio DataForge proxy endpoints. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeContextResponse GetContext(
		[Description("Parameters: requirement-summary/candidate-terms/lookup-hints/relation-pairs, environment-name (required).")]
		[Required]
		DataForgeContextArgs args) {
		try {
			DataForgeContextAggregationResult contextResult = Execute(args, options => {
				IDataForgeContextService resolvedContextService = ResolveService(options, contextService);
				return resolvedContextService.GetContext(
					new DataForgeContextRequest(
						args.RequirementSummary,
						args.CandidateTerms,
						args.LookupHints,
						args.RelationPairs?.Select(pair => new DataForgeRelationPair(pair.SourceTable, pair.TargetTable)).ToList()));
			});
			return new DataForgeContextResponse(
				true,
				SourceName,
				contextResult.CorrelationId,
				contextResult.Warnings,
				null,
				contextResult.Health,
				contextResult.Status,
				contextResult.SimilarTables,
				contextResult.SimilarLookups,
				contextResult.Relations,
				contextResult.Columns,
				contextResult.Coverage);
		} catch (Exception ex) {
			return new DataForgeContextResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("context_error", ex.Message),
				null,
				null,
				[],
				[],
				new Dictionary<string, IReadOnlyList<string>>(),
				new Dictionary<string, IReadOnlyList<DataForgeColumnResult>>(),
				new DataForgeCoverage(false, false, false, false, false));
		}
	}

	[McpServerTool(Name = DataForgeInitializeToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Schedules DataForge initialize jobs via the Creatio DataForgeMaintenanceService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeMaintenanceResponse Initialize(
		[Description("Parameters: environment-name (required).")]
		[Required]
		DataForgeMaintenanceArgs args) {
		try {
			DataForgeMaintenanceStatusResult result = Execute(args, options => {
				IDataForgeMaintenanceClient maintenance = ResolveService(options, maintenanceClient);
				return maintenance.Initialize();
			});
			return new DataForgeMaintenanceResponse(true, SourceName, string.Empty, [], null, result);
		} catch (Exception ex) {
			return new DataForgeMaintenanceResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("initialize_error", ex.Message),
				new DataForgeMaintenanceStatusResult(false, "Failed", ex.Message));
		}
	}

	[McpServerTool(Name = DataForgeUpdateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Schedules DataForge update jobs via the Creatio DataForgeMaintenanceService proxy. " + PlatformRequirementDescription + " The Creatio user must have the CanReadDataStructureColumnDetails operation permission.")]
	public DataForgeMaintenanceResponse Update(
		[Description("Parameters: environment-name (required).")]
		[Required]
		DataForgeMaintenanceArgs args) {
		try {
			DataForgeMaintenanceStatusResult result = Execute(args, options => {
				IDataForgeMaintenanceClient maintenance = ResolveService(options, maintenanceClient);
				return maintenance.Update();
			});
			return new DataForgeMaintenanceResponse(true, SourceName, string.Empty, [], null, result);
		} catch (Exception ex) {
			return new DataForgeMaintenanceResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("update_error", ex.Message),
				new DataForgeMaintenanceStatusResult(false, "Failed", ex.Message));
		}
	}

	private T Execute<T>(DataForgeConnectionArgsBase args, Func<DataForgeTargetOptions, T> action) {
		EnsureRequired(args.EnvironmentName, "environment-name");
		DataForgeTargetOptions options = CreateTargetOptions(args);
		return action(options);
	}

	private static void EnsureRequired(string? value, string parameterName) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw new ArgumentException($"{parameterName} is required.");
		}
	}

	private TService ResolveService<TService>(DataForgeTargetOptions options, TService fallback) where TService : class {
		return commandResolver.Resolve<TService>(options);
	}

	private static DataForgeTargetOptions CreateTargetOptions(DataForgeConnectionArgsBase args) {
		return new DataForgeTargetOptions {
			Environment = args.EnvironmentName
		};
	}
}

public sealed record DataForgeRelationPairArgs(
	[property: JsonPropertyName("source-table")] string SourceTable,
	[property: JsonPropertyName("target-table")] string TargetTable
);

/// <summary>
/// Provides the shared Data Forge connection payload used by all Data Forge MCP tools.
/// All DataForge calls are proxied through Creatio; only a registered environment name is needed.
/// </summary>
public abstract record DataForgeConnectionArgsBase {
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name.")]
	public string? EnvironmentName { get; init; }
}

public sealed record DataForgeStatusArgs : DataForgeConnectionArgsBase;

public sealed record DataForgeMaintenanceArgs : DataForgeConnectionArgsBase;

public sealed record DataForgeFindTablesArgs(
	[property: JsonPropertyName("query")] string? Query,
	[property: JsonPropertyName("limit")] int? Limit = null) : DataForgeConnectionArgsBase;

public sealed record DataForgeFindLookupsArgs(
	[property: JsonPropertyName("query")] string? Query,
	[property: JsonPropertyName("schema-name")] string? SchemaName = null,
	[property: JsonPropertyName("limit")] int? Limit = null) : DataForgeConnectionArgsBase;

public sealed record DataForgeGetRelationsArgs(
	[property: JsonPropertyName("source-table")] string? SourceTable,
	[property: JsonPropertyName("target-table")] string? TargetTable,
	[property: JsonPropertyName("limit")] int? Limit = null) : DataForgeConnectionArgsBase;

public sealed record DataForgeGetTableColumnsArgs(
	[property: JsonPropertyName("table-name")] string? TableName) : DataForgeConnectionArgsBase;

public sealed record DataForgeContextArgs(
	[property: JsonPropertyName("requirement-summary")] string? RequirementSummary,
	[property: JsonPropertyName("candidate-terms")] IReadOnlyList<string>? CandidateTerms = null,
	[property: JsonPropertyName("lookup-hints")] IReadOnlyList<string>? LookupHints = null,
	[property: JsonPropertyName("relation-pairs")] IReadOnlyList<DataForgeRelationPairArgs>? RelationPairs = null) : DataForgeConnectionArgsBase;
