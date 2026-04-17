using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that batches multiple schema operations (create lookups, create entities,
/// seed data, update entities) into a single call, reducing MCP round-trips,
/// lock acquisitions, and sleep overhead.
/// </summary>
[McpServerToolType]
public sealed class SchemaSyncTool(
	IToolCommandResolver commandResolver,
	ILogger logger,
	ISchemaEnrichmentService? enrichmentService = null) {

	internal const string ToolName = "sync-schemas";
	private const string CreateLookupOperationName = "create-lookup";
	private const string CreateEntityOperationName = "create-entity";
	private const string UpdateEntityOperationName = "update-entity";
	private const string SeedDataOperationName = "seed-data";

	/// <summary>
	/// Executes a batch of schema operations in a single MCP call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Executes a batch of schema operations in a single call: " +
		"create lookups, create entities, seed data, update entities. " +
		"Reduces MCP round-trips and lock overhead compared to individual tool calls. " +
		"Stops on first failure because subsequent operations may depend on earlier ones.")]
	public async Task<SchemaSyncResponse> SchemaSync(
		[Description("Parameters: environment-name, package-name (required); operations array (required)")]
		[Required] SchemaSyncArgs args) {
		ApplicationDataForgeResult? dataForge = enrichmentService is not null
			? await enrichmentService.EnrichAsync(
				args.EnvironmentName,
				CollectCandidateTerms(args),
				CollectLookupHints(args))
			: null;
		var results = new List<SchemaSyncOperationResult>();
		lock (McpToolExecutionLock.SyncRoot) {
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				foreach ((SchemaSyncOperation op, int index) in args.Operations.Select((operation, operationIndex) => (operation, operationIndex))) {
					logger.ClearMessages();
					if (TryValidateSeedRows(op, index, out SchemaSyncOperationResult? seedValidationFailure)) {
						results.Add(seedValidationFailure);
						break;
					}
					SchemaSyncOperationResult result = ExecuteOperation(op, args, index);
					results.Add(result);
					if (!result.Success) {
						break;
					}
					if (op.SeedRows?.Any() == true) {
						logger.ClearMessages();
						SchemaSyncOperationResult seedResult = ExecuteSeedData(op, args);
						results.Add(seedResult);
						if (!seedResult.Success) {
							break;
						}
					}
				}
			} finally {
				logger.ClearMessages();
				logger.PreserveMessages = previousPreserveMessages;
			}
		}
		return new SchemaSyncResponse {
			Success = results.Count > 0 && results.All(r => r.Success),
			Results = results,
			DataForge = dataForge
		};
	}

	private static IReadOnlyList<string> CollectCandidateTerms(SchemaSyncArgs args) {
		return args.Operations
			.Where(op => !string.IsNullOrWhiteSpace(op.SchemaName))
			.Select(op => op.SchemaName.Trim())
			.Concat(args.Operations
				.SelectMany(op => (IEnumerable<string>?)op.TitleLocalizations?.Values ?? [])
				.Where(title => !string.IsNullOrWhiteSpace(title))
				.Select(title => title.Trim()))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IReadOnlyList<string> CollectLookupHints(SchemaSyncArgs args) {
		return args.Operations
			.Where(op => string.Equals(op.Type, "create-lookup", StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(op.SchemaName))
			.Select(op => op.SchemaName.Trim())
			.Concat(args.Operations
				.Where(op => string.Equals(op.Type, "create-lookup", StringComparison.Ordinal))
				.SelectMany(op => (IEnumerable<string>?)op.TitleLocalizations?.Values ?? [])
				.Where(title => !string.IsNullOrWhiteSpace(title))
				.Select(title => title.Trim()))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private SchemaSyncOperationResult ExecuteOperation(SchemaSyncOperation op, SchemaSyncArgs args, int operationIndex) {
		return op.Type switch {
			CreateLookupOperationName => ExecuteCreateSchema(op, args, "BaseLookup", false, CreateLookupOperationName),
			CreateEntityOperationName => ExecuteCreateSchema(op, args, op.ParentSchemaName, op.ExtendParent, CreateEntityOperationName),
			UpdateEntityOperationName => ExecuteUpdateEntity(op, args),
			_ => new SchemaSyncOperationResult {
				Type = GetReportedOperationType(op),
				SchemaName = op.SchemaName,
				Success = false,
				Error = BuildUnknownOperationError(op, operationIndex)
			}
		};
	}

	private static bool TryValidateSeedRows(
		SchemaSyncOperation op,
		int operationIndex,
		out SchemaSyncOperationResult? validationFailure) {
		validationFailure = null;
		if (op.SeedRows?.Any() != true) {
			return false;
		}

		if (op.SeedRows.Any(row => row is null || row.Values is null)) {
			validationFailure = new SchemaSyncOperationResult {
				Type = SeedDataOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = $"sync-schemas operations[{operationIndex}] seed-rows validation failed: each row must contain a non-null 'values' object."
			};
			return true;
		}

		return false;
	}

	private SchemaSyncOperationResult ExecuteCreateSchema(
		SchemaSyncOperation op, SchemaSyncArgs args,
		string parentSchemaName, bool extendParent, string operationName) {
		try {
			string context = $"{operationName} operation for schema '{op.SchemaName}'";
			IReadOnlyDictionary<string, string> titleLocalizations = EntitySchemaLocalizationContract.RequireTitleLocalizations(
				op.TitleLocalizations,
				op.LegacyTitle,
				context);
			if (string.Equals(operationName, CreateLookupOperationName, StringComparison.Ordinal)) {
				ModelingGuardrails.EnsureLookupColumnsDoNotShadowInheritedBaseLookupColumns(op.Columns);
			}
			CreateEntitySchemaOptions options = CreateEntitySchemaTool.CreateOptions(
				new CreateLookupArgs(
					args.PackageName, op.SchemaName,
					new Dictionary<string, string>(titleLocalizations, StringComparer.OrdinalIgnoreCase), args.EnvironmentName,
					op.Columns),
				parentSchemaName, extendParent);
			CreateEntitySchemaCommand command = commandResolver.Resolve<CreateEntitySchemaCommand>(options);
			int exitCode = command.Execute(options);
			if (exitCode == 0 && string.Equals(operationName, CreateLookupOperationName, StringComparison.Ordinal)) {
				ILookupRegistrationService registrationService =
					commandResolver.Resolve<ILookupRegistrationService>(options);
				registrationService.EnsureLookupRegistration(
					args.PackageName,
					op.SchemaName,
					EntitySchemaLocalizationContract.GetDefaultTitle(titleLocalizations, context));
			}
			IReadOnlyList<LogMessage> messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
			SchemaSyncCollisionInfo? collisionInfo = exitCode != 0
				? TryGetCollisionInfo(op.SchemaName, args)
				: null;
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = op.SchemaName,
				Success = exitCode == 0,
				Messages = messages,
				Error = BuildOperationError(operationName, exitCode, messages),
				CollisionInfo = collisionInfo
			};
		} catch (Exception ex) {
			SchemaSyncCollisionInfo? collisionInfo = TryGetCollisionInfo(op.SchemaName, args);
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = ex.Message,
				Messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)],
				CollisionInfo = collisionInfo
			};
		}
	}

	private SchemaSyncCollisionInfo? TryGetCollisionInfo(string schemaName, SchemaSyncArgs args) {
		try {
			FindEntitySchemaOptions findOptions = new() {
				Environment = args.EnvironmentName,
				SchemaName = schemaName
			};
			FindEntitySchemaCommand findCommand = commandResolver.Resolve<FindEntitySchemaCommand>(findOptions);
			IReadOnlyList<EntitySchemaSearchResult> results = findCommand.FindSchemas(findOptions);
			EntitySchemaSearchResult? existing = results.FirstOrDefault();
			if (existing is null) {
				return null;
			}
			string hint = string.Equals(existing.PackageName, args.PackageName, StringComparison.OrdinalIgnoreCase)
				? "Schema already exists in the target package. Use update-entity to add columns or proceed to seed-data without recreating."
				: $"Schema already exists in package '{existing.PackageName}'. Reuse it by referencing it without creation, or call delete-schema first to remove the stale version before recreating.";
			return new SchemaSyncCollisionInfo(existing.PackageName, hint);
		} catch {
			return null;
		}
	}

	private SchemaSyncOperationResult ExecuteUpdateEntity(SchemaSyncOperation op, SchemaSyncArgs args) {
		try {
				if (op.UpdateOperations?.Any() != true) {
					return new SchemaSyncOperationResult {
						Type = UpdateEntityOperationName,
					SchemaName = op.SchemaName,
					Success = false, Error = "update-entity requires at least one operation in update-operations"
				};
			}
			UpdateEntitySchemaOptions options = new() {
				Environment = args.EnvironmentName,
				Package = args.PackageName,
				SchemaName = op.SchemaName,
				Operations = UpdateEntitySchemaTool.SerializeOperations(op.UpdateOperations, op.SchemaName)
			};
			UpdateEntitySchemaCommand command = commandResolver.Resolve<UpdateEntitySchemaCommand>(options);
			int exitCode = command.Execute(options);
			IReadOnlyList<LogMessage> messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
				return new SchemaSyncOperationResult {
					Type = UpdateEntityOperationName,
				SchemaName = op.SchemaName,
				Success = exitCode == 0,
				Messages = messages,
					Error = BuildOperationError(UpdateEntityOperationName, exitCode, messages)
				};
			} catch (Exception ex) {
				return new SchemaSyncOperationResult {
					Type = UpdateEntityOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = ex.Message,
				Messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)]
			};
		}
	}

	private SchemaSyncOperationResult ExecuteSeedData(SchemaSyncOperation op, SchemaSyncArgs args) {
		try {
			string rowsJson = JsonSerializer.Serialize(op.SeedRows);
			CreateDataBindingDbOptions options = new() {
				Environment = args.EnvironmentName,
				PackageName = args.PackageName,
				SchemaName = op.SchemaName,
				RowsJson = rowsJson
			};
			CreateDataBindingDbCommand command = commandResolver.Resolve<CreateDataBindingDbCommand>(options);
			int exitCode = command.Execute(options);
			IReadOnlyList<LogMessage> messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
			return new SchemaSyncOperationResult {
				Type = SeedDataOperationName,
				SchemaName = op.SchemaName,
				Success = exitCode == 0,
				Messages = messages,
				Error = BuildOperationError(SeedDataOperationName, exitCode, messages)
			};
		} catch (Exception ex) {
			return new SchemaSyncOperationResult {
				Type = SeedDataOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = ex.Message,
				Messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)]
			};
		}
	}

	private static string GetReportedOperationType(SchemaSyncOperation op) {
		if (!string.IsNullOrWhiteSpace(op.Type)) {
			return op.Type;
		}
		if (op.ExtensionData?.TryGetValue("operation", out JsonElement legacyOperation) == true &&
			legacyOperation.ValueKind == JsonValueKind.String) {
			return legacyOperation.GetString() ?? string.Empty;
		}
		return string.Empty;
	}

	private static string BuildUnknownOperationError(SchemaSyncOperation op, int operationIndex) {
		if (string.IsNullOrWhiteSpace(op.Type)) {
			if (op.ExtensionData?.TryGetValue("operation", out JsonElement legacyOperation) == true &&
				legacyOperation.ValueKind == JsonValueKind.String) {
				string legacyOperationName = legacyOperation.GetString() ?? string.Empty;
				return $"sync-schemas operations[{operationIndex}] uses unsupported request field 'operation'. Send 'type': '{legacyOperationName}' instead.";
			}
			return $"sync-schemas operations[{operationIndex}] is missing required field 'type'.";
		}

		string supportedTypes = string.Join(", ", CreateLookupOperationName, CreateEntityOperationName, UpdateEntityOperationName);
		return $"sync-schemas operations[{operationIndex}].type '{op.Type}' is invalid. Supported values: {supportedTypes}.";
	}

	private static string? BuildOperationError(string operationName, int exitCode, IReadOnlyList<LogMessage> messages) {
		if (exitCode == 0) {
			return null;
		}

		string fallback = $"{operationName} failed with exit code {exitCode}";
		string? detailedError = messages
			.LastOrDefault(message => message.LogDecoratorType == LogDecoratorType.Error)
			?.Value
			?.ToString()
			?.Trim();

		if (string.IsNullOrWhiteSpace(detailedError)) {
			return fallback;
		}

		return $"{fallback}: {detailedError}";
	}
}

