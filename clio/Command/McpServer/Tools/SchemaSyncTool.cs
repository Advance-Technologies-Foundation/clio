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
	ISchemaEnrichmentService? enrichmentService = null,
	IRetryDelay? retryDelay = null,
	TimeSpan? maxCumulativeRetryDelay = null) {

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
	/// Total number of attempts (including the first) for an operation whose failure is classified as a
	/// transient network fault (ENG-93374).
	/// </summary>
	internal const int MaxAttempts = 3;

	/// <summary>
	/// Sentinel exit code the retry attempt returns when a durable collision is only observable on a
	/// re-classify (retry) read. It is distinct from any command exit code so the post-loop code can
	/// rebuild the structured collision result from the (captured) reclassified plan via the same helper
	/// as the pre-emptive path, without a separate captured-nullable flag.
	/// </summary>
	private const int ReclassifiedCollisionExitCode = int.MinValue;

	/// <summary>
	/// Backoff applied before each retry of a transient failure. Index 0 is the wait after the first
	/// attempt, index 1 after the second — worst-case ~3s of added latency per retried step. A
	/// create-lookup has two retryable steps (create + registration), so its worst case is ~6s, and the
	/// added latency accumulates across the operations in a batch. Kept small so even a fully-flapping
	/// batch stays well under the MCP client per-call ceiling while it holds the per-tenant lock.
	/// </summary>
	private static readonly TimeSpan[] RetryBackoffs = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

	/// <summary>
	/// Cap on the TOTAL retry backoff a single sync-schemas call may spend across all of its operations.
	/// Per-op backoff is small, but a large batch under sustained flapping would otherwise accumulate
	/// synchronous in-lock sleep toward the MCP client per-call ceiling; once this budget is spent the
	/// remaining operations degrade to fail-fast (no further retry) and surface a resume-plan instead.
	/// </summary>
	private static readonly TimeSpan DefaultMaxCumulativeRetryDelay = TimeSpan.FromSeconds(30);

	private readonly IRetryDelay _retryDelay = retryDelay ?? ThreadSleepRetryDelay.Shared;
	private readonly TimeSpan _maxCumulativeRetryDelay = maxCumulativeRetryDelay ?? DefaultMaxCumulativeRetryDelay;

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
		"create-lookup, create-entity, and update-entity are convergent (create-if-absent + reconcile only the missing delta), so after a failure, fix the cause and re-submit the whole batch verbatim — already-applied schema operations replay as already-satisfied/reconciled with no duplicate mutation; do NOT hand-compose a batch of only the remaining operations. " +
		"Transient network failures (DNS resolution, connection reset/refused, timeouts, gateway errors) are retried per operation " +
		"(up to 3 attempts with short backoff) before the operation is failed. " +
		"On a mid-batch abort the response carries a 'resume-plan' with per-operation status (completed/failed/not-run) and a ready-to-resubmit 'operations' array; " +
		"resubmitting resume-plan.operations is the efficient recovery path (it excludes completed ops and converts a post-create seed failure to a standalone seed-data op, since seed-data is NOT replay-safe). " +
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
		int abortedAtIndex = -1;
		SchemaSyncOperation? failedResumeOperation = null;
		// FR-05: serialize on the per-tenant lock keyed by the environment the batch's schema commands
		// resolve under, so different tenants run concurrently instead of behind one global lock.
		string tenantKey = commandResolver.GetTenantKey(new EnvironmentOptions { Environment = args.EnvironmentName });
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			McpToolExecutionLock.MarkInUse(tenantKey);
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			// Batch-level cap on total retry backoff so a large flapping batch cannot accumulate
			// synchronous in-lock sleep toward the MCP client per-call ceiling (see DefaultMaxCumulativeRetryDelay).
			var retryBudget = new RetryBudget(_maxCumulativeRetryDelay);
			try {
				for (int index = 0; index < total; index++) {
					cancellationToken.ThrowIfCancellationRequested();
					SchemaSyncOperation op = operations[index];
					logger.ClearMessages();
					if (TryValidateSeedRows(op, index, out SchemaSyncOperationResult? seedValidationFailure)) {
						results.Add(Classify(seedValidationFailure!, index));
						abortedAtIndex = index;
						// A validation failure applied nothing on the server, so the whole operation is
						// resubmittable as-is.
						failedResumeOperation = op;
						break;
					}
					reportStage($"{index + 1}/{total}: {GetReportedOperationType(op)} {op.SchemaName}");
					SchemaSyncOperationResult result = Classify(ExecuteOperation(op, args, index, tenantKey, retryBudget), index);
					results.Add(result);
					if (!result.Success) {
						abortedAtIndex = index;
						failedResumeOperation = op;
						break;
					}
					if (op.SeedRows?.Any() == true && !IsSeedDataOperation(op)) {
						reportStage($"{index + 1}/{total}: seed-data {op.SchemaName}");
						logger.ClearMessages();
						SchemaSyncOperationResult seedResult = Classify(ExecuteSeedData(op, args, tenantKey, retryBudget), index);
						results.Add(seedResult);
						if (!seedResult.Success) {
							abortedAtIndex = index;
							// The create step already applied server-side, so resuming must NOT recreate the
							// schema — resubmit only the seeding as a first-class seed-data operation.
							failedResumeOperation = BuildSeedResumeOperation(op);
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
			ResumePlan = BuildResumePlan(operations, results, abortedAtIndex, failedResumeOperation),
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

	// Stamps the machine-readable status and the input operation index onto a result so callers can
	// separate completed from failed operations without positional guessing.
	private static SchemaSyncOperationResult Classify(SchemaSyncOperationResult result, int operationIndex) {
		result.OperationIndex = operationIndex;
		// Preserve a status already set by FinalizeResult (the create/update/seed success paths — including
		// already-satisfied — route through it). Only default it for results that never reach FinalizeResult
		// and set no status themselves: validation failures, the unknown-op result, the collision result, the
		// missing-update-operations error, and the deterministic catch-path failures.
		result.Status ??= result.Success ? "completed" : "failed";
		return result;
	}

	private static bool IsSeedDataOperation(SchemaSyncOperation op) =>
		string.Equals(op.Type, SeedDataOperationName, StringComparison.Ordinal);

	private SchemaSyncOperationResult ExecuteOperation(SchemaSyncOperation op, SchemaSyncArgs args, int operationIndex, string tenantKey, RetryBudget retryBudget) {
		return op.Type switch {
			CreateLookupOperationName => ExecuteCreateSchema(op, args, "BaseLookup", false, CreateLookupOperationName, tenantKey, retryBudget),
			CreateEntityOperationName => ExecuteCreateSchema(op, args, op.ParentSchemaName, op.ExtendParent, CreateEntityOperationName, tenantKey, retryBudget),
			UpdateEntityOperationName => ExecuteUpdateEntity(op, args, tenantKey, retryBudget),
			SeedDataOperationName => ExecuteSeedData(op, args, tenantKey, retryBudget),
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
		if (IsSeedDataOperation(op) && op.SeedRows?.Any() != true) {
			validationFailure = new SchemaSyncOperationResult {
				Type = SeedDataOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = $"sync-schemas operations[{operationIndex}] is invalid: a seed-data operation requires a non-empty 'seed-rows' array."
			};
			return true;
		}
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
		string parentSchemaName, bool extendParent, string operationName, string tenantKey, RetryBudget retryBudget) {
		try {
			string context = $"{operationName} operation for schema '{op.SchemaName}'";
			IReadOnlyDictionary<string, string> titleLocalizations = EntitySchemaLocalizationContract.RequireTitleLocalizations(
				op.TitleLocalizations,
				op.LegacyTitle,
				context);
			bool isLookup = string.Equals(operationName, CreateLookupOperationName, StringComparison.Ordinal);
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
				// Pre-emptive durable collision (cross-package or incompatible parent/kind): surfaced as a
				// success:false result before any create attempt, so a stale schema can no longer masquerade
				// as "completed" (this replaces the ENG-93374 reactive collision-probe/verify-against-intent
				// heuristic, which convergence subsumes).
				return BuildCollisionResult(operationName, op.SchemaName, plan, tenantKey);
			}

			// Run the FULL convergent operation (classify → apply) inside the transient-network-retry wrapper
			// (ENG-93374). On a retry the operation RE-CLASSIFIES first: a transient/lost-response flap on the
			// mutation may have actually applied server-side, so re-reading the current state lets the op
			// converge IN-CALL (a `created` first attempt becomes `already-satisfied`/`reconciled` on the retry)
			// instead of failing on a spurious "already exists" and deferring recovery to a batch resubmit.
			// The pre-emptive classify above is reused on the FIRST attempt, so the happy path still issues a
			// single read; the extra read happens only on the exception/retry path. Re-classifying is
			// side-effect-free, so it cannot duplicate a mutation, and a Collision observed on re-read fails
			// fast (it is not a transient fault, so the loop never spins on it).
			SchemaConvergencePlan currentPlan = plan;
			bool reclassify = false;
			OperationExecution execution = RunAttempts(() => {
				if (reclassify) {
					currentPlan = convergenceService.Classify(target);
				}
				reclassify = true;
				if (currentPlan.Outcome == SchemaConvergenceOutcome.Collision) {
					// A durable collision only observable on a re-read (near-impossible once the first attempt was
					// non-collision, since our own apply would land in the target package). Return the distinct
					// message-less ReclassifiedCollisionExitCode so RunAttempts fails fast without retrying (a
					// collision is not a transient fault); the structured collision result (outcome:collision +
					// collision-info) is built after the loop from the (captured) currentPlan by the SAME helper as
					// the pre-emptive path, keeping the contract shape.
					return ReclassifiedCollisionExitCode;
				}
				return ApplyConvergencePlan(currentPlan, op, args, parentSchemaName, extendParent, operationName, titleLocalizations);
			}, retryBudget);

			if (execution.ExitCode == ReclassifiedCollisionExitCode && execution.CaughtException is null) {
				return BuildCollisionResult(operationName, op.SchemaName, currentPlan, tenantKey);
			}

			// FR-02: ensure the Lookups registration on EVERY successful create-lookup path (created,
			// reconciled, already-satisfied) — the registration service is idempotent by name, so this is
			// safe to run on an already-existing schema whose registration might still be missing. Retried on
			// its OWN scope so a registration flap never re-runs the (already applied) create/reconcile.
			if (execution.ExitCode == 0 && execution.CaughtException is null && isLookup) {
				OperationExecution registration = RunAttempts(() => {
					ILookupRegistrationService registrationService =
						commandResolver.Resolve<ILookupRegistrationService>(new EnvironmentOptions { Environment = args.EnvironmentName });
					registrationService.EnsureLookupRegistration(
						args.PackageName,
						op.SchemaName,
						EntitySchemaLocalizationContract.GetDefaultTitle(titleLocalizations, context));
					return 0;
				}, retryBudget);
				execution = execution.Append(registration);
			}

			return FinalizeResult(operationName, op.SchemaName, execution, tenantKey,
				outcome: execution.ExitCode == 0 && execution.CaughtException is null ? MapOutcome(currentPlan.Outcome) : null);
		} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
			// Deterministic option-building failures (localization/guardrail validation) are not network
			// faults and are never retried — surface them exactly as before.
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
			};
		}
	}

	// Builds the structured collision result shared by the pre-emptive collision gate and the retry
	// re-classify path: success:false, outcome:collision, the user-friendly error, and collision-info naming
	// the owning package (when the classifier resolved one). Keeps both paths on one contract shape.
	private SchemaSyncOperationResult BuildCollisionResult(
		string operationName, string schemaName, SchemaConvergencePlan plan, string tenantKey) {
		return new SchemaSyncOperationResult {
			Type = operationName,
			SchemaName = schemaName,
			Success = false,
			Outcome = CollisionOutcome,
			Error = plan.Error,
			Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)],
			CollisionInfo = plan.CollisionPackageName is null
				? null
				: new SchemaSyncCollisionInfo(plan.CollisionPackageName, plan.Error ?? string.Empty)
		};
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

	private SchemaSyncOperationResult ExecuteUpdateEntity(SchemaSyncOperation op, SchemaSyncArgs args, string tenantKey, RetryBudget retryBudget) {
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

			// Run the FULL convergent operation (read-columns → reconcile → apply-delta) inside the
			// transient-network-retry wrapper (ENG-93374). On a retry the columns are RE-READ: a
			// transient/lost-response flap on the add/modify may have applied server-side, so the re-read
			// recomputes an empty delta and the op converges IN-CALL to already-satisfied instead of failing on
			// a spurious duplicate-add. The happy path still issues a single column read (attempt 1); the extra
			// read happens only on the exception/retry path, and re-reading is side-effect-free.
			// FR-04/FR-05/FR-06: add-if-absent, modify-if-different, remove→ensure-absent, and drop an
			// already-satisfied add. Columns not named in the request are never touched (no delete-unlisted full
			// reconcile — AC-07/OQ-02). Emit only the resulting delta.
			string? updateOutcome = null;
			OperationExecution execution = RunAttempts(() => {
				IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> existingColumns =
					convergenceService.ReadColumns(args.EnvironmentName, op.SchemaName);
				IReadOnlyList<UpdateEntitySchemaOperationArgs> delta = ReconcileUpdateOperations(requestedOperations, existingColumns);
				if (delta.Count == 0) {
					// Every requested operation is already satisfied (columns present and identical, or a remove of
					// an already-absent column). On replay this is a success, not a failure, and issues no
					// duplicate mutation (residual hole b) — no update command is executed.
					updateOutcome = AlreadySatisfiedOutcome;
					return 0;
				}
				updateOutcome = ReconciledOutcome;
				UpdateEntitySchemaOptions options = new() {
					Environment = args.EnvironmentName,
					Package = args.PackageName,
					SchemaName = op.SchemaName,
					Operations = UpdateEntitySchemaTool.SerializeOperations(delta, op.SchemaName)
				};
				return commandResolver.Resolve<UpdateEntitySchemaCommand>(options).Execute(options);
			}, retryBudget);
			return FinalizeResult(UpdateEntityOperationName, op.SchemaName, execution, tenantKey,
				outcome: execution.ExitCode == 0 && execution.CaughtException is null ? updateOutcome : null);
		} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
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

	private SchemaSyncOperationResult ExecuteSeedData(SchemaSyncOperation op, SchemaSyncArgs args, string tenantKey, RetryBudget retryBudget) {
		try {
			string rowsJson = JsonSerializer.Serialize(op.SeedRows);
			CreateDataBindingDbOptions options = new() {
				Environment = args.EnvironmentName,
				PackageName = args.PackageName,
				SchemaName = op.SchemaName,
				RowsJson = rowsJson
			};
			// Seeding is a non-idempotent write: do NOT auto-retry it. A committed-but-lost response
			// would otherwise be re-inserted silently. A transient seed failure fails fast into the
			// resume-plan (a standalone seed-data op) for a deliberate operator/agent resubmit.
			OperationExecution execution = RunAttempts(() =>
				commandResolver.Resolve<CreateDataBindingDbCommand>(options).Execute(options),
				retryBudget, retryable: false);
			return FinalizeResult(SeedDataOperationName, op.SchemaName, execution, tenantKey);
		} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
			return new SchemaSyncOperationResult {
				Type = SeedDataOperationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
			};
		}
	}

	// Runs a single command attempt, retrying up to MaxAttempts when the failure is a transient network
	// fault AND the operation is safe to re-run. Because the executor commands swallow their own exceptions
	// into an exit code + a logged error message, classification checks BOTH the caught exception (when one
	// still surfaces) and the last error message (pre-redaction). Only the final attempt's messages are
	// kept — earlier attempts contribute an info-level retry note instead of duplicating their error output.
	// retryable is false for non-idempotent writes (seed-data): re-running a committed-but-lost insert would
	// silently double-apply rows, so those fail fast into the resume-plan for a deliberate resubmit instead.
	private OperationExecution RunAttempts(Func<int> attempt, RetryBudget retryBudget, bool retryable = true) {
		var retryNotes = new List<LogMessage>();
		int attempts = 0;
		while (true) {
			attempts++;
			logger.ClearMessages();
			int exitCode = 1;
			Exception? caught = null;
			try {
				exitCode = attempt();
			} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
				caught = ex;
			}
			IReadOnlyList<LogMessage> rawMessages = logger.FlushAndSnapshotMessages(clearMessages: true);
			bool failed = caught is not null || exitCode != 0;
			bool transient = retryable && failed && (caught is not null
				? TransientNetworkFailureClassifier.IsTransient(caught)
				: TransientNetworkFailureClassifier.IsTransientErrorMessage(TryGetLastErrorMessage(rawMessages)));
			if (transient && attempts < MaxAttempts) {
				// Clamp defensively so raising MaxAttempts without extending RetryBackoffs reuses the last
				// backoff rather than throwing IndexOutOfRangeException inside the per-tenant lock.
				TimeSpan backoff = RetryBackoffs[Math.Min(attempts - 1, RetryBackoffs.Length - 1)];
				// Stop retrying once the batch-level backoff budget is spent so cumulative in-lock sleep
				// stays bounded regardless of batch size / flap intensity.
				if (!retryBudget.TryConsume(backoff)) {
					retryNotes.Add(new InfoMessage(
						$"sync-schemas: transient network failure on attempt {attempts}/{MaxAttempts}; batch retry budget exhausted, failing fast."));
					var exhausted = new List<LogMessage>(retryNotes);
					exhausted.AddRange(rawMessages);
					return new OperationExecution(exitCode, caught, exhausted, attempts);
				}
				retryNotes.Add(new InfoMessage(
					$"sync-schemas: transient network failure on attempt {attempts}/{MaxAttempts}; retrying in {backoff.TotalSeconds:0.#}s."));
				_retryDelay.Wait(backoff);
				continue;
			}
			var combined = new List<LogMessage>(retryNotes);
			combined.AddRange(rawMessages);
			return new OperationExecution(exitCode, caught, combined, attempts);
		}
	}

	// Builds the final operation result from a (possibly retried) execution (success, transient-exhausted, or
	// a non-transient failure). Convergence surfaces durable collisions pre-emptively (before any mutation),
	// so this path never re-probes for a collision. The additive convergence <paramref name="outcome"/>
	// (created/reconciled/…) is stamped only on a genuinely successful, post-registration execution.
	private SchemaSyncOperationResult FinalizeResult(
		string operationName, string schemaName, OperationExecution execution, string tenantKey,
		string? outcome = null) {
		IReadOnlyList<LogMessage> messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. execution.Messages], tenantKey)];
		int? attempts = execution.Attempts > 1 ? execution.Attempts : null;
		if (execution.CaughtException is not null) {
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = schemaName,
				Success = false,
				Status = "failed",
				Error = SensitiveErrorTextRedactor.Redact(execution.CaughtException.Message),
				Messages = messages,
				Attempts = attempts
			};
		}
		bool success = execution.ExitCode == 0;
		return new SchemaSyncOperationResult {
			Type = operationName,
			SchemaName = schemaName,
			Success = success,
			Status = success ? "completed" : "failed",
			Outcome = success ? outcome : null,
			Messages = messages,
			Error = BuildOperationError(operationName, execution.ExitCode, messages),
			Attempts = attempts
		};
	}

	private static string? TryGetLastErrorMessage(IReadOnlyList<LogMessage> messages) =>
		messages
			.LastOrDefault(message => message.LogDecoratorType == LogDecoratorType.Error)
			?.Value
			?.ToString()
			?.Trim();

	// Synthesizes the seed-only resume operation for the case where a create succeeded but its inline
	// seeding failed — resubmitting the original create op would collide with the schema just created.
	private static SchemaSyncOperation BuildSeedResumeOperation(SchemaSyncOperation op) =>
		new(SeedDataOperationName, op.SchemaName, SeedRows: op.SeedRows);

	// Assembles the resume plan for a mid-batch abort: the failed operation followed by every operation
	// that never ran, all echoed in re-submittable input shape. Returns null on a fully-successful batch.
	private static SchemaSyncResumePlan? BuildResumePlan(
		IReadOnlyList<SchemaSyncOperation> operations,
		IReadOnlyList<SchemaSyncOperationResult> results,
		int abortedAtIndex,
		SchemaSyncOperation? failedResumeOperation) {
		if (abortedAtIndex < 0 || failedResumeOperation is null) {
			return null;
		}
		SchemaSyncOperationResult? failedResult = results.LastOrDefault(r => !r.Success);
		var notRunIndexes = new List<int>();
		var resumeOperations = new List<SchemaSyncOperation> { failedResumeOperation };
		for (int index = abortedAtIndex + 1; index < operations.Count; index++) {
			notRunIndexes.Add(index);
			resumeOperations.Add(operations[index]);
		}
		return new SchemaSyncResumePlan {
			Instruction = "Batch aborted before completing. Resubmit ONLY the operations in resume-plan.operations "
				+ "(the failed operation followed by the not-run operations) as a new sync-schemas call; "
				+ "do NOT resubmit the operations already marked completed.",
			FailedOperation = new SchemaSyncResumeFailure(
				abortedAtIndex,
				failedResult?.Type ?? failedResumeOperation.Type,
				failedResumeOperation.SchemaName,
				failedResult?.Error),
			NotRunOperationIndexes = notRunIndexes,
			Operations = resumeOperations
		};
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

		string supportedTypes = string.Join(", ", CreateLookupOperationName, CreateEntityOperationName, UpdateEntityOperationName, SeedDataOperationName);
		return $"sync-schemas operations[{operationIndex}].type '{op.Type}' is invalid. Supported values: {supportedTypes}.";
	}

	private static string? BuildOperationError(string operationName, int exitCode, IReadOnlyList<LogMessage> messages) {
		if (exitCode == 0) {
			return null;
		}

		string fallback = $"{operationName} failed with exit code {exitCode}";
		string? detailedError = TryGetLastErrorMessage(messages);

		if (string.IsNullOrWhiteSpace(detailedError)) {
			return fallback;
		}

		return $"{fallback}: {detailedError}";
	}

	/// <summary>
	/// Mutable per-call budget bounding the TOTAL retry backoff a single sync-schemas call may spend
	/// across all of its operations. Not thread-safe by design — one instance is created per call and
	/// used only while the call holds the per-tenant lock.
	/// </summary>
	private sealed class RetryBudget(TimeSpan total) {
		private TimeSpan _remaining = total;

		/// <summary>
		/// Attempts to consume the given backoff from the remaining budget. Returns <see langword="true"/>
		/// and decrements when it fits; returns <see langword="false"/> (leaving the budget unchanged) when
		/// it would overspend, signalling the caller to stop retrying.
		/// </summary>
		public bool TryConsume(TimeSpan amount) {
			if (amount > _remaining) {
				return false;
			}
			_remaining -= amount;
			return true;
		}
	}

	/// <summary>
	/// Outcome of a (possibly retried) single command execution: the resolved exit code, the caught
	/// recoverable exception (if the command threw rather than returning a code), the messages to surface
	/// (final attempt's output plus any retry notes), and the number of attempts made.
	/// </summary>
	private readonly record struct OperationExecution(
		int ExitCode,
		Exception? CaughtException,
		IReadOnlyList<LogMessage> Messages,
		int Attempts) {

		/// <summary>
		/// Combines a follow-up execution (e.g. lookup registration after a successful create) into this
		/// one: the follow-up's outcome wins, messages concatenate, and the attempt count is the larger of
		/// the two so the surfaced count reflects the worst retry burst.
		/// </summary>
		public OperationExecution Append(OperationExecution next) {
			var messages = new List<LogMessage>(Messages);
			messages.AddRange(next.Messages);
			return new OperationExecution(next.ExitCode, next.CaughtException, messages, Math.Max(Attempts, next.Attempts));
		}
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
	[property: Description("Operation type: create-lookup, create-entity, update-entity, or seed-data")]
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
	[property: Description("Rows to seed after creating the schema (create-lookup/create-entity), or the rows to insert for a standalone seed-data operation. Each object must have a 'values' key.")]
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

	/// <summary>
	/// Recovery affordance emitted only when the batch aborted before completing. Enumerates the failed
	/// and not-run operations and provides a ready-to-resubmit <c>operations</c> array (ENG-93374).
	/// </summary>
	[JsonPropertyName("resume-plan")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SchemaSyncResumePlan? ResumePlan { get; init; }

	[JsonPropertyName("dataforge")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ApplicationDataForgeResult? DataForge { get; init; }
}

/// <summary>
/// Resume plan describing which operations completed, which failed, and which never ran when a
/// <c>sync-schemas</c> batch aborts mid-way, plus the operations to resubmit (ENG-93374).
/// </summary>
public sealed class SchemaSyncResumePlan {

	[JsonPropertyName("instruction")]
	public string Instruction { get; init; } = string.Empty;

	[JsonPropertyName("failed-operation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SchemaSyncResumeFailure? FailedOperation { get; init; }

	[JsonPropertyName("not-run-operation-indexes")]
	public IReadOnlyList<int> NotRunOperationIndexes { get; init; } = [];

	[JsonPropertyName("operations")]
	public IReadOnlyList<SchemaSyncOperation> Operations { get; init; } = [];
}

/// <summary>
/// Summary of the operation that aborted a <c>sync-schemas</c> batch.
/// </summary>
public sealed record SchemaSyncResumeFailure(
	[property: JsonPropertyName("operation-index")] int OperationIndex,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("error")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Error
);

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

	/// <summary>
	/// Machine-readable status: <c>completed</c> or <c>failed</c>. Operations that never ran are not
	/// present in <c>results</c> — they are enumerated in <see cref="SchemaSyncResponse.ResumePlan"/> (ENG-93374).
	/// </summary>
	[JsonPropertyName("status")]
	public string Status { get; set; }

	/// <summary>
	/// Zero-based index of the originating operation in the request <c>operations</c> array (ENG-93374).
	/// </summary>
	[JsonPropertyName("operation-index")]
	public int OperationIndex { get; set; }

	/// <summary>
	/// Number of attempts made when the operation was retried for a transient network fault. Omitted when
	/// the operation succeeded on the first attempt (ENG-93374).
	/// </summary>
	[JsonPropertyName("attempts")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Attempts { get; init; }

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
