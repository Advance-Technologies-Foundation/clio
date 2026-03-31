using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
	ILogger logger) {

	internal const string ToolName = "schema-sync";
	private const string CreateLookupOperationName = "create-lookup";

	/// <summary>
	/// Executes a batch of schema operations in a single MCP call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Executes a batch of schema operations in a single call: " +
		"create lookups, create entities, seed data, update entities. " +
		"Reduces MCP round-trips and lock overhead compared to individual tool calls. " +
		"Stops on first failure because subsequent operations may depend on earlier ones.")]
	public SchemaSyncResponse SchemaSync(
		[Description("Parameters: environment-name, package-name (required); operations array (required)")]
		[Required] SchemaSyncArgs args) {
		var results = new List<SchemaSyncOperationResult>();
		lock (McpToolExecutionLock.SyncRoot) {
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				foreach (SchemaSyncOperation op in args.Operations) {
					logger.ClearMessages();
					SchemaSyncOperationResult result = ExecuteOperation(op, args);
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
			Results = results
		};
	}

	private SchemaSyncOperationResult ExecuteOperation(SchemaSyncOperation op, SchemaSyncArgs args) {
		return op.Type switch {
			CreateLookupOperationName => ExecuteCreateSchema(op, args, "BaseLookup", false, CreateLookupOperationName),
			"create-entity" => ExecuteCreateSchema(op, args, op.ParentSchemaName, op.ExtendParent, "create-entity"),
			"update-entity" => ExecuteUpdateEntity(op, args),
			_ => new SchemaSyncOperationResult {
				Operation = op.Type, SchemaName = op.SchemaName,
				Success = false, Error = $"Unknown operation type: {op.Type}"
			}
		};
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
			return BuildCommandResult(operationName, op.SchemaName, exitCode);
		} catch (Exception ex) {
			return BuildExceptionResult(operationName, op.SchemaName, ex);
		}
	}

	private SchemaSyncOperationResult ExecuteUpdateEntity(SchemaSyncOperation op, SchemaSyncArgs args) {
		try {
			if (op.UpdateOperations?.Any() != true) {
				return new SchemaSyncOperationResult {
					Operation = "update-entity", SchemaName = op.SchemaName,
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
			return BuildCommandResult("update-entity", op.SchemaName, exitCode);
		} catch (Exception ex) {
			return BuildExceptionResult("update-entity", op.SchemaName, ex);
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
			return BuildCommandResult("seed-data", op.SchemaName, exitCode);
		} catch (Exception ex) {
			return BuildExceptionResult("seed-data", op.SchemaName, ex);
		}
	}

	private SchemaSyncOperationResult BuildCommandResult(string operationName, string schemaName, int exitCode) {
		IReadOnlyList<LogMessage> messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
		return new SchemaSyncOperationResult {
			Operation = operationName,
			SchemaName = schemaName,
			Success = exitCode == 0,
			Messages = messages,
			Error = BuildOperationError(operationName, exitCode, messages)
		};
	}

	private SchemaSyncOperationResult BuildExceptionResult(string operationName, string schemaName, Exception exception) {
		return new SchemaSyncOperationResult {
			Operation = operationName,
			SchemaName = schemaName,
			Success = false,
			Error = exception.Message,
			Messages = [.. logger.FlushAndSnapshotMessages(clearMessages: true)]
		};
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
/// Top-level arguments for the <c>schema-sync</c> MCP tool.
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
/// A single schema operation within a <c>schema-sync</c> batch.
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
}

/// <summary>
/// A seed row for the <c>schema-sync</c> tool.
/// </summary>
public sealed record SchemaSyncSeedRow(
	[property: JsonPropertyName("values")]
	[property: Description("Column name-value pairs for the seed row")]
	[property: Required]
	Dictionary<string, JsonElement> Values
);

/// <summary>
/// Response from the <c>schema-sync</c> MCP tool.
/// </summary>
public sealed class SchemaSyncResponse {

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("results")]
	public IReadOnlyList<SchemaSyncOperationResult> Results { get; init; } = [];
}

/// <summary>
/// Result of a single operation within a <c>schema-sync</c> batch.
/// </summary>
public sealed class SchemaSyncOperationResult {

	[JsonPropertyName("operation")]
	public string Operation { get; init; }

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
}