/// <summary>
/// Top-level arguments for the <c>sync-schemas</c> MCP tool.
/// </summary>
public sealed record SchemaSyncArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("operations")]
	[property: Description("Ordered list of schema operations to execute")]
	[property: Required]
	IEnumerable<SchemaSyncOperation> Operations
);

/// <summary>
/// A single schema operation within a <c>sync-schemas</c> batch.
/// </summary>
public sealed record SchemaSyncOperation(
	[property: JsonPropertyName("type")]
	[property: Description("Operation type: create-lookup, create-entity, or update-entity")]
	[property: Required]
	string Type,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Target entity schema name")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Schema title/caption localizations for create operations. Must include en-US.")]
	Dictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("parent-schema-name")]
	[property: Description("Parent schema name (for create-entity)")]
	string? ParentSchemaName = null,

	[property: JsonPropertyName("extend-parent")]
	[property: Description("Create a replacement schema (for create-entity)")]
	bool ExtendParent = false,

	[property: JsonPropertyName("columns")]
	[property: Description("Initial columns for create-lookup or create-entity operations")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null,

	[property: JsonPropertyName("update-operations")]
	[property: Description("Column mutation operations for update-entity")]
	IEnumerable<UpdateEntitySchemaOperationArgs>? UpdateOperations = null,

	[property: JsonPropertyName("seed-rows")]
	[property: Description("Rows to seed after creating the schema. Each object must have a 'values' key.")]
	IEnumerable<SchemaSyncSeedRow>? SeedRows = null
) {
	[property: JsonPropertyName("title")]
	[property: Description("Legacy scalar title. Not accepted by MCP. Use title-localizations instead.")]
	public string? LegacyTitle { get; init; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// A seed row for the <c>sync-schemas</c> tool.
/// </summary>
public sealed record SchemaSyncSeedRow(
	[property: JsonPropertyName("values")]
	[property: Description("Column name-value pairs for the seed row")]
	[property: Required]
	Dictionary<string, JsonElement> Values
);

/// <summary>
/// Response from the <c>sync-schemas</c> MCP tool.
/// </summary>
public sealed class SchemaSyncResponse {

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("results")]
	public IReadOnlyList<SchemaSyncOperationResult> Results { get; init; } = [];

	[JsonPropertyName("dataforge")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ApplicationDataForgeResult? DataForge { get; init; }
}

/// <summary>
/// Result of a single operation within a <c>sync-schemas</c> batch.
/// </summary>
public sealed class SchemaSyncOperationResult {

	[JsonPropertyName("type")]
	public string Type { get; init; }

	[JsonPropertyName("schema-name")]
	public string SchemaName { get; init; }

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	[JsonPropertyName("messages")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<LogMessage>? Messages { get; init; }

	[JsonPropertyName("collision-info")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SchemaSyncCollisionInfo? CollisionInfo { get; init; }
}

/// <summary>
/// Schema collision details included in a failed create operation when the schema already exists on the server.
/// </summary>
public sealed record SchemaSyncCollisionInfo(
	[property: JsonPropertyName("existing-package-name")] string ExistingPackageName,
	[property: JsonPropertyName("hint")] string Hint
);
