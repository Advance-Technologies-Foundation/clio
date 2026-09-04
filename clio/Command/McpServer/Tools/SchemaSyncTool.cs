using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

	/// <summary>
	/// The <c>operation-index</c> a result carries when it describes the CALL and not an operation: the whole-call
	/// argument rejection, which happens before the <c>operations</c> array is materialized. A real index is
	/// zero-based, so <c>-1</c> cannot collide with one.
	/// </summary>
	internal const int NoOperationIndex = -1;
	private const string CreateLookupOperationName = "create-lookup";
	private const string CreateEntityOperationName = "create-entity";
	private const string UpdateEntityOperationName = "update-entity";
	private const string SeedDataOperationName = "seed-data";
	private const string AddAction = "add";
	private const string ModifyAction = "modify";
	private const string RemoveAction = "remove";
	private const string CreatedOutcome = "created";
	private const string ReconciledOutcome = "reconciled";
	private const string AlreadySatisfiedOutcome = "already-satisfied";
	private const string CollisionOutcome = "collision";
	private const string FailedStatus = "failed";
	private const string SeedRowsFieldName = "seed-rows";

	/// <summary>
	/// Total number of attempts (including the first) for an operation whose failure is classified as a
	/// transient network fault (ENG-93374).
	/// </summary>
	internal const int MaxAttempts = 3;

	/// <summary>
	/// Sentinel exit code an attempt returns when its read observes a durable collision (on the first read or
	/// any retry re-classify) — either a schema-level collision from the classifier, or an <c>update-entity</c>
	/// per-column type collision. It is distinct from any command exit code so the post-loop code can rebuild
	/// the structured collision result from the captured plan/collisions via a single helper. A
	/// collision is not a transient fault (the sentinel carries no error message, so the classifier reports
	/// it non-transient), so RunAttempts fails fast on it instead of spinning the retry loop.
	/// </summary>
	private const int CollisionExitCode = int.MinValue;

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
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.Progress,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Executes a batch of schema operations in a single call: " +
		"create lookups, create entities, seed data, update entities. " +
		"For create-entity, set is-virtual to true only when the schema must not have a physical database table; it defaults to false. " +
		"Before setting is-virtual to true, call get-guidance with name virtual-entities and follow its schema-before-executor, bounded-provider, authorization, and version-gated write rules. " +
		"Reduces MCP round-trips and lock overhead compared to individual tool calls. " +
		"Stops on first failure because subsequent operations may depend on earlier ones. " +
		"create-lookup, create-entity, and update-entity are convergent (create-if-absent + reconcile only the missing delta), so after a failure, fix the cause and re-submit the whole batch verbatim — already-applied schema operations replay as already-satisfied/reconciled with no duplicate mutation; do NOT hand-compose a batch of only the remaining operations. " +
		"Whole-batch verbatim replay is safe for the convergent SCHEMA operations only; when the batch contains seed-data (or the response carried a 'resume-plan'), prefer resume-plan.operations, because seed-data is NOT replay-safe for rows without a 'Name' (a stable-Id, no-Name row PK-conflicts on replay). " +
		"Transient network failures (DNS resolution, connection reset/refused, timeouts, gateway errors) are retried per operation " +
		"(up to 3 attempts with short backoff) before the operation is failed. " +
		"On a mid-batch abort the response carries a 'resume-plan' with per-operation status (completed/failed/not-run) and a ready-to-resubmit 'operations' array; " +
		"resubmitting resume-plan.operations is the efficient recovery path (it excludes completed ops and converts a post-create seed failure to a standalone seed-data op, since seed-data is NOT replay-safe). " +
		"A fully-successful batch also carries a 'resume-plan' (with no 'failed-operation') when a create converged to already-satisfied and its INLINE seed-rows were therefore skipped to stay replay-safe: resubmit those standalone seed-data operations only if the rows are not yet on the server. " +
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
		// The top-level field-shape check runs BEFORE the operations array is materialized. Order matters: a
		// mis-keyed `operations` (or an omitted one) leaves the non-nullable positional property null, and
		// `Enumerable.ToList(null)` would throw ArgumentNullException — surfacing as "Value cannot be null.
		// (Parameter 'source')", which is exactly the unactionable answer this validation exists to remove.
		string? argsError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData,
			ArgsFieldAliases,
			".",
			ArgsFieldHint);
		if (argsError is not null) {
			return BuildTopLevelRejection($"sync-schemas arguments are invalid: {argsError} Nothing was applied.");
		}
		// An omitted `operations` leaves ExtensionData empty, so the alias check above cannot catch it.
		if (args.Operations is null) {
			return BuildTopLevelRejection(
				$"sync-schemas arguments are invalid: 'operations' is required and must be a non-empty array of schema operations. {ArgsFieldHint} Nothing was applied.");
		}
		IReadOnlyList<SchemaSyncOperation> operations =
			args.Operations as IReadOnlyList<SchemaSyncOperation> ?? args.Operations.ToList();
		if (operations.Count == 0) {
			return BuildTopLevelRejection(
				$"sync-schemas arguments are invalid: 'operations' is empty, so there is nothing to apply. Send at least one operation. {ArgsFieldHint} Nothing was applied.");
		}
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
					CollectLookupHints(operations),
					cancellationToken);
			} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
				// Rethrow only when the CALLER's own token requested the cancellation — it must propagate
				// rather than be degraded into a warning and let the batch continue. TaskCanceledException
				// (which derives from OperationCanceledException) can also surface from an alternate
				// enrichment implementation's own independent timeout; that is an operational failure like
				// any other and must still degrade to a warning (review #1143 follow-up).
				throw;
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
		var state = new BatchExecutionState();
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
			BatchContext batchContext = new(args, tenantKey, retryBudget, reportStage, total, state);
			try {
				for (int index = 0; index < total; index++) {
					cancellationToken.ThrowIfCancellationRequested();
					if (!ExecuteBatchOperation(operations[index], index, batchContext)) {
						break;
					}
				}
			} finally {
				logger.ClearMessages();
				logger.PreserveMessages = previousPreserveMessages;
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
		return new SchemaSyncResponse {
			Success = state.Results.Count > 0 && state.Results.All(r => r.Success),
			Results = state.Results,
			ResumePlan = BuildResumePlan(operations, state),
			DataForge = dataForge
		};
	}

	/// <summary>
	/// Runs one batch operation (validation → schema step → inline seed step), recording its results and any
	/// abort/deferred-seed bookkeeping on <paramref name="state"/>. Returns <see langword="false"/> when the
	/// batch must stop (stop-on-first-failure), <see langword="true"/> to continue with the next operation.
	/// </summary>
	private bool ExecuteBatchOperation(SchemaSyncOperation op, int index, BatchContext ctx) {
		logger.ClearMessages();
		// Field-shape and schema-name checks run before the seed-row checks and before any server call: an
		// operation whose keys did not bind must never reach the convergence read, or the caller gets a
		// success envelope for work that silently did nothing.
		// NOTE: the || short-circuit is load-bearing — every Try* method opens by setting the out parameter to
		// null, so a later call must not run once an earlier one has produced the failure.
		bool shapeRejected =
			TryValidateOperationFields(op, index, out SchemaSyncOperationResult? fieldValidationFailure)
			|| TryValidateSchemaName(op, index, out fieldValidationFailure)
			|| TryValidateVirtualSeedRows(op, index, out fieldValidationFailure);
		if (shapeRejected || TryValidateSeedRows(op, index, out fieldValidationFailure)) {
			ctx.State.Results.Add(Classify(fieldValidationFailure, index));
			// A validation failure applied nothing on the server. A seed-row failure is still resubmittable
			// as-is once the rows are corrected, but a SHAPE rejection must not be echoed back under a
			// "resubmit verbatim" instruction — the caller has to fix the field names first.
			ctx.State.Abort(index, op, resubmittableVerbatim: !shapeRejected);
			return false;
		}
		ctx.ReportStage($"{index + 1}/{ctx.Total}: {GetReportedOperationType(op)} {op.SchemaName}");
		SchemaSyncOperationResult result =
			Classify(ExecuteOperation(op, ctx.Args, index, ctx.TenantKey, ctx.RetryBudget), index);
		ctx.State.Results.Add(result);
		if (!result.Success) {
			// An operation whose `type` never bound to a dispatch arm (legacy `operation` key, missing or
			// invalid `type`) fails HERE rather than in the Try* chain above, but it is the same class of
			// failure: no edit to the rows or the environment can make that payload succeed. Echoing it back
			// under "resubmit ONLY the operations in resume-plan.operations" would send an agent into an
			// endless replay of a call that can never bind (PR #1354 review), so it is excluded from the plan
			// exactly like the field-shape rejections.
			ctx.State.Abort(index, op, resubmittableVerbatim: !IsUnbindableOperationType(op));
			return false;
		}
		if (op.SeedRows?.Any() != true || IsSeedDataOperation(op)) {
			return true;
		}
		return ExecuteInlineSeedStep(op, index, ctx, result);
	}

	/// <summary>
	/// Invariant context for one <c>sync-schemas</c> batch: everything the per-operation steps need besides the
	/// operation and its index. Bundled into one record so the step methods stay within the parameter budget
	/// (Sonar S107) and a new batch-wide concern is threaded by adding a field here rather than a parameter to
	/// every step.
	/// </summary>
	private sealed record BatchContext(
		SchemaSyncArgs Args,
		string TenantKey,
		RetryBudget RetryBudget,
		Action<string> ReportStage,
		int Total,
		BatchExecutionState State);

	/// <summary>
	/// Runs (or deliberately skips) the inline seed step attached to a succeeded create operation. Returns
	/// <see langword="false"/> when the seed failed and the batch must abort.
	/// </summary>
	private bool ExecuteInlineSeedStep(
		SchemaSyncOperation op, int index, BatchContext ctx, SchemaSyncOperationResult createResult) {
		if (string.Equals(createResult.Outcome, AlreadySatisfiedOutcome, StringComparison.Ordinal)) {
			// AC-2 replay-safety: an `already-satisfied` create is the verbatim-replay signal — the schema
			// already fully existed, so nothing was applied on this call. Inline seed-rows are NOT replay-safe
			// for rows without a stable key: EnsureRowId mints a fresh Guid for every row that has neither a
			// Name nor an explicit Id, so RowExistsInTable is always false and a verbatim replay of a
			// `create + inline seed-rows` batch would double-insert. Skip the inline seed on this signal and
			// surface both an explicit note AND a resume-plan entry carrying the equivalent standalone
			// seed-data op: the outcome may also come from a landed-but-lost-response create earlier in THIS
			// call (attempt 1 committed, attempt 2 re-classified to `already-satisfied`), where the rows were
			// genuinely never seeded — a success-keyed consumer must still get a recovery affordance instead
			// of silently losing the writes. Resubmitting that standalone op reconciles rows by key.
			// `reconciled` is deliberately NOT skipped: a delta was genuinely applied this call (first
			// application), so the inline seed must run; the next verbatim replay then classifies as
			// `already-satisfied` and is skipped here.
			ctx.State.Results.Add(Classify(BuildSkippedInlineSeedResult(op), index));
			ctx.State.DeferredSeedOperations.Add((index, BuildSeedResumeOperation(op)));
			return true;
		}
		ctx.ReportStage($"{index + 1}/{ctx.Total}: seed-data {op.SchemaName}");
		logger.ClearMessages();
		SchemaSyncOperationResult seedResult =
			Classify(ExecuteSeedData(op, ctx.Args, ctx.TenantKey, ctx.RetryBudget), index);
		ctx.State.Results.Add(seedResult);
		if (seedResult.Success) {
			return true;
		}
		// The create step already applied server-side, so resuming must NOT recreate the schema — resubmit
		// only the seeding as a first-class seed-data operation.
		ctx.State.Abort(index, BuildSeedResumeOperation(op));
		return false;
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
		result.Status ??= result.Success ? "completed" : FailedStatus;
		return result;
	}

	private static bool IsSeedDataOperation(SchemaSyncOperation op) =>
		string.Equals(op.Type, SeedDataOperationName, StringComparison.Ordinal);

	/// <summary>
	/// Whether this operation's <c>type</c> binds to a dispatch arm of <see cref="ExecuteOperation"/>. The
	/// complement of the <c>_ =&gt;</c> default branch, kept beside it so the two cannot drift: a false here
	/// means the operation reports <see cref="BuildUnknownOperationError"/> and is NOT resubmittable verbatim.
	/// </summary>
	private static bool IsUnbindableOperationType(SchemaSyncOperation op) =>
		op.Type is not (CreateLookupOperationName or CreateEntityOperationName
			or UpdateEntityOperationName or SeedDataOperationName);

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

	/// <summary>
	/// Mis-spellings of the top-level <see cref="SchemaSyncArgs"/> fields, mapped to their canonical
	/// kebab-case names.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> ArgsFieldAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["packageName"] = "package-name",
			["package_name"] = "package-name",
			["package"] = "package-name",
			["ops"] = "operations",
			["operation-list"] = "operations",
			["Operations"] = "operations"
		}.Concat(McpToolArgumentSupport.EnvironmentNameAliases)
		.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

	/// <summary>The valid top-level field names, listed in every top-level rejection.</summary>
	private const string ArgsFieldHint = "Valid fields: environment-name, package-name, operations.";

	/// <summary>
	/// Builds the whole-call rejection envelope used when the arguments fail before any operation runs, so the
	/// response literal is not duplicated per failure branch.
	/// </summary>
	private static SchemaSyncResponse BuildTopLevelRejection(string error) {
		return new SchemaSyncResponse {
			Success = false,
			Results = [
				new SchemaSyncOperationResult {
					Type = ToolName,
					Success = false,
					Status = FailedStatus,
					// NO operation ran, and none was even examined - the arguments were rejected before the
					// `operations` array was materialized. Leaving the default 0 here serialized
					// `operation-index: 0`, telling a caller that operations[0] is the culprit; an agent keying
					// recovery off that field would resubmit from index 1 and skip a schema operation that was
					// never applied (PR #1354 review). NoOperationIndex is the sentinel for "this result is
					// about the call, not about an operation".
					OperationIndex = NoOperationIndex,
					Error = error
				}
			]
		};
	}

	/// <summary>
	/// The mis-spellings of a <see cref="SchemaSyncOperation"/> field an LLM tends to emit, each mapped to the
	/// canonical kebab-case name. Without this table an unbound field lands in
	/// <see cref="SchemaSyncOperation.ExtensionData"/> and is dropped by System.Text.Json without a word, so a
	/// <c>create-lookup</c> carrying rows under the wrong key reports <c>outcome: created</c> and seeds nothing
	/// (issue #1303 A1).
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> OperationFieldAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["seed-data"] = SeedRowsFieldName,
			["seedData"] = SeedRowsFieldName,
			["seed_data"] = SeedRowsFieldName,
			["seedRows"] = SeedRowsFieldName,
			["seed_rows"] = SeedRowsFieldName,
			["rows"] = SeedRowsFieldName,
			["values"] = SeedRowsFieldName,
			["name"] = "schema-name",
			["schemaName"] = "schema-name",
			["schema_name"] = "schema-name",
			["titleLocalizations"] = "title-localizations",
			["title_localizations"] = "title-localizations",
			["parentSchemaName"] = "parent-schema-name",
			["parent_schema_name"] = "parent-schema-name",
			["extendParent"] = "extend-parent",
			["extend_parent"] = "extend-parent",
			["updateOperations"] = "update-operations",
			["update_operations"] = "update-operations",
			["isVirtual"] = "is-virtual",
			["is_virtual"] = "is-virtual"
		};

	/// <summary>
	/// Field names in <see cref="SchemaSyncOperation.ExtensionData"/> that the tool itself consumes, so they
	/// must not be reported as unknown. <c>operation</c> is the legacy spelling of <c>type</c> read back in
	/// <c>GetReportedOperationType</c>/<c>BuildUnknownOperationError</c>.
	/// </summary>
	private static readonly IReadOnlySet<string> ConsumedOperationExtensionFields =
		new HashSet<string>(StringComparer.Ordinal) { "operation" };

	/// <summary>
	/// The valid <see cref="SchemaSyncOperation"/> field names, listed in the unknown-field error so the caller
	/// can correct the call without re-reading the whole contract.
	/// </summary>
	private const string OperationFieldHint =
		"Valid operation fields: type, schema-name, title-localizations, parent-schema-name, extend-parent, " +
		"columns, update-operations, seed-rows, is-virtual (legacy: title).";

	/// <summary>
	/// Rejects an operation whose JSON carried fields the tool cannot bind. This runs BEFORE any server call so
	/// a mis-keyed payload fails loudly instead of being partially applied: previously every unbound field was
	/// swallowed silently, which is why rows sent under <c>seed-data</c> disappeared while the operation still
	/// reported success.
	/// </summary>
	private static bool TryValidateOperationFields(
		SchemaSyncOperation op,
		int operationIndex,
		[NotNullWhen(true)] out SchemaSyncOperationResult? validationFailure) {
		validationFailure = null;
		Dictionary<string, JsonElement> unbound = (op.ExtensionData ?? [])
			.Where(pair => !ConsumedOperationExtensionFields.Contains(pair.Key))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			unbound,
			OperationFieldAliases,
			".",
			OperationFieldHint);
		if (aliasError is null) {
			return false;
		}
		// 'seed-data' is a legitimate VALUE of the 'type' field, so an unqualified "rename seed-data" hint
		// reads as "the seed-data operation type is gone". Say which one the caller got wrong.
		string note = BuildRenameShapeNote(unbound);
		validationFailure = new SchemaSyncOperationResult {
			Type = GetReportedOperationType(op),
			SchemaName = op.SchemaName,
			Success = false,
			Error = $"sync-schemas operations[{operationIndex}] is invalid: {aliasError}{note} " +
				"Nothing was applied for this operation."
		};
		return true;
	}

	/// <summary>
	/// Adds the shape reminder a bare rename hint cannot carry. Renaming <c>seed-data</c>/<c>values</c>/
	/// <c>rows</c> to <c>seed-rows</c> is not enough on its own: <c>seed-rows</c> is an ARRAY whose items each
	/// wrap their columns in a <c>values</c> map, so a caller who follows the rename literally would just fail
	/// a second time — this time in the SDK binder, outside the structured envelope.
	/// </summary>
	private static string BuildRenameShapeNote(IReadOnlyDictionary<string, JsonElement> unbound) {
		if (unbound.ContainsKey("seed-data")) {
			return " Note: 'seed-data' is an operation TYPE (\"type\": \"seed-data\"), not a field; rows always go in "
				+ "'seed-rows', which is an array of row objects: \"seed-rows\": [{\"values\": {\"Name\": \"Positive\"}}].";
		}
		if (unbound.ContainsKey("values") || unbound.ContainsKey("rows")) {
			return " Note: 'seed-rows' is an ARRAY of row objects, each wrapping its columns in a 'values' map: "
				+ "\"seed-rows\": [{\"values\": {\"Name\": \"Positive\"}}].";
		}
		return string.Empty;
	}

	/// <summary>
	/// Rejects an operation with no <c>schema-name</c> up front. Without this check a blank name reaches
	/// <c>FindEntitySchemaCommand.Validate</c>, whose message talks about the <c>--schema-name</c>/
	/// <c>--search-pattern</c>/<c>--uid</c> CLI switches that an MCP caller never used (issue #1303 C2).
	/// </summary>
	private static bool TryValidateSchemaName(
		SchemaSyncOperation op,
		int operationIndex,
		[NotNullWhen(true)] out SchemaSyncOperationResult? validationFailure) {
		validationFailure = null;
		if (!string.IsNullOrWhiteSpace(op.SchemaName)) {
			return false;
		}
		validationFailure = new SchemaSyncOperationResult {
			Type = GetReportedOperationType(op),
			SchemaName = op.SchemaName,
			Success = false,
			Error = $"sync-schemas operations[{operationIndex}] is invalid: 'schema-name' is required and cannot be empty. " +
				"Nothing was applied for this operation."
		};
		return true;
	}

	/// <summary>
	/// Rejects a virtual <c>create-entity</c> that also carries <c>seed-rows</c>. Classified with the SHAPE
	/// rejections rather than the seed-row ones on purpose: no edit to the rows can make this operation valid —
	/// the caller must drop either <c>seed-rows</c> or <c>is-virtual</c> — so echoing it back under a "resubmit
	/// these operations" instruction would advertise a recovery path that rejects forever.
	/// </summary>
	private static bool TryValidateVirtualSeedRows(
		SchemaSyncOperation op,
		int operationIndex,
		[NotNullWhen(true)] out SchemaSyncOperationResult? validationFailure) {
		validationFailure = null;
		if (op.SeedRows?.Any() != true
			|| !string.Equals(op.Type, CreateEntityOperationName, StringComparison.Ordinal)
			|| !op.IsVirtual) {
			return false;
		}
		validationFailure = new SchemaSyncOperationResult {
			Type = CreateEntityOperationName,
			SchemaName = op.SchemaName,
			Success = false,
			Error = $"sync-schemas operations[{operationIndex}] is invalid: virtual create-entity operations cannot "
				+ "include seed-rows because virtual entities have no physical database table. Drop 'seed-rows', or "
				+ "set 'is-virtual' to false. Nothing was applied for this operation."
		};
		return true;
	}

	private static bool TryValidateSeedRows(
		SchemaSyncOperation op,
		int operationIndex,
		[NotNullWhen(true)] out SchemaSyncOperationResult? validationFailure) {
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

			// Run the FULL convergent operation (classify → apply) inside the transient-network-retry wrapper
			// (ENG-93374/ENG-93807). Every attempt — including the FIRST — starts by reading the current server
			// state and classifying (create-if-absent / reconcile-delta / already-satisfied / durable collision)
			// BEFORE any mutation, replacing the old create-unconditionally-then-probe path. All reads are
			// server-side within this batch call. Keeping the first classify inside the loop means a transient
			// network fault on that initial read is retried like any other transient step (rather than aborting
			// the operation), honouring the advertised "transient network failures are retried per operation"
			// contract; the happy path still issues a single read (attempt 1). On a retry the re-classify lets a
			// transient/lost-response flap on the mutation converge IN-CALL (a `created` first attempt becomes
			// `already-satisfied`/`reconciled`) instead of failing on a spurious "already exists". Classifying is
			// side-effect-free, so it cannot duplicate a mutation, and a Collision observed on any read fails fast
			// (it is not a transient fault, so the loop never spins on it).
			IReadOnlyList<CreateEntitySchemaColumnArgs> requestedColumns =
				op.Columns as IReadOnlyList<CreateEntitySchemaColumnArgs> ?? op.Columns?.ToList() ?? [];
			SchemaConvergenceTarget target = new(
				args.EnvironmentName, args.PackageName, op.SchemaName,
				isLookup ? "BaseLookup" : parentSchemaName, isLookup, extendParent, requestedColumns);
			SchemaConvergencePlan? currentPlan = null;
			OperationExecution execution = RunAttempts(() => {
				currentPlan = convergenceService.Classify(target);
				if (currentPlan.Outcome == SchemaConvergenceOutcome.Collision) {
					// Durable collision (cross-package or incompatible parent/kind), whether surfaced on the first
					// read or a retry re-classify: return the distinct message-less CollisionExitCode so RunAttempts
					// fails fast without retrying (a collision is not a transient fault). The structured collision
					// result (success:false + outcome:collision + collision-info) is built after the loop from the
					// captured currentPlan by BuildCollisionResult, keeping one contract shape for every collision.
					return CollisionExitCode;
				}
				return ApplyConvergencePlan(currentPlan, op, args, parentSchemaName, extendParent, operationName, titleLocalizations);
			}, retryBudget);

			if (execution.ExitCode == CollisionExitCode && execution.CaughtException is null) {
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
			return BuildDeterministicFailureResult(operationName, op.SchemaName, ex, tenantKey);
		}
	}

	/// <summary>
	/// Builds the failure result for a deterministic (non-transient, never-retried) exception on an operation
	/// path. Single home for the security-sensitive surfacing contract shared by every such catch block: the
	/// message goes through <see cref="SensitiveErrorTextRedactor"/> and the preserved log messages are flushed
	/// through <see cref="McpPassthroughRedaction"/>, so the two cannot drift apart per call site.
	/// </summary>
	private SchemaSyncOperationResult BuildDeterministicFailureResult(
		string operationName, string schemaName, Exception ex, string tenantKey) =>
		new() {
			Type = operationName,
			SchemaName = schemaName,
			Success = false,
			Error = SensitiveErrorTextRedactor.Redact(ex.Message),
			Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
		};

	// Builds the structured collision result shared by the pre-emptive collision gate and the retry
	// re-classify path: success:false, outcome:collision, the user-friendly error, and collision-info naming
	// the owning package (when the classifier resolved one). Keeps both paths on one contract shape.
	private SchemaSyncOperationResult BuildCollisionResult(
		string operationName, string schemaName, SchemaConvergencePlan plan, string tenantKey) {
		// Defense in depth: the classifier composes plan.Error from schema/package identifiers only, so there is
		// nothing sensitive to strip today — but every other error surfaced from this tool goes through the
		// redactor, and keeping this path on the same contract means a future classifier message that starts
		// interpolating a URI/path/connection detail cannot leak by omission.
		string? error = plan.Error is null ? null : SensitiveErrorTextRedactor.Redact(plan.Error);
		return new SchemaSyncOperationResult {
			Type = operationName,
			SchemaName = schemaName,
			Success = false,
			Outcome = CollisionOutcome,
			Error = error,
			Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)],
			CollisionInfo = plan.CollisionPackageName is null
				? null
				: new SchemaSyncCollisionInfo(plan.CollisionPackageName, error ?? string.Empty)
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
				// surfaced for a present-but-different column (the modify write path converges the existing
				// column's type here). CreateEntitySchemaCommand is never invoked here — it is create-only.
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
			List<ColumnTypeCollision> collisions = [];
			OperationExecution execution = RunAttempts(() => {
				IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> existingColumns =
					convergenceService.ReadColumns(args.EnvironmentName, op.SchemaName);
				collisions.Clear();
				IReadOnlyList<UpdateEntitySchemaOperationArgs> delta =
					ReconcileUpdateOperations(requestedOperations, existingColumns, collisions);
				if (collisions.Count > 0) {
					// A durable per-column collision: an add names a present column of a different type
					// (ENG-93807 review). Like the schema-level collision, this is not a transient fault, so
					// return the message-less sentinel to fail fast without burning the retry budget; the
					// structured result is rebuilt after the loop from the captured collisions. The list is
					// cleared per attempt so a retry re-read that no longer collides is not judged by a stale
					// classification.
					return CollisionExitCode;
				}
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
			if (execution.ExitCode == CollisionExitCode && execution.CaughtException is null) {
				return BuildColumnCollisionResult(op.SchemaName, collisions, tenantKey);
			}
			return FinalizeResult(UpdateEntityOperationName, op.SchemaName, execution, tenantKey,
				outcome: execution.ExitCode == 0 && execution.CaughtException is null ? updateOutcome : null);
		} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
			return BuildDeterministicFailureResult(UpdateEntityOperationName, op.SchemaName, ex, tenantKey);
		}
	}

	/// <summary>
	/// A requested <c>add</c> that names an already-present column whose type differs from the request. The
	/// add is not rewritten into a type-changing modify; it is surfaced so the caller sees that the "add a new
	/// column" intent did not match reality (ENG-93807 review).
	/// </summary>
	private sealed record ColumnTypeCollision(string ColumnName, string? ExistingType, string? RequestedType);

	/// <summary>
	/// Builds the structured result for one or more per-column type collisions: <c>success:false</c>,
	/// <c>outcome:collision</c>, and an error naming every colliding column with its existing and requested
	/// type plus the explicit-<c>modify</c> remedy. No <c>collision-info</c> is emitted — that block names the
	/// owning package of a colliding SCHEMA and does not apply to a column collision.
	/// </summary>
	private SchemaSyncOperationResult BuildColumnCollisionResult(
		string schemaName, IReadOnlyList<ColumnTypeCollision> collisions, string tenantKey) {
		string details = string.Join("; ", collisions.Select(collision =>
			$"'{collision.ColumnName}' exists as '{collision.ExistingType}' but was requested as '{collision.RequestedType}'"));
		string error = SensitiveErrorTextRedactor.Redact(
			$"Column collision on schema '{schemaName}': {details}. "
			+ "An 'add' never changes an existing column's type — send an explicit 'modify' action to converge "
			+ "the type, or use a different column name.");
		return new SchemaSyncOperationResult {
			Type = UpdateEntityOperationName,
			SchemaName = schemaName,
			Success = false,
			Outcome = CollisionOutcome,
			Error = error,
			Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)]
		};
	}

	/// <summary>
	/// Computes the per-column delta for an <c>update-entity</c> operation against the current server column
	/// state (<paramref name="existingColumns"/>). A <c>remove</c> is issued only when the target column is
	/// present (an already-absent remove is a satisfied "ensure absent" no-op); an <c>add</c>/<c>modify</c> of
	/// an absent column is kept so the column is materialized or forwarded; an <c>add</c> of a present column
	/// is dropped when its type already matches (idempotent replay) and reported as a COLUMN COLLISION (via
	/// <paramref name="collisions"/>) when the type differs — an add is never rewritten into a type-changing
	/// <c>modify</c>, so a different pre-existing column that happens to own the name is surfaced instead of
	/// being silently mutated (ENG-93807 review). Changing a present column's type therefore requires an
	/// explicit <c>modify</c> action, including in the <c>columns</c> add-batch round-trip (FR-04). Note the
	/// deliberate asymmetry with the <c>create-entity</c> reconcile path, where
	/// <see cref="SchemaConvergencePlan.ColumnsToModify"/> still converges a type divergence: that path is
	/// scoped to the whole-schema ensure contract and is unchanged here. The add/columns shape reconciles by
	/// TYPE only: a present column with a matching type is
	/// treated as satisfied, so any non-type attribute change (required, reference-schema, flags, caption)
	/// must be sent as an explicit <c>modify</c> op, which is forwarded unconditionally (the column read does
	/// not expose every attribute — e.g. indexed/cloneable/caption localizations — so a modify cannot be
	/// proven a no-op; a re-run to the same value is a backend no-op, never a failure). Type equivalence is
	/// resolved by <see cref="EntitySchemaDesignerSupport.AreColumnTypesEquivalent"/> (ordinal-normalized), so
	/// a divergent read-back vocabulary does not force a spurious mutation on replay. Columns not named in
	/// <paramref name="requestedOperations"/> are never touched — there is no delete-unlisted reconcile (AC-07).
	/// Operations are reconciled in order against a column view that ADVANCES with each classified operation,
	/// not against the pre-batch snapshot, so an ordered <c>remove X</c> + <c>add X</c> pair recreates the
	/// column (the re-add sees it absent) instead of having the re-add dropped as already-satisfied — and a
	/// remove-then-re-add at a different type is a legitimate recreate, not a collision.
	/// </summary>
	private static IReadOnlyList<UpdateEntitySchemaOperationArgs> ReconcileUpdateOperations(
		IReadOnlyList<UpdateEntitySchemaOperationArgs> requestedOperations,
		IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> existingColumns,
		List<ColumnTypeCollision> collisions) {
		// Project the server read into a mutable per-column view and advance it as each operation is
		// classified, so an operation is reconciled against the state its predecessors in the SAME batch
		// leave behind rather than against the pre-batch snapshot. Without this, an ordered
		// `remove X` + `add X` pair (the sanctioned way to change a column's shape in one call) classifies
		// the re-add against the still-present snapshot column and DROPS it as already-satisfied, so the
		// column is removed and never restored. It also makes `remove X` + `add X` with a different type a
		// legitimate recreate instead of a collision.
		Dictionary<string, string?> columnTypes = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, EntitySchemaPropertyColumnInfo> column in existingColumns) {
			columnTypes[column.Key] = column.Value.Type;
		}
		List<UpdateEntitySchemaOperationArgs> delta = [];
		foreach (UpdateEntitySchemaOperationArgs operation in requestedOperations) {
			UpdateEntitySchemaOperationArgs? reconciled = ReconcileUpdateOperation(operation, columnTypes, collisions);
			if (reconciled is not null) {
				delta.Add(reconciled);
			}
		}
		return delta;
	}

	/// <summary>
	/// Reconciles a single requested column operation against the current server column state, returning the
	/// operation to issue or <see langword="null"/> when it is already satisfied (and must be dropped) or when
	/// it collided — a collision is appended to <paramref name="collisions"/> and aborts the whole operation
	/// after every requested column has been classified, so one response names every colliding column rather
	/// than only the first. Extracted from <see cref="ReconcileUpdateOperations"/> so the per-column decision
	/// tree reads as one unit.
	/// </summary>
	private static UpdateEntitySchemaOperationArgs? ReconcileUpdateOperation(
		UpdateEntitySchemaOperationArgs operation,
		Dictionary<string, string?> columnTypes,
		List<ColumnTypeCollision> collisions) {
		string? columnName = operation.ResolveColumnName();
		if (string.IsNullOrWhiteSpace(columnName)) {
			// Forward unchanged so the downstream serializer surfaces the missing-column-name error as before.
			return operation;
		}
		bool isModify = IsModifyAction(operation.Action);
		if (!IsAddAction(operation.Action) && !isModify && !IsRemoveAction(operation.Action)) {
			// Unsupported action verb (e.g. 'rename', a typo, or a missing action) — never converge or
			// rewrite it. Forward it unchanged so UpdateEntitySchemaCommand's own validator rejects it with
			// "Action must be one of: add, modify, remove.", instead of silently dropping it (present, same
			// type) or coercing it to a modify (present, different type) and bypassing that validation. The
			// tracked column state is left untouched: what such an operation does is for the validator to
			// decide, so no successor may be reconciled against a guess about its effect.
			return operation;
		}
		bool present = columnTypes.TryGetValue(columnName, out string? existingType);
		if (IsRemoveAction(operation.Action)) {
			// Present → issue the remove; absent → "ensure absent" is already satisfied, issue nothing.
			if (!present) {
				return null;
			}
			columnTypes.Remove(columnName);
			return operation;
		}
		if (!present) {
			// Absent column: materialize it (add) or forward the requested modify unchanged.
			columnTypes[columnName] = operation.ResolveType();
			return operation;
		}
		if (!EntitySchemaDesignerSupport.AreColumnTypesEquivalent(operation.ResolveType(), existingType)) {
			// Present but different type: an explicit modify is forwarded as-is (the caller asked to converge
			// the type). An add — explicit, or the implicit add-batch coerced from a `columns` payload — is a
			// COLUMN COLLISION and is reported as such, never auto-rewritten to a type-changing modify: a
			// genuine replay of the caller's own add carries the SAME type and is dropped below as satisfied,
			// so this branch fires only when a DIFFERENT, pre-existing column already owns the name. Silently
			// mutating that column's type would reinterpret "add a new column" as "change the existing one" —
			// the per-column analogue of the masked schema collision this feature exists to close. An
			// incompatible modify is still surfaced by the backend command as a modify-conflict error
			// (success:false), NOT a collision.
			if (!isModify) {
				collisions.Add(new ColumnTypeCollision(columnName, existingType, operation.ResolveType()));
				return null;
			}
			columnTypes[columnName] = operation.ResolveType();
			return operation;
		}
		// Present, matching type: the add/columns shape reconciles by TYPE only, so a caller changing a
		// non-type attribute (required/reference/flags/caption) must use an explicit modify — forward that
		// unconditionally (a re-run to the same value is a backend no-op, never a failure). A present add entry
		// with a matching type has its type-only contract satisfied, so it is dropped (idempotent replay).
		return isModify ? operation : null;
	}

	private static bool IsAddAction(string? action) =>
		string.Equals(action, AddAction, StringComparison.OrdinalIgnoreCase);

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
			return BuildDeterministicFailureResult(SeedDataOperationName, op.SchemaName, ex, tenantKey);
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
				Status = FailedStatus,
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
			Status = success ? "completed" : FailedStatus,
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

	// Builds the observable result row for an inline seed skipped because its create converged to
	// `already-satisfied` (the verbatim-replay signal). Success (nothing failed), but carries an explicit note
	// so a genuine "seed a pre-existing schema" intent is routed to a standalone seed-data op rather than being
	// silently dropped. See the skip rationale at the seed step in ExecuteBatch.
	private static SchemaSyncOperationResult BuildSkippedInlineSeedResult(SchemaSyncOperation op) =>
		new() {
			Type = SeedDataOperationName,
			SchemaName = op.SchemaName,
			Success = true,
			// Status is defaulted to "completed" by Classify (Success:true); left unset here to avoid a
			// duplicated string literal.
			Outcome = AlreadySatisfiedOutcome,
			Messages = [new InfoMessage(
				"sync-schemas: schema already existed (already-satisfied); inline seed-rows were SKIPPED to stay "
				+ "replay-safe (no-Name/no-Id rows are not idempotent). To seed an existing schema, submit a "
				+ "standalone seed-data operation, which reconciles rows by key — the response's resume-plan "
				+ "already carries it, ready to resubmit if the rows are not yet on the server.")]
		};

	// Synthesizes the seed-only resume operation for the case where a create succeeded but its inline
	// seeding failed — resubmitting the original create op would collide with the schema just created.
	private static SchemaSyncOperation BuildSeedResumeOperation(SchemaSyncOperation op) =>
		new(SeedDataOperationName, op.SchemaName, SeedRows: op.SeedRows);

	// Assembles the resume plan for a mid-batch abort: the failed operation followed by every operation
	// that never ran, all echoed in re-submittable input shape. A fully-successful batch still gets a plan
	// when it deliberately skipped an inline seed (see BuildDeferredSeedResumePlan); otherwise null.
	private static SchemaSyncResumePlan? BuildResumePlan(
		IReadOnlyList<SchemaSyncOperation> operations, BatchExecutionState state) {
		if (state.AbortedAtIndex < 0 || state.FailedResumeOperation is null) {
			return BuildDeferredSeedResumePlan(state);
		}
		SchemaSyncOperationResult? failedResult = state.Results.LastOrDefault(r => !r.Success);
		var notRunIndexes = new List<int>();
		var resumeOperations = new List<SchemaSyncOperation>();
		if (state.FailedOperationIsResubmittableVerbatim) {
			resumeOperations.Add(state.FailedResumeOperation);
		}
		for (int index = state.AbortedAtIndex + 1; index < operations.Count; index++) {
			notRunIndexes.Add(index);
			resumeOperations.Add(operations[index]);
		}
		// Earlier operations in this batch may have skipped their inline seed (already-satisfied create) —
		// carry those standalone seed-data ops along so an abort does not drop the deferred seeding.
		resumeOperations.AddRange(state.DeferredSeedOperations.Select(deferred => deferred.Operation));
		// An empty `operations` is a legitimate plan and is still EMITTED (PR #1354 review). This happens on a
		// shape rejection of the last (or only) operation with no deferred seeds - the common single-operation
		// LLM call. Suppressing the whole plan there made `resume-plan` absent on an abort, which contradicts
		// the served contract and drops the structured `failed-operation` summary (operation-index / type /
		// schema-name / error) consumers key off. The shape-rejection instruction below already tells the
		// caller to correct the field names and resubmit the operation itself, so an empty `operations` array
		// advertises no recovery path that does not exist - it says "nothing here is resubmittable AS SENT".
		return new SchemaSyncResumePlan {
			Instruction = state.FailedOperationIsResubmittableVerbatim
				? "Batch aborted before completing. Resubmit ONLY the operations in resume-plan.operations "
					+ "(the failed operation, the not-run operations, and any deferred seed-data operations) as a new "
					+ "sync-schemas call; do NOT resubmit the operations already marked completed."
				: "Batch aborted before completing. The failed operation was rejected for its FIELD SHAPE and is "
					+ "deliberately NOT included in resume-plan.operations — correct the field names reported in its "
					+ "error, then resubmit it together with the operations listed here (the not-run operations and any "
					+ "deferred seed-data operations); do NOT resubmit the operations already marked completed.",
			FailedOperation = new SchemaSyncResumeFailure(
				state.AbortedAtIndex,
				failedResult?.Type ?? state.FailedResumeOperation.Type,
				state.FailedResumeOperation.SchemaName,
				failedResult?.Error),
			NotRunOperationIndexes = notRunIndexes,
			Operations = resumeOperations
		};
	}

	/// <summary>
	/// Builds the success-path resume plan for inline seed steps that were deliberately skipped because their
	/// create converged to <c>already-satisfied</c>. Nothing failed, so <c>failed-operation</c> stays null —
	/// the plan is a pure recovery affordance: the `already-satisfied` outcome cannot distinguish "the schema
	/// and its rows already existed" from "attempt 1 of THIS call created it but lost its response", and in the
	/// latter case the rows were never seeded. Offering the equivalent standalone seed-data op (which
	/// reconciles by key) keeps a success-keyed consumer from silently losing those writes, without changing
	/// the replay-safe skip semantics. Returns null when no seed step was skipped.
	/// </summary>
	private static SchemaSyncResumePlan? BuildDeferredSeedResumePlan(BatchExecutionState state) {
		if (state.DeferredSeedOperations.Count == 0) {
			return null;
		}
		return new SchemaSyncResumePlan {
			Instruction = "Batch completed, but the inline seed-rows of the operations listed in "
				+ "resume-plan.not-run-operation-indexes were SKIPPED to stay replay-safe (their create converged to "
				+ "'already-satisfied'). If those rows are not yet present on the server — e.g. the create landed on an "
				+ "earlier retry attempt of this same call and lost its response — resubmit resume-plan.operations "
				+ "(standalone seed-data operations, which reconcile rows by key) as a new sync-schemas call. "
				+ "Do NOT resubmit the create operations; they are already satisfied.",
			NotRunOperationIndexes = [.. state.DeferredSeedOperations.Select(deferred => deferred.Index)],
			Operations = [.. state.DeferredSeedOperations.Select(deferred => deferred.Operation)]
		};
	}

	/// <summary>
	/// Mutable bookkeeping for a single <c>sync-schemas</c> batch run: the per-operation results, the
	/// stop-on-first-failure abort point with its re-submittable operation, and the inline seed steps that were
	/// deliberately skipped and therefore surfaced as deferred seed-data operations in the resume plan.
	/// Not thread-safe by design — one instance per call, used only while the call holds the per-tenant lock.
	/// </summary>
	private sealed class BatchExecutionState {

		public List<SchemaSyncOperationResult> Results { get; } = [];

		public int AbortedAtIndex { get; private set; } = -1;

		public SchemaSyncOperation? FailedResumeOperation { get; private set; }

		/// <summary>
		/// Whether the failed operation may be resubmitted BYTE-FOR-BYTE. False when it was rejected for its
		/// field shape (an unbindable key, or a missing schema-name): echoing such an operation back under a
		/// "resubmit these" instruction would tell the caller to replay the very payload just rejected.
		/// </summary>
		public bool FailedOperationIsResubmittableVerbatim { get; private set; } = true;

		public List<(int Index, SchemaSyncOperation Operation)> DeferredSeedOperations { get; } = [];

		/// <summary>
		/// Records the stop-on-first-failure abort point and the operation shape to resubmit for it.
		/// </summary>
		public void Abort(int index, SchemaSyncOperation resumeOperation, bool resubmittableVerbatim = true) {
			AbortedAtIndex = index;
			FailedResumeOperation = resumeOperation;
			FailedOperationIsResubmittableVerbatim = resubmittableVerbatim;
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
) {
	/// <summary>
	/// Overflow bag for top-level fields that did not bind. Without it a camelCase
	/// <c>environmentName</c>/<c>packageName</c> is dropped by System.Text.Json and the batch runs against a
	/// null environment instead of telling the caller to rename the field.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

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
	/// Recovery affordance emitted when the batch aborted before completing (enumerating the failed and
	/// not-run operations) and also on a fully-successful batch that deliberately skipped an inline seed step
	/// because its create converged to <c>already-satisfied</c> — in that case <c>failed-operation</c> is null
	/// and <c>operations</c> carries the equivalent standalone seed-data ops. Either way it provides a
	/// ready-to-resubmit <c>operations</c> array (ENG-93374/ENG-93807).
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
	/// Zero-based index of the originating operation in the request <c>operations</c> array (ENG-93374), or
	/// <c>-1</c> when the result is about the CALL rather than about an operation (a whole-call argument
	/// rejection, where no operation ran or was examined).
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
