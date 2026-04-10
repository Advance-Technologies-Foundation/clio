using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class DataForgeTool(
	IDataForgeClient dataForgeClient,
	IDataForgeMaintenanceClient maintenanceClient,
	IRuntimeEntitySchemaReader runtimeEntitySchemaReader,
	IDataForgeContextService contextService,
	IToolCommandResolver commandResolver) {
	internal const string DataForgeHealthToolName = "dataforge-health";
	internal const string DataForgeStatusToolName = "dataforge-status";
	internal const string DataForgeFindTablesToolName = "dataforge-find-tables";
	internal const string DataForgeFindLookupsToolName = "dataforge-find-lookups";
	internal const string DataForgeGetRelationsToolName = "dataforge-get-relations";
	internal const string DataForgeGetTableColumnsToolName = "dataforge-get-table-columns";
	internal const string DataForgeContextToolName = "dataforge-context";
	internal const string DataForgeInitializeToolName = "dataforge-initialize";
	internal const string DataForgeUpdateToolName = "dataforge-update";

	private const string DefaultDataForgeScope = "use_enrichment";
	private const string SourceName = "clio+dataforge-service";

	[McpServerTool(Name = DataForgeHealthToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Checks direct health endpoints of dataforge-service.")]
	public async Task<DataForgeHealthResponse> GetHealth(
		[Description("Parameters: environment-name or explicit connection args.")]
		[Required]
		DataForgeHealthArgs args) {
		try {
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeClient client = ResolveService(options, dataForgeClient);
			DataForgeHealthResult health = await client.CheckHealthAsync(BuildConfigRequest(options));
			return new DataForgeHealthResponse(true, SourceName, health.CorrelationId, [], null, health);
		} catch (Exception ex) {
			return new DataForgeHealthResponse(
				false,
				SourceName,
				string.Empty,
				[],
				new DataForgeErrorResult("health_error", ex.Message),
				null);
		}
	}

	[McpServerTool(Name = DataForgeStatusToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Combines direct dataforge-service health with Creatio DataForge maintenance status.")]
	public async Task<DataForgeStatusResponse> GetStatus(
		[Description("Parameters: environment-name or explicit connection args.")]
		[Required]
		DataForgeStatusArgs args) {
		try {
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeClient client = ResolveService(options, dataForgeClient);
			IDataForgeMaintenanceClient maintenance = ResolveService(options, maintenanceClient);
			DataForgeHealthResult health = await client.CheckHealthAsync(BuildConfigRequest(options));
			DataForgeMaintenanceStatusResult status = maintenance.GetStatus();
			return new DataForgeStatusResponse(true, SourceName, health.CorrelationId, [], null, health, status);
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
	[Description("Finds similar tables through dataforge-service similarDetails endpoint.")]
	public async Task<DataForgeFindTablesResponse> FindTables(
		[Description("Parameters: query (required), optional limit, plus target connection args.")]
		[Required]
		DataForgeFindTablesArgs args) {
		try {
			EnsureRequired(args.Query, "query");
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeClient client = ResolveService(options, dataForgeClient);
			IReadOnlyList<SimilarTableResult> results = await client.FindSimilarTablesAsync(
				args.Query!,
				args.Limit,
				BuildConfigRequest(options));
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
	[Description("Finds similar lookups through dataforge-service.")]
	public async Task<DataForgeFindLookupsResponse> FindLookups(
		[Description("Parameters: query (required), optional schema-name, optional limit, plus target connection args.")]
		[Required]
		DataForgeFindLookupsArgs args) {
		try {
			EnsureRequired(args.Query, "query");
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeClient client = ResolveService(options, dataForgeClient);
			IReadOnlyList<SimilarLookupResult> results = await client.FindSimilarLookupsAsync(
				args.Query!,
				args.SchemaName,
				args.Limit,
				BuildConfigRequest(options));
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
	[Description("Retrieves cypher relation paths between two tables through dataforge-service.")]
	public async Task<DataForgeRelationsResponse> GetRelations(
		[Description("Parameters: source-table, target-table (required), optional limit, plus target connection args.")]
		[Required]
		DataForgeGetRelationsArgs args) {
		try {
			EnsureRequired(args.SourceTable, "source-table");
			EnsureRequired(args.TargetTable, "target-table");
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeClient client = ResolveService(options, dataForgeClient);
			IReadOnlyList<string> results = await client.GetTableRelationshipsAsync(
				args.SourceTable!,
				args.TargetTable!,
				args.Limit,
				BuildConfigRequest(options));
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
	[Description("Reads runtime entity columns from Creatio without requiring package context.")]
	public DataForgeColumnsResponse GetTableColumns(
		[Description("Parameters: table-name (required), plus target connection args.")]
		[Required]
		DataForgeGetTableColumnsArgs args) {
		try {
			EnsureRequired(args.TableName, "table-name");
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IRuntimeEntitySchemaReader reader = ResolveService(options, runtimeEntitySchemaReader);
			RuntimeEntitySchemaResult runtimeSchema = reader.GetByName(args.TableName!);
			IReadOnlyList<DataForgeColumnResult> results = DataForgeRuntimeSchemaMapper.MapColumns(runtimeSchema);
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
	[Description("Aggregates tables, lookups, relations, columns, and service status for DataForge-assisted context reads.")]
	public async Task<DataForgeContextResponse> GetContext(
		[Description("Parameters: requirement-summary/candidate-terms/lookup-hints/relation-pairs plus target connection args.")]
		[Required]
		DataForgeContextArgs args) {
		try {
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeContextService resolvedContextService = ResolveService(options, contextService);
			DataForgeContextAggregationResult contextResult = await resolvedContextService.GetContextAsync(
				new DataForgeContextRequest(
					args.RequirementSummary,
					args.CandidateTerms,
					args.LookupHints,
					args.RelationPairs?.Select(pair => new DataForgeRelationPair(pair.SourceTable, pair.TargetTable)).ToList()),
				BuildConfigRequest(options));
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
	[Description("Schedules DataForge initialize jobs through Creatio maintenance service.")]
	public DataForgeMaintenanceResponse Initialize(
		[Description("Parameters: environment-name or explicit connection args.")]
		[Required]
		DataForgeMaintenanceArgs args) {
		try {
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeMaintenanceClient maintenance = ResolveService(options, maintenanceClient);
			DataForgeMaintenanceStatusResult result = maintenance.Initialize();
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
	[Description("Schedules DataForge update jobs through Creatio maintenance service.")]
	public DataForgeMaintenanceResponse Update(
		[Description("Parameters: environment-name or explicit connection args.")]
		[Required]
		DataForgeMaintenanceArgs args) {
		try {
			DataForgeTargetOptions options = CreateTargetOptions(args);
			IDataForgeMaintenanceClient maintenance = ResolveService(options, maintenanceClient);
			DataForgeMaintenanceStatusResult result = maintenance.Update();
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

	private static void EnsureRequired(string? value, string parameterName) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw new ArgumentException($"{parameterName} is required.");
		}
	}

	private TService ResolveService<TService>(DataForgeTargetOptions options, TService fallback) where TService : class {
		if (HasExplicitTarget(options)) {
			return commandResolver.Resolve<TService>(options);
		}

		return fallback;
	}

	private static bool HasExplicitTarget(DataForgeTargetOptions options) {
		return !string.IsNullOrWhiteSpace(options.Environment)
			|| !string.IsNullOrWhiteSpace(options.Uri)
			|| !string.IsNullOrWhiteSpace(options.Login)
			|| !string.IsNullOrWhiteSpace(options.Password)
			|| !string.IsNullOrWhiteSpace(options.ClientId)
			|| !string.IsNullOrWhiteSpace(options.ClientSecret)
			|| !string.IsNullOrWhiteSpace(options.AuthAppUri);
	}

	private static DataForgeConfigRequest BuildConfigRequest(DataForgeTargetOptions options) {
		return new DataForgeConfigRequest {
			AuthAppUri = options.AuthAppUri,
			ClientId = options.ClientId,
			ClientSecret = options.ClientSecret,
			AllowSysSettingsAuthFallback = options.AllowSysSettingsAuthFallback,
			Scope = options.Scope
		};
	}

	private static DataForgeTargetOptions CreateTargetOptions(DataForgeConnectionArgsBase args) {
		return new DataForgeTargetOptions {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			ClientId = args.ClientId,
			ClientSecret = args.ClientSecret,
			AuthAppUri = args.AuthAppUri,
			AllowSysSettingsAuthFallback = args.AllowSysSettingsAuthFallback,
			Scope = string.IsNullOrWhiteSpace(args.Scope) ? DefaultDataForgeScope : args.Scope.Trim()
		};
	}
}

public sealed record DataForgeRelationPairArgs(
	[property: JsonPropertyName("source-table")] string SourceTable,
	[property: JsonPropertyName("target-table")] string TargetTable
);

/// <summary>
/// Provides the shared Data Forge connection payload used by all Data Forge MCP tools.
/// </summary>
public abstract record DataForgeConnectionArgsBase {
	[JsonPropertyName("environment-name")]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	public string? Password { get; init; }

	[JsonPropertyName("client-id")]
	public string? ClientId { get; init; }

	[JsonPropertyName("client-secret")]
	public string? ClientSecret { get; init; }

	[JsonPropertyName("auth-app-uri")]
	public string? AuthAppUri { get; init; }

	[JsonPropertyName("allow-syssettings-auth-fallback")]
	public bool AllowSysSettingsAuthFallback { get; init; } = true;

	[JsonPropertyName("scope")]
	public string? Scope { get; init; }
}

public sealed record DataForgeHealthArgs : DataForgeConnectionArgsBase;

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
