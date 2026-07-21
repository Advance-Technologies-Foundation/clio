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
	ISchemaEnrichmentService? enrichmentService = null,
	IRetryDelay? retryDelay = null,
	TimeSpan? maxCumulativeRetryDelay = null) {

	internal const string ToolName = "sync-schemas";
	private const string CreateLookupOperationName = "create-lookup";
	private const string CreateEntityOperationName = "create-entity";
	private const string UpdateEntityOperationName = "update-entity";
	private const string SeedDataOperationName = "seed-data";

	/// <summary>
	/// Total number of attempts (including the first) for an operation whose failure is classified as a
	/// transient network fault (ENG-93374).
	/// </summary>
	internal const int MaxAttempts = 3;

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
		"Transient network failures (DNS resolution, connection reset/refused, timeouts, gateway errors) are retried per operation " +
		"(up to 3 attempts with short backoff) before the operation is failed. " +
		"On a mid-batch abort the response carries a 'resume-plan' with per-operation status (completed/failed/not-run) and a " +
		"ready-to-resubmit 'operations' array — resubmit ONLY resume-plan.operations, never the whole batch. " +
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
		// Preserve a status already set by FinalizeResult (e.g. the forced "resumed-existing"); only default
		// it when nothing set one (validation/unknown-op/catch-path results that never reach FinalizeResult).
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
			CreateEntitySchemaOptions options = CreateEntitySchemaTool.CreateOptions(
				new CreateLookupArgs(
					args.PackageName, op.SchemaName,
					new Dictionary<string, string>(titleLocalizations, StringComparer.OrdinalIgnoreCase), args.EnvironmentName,
					op.Columns),
				parentSchemaName, extendParent,
				isVirtual: string.Equals(operationName, CreateEntityOperationName, StringComparison.Ordinal)
					&& op.IsVirtual);
			// Retry the create step on transient network faults, resolving a fresh command per attempt.
			OperationExecution createExecution = RunAttempts(() =>
				commandResolver.Resolve<CreateEntitySchemaCommand>(options).Execute(options), retryBudget);
			OperationExecution execution = createExecution;
			// Probe the collision at most once per create op and reuse it for both the idempotent-resume
			// decision and the failure-result hint, so a failed create-lookup never issues two identical
			// FindEntitySchema round-trips under the per-tenant lock.
			SchemaSyncCollisionInfo? collision = null;
			bool collisionProbed = false;
			bool resumedExisting = false;
			if (isLookup) {
				LookupResumeDecision resume = ResolveLookupCreateResume(op, args, createExecution);
				execution = resume.Execution;
				collision = resume.Collision;
				collisionProbed = resume.CollisionProbed;
				resumedExisting = resume.ResumedExisting;
				// A transient fault in registration is retried on its OWN scope, so a registration flap
				// never re-runs the (already applied) create.
				if (resume.SchemaApplied) {
					OperationExecution registration = RunAttempts(() => {
						ILookupRegistrationService registrationService =
							commandResolver.Resolve<ILookupRegistrationService>(options);
						registrationService.EnsureLookupRegistration(
							args.PackageName,
							op.SchemaName,
							EntitySchemaLocalizationContract.GetDefaultTitle(titleLocalizations, context));
						return 0;
					}, retryBudget);
					execution = execution.Append(registration);
				}
			}
			return FinalizeResult(operationName, op.SchemaName, execution, tenantKey,
				new CollisionProbeContext(args, collision, collisionProbed),
				forcedStatus: resumedExisting ? "resumed-existing" : null);
		} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
			// Deterministic option-building failures (localization/guardrail validation) are not network
			// faults and are never retried — surface them exactly as before.
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = op.SchemaName,
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				Messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)],
				CollisionInfo = TryGetCollisionInfo(op.SchemaName, args)
			};
		}
	}

	// Idempotent-resume decision for a create-lookup after the create attempt. On a same-package collision it
	// verifies the requested columns and turns a "lost response" into a legitimate resume (Verified), a
	// warning-degraded resumed-existing (Unverified), or leaves a durable collision as a genuine failure
	// (Missing) — ENG-93374. The single collision probe is captured for reuse by the failure-result hint.
	private LookupResumeDecision ResolveLookupCreateResume(
		SchemaSyncOperation op, SchemaSyncArgs args, OperationExecution createExecution) {
		bool schemaApplied = createExecution.ExitCode == 0 && createExecution.CaughtException is null;
		if (schemaApplied || createExecution.CaughtException is not null) {
			return new LookupResumeDecision(createExecution, schemaApplied, false, null, false);
		}
		SchemaSyncCollisionInfo? collision = TryGetCollisionInfo(op.SchemaName, args);
		if (collision is null
			|| !string.Equals(collision.ExistingPackageName, args.PackageName, StringComparison.OrdinalIgnoreCase)) {
			return new LookupResumeDecision(createExecution, false, false, collision, true);
		}
		// Idempotent resume: the schema already exists in the TARGET package. A prior (possibly interrupted)
		// attempt may have applied the create, so instead of failing blindly on the collision, verify the
		// requested columns against the schema's actual columns and only resume when they landed. A same-package
		// collision with non-empty columns issues exactly one extra read-only round-trip to answer "did my
		// columns land?".
		bool hasColumns = op.Columns?.Any() == true;
		// Empty columns → resume exactly as before (FR-01 fast-path, no read round-trip).
		ColumnVerification outcome = ColumnVerification.Verified;
		IReadOnlyList<string> missing = [];
		Exception? probeFault = null;
		if (hasColumns) {
			(outcome, missing, probeFault) = VerifyRequestedColumns(op, args);
		}
		switch (outcome) {
			case ColumnVerification.Verified:
				// FR-04/FR-01: legitimate resume. Build a fresh info note ONLY — the failed create's
				// Error-level messages are dropped so a completed result never carries them (FR-06).
				return new LookupResumeDecision(
					new OperationExecution(0, null,
						[new InfoMessage($"sync-schemas: '{op.SchemaName}' already exists in package '{args.PackageName}'; skipping re-create and completing lookup registration.")],
						createExecution.Attempts),
					true, false, collision, true);
			case ColumnVerification.Unverified:
				// FR-05: the column read could not run, so we cannot confirm the columns landed. Degrade to the
				// distinct resumed-existing status with a warning (never blind success), carrying a fresh
				// warning line ONLY (FR-06).
				return new LookupResumeDecision(
					new OperationExecution(0, null,
						[new WarningMessage($"sync-schemas: '{op.SchemaName}' already exists in package '{args.PackageName}' but the requested columns could NOT be verified ({DescribeProbeFault(probeFault)}); completing registration, but the requested columns are NOT confirmed present — verify with get-entity-schema-properties or resubmit.")],
						createExecution.Attempts),
					true, true, collision, true);
			default:
				// FR-03: a confirmed durable collision — the schema pre-existed WITHOUT the requested columns.
				// Do NOT force success: leave SchemaApplied=false so the normal failure path returns
				// success:false plus the "use update-entity to add columns" collision hint; registration is NOT
				// invoked. Name the missing columns for actionability while preserving the genuine failure
				// diagnostics.
				return new LookupResumeDecision(
					new OperationExecution(
						createExecution.ExitCode, createExecution.CaughtException,
						[.. createExecution.Messages,
							new WarningMessage($"sync-schemas: '{op.SchemaName}' already exists in package '{args.PackageName}' but is missing the requested column(s): {string.Join(", ", missing)}. Use update-entity to add them.")],
						createExecution.Attempts),
					false, false, collision, true);
		}
	}

	// Result of the create-lookup idempotent-resume decision: the (possibly rewritten) execution, whether the
	// schema is considered applied (so lookup registration should run), whether the resume degraded to the
	// resumed-existing status, and the single reused collision probe (with a flag marking it already probed).
	private readonly record struct LookupResumeDecision(
		OperationExecution Execution, bool SchemaApplied, bool ResumedExisting,
		SchemaSyncCollisionInfo? Collision, bool CollisionProbed);

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
				? "Schema already exists in the target package. A collision after a network retry usually means the create WAS applied server-side despite the lost response — skip this operation and resume with the remaining ops, or use update-entity to add columns / proceed to seed-data without recreating."
				: $"Schema already exists in package '{existing.PackageName}'. Reuse it by referencing it without creation, or call delete-schema first to remove the stale version before recreating.";
			return new SchemaSyncCollisionInfo(existing.PackageName, hint);
		} catch {
			return null;
		}
	}

	/// <summary>
	/// Outcome of verifying a create-lookup op's requested columns against the existing target-package
	/// schema on a same-package collision.
	/// </summary>
	private enum ColumnVerification {
		/// <summary>Every requested column is already present (or none were requested) — legitimate resume.</summary>
		Verified,
		/// <summary>The read succeeded and at least one requested column is absent — a durable collision.</summary>
		Missing,
		/// <summary>The column read could not run (threw or exited non-zero), so presence is unconfirmed.</summary>
		Unverified
	}

	// Verifies the create-lookup op's requested columns against the existing schema's actual columns via a
	// SINGLE read-only round-trip (reusing the resolver, like the collision probe — NOT wrapped in RunAttempts).
	// Routes on read-success vs read-failure: a "missing" answer can only come from a successful read, so a
	// probe fault degrades to Unverified (never Missing), which the caller turns into resumed-existing rather
	// than a false durable-collision failure. The caller invokes this only for a same-package collision with
	// non-empty columns; the empty-columns fast-path never reads.
	private (ColumnVerification Outcome, IReadOnlyList<string> Missing, Exception? ProbeFault)
		VerifyRequestedColumns(SchemaSyncOperation op, SchemaSyncArgs args) {
		IReadOnlyList<string> requested = (op.Columns ?? [])
			.Select(column => column.ResolveName())
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name!)
			.ToList();
		if (requested.Count == 0) {
			return (ColumnVerification.Verified, [], null);
		}
		try {
			GetEntitySchemaPropertiesOptions readOptions = new() {
				Environment = args.EnvironmentName,
				SchemaName = op.SchemaName
			};
			// Read the merged/effective schema (no --package filter): lookup columns are own-to-target-package,
			// so name-presence in the merged view is equivalent to the target-package layer, and merged is simpler.
			EntitySchemaPropertiesInfo properties = commandResolver
				.Resolve<GetEntitySchemaPropertiesCommand>(readOptions)
				.GetSchemaProperties(readOptions);
			var existing = new HashSet<string>(
				(properties.Columns ?? []).Select(column => column.Name), StringComparer.OrdinalIgnoreCase);
			IReadOnlyList<string> missing = requested.Where(name => !existing.Contains(name)).ToList();
			return missing.Count == 0
				? (ColumnVerification.Verified, [], null)
				: (ColumnVerification.Missing, missing, null);
		} catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
			return (ColumnVerification.Unverified, [], ex);
		}
	}

	// Text-only enrichment of the resumed-existing warning: names the likely cause of an unverifiable read.
	// The transient classifier is used ONLY to phrase the warning — it never routes (routing is strictly
	// read-success vs read-failure), so it cannot swallow a real missing-column signal.
	private static string DescribeProbeFault(Exception? probeFault) =>
		TransientNetworkFailureClassifier.IsTransient(probeFault)
			? "transient network fault"
			: "the existing schema could not be read";

	private SchemaSyncOperationResult ExecuteUpdateEntity(SchemaSyncOperation op, SchemaSyncArgs args, string tenantKey, RetryBudget retryBudget) {
		try {
			IReadOnlyList<UpdateEntitySchemaOperationArgs> updateOperations = ResolveUpdateOperations(op);
			if (updateOperations.Count == 0) {
				return new SchemaSyncOperationResult {
					Type = UpdateEntityOperationName,
					SchemaName = op.SchemaName,
					Success = false, Error = BuildMissingUpdateOperationsError()
				};
			}
			UpdateEntitySchemaOptions options = new() {
				Environment = args.EnvironmentName,
				Package = args.PackageName,
				SchemaName = op.SchemaName,
				Operations = UpdateEntitySchemaTool.SerializeOperations(updateOperations, op.SchemaName)
			};
			OperationExecution execution = RunAttempts(() =>
				commandResolver.Resolve<UpdateEntitySchemaCommand>(options).Execute(options), retryBudget);
			return FinalizeResult(UpdateEntityOperationName, op.SchemaName, execution, tenantKey, CollisionProbeContext.None);
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
			return FinalizeResult(SeedDataOperationName, op.SchemaName, execution, tenantKey, CollisionProbeContext.None);
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

	// Bundles the collision-probe context so FinalizeResult stays within the parameter limit. Create ops pass
	// their args (and, when they already probed the collision, the reused result plus AlreadyProbed=true);
	// non-create ops pass CollisionProbeContext.None so no probe is attempted.
	private readonly record struct CollisionProbeContext(
		SchemaSyncArgs? Args, SchemaSyncCollisionInfo? Precomputed, bool AlreadyProbed) {
		public static readonly CollisionProbeContext None = new(null, null, false);
	}

	// Builds the final operation result from a completed execution (success, transient-exhausted, or a
	// non-transient failure). collision.Args is non-null only for create operations, where a probe adds a
	// hint when the schema already exists on the server.
	private SchemaSyncOperationResult FinalizeResult(
		string operationName, string schemaName, OperationExecution execution, string tenantKey,
		CollisionProbeContext collision, string? forcedStatus = null) {
		IReadOnlyList<LogMessage> messages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. execution.Messages], tenantKey)];
		int? attempts = execution.Attempts > 1 ? execution.Attempts : null;
		// Reuse a collision already probed by the caller (create path) rather than issuing a second
		// identical FindEntitySchema round-trip under the lock.
		SchemaSyncCollisionInfo? ResolveCollision() {
			if (collision.AlreadyProbed) {
				return collision.Precomputed;
			}
			return collision.Args is not null ? TryGetCollisionInfo(schemaName, collision.Args) : null;
		}
		if (execution.CaughtException is not null) {
			// A registration (or other) throw after a resumed-existing degrade must surface as failed —
			// the forced status only sticks on a genuinely successful, post-registration execution.
			return new SchemaSyncOperationResult {
				Type = operationName,
				SchemaName = schemaName,
				Success = false,
				Status = "failed",
				Error = SensitiveErrorTextRedactor.Redact(execution.CaughtException.Message),
				Messages = messages,
				CollisionInfo = ResolveCollision(),
				Attempts = attempts
			};
		}
		bool success = execution.ExitCode == 0;
		return new SchemaSyncOperationResult {
			Type = operationName,
			SchemaName = schemaName,
			Success = success,
			// Derive from the FINAL execution: a forced status (resumed-existing) sticks only when the whole
			// op — including the outstanding lookup registration — reached exit 0; otherwise it is failed.
			Status = success ? (forcedStatus ?? "completed") : "failed",
			Messages = messages,
			Error = BuildOperationError(operationName, execution.ExitCode, messages),
			CollisionInfo = !success ? ResolveCollision() : null,
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
