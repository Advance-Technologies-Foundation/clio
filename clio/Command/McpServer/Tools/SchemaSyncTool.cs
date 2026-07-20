using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.DataForge;
using ModelContextProtocol.Protocol;
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
	ISchemaConvergenceService convergenceService,
	ISchemaEnrichmentService? enrichmentService = null) {

	internal const string ToolName = "sync-schemas";
	private const string CreateLookupOperationName = "create-lookup";
	private const string CreateEntityOperationName = "create-entity";
	private const string UpdateEntityOperationName = "update-entity";
	private const string SeedDataOperationName = "seed-data";
	private const string ModifyAction = "modify";
	private const string RemoveAction = "remove";
	private const string CreatedOutcome = "created";
	private const string ReconciledOutcome = "reconciled";
	private const string AlreadySatisfiedOutcome = "already-satisfied";
	private const string CollisionOutcome = "collision";

	/// <summary>
	/// Executes a batch of schema operations in a single MCP call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Executes a batch of schema operations in a single call: " +
		"create lookups, create entities, seed data, update entities. " +
		"For create-entity, set is-virtual to true only when the schema must not have a physical database table; it defaults to false. " +
		"Before setting is-virtual to true, call get-guidance with name virtual-entities and follow its schema-before-executor, bounded-provider, authorization, and version-gated write rules. " +
		"Reduces MCP round-trips and lock overhead compared to individual tool calls. " +
		"Stops on first failure because subsequent operations may depend on earlier ones. " +
		"For update-entity, column field names match the get-app-info read shape (read-shape aliases " +
		"name/data-value-type/reference-schema/is-required/caption are accepted), so a column read from " +
		"get-app-info can be sent back without field translation — add an 'action' verb for modify/remove, " +
		"or drop read/create-shape columns into a 'columns' array for an implicit add-batch. " +
		"Long-running: streams notifications/progress (a per-operation stage marker before each op) while " +
		"working — await completion and do not retry on a perceived timeout.")]
	public async Task<SchemaSyncResponse> SchemaSync(
		[Description("Parameters: environment-name, package-name (required); operations array (required)")]
		[Required] SchemaSyncArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken = default) {
		// Heartbeat-only overload (no RunWithProgressAndDeadlineAsync): sync-schemas returns per-operation
		// results and has no single "in-progress, poll" envelope. It executes stop-on-first-failure under
		// McpToolExecutionLock and each operation is individually bounded, so the deadline/background-
		// continuation contract used by create-app-section does not map cleanly here.
		return await McpProgressHeartbeat.RunWithProgressAsync(
			server,
			requestContext?.Params?.ProgressToken,
			ToolName,
			reportStage => ExecuteBatch(args, reportStage, cancellationToken),
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Runs the batch synchronously, pushing a stage marker through <paramref name="reportStage"/> before
	/// each operation (and its seed step) so a long publish sequence shows per-operation progress.
	/// </summary>
	internal SchemaSyncResponse ExecuteBatch(SchemaSyncArgs args, Action<string> reportStage,
		CancellationToken cancellationToken = default) {
		// Materialize the operations once so the enrichment collectors and the execution loop share a single
		// enumeration pass (args.Operations may be a lazy IEnumerable).
		IReadOnlyList<SchemaSyncOperation> operations =
			args.Operations as IReadOnlyList<SchemaSyncOperation> ?? args.Operations.ToList();
		// Data Forge enrichment is DIAGNOSTIC ONLY — it never gates the schema operations below. The
		// builder already degrades gracefully (an unhealthy dataforge subsystem, e.g. 'baseUri: Value
		// cannot be null', is caught and surfaced as a warning rather than thrown). This outer guard is
		// belt-and-suspenders: a throwing enrichment service must NEVER fail an otherwise-valid column
		// op — degrade by attaching the warning and proceeding (field-test defect #2).
		ApplicationDataForgeResult? dataForge = null;
		if (enrichmentService is not null) {
			try {
				dataForge = enrichmentService.Enrich(
					args.EnvironmentName,
					CollectCandidateTerms(operations),
					CollectLookupHints(operations));
			} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
				// Degrade ONLY operational enrichment failures (dataforge/HTTP/data-layer) into a warning —
				// a fatal condition or programming defect (OOM/NRE/…) must propagate, not be hidden here
				// (project rule: no blanket catch). The recoverable set is open-ended, so we exclude the
				// unrecoverable set rather than enumerate every operational type the builder may surface.
				dataForge = new ApplicationDataForgeResult(
					Used: true,
					Health: null,
					Status: null,
					Coverage: new DataForgeCoverage(false, false, false, false, false),
					// Redact before surfacing: a dataforge/HTTP/data-layer failure routinely carries
					// absolute paths, target URIs, and connection-string hosts (e.g. the 'baseUri: …'
					// case named above), and this warning is copied verbatim into the MCP client/
					// transcript — the same information-disclosure class the throw paths already redact.
					Warnings: [$"dataforge:{SensitiveErrorTextRedactor.Redact(ex.Message)}"],
					ContextSummary: new ApplicationDataForgeContextSummary([], [], [], []));
			}
		}
		int total = operations.Count;
		var results = new List<SchemaSyncOperationResult>();
		// FR-05: serialize on the per-tenant lock keyed by the environment the batch's schema commands
		// resolve under, so different tenants run concurrently instead of behind one global lock.
		string tenantKey = commandResolver.GetTenantKey(new EnvironmentOptions { Environment = args.EnvironmentName });
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			McpToolExecutionLock.MarkInUse(tenantKey);
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				for (int index = 0; index < total; index++) {
					cancellationToken.ThrowIfCancellationRequested();
					SchemaSyncOperation op = operations[index];
					logger.ClearMessages();
					if (TryValidateSeedRows(op, index, out SchemaSyncOperationResult? seedValidationFailure)) {
						results.Add(seedValidationFailure);
						break;
					}
					reportStage($"{index + 1}/{total}: {GetReportedOperationType(op)} {op.SchemaName}");
					SchemaSyncOperationResult result = ExecuteOperation(op, args, index, tenantKey);
					results.Add(result);
					if (!result.Success) {
						break;
					}
					if (op.SeedRows?.Any() == true) {
						reportStage($"{index + 1}/{total}: seed-data {op.SchemaName}");
						logger.ClearMessages();
						SchemaSyncOperationResult seedResult = ExecuteSeedData(op, args, tenantKey);
						results.Add(seedResult);
						if (!seedResult.Success) {
							break;
						}
					}
				}
			} finally {
				logger.ClearMessages();
				logger.PreserveMessages = previousPreserveMessages;
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
		return new SchemaSyncResponse {
			Success = results.Count > 0 && results.All(r => r.Success),
			Results = results,
			DataForge = dataForge
		};
	}

	private static IReadOnlyList<string> CollectCandidateTerms(IReadOnlyList<SchemaSyncOperation> operations) {
		return operations
			.Where(op => !string.IsNullOrWhiteSpace(op.SchemaName))
			.Select(op => op.SchemaName.Trim())
			.Concat(operations
				.SelectMany(op => (IEnumerable<string>?)op.TitleLocalizations?.Values ?? [])
				.Where(title => !string.IsNullOrWhiteSpace(title))
				.Select(title => title.Trim()))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IReadOnlyList<string> CollectLookupHints(IReadOnlyList<SchemaSyncOperation> operations) {
		return operations
			.Where(op => string.Equals(op.Type, "create-lookup", StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(op.SchemaName))
			.Select(op => op.SchemaName.Trim())
			.Concat(operations
				.Where(op => string.Equals(op.Type, "create-lookup", StringComparison.Ordinal))
				.SelectMany(op => (IEnumerable<string>?)op.TitleLocalizations?.Values ?? [])
				.Where(title => !string.IsNullOrWhiteSpace(title))
				.Select(title => title.Trim()))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private SchemaSyncOperationResult ExecuteOperation(SchemaSyncOperation op, SchemaSyncArgs args, int operationIndex, string tenantKey) {
		return op.Type switch {
			CreateLookupOperationName => ExecuteCreateSchema(op, args, "BaseLookup", false, CreateLookupOperationName, tenantKey),
			CreateEntityOperationName => ExecuteCreateSchema(op, args, op.ParentSchemaName, op.ExtendParent, CreateEntityOperationName, tenantKey),
			UpdateEntityOperationName => ExecuteUpdateEntity(op, args, tenantKey),
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
		if (string.Equals(op.Type, CreateEntityOperationName, StringComparison.Ordinal) && op.IsVirtual) {
			validationFailure = new SchemaSyncOperationResult {
				Type = CreateEntityOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = $"sync-schemas operations[{operationIndex}] is invalid: virtual create-entity operations cannot include seed-rows because virtual entities have no physical database table."
			};
			return true;
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
		string parentSchemaName, bool extendParent, string operationName, string tenantKey) {
		bool isLookup = string.Equals(operationName, CreateLookupOperationName, StringComparison.Ordinal);
		try {
			string context = $"{operationName} operation for schema '{op.SchemaName}'";
			IReadOnlyDictionary<string, string> titleLocalizations = EntitySchemaLocalizationContract.RequireTitleLocalizations(
				op.TitleLocalizations,
				op.LegacyTitle,
				context);
			if (isLookup) {
				ModelingGuardrails.EnsureLookupColumnsDoNotShadowInheritedBaseLookupColumns(op.Columns);
			}

			// Read the current server state ONCE and classify (create-if-absent / reconcile-delta /
			// already-satisfied / durable collision) BEFORE any mutation, replacing the old
			// create-unconditionally-then-probe path. All reads are server-side within this batch call.
			IReadOnlyList<CreateEntitySchemaColumnArgs> requestedColumns =
				op.Columns as IReadOnlyList<CreateEntitySchemaColumnArgs> ?? op.Columns?.ToList() ?? [];
			SchemaConvergenceTarget target = new(
				args.EnvironmentName, args.PackageName, op.SchemaName,
				isLookup ? "BaseLookup" : parentSchemaName, isLookup, extendParent, requestedColumns);
			SchemaConvergencePlan plan = convergenceService.Classify(target);

			if (plan.Outcome == SchemaConvergenceOutcome.Collision) {
				return new SchemaSyncOperationResult {
					Type = operationName,
					SchemaName = op.SchemaName,
					Success = false,
					Outcome = CollisionOutcome,
					Error = plan.Error,
					Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)],
					CollisionInfo = plan.CollisionPackageName is null
						? null
						: new SchemaSyncCollisionInfo(plan.CollisionPackageName, plan.Error ?? string.Empty)
				};
			}

			int exitCode = ApplyConvergencePlan(plan, op, args, parentSchemaName, extendParent, operationName, titleLocalizations);

			// FR-02: ensure the Lookups registration on EVERY successful create-lookup path (created,
			// reconciled, already-satisfied) — the registration service is idempotent by name, so this is
			// safe to run on an already-existing schema whose registration might still be missing.
			if (exitCode == 0 && isLookup) {
				ILookupRegistrationService registrationService =
					commandResolver.Resolve<ILookupRegistrationService>(new EnvironmentOptions { Environment = args.EnvironmentName });
				registrationService.EnsureLookupRegistration(
					args.PackageName,
					op.SchemaName,
					EntitySchemaLocalizationContract.GetDefaultTitle(titleLocalizations, context));
			}
			IReadOnlyList<LogMessage> messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)];
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = op.SchemaName,
				Success = exitCode == 0,
				Outcome = exitCode == 0 ? MapOutcome(plan.Outcome) : null,
				Messages = messages,
				Error = BuildOperationError(operationName, exitCode, messages)
			};
		} catch (Exception ex) {
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
			};
		}
	}

	/// <summary>
	/// Applies the mutation implied by a non-collision convergence plan: create the absent schema (columns
	/// applied inline), add only the missing columns to an existing schema via <see cref="UpdateEntitySchemaCommand"/>'s
	/// add-column operation (never recreating — <see cref="CreateEntitySchemaCommand"/> is create-only), or
	/// perform no mutation when the schema is already satisfied. Returns the underlying command exit code.
	/// </summary>
	private int ApplyConvergencePlan(
		SchemaConvergencePlan plan, SchemaSyncOperation op, SchemaSyncArgs args,
		string parentSchemaName, bool extendParent, string operationName,
		IReadOnlyDictionary<string, string> titleLocalizations) {
		switch (plan.Outcome) {
			case SchemaConvergenceOutcome.Create:
				CreateEntitySchemaOptions createOptions = CreateEntitySchemaTool.CreateOptions(
					new CreateLookupArgs(
						args.PackageName, op.SchemaName,
						new Dictionary<string, string>(titleLocalizations, StringComparer.OrdinalIgnoreCase), args.EnvironmentName,
						op.Columns),
					parentSchemaName, extendParent,
					isVirtual: string.Equals(operationName, CreateEntityOperationName, StringComparison.Ordinal)
						&& op.IsVirtual);
				CreateEntitySchemaCommand createCommand = commandResolver.Resolve<CreateEntitySchemaCommand>(createOptions);
				return createCommand.Execute(createOptions);
			case SchemaConvergenceOutcome.Reconcile:
				// Apply the full reconcile delta in a single UpdateEntitySchemaCommand batch: the missing
				// columns as additive add operations plus the per-column modify operations the classifier
				// surfaced for a present-but-different column (the modify write path Story 1 surfaced but
				// deferred). CreateEntitySchemaCommand is never invoked here — it is create-only.
				List<UpdateEntitySchemaOperationArgs> reconcileOperations = [
					.. plan.ColumnsToAdd.Select(CoerceColumnToAddOperation),
					.. plan.ColumnsToModify
				];
				if (reconcileOperations.Count == 0) {
					return 0;
				}
				UpdateEntitySchemaOptions updateOptions = new() {
					Environment = args.EnvironmentName,
					Package = args.PackageName,
					SchemaName = op.SchemaName,
					Operations = UpdateEntitySchemaTool.SerializeOperations(reconcileOperations, op.SchemaName)
				};
				UpdateEntitySchemaCommand updateCommand = commandResolver.Resolve<UpdateEntitySchemaCommand>(updateOptions);
				return updateCommand.Execute(updateOptions);
			default:
				// AlreadySatisfied: the requested shape is already present, so no mutation is issued.
				return 0;
		}
	}

	private static string MapOutcome(SchemaConvergenceOutcome outcome) {
		return outcome switch {
			SchemaConvergenceOutcome.Create => CreatedOutcome,
			SchemaConvergenceOutcome.Reconcile => ReconciledOutcome,
			SchemaConvergenceOutcome.AlreadySatisfied => AlreadySatisfiedOutcome,
			_ => CollisionOutcome
		};
	}

	private SchemaSyncOperationResult ExecuteUpdateEntity(SchemaSyncOperation op, SchemaSyncArgs args, string tenantKey) {
		try {
			IReadOnlyList<UpdateEntitySchemaOperationArgs> requestedOperations = ResolveUpdateOperations(op);
			if (requestedOperations.Count == 0) {
				return new SchemaSyncOperationResult {
					Type = UpdateEntityOperationName,
					SchemaName = op.SchemaName,
					Success = false,
					Error = BuildMissingUpdateOperationsError()
				};
			}

			// FR-04/FR-05/FR-06: read the current columns ONCE (one server-side read/op, no new MCP round-trip)
			// and reconcile the requested operations against them — add-if-absent, modify-if-different,
			// remove→ensure-absent, and drop an already-satisfied add. Columns not named in the request are
			// never touched (no delete-unlisted full reconcile — AC-07/OQ-02). Emit only the resulting delta.
			IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> existingColumns =
				convergenceService.ReadColumns(args.EnvironmentName, op.SchemaName);
			IReadOnlyList<UpdateEntitySchemaOperationArgs> delta = ReconcileUpdateOperations(requestedOperations, existingColumns);

			if (delta.Count == 0) {
				// Every requested operation is already satisfied (columns present and identical, or a remove of
				// an already-absent column). On replay this is a success, not a failure, and issues no
				// duplicate mutation (residual hole b).
				return new SchemaSyncOperationResult {
					Type = UpdateEntityOperationName,
					SchemaName = op.SchemaName,
					Success = true,
					Outcome = AlreadySatisfiedOutcome,
					Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
				};
			}

			UpdateEntitySchemaOptions options = new() {
				Environment = args.EnvironmentName,
				Package = args.PackageName,
				SchemaName = op.SchemaName,
				Operations = UpdateEntitySchemaTool.SerializeOperations(delta, op.SchemaName)
			};
			UpdateEntitySchemaCommand command = commandResolver.Resolve<UpdateEntitySchemaCommand>(options);
			int exitCode = command.Execute(options);
			IReadOnlyList<LogMessage> messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)];
			return new SchemaSyncOperationResult {
				Type = UpdateEntityOperationName,
				SchemaName = op.SchemaName,
				Success = exitCode == 0,
				Outcome = exitCode == 0 ? ReconciledOutcome : null,
				Messages = messages,
				Error = BuildOperationError(UpdateEntityOperationName, exitCode, messages)
			};
		} catch (Exception ex) {
			return new SchemaSyncOperationResult {
				Type = UpdateEntityOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
			};
		}
	}

	/// <summary>
	/// Computes the per-column delta for an <c>update-entity</c> operation against the current server column
	/// state (<paramref name="existingColumns"/>). A <c>remove</c> is issued only when the target column is
	/// present (an already-absent remove is a satisfied "ensure absent" no-op); an <c>add</c>/<c>modify</c> of
	/// an absent column is kept so the column is materialized or forwarded; an <c>add</c> of a present column
	/// is dropped when its type already matches (idempotent replay) and converged to a <c>modify</c> when the
	/// type differs. The add/columns shape reconciles by TYPE only: a present column with a matching type is
	/// treated as satisfied, so any non-type attribute change (required, reference-schema, flags, caption)
	/// must be sent as an explicit <c>modify</c> op, which is forwarded unconditionally (the column read does
	/// not expose every attribute — e.g. indexed/cloneable/caption localizations — so a modify cannot be
	/// proven a no-op; a re-run to the same value is a backend no-op, never a failure). Type equivalence is
	/// resolved by <see cref="EntitySchemaDesignerSupport.AreColumnTypesEquivalent"/> (ordinal-normalized), so
	/// a divergent read-back vocabulary does not force a spurious mutation on replay. Columns not named in
	/// <paramref name="requestedOperations"/> are never touched — there is no delete-unlisted reconcile (AC-07).
	/// </summary>
	private static IReadOnlyList<UpdateEntitySchemaOperationArgs> ReconcileUpdateOperations(
		IReadOnlyList<UpdateEntitySchemaOperationArgs> requestedOperations,
		IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> existingColumns) {
		List<UpdateEntitySchemaOperationArgs> delta = [];
		foreach (UpdateEntitySchemaOperationArgs operation in requestedOperations) {
			string? columnName = operation.ResolveColumnName();
			if (string.IsNullOrWhiteSpace(columnName)) {
				// Forward unchanged so the downstream serializer surfaces the missing-column-name error as before.
				delta.Add(operation);
				continue;
			}
			bool present = existingColumns.TryGetValue(columnName, out EntitySchemaPropertyColumnInfo? existingColumn);
			if (IsRemoveAction(operation.Action)) {
				if (present) {
					delta.Add(operation);
				}
				// Absent → "ensure absent" is already satisfied; issue nothing.
				continue;
			}
			if (!present) {
				// Absent column: materialize it (add) or forward the requested modify unchanged.
				delta.Add(operation);
				continue;
			}
			if (!EntitySchemaDesignerSupport.AreColumnTypesEquivalent(operation.ResolveType(), existingColumn!.Type)) {
				// Present but different type: converge via a modify so a re-add does not fail as a duplicate.
				// An incompatible modify is surfaced by the backend command as a modify-conflict error
				// (success:false), NOT a whole-schema collision.
				delta.Add(operation with { Action = ModifyAction });
				continue;
			}
			if (IsModifyAction(operation.Action)) {
				// Present, matching type: the add/columns shape reconciles by TYPE only, so a caller changing a
				// non-type attribute (required/reference/flags/caption) must use an explicit modify — forward it
				// unconditionally (a re-run to the same value is a backend no-op, never a failure).
				delta.Add(operation);
			}
			// Present add/columns entry with a matching type → the type-only add-shape contract is satisfied,
			// so drop it (idempotent replay). Non-type changes require an explicit modify op.
		}
		return delta;
	}

	private static bool IsRemoveAction(string? action) =>
		string.Equals(action, RemoveAction, StringComparison.OrdinalIgnoreCase);

	private static bool IsModifyAction(string? action) =>
		string.Equals(action, ModifyAction, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Resolves the column mutation operations for an <c>update-entity</c> operation. Prefers the explicit
	/// <c>update-operations</c> array; when it is absent, coerces a read/create-shape <c>columns</c> payload
	/// (no <c>action</c> verbs) into an implicit add-batch so the natural read-modify-write workflow round-trips
	/// without manual field translation (ENG-90313, Option A).
	/// </summary>
	private static IReadOnlyList<UpdateEntitySchemaOperationArgs> ResolveUpdateOperations(SchemaSyncOperation op) {
		if (op.UpdateOperations?.Any() == true) {
			return op.UpdateOperations.ToList();
		}
		if (op.Columns?.Any() == true) {
			return op.Columns.Select(CoerceColumnToAddOperation).ToList();
		}
		return [];
	}

	/// <summary>
	/// Maps a read/create-shape column onto an <c>add</c> column-mutation operation. Read-shape aliases
	/// (<c>data-value-type</c>, <c>reference-schema</c>, <c>is-required</c>) are resolved to their canonical
	/// names, and the read-shape scalar <c>caption</c> is promoted to <c>title-localizations</c> so a column
	/// read verbatim from <c>get-app-info</c> (which reports its caption as a scalar) round-trips into an add
	/// without manual field translation (ENG-90313).
	/// </summary>
	private static UpdateEntitySchemaOperationArgs CoerceColumnToAddOperation(CreateEntitySchemaColumnArgs column) {
		return new UpdateEntitySchemaOperationArgs(
			Action: "add",
			ColumnName: column.ResolveName() ?? string.Empty,
			Type: column.ResolveType(),
			TitleLocalizations: ResolveAddBatchTitleLocalizations(column),
			ReferenceSchemaName: column.ResolveReferenceSchemaName(),
			IsRequired: column.ResolveRequired(),
			DefaultValue: column.DefaultValue,
			DefaultValueSource: column.DefaultValueSource,
			Masked: column.Masked) {
			LegacyTitle = column.LegacyTitle,
			LegacyCaption = column.LegacyCaption,
			DefaultValueConfig = column.DefaultValueConfig
		};
	}

	/// <summary>
	/// Resolves the title localizations for a coerced add operation. Prefers the explicit
	/// <c>title-localizations</c> map; when it is absent but the read-shape scalar <c>caption</c> is present,
	/// promotes that caption to an <c>en-US</c> localization so the <c>get-app-info</c> read shape round-trips
	/// without manual translation (ENG-90313).
	/// </summary>
	private static Dictionary<string, string>? ResolveAddBatchTitleLocalizations(CreateEntitySchemaColumnArgs column) {
		if (column.TitleLocalizations?.Count > 0) {
			return column.TitleLocalizations;
		}
		if (!string.IsNullOrWhiteSpace(column.LegacyCaption)) {
			return new Dictionary<string, string> { ["en-US"] = column.LegacyCaption.Trim() };
		}
		return column.TitleLocalizations;
	}

	private static string BuildMissingUpdateOperationsError() {
		return "sync-schemas update-entity requires either an 'update-operations' array "
			+ "(each item: 'action' = add|modify|remove, 'column-name' [alias 'name'], 'type' [alias 'data-value-type'], "
			+ "'reference-schema-name' [alias 'reference-schema'], 'required' [alias 'is-required'], plus optional flags) "
			+ "or a 'columns' array (read/create shape: 'name', 'type' [alias 'data-value-type'], "
			+ "'title-localizations' [the read-shape scalar 'caption' is also accepted], "
			+ "'required' [alias 'is-required'], 'reference-schema-name' [alias 'reference-schema']) "
			+ "which is treated as an implicit add-batch. "
			+ "A column read from get-app-info ('name', 'type'/'data-value-type', "
			+ "'reference-schema-name'/'reference-schema', 'caption', 'required') can be sent back directly — "
			+ "add an 'action' for modify/remove.";
	}

	private SchemaSyncOperationResult ExecuteSeedData(SchemaSyncOperation op, SchemaSyncArgs args, string tenantKey) {
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
			IReadOnlyList<LogMessage> messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)];
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
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
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
	[property: Description(McpToolDescriptions.EnvironmentName)]
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
	[property: Description("Target entity schema name. " +
		"For create-entity and create-lookup operations, must use the active SchemaNamePrefix as prefix " +
		"(e.g. 'UsrAlpha' when prefix is 'Usr', 'MyPrefixAlpha' when prefix is 'MyPrefix'). " +
		"When `schema-name-prefix` is empty, use plain PascalCase with no prefix. " +
		"Read the prefix from the `schema-name-prefix` field returned by `get-app-info`, " +
		"or call `get-schema-name-prefix` if you have not called `get-app-info` yet.")]
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
	[property: Description("Initial columns for create-lookup or create-entity operations. " +
		"Column codes must also use the active SchemaNamePrefix (e.g. 'UsrEmail' when prefix is 'Usr'). " +
		"When `schema-name-prefix` is empty, use plain column names with no prefix. " +
		"Use the same prefix value from `schema-name-prefix`.")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null,

	[property: JsonPropertyName("update-operations")]
	[property: Description("Column mutation operations for update-entity")]
	IEnumerable<UpdateEntitySchemaOperationArgs>? UpdateOperations = null,

	[property: JsonPropertyName("seed-rows")]
	[property: Description("Rows to seed after creating the schema. Each object must have a 'values' key.")]
	IEnumerable<SchemaSyncSeedRow>? SeedRows = null
) {
	/// <summary>
	/// Gets whether a <c>create-entity</c> operation creates a virtual schema without a physical database table.
	/// </summary>
	[property: JsonPropertyName("is-virtual")]
	[property: Description("For create-entity only: create a virtual schema without a physical database table. Defaults to false. Virtual entities cannot include seed-rows.")]
	public bool IsVirtual { get; init; }

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

	/// <summary>
	/// Convergence discriminator for the operation: <c>created</c>, <c>reconciled</c>,
	/// <c>already-satisfied</c>, or <c>collision</c>. Additive and omitted when null so the existing
	/// wire shape is preserved for callers that predate the convergent semantics.
	/// </summary>
	[JsonPropertyName("outcome")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Outcome { get; init; }

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
