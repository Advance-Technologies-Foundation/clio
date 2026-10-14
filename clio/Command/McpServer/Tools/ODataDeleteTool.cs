using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for deleting Creatio records by id via OData v4 (HTTP DELETE), explaining every refusal.
/// </summary>
[McpServerToolType]
public sealed class ODataDeleteTool(
	IToolCommandResolver commandResolver,
	IOperationCorrelationIdProvider correlationIds) {

	internal const string ToolName = "odata-delete";

	/// <summary>Most ids one call takes.</summary>
	internal const int MaxIds = 50;

	/// <summary>Most rows a cascade deletes for one target; a larger tree is refused, not truncated.</summary>
	internal const int MaxCascadeRows = 100;

	/// <summary>How many levels of owned children a cascade follows.</summary>
	internal const int MaxCascadeDepth = 3;

	/// <summary>Most ids one cascade call takes; every id gets its own diagnosis budget.</summary>
	internal const int MaxCascadeIds = 10;

	// Shared master data: a required lookup to them is a reference, not ownership, so cascade never starts there.
	private static readonly HashSet<string> CascadeRefusedEntities = new(StringComparer.OrdinalIgnoreCase) {
		"Contact", "SysAdminUnit", "Employee", "SysUserInRole"
	};

	private const int RequestTimeoutMs = 30_000;
	private const int MinimumDeleteMs = 1_000;
	private const string DeleteVerb = "delete";
	// Passes over an ids batch: a record refused because another of the batch still referenced it goes again.
	private const int MaxBatchPasses = 3;
	private const int DiagnosisRequestMs = 10_000;
	private static readonly TimeSpan DiagnosisBudget = TimeSpan.FromSeconds(25);

	// The parent kills a ParentKillDefault worker at 120 s and the answer is then lost, together with the list of
	// what was already deleted. The whole call therefore ends by CallBudget, and an id is only started while
	// IdStartReserve is still left for its diagnosis and its DELETE.
	private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(100);
	private static readonly TimeSpan IdStartReserve = TimeSpan.FromSeconds(30);

	/// <summary>Deletes Creatio records by id using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Delete Creatio records by GUID via OData v4 (DELETE): one `id`, or up to 50 `ids` of the same entity. " +
		"This tool never performs a keyless mass delete. " +
		"This is a destructive operation: it requires confirm=true to proceed. " +
		"A refusal is always explained in `explanation`: the server's own reason (\"An error has occurred.\" hides it), " +
		"the rows that still reference the record (`referencing`, each owned/linked/cascade), a binding to a running " +
		"process, or the rights/event-handler cause when none of those applies. " +
		"`cascade: true` also deletes OWNED children - rows whose required lookup points at the record - deepest " +
		"first, after checking the whole tree; anything the check can see (a linked row, a process binding, an " +
		"unreadable table, a walked row this user cannot read) refuses it with nothing deleted. Never for a lookup " +
		"(dictionary) record. It needs the `cascade-plan-hash` of a dry run and refuses when the plan changed since. " +
		"A refusal the check cannot see in advance (delete rights, an event handler, a row hidden by record rights) " +
		"can still stop the record after its owned rows went; `cascade-deleted` then lists them. `dry-run: true` " +
		"lists what would be deleted and what blocks it, deletes nothing and needs no confirm. " +
		McpToolDescriptions.CorrelationIdOnEveryResponse +
		"Use odata-read to find the record by its fields and obtain its Id. " +
		"Call get-tool-contract for odata-delete to see usage examples and discovery workflow hints.")]
	public ODataWriteResponse Delete(
		[Description("Parameters: entity, environment-name, and id or ids.")]
		[Required]
		ODataDeleteArgs args) {
		//Minted once and stamped on the single exit, so every response carries the correlation-id
		//core-rules promises - refusals and validation failures included.
		string correlationId = correlationIds.New();
		ODataWriteResponse result = DeleteCore(args, out bool attempted, out bool received);
		return result with {
			CorrelationId = correlationId,
			Diagnostic = result.Plan is not null ? null : DataWriteDiagnostic.Create(DeleteVerb, args.Entity, null,
				attempted, received, result.Success, result.Error)
		};
	}

	private ODataWriteResponse DeleteCore(ODataDeleteArgs args, out bool attempted, out bool received) {
		attempted = false;
		received = false;
		try {
			ODataWriteResponse invalid = ValidateTargets(args, out List<string> ids);
			if (invalid is not null) {
				return invalid;
			}
			string entity = args.Entity.Trim();
			if (!args.DryRun) {
				ODataWriteResponse notConfirmed = ODataKeyedWrite.RequireConfirmation(args.Confirm, entity,
					string.Join(", ", ids), DeleteVerb, "deletion");
				if (notConfirmed is not null) {
					return notConfirmed;
				}
			}
			if (args.Cascade && CascadeRefusedEntities.Contains(entity)) {
				return ODataWriteResponse.Failure($"cascade is refused for {entity}: rows that require a {entity} only "
					+ "refer to it, they are not owned by it. Delete without cascade, and read explanation for what blocks it.");
			}
			if (args.Cascade && ids.Count > MaxCascadeIds) {
				return ODataWriteResponse.Failure($"cascade takes at most {MaxCascadeIds} ids per call.");
			}
			IRecordDeletionService service = commandResolver.Resolve<IRecordDeletionService>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			DateTimeOffset callDeadline = DateTimeOffset.UtcNow + CallBudget;
			if (args.Cascade && service.IsLookup(entity, DiagnosisRequestMs)) {
				return ODataWriteResponse.Failure($"cascade is refused for {entity}: it is a lookup (dictionary), or could "
					+ "not be shown not to be one, so the rows that require one of its values only refer to it - they are "
					+ "not owned by it. Delete without cascade, and read explanation for what blocks it.");
			}
			if (args.DryRun) {
				return BuildDryRun(service, entity, ids, args.Cascade, callDeadline);
			}
			return args.Cascade
				? DeleteWithCascade(args, service, entity, ids, callDeadline, ref attempted, ref received)
				: DeleteWithoutCascade(args, service, entity, ids, callDeadline, ref attempted, ref received);
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	internal const string HiddenRowsNote = "Rows this user cannot read because of record-level rights are not returned to "
		+ "the check, so they are not listed here; such a row can still refuse a delete, and with cascade it can stop "
		+ "the record after its owned rows were deleted.";

	// No top-level id: a dry run deleted nothing, and an id there would read like a deletion.
	private static ODataWriteResponse BuildDryRun(IRecordDeletionService service, string entity, List<string> ids,
		bool cascade, DateTimeOffset callDeadline) {
		List<(string Id, CascadePlan Plan)> plans = [];
		List<ODataDeletePlanItem> items = [];
		foreach (string id in ids) {
			if (DateTimeOffset.UtcNow >= callDeadline - IdStartReserve) {
				plans.Add((id, null));
				items.Add(new ODataDeletePlanItem(id, null, [], [], cascade ? NotCheckedInTime : null,
					cascade ? null : NotCheckedInTime, false));
				continue;
			}
			(ODataDeletePlanItem item, CascadePlan plan) = BuildPlan(service, entity, id, cascade, DiagnosisStop(callDeadline));
			plans.Add((id, plan));
			items.Add(item);
		}
		return new ODataWriteResponse(true) {
			Plan = items,
			CascadePlanHash = cascade ? HashPlans(plans) : null,
			Note = HiddenRowsNote
		};
	}

	private const string NotCheckedInTime = "not checked: this call's time budget was used up by the ids before it. "
		+ "Call again for it.";

	// The plan the caller approved is bound to the call: the destructive call recomputes it and deletes only when it
	// is the one the dry run showed, so owned rows that appeared since are never deleted unseen.
	private static ODataWriteResponse DeleteWithCascade(ODataDeleteArgs args, IRecordDeletionService service,
		string entity, List<string> ids, DateTimeOffset callDeadline, ref bool attempted, ref bool received) {
		if (string.IsNullOrWhiteSpace(args.CascadePlanHash)) {
			return ODataWriteResponse.Failure("cascade needs cascade-plan-hash: run the same call with dry-run: true, "
				+ "show the user cascade-would-delete, and pass the cascade-plan-hash it returns together with confirm. "
				+ "Nothing was deleted.");
		}
		List<(string Id, CascadePlan Plan)> plans = ids
			.Select(id => (id, DateTimeOffset.UtcNow >= callDeadline - IdStartReserve
				? null
				: CollectOwned(service, entity, id, DiagnosisStop(callDeadline))))
			.ToList();
		if (!string.Equals(args.CascadePlanHash.Trim(), HashPlans(plans), StringComparison.OrdinalIgnoreCase)) {
			int owned = plans.Sum(plan => plan.Plan?.Order.Count ?? 0);
			return ODataWriteResponse.Failure($"The cascade plan is not the one cascade-plan-hash was made from: it now "
				+ $"deletes {owned} owned row(s), so rows were added, removed or became unreadable since the dry run. "
				+ "Nothing was deleted. Run dry-run again, show the user the new plan, and pass its hash.");
		}
		HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase);
		List<ODataDeleteItemResult> results = [];
		foreach ((string id, CascadePlan plan) in plans) {
			if (removed.Contains(KeyOf(entity, id))) {
				results.Add(new ODataDeleteItemResult(id, true, null,
					"Deleted as an owned row of an earlier id's cascade in this call.", null, null));
				continue;
			}
			ODataDeleteItemResult result = plan is null
				? new ODataDeleteItemResult(id, false, NotAttemptedInTime, null, null, null)
				: DeleteOne(service, entity, id, plan, callDeadline, ref attempted, ref received);
			removed.UnionWith((result.CascadeDeleted ?? []).Select(key => KeyOf(key.Entity, key.Id)));
			if (result.Success) {
				removed.Add(KeyOf(entity, id));
			}
			results.Add(result);
		}
		return BuildDeleteResponse(args, results);
	}

	private const string NotAttemptedInTime = "not attempted: this call's time budget was used up by the ids before "
		+ "it; nothing was deleted for this id. Call again for it.";

	private static string KeyOf(string entity, string id) =>
		entity + "(" + (Guid.TryParse(id, out Guid parsed) ? parsed.ToString("D") : id) + ")";

	// One DELETE per id; an id refused while others in the batch were deleted is tried again after them (a parent
	// listed before its child), while a pass still deletes something. Only what is still refused is diagnosed.
	private static ODataWriteResponse DeleteWithoutCascade(ODataDeleteArgs args, IRecordDeletionService service,
		string entity, List<string> ids, DateTimeOffset callDeadline, ref bool attempted, ref bool received) {
		Dictionary<string, ODataDeleteItemResult> results = new(StringComparer.OrdinalIgnoreCase);
		List<string> pending = SkipMissing(service, entity, ids, callDeadline, results);
		BatchState batch = new(results);
		for (int pass = 0; pass < MaxBatchPasses && pending.Count > 0; pass++) {
			List<string> again = DeletePass(service, entity, pending, callDeadline, batch);
			bool progressed = again.Count < pending.Count;
			pending = again;
			if (!progressed) {
				break;
			}
		}
		attempted |= batch.Attempted;
		received |= batch.Received;
		foreach (string id in pending) {
			results[id] = batch.Refusals.TryGetValue(id, out RecordDeleteOutcome refusal)
				? Explain(service, entity, id, refusal, callDeadline, null)
				: new ODataDeleteItemResult(id, false, $"{entity}({id}) was not deleted: this call's time budget ran out "
					+ "before its DELETE could be sent.", null, null, null);
		}
		return BuildDeleteResponse(args, ids.Select(id => results[id]).ToList());
	}

	// The ids to delete: a DELETE of a missing record is answered like a deletion, so each is looked up first, and a
	// missing one is answered "not found" (QA D2).
	private static List<string> SkipMissing(IRecordDeletionService service, string entity, List<string> ids,
		DateTimeOffset callDeadline, Dictionary<string, ODataDeleteItemResult> results) {
		List<string> pending = [];
		foreach (string id in ids) {
			if (DateTimeOffset.UtcNow >= callDeadline - IdStartReserve) {
				results[id] = new ODataDeleteItemResult(id, false, NotAttemptedInTime, null, null, null);
			}
			else if (DeleteBudget(callDeadline) is { } existsMs
					&& service.Exists(entity, id, Math.Min(existsMs, DiagnosisRequestMs)) == false) {
				results[id] = new ODataDeleteItemResult(id, false, $"{entity}({id}) was not found: it does not exist, was "
					+ "already deleted, or this user cannot read it. Nothing was deleted.", null, null, null);
			}
			else {
				pending.Add(id);
			}
		}
		return pending;
	}

	// One pass over the pending ids; returns those refused (or not sent for lack of time), to try again.
	private sealed class BatchState(Dictionary<string, ODataDeleteItemResult> results) {
		public Dictionary<string, ODataDeleteItemResult> Results { get; } = results;
		public Dictionary<string, RecordDeleteOutcome> Refusals { get; } = new(StringComparer.OrdinalIgnoreCase);
		public bool Attempted { get; set; }
		public bool Received { get; set; }
	}

	private static List<string> DeletePass(IRecordDeletionService service, string entity, List<string> pending,
		DateTimeOffset callDeadline, BatchState batch) {
		List<string> again = [];
		foreach (string id in pending) {
			if (DeleteBudget(callDeadline) is not { } deleteMs) {
				again.Add(id);
				continue;
			}
			RecordDeleteOutcome outcome = service.Delete(entity, id, deleteMs);
			batch.Attempted |= outcome.Attempted;
			batch.Received |= outcome.Received;
			if (outcome.Success || !outcome.Received) {
				// A transport failure has no refusal to explain, and its write may have landed.
				batch.Results[id] = new ODataDeleteItemResult(id, outcome.Success, outcome.Success ? null : outcome.Error,
					null, null, null);
				batch.Refusals.Remove(id);
			}
			else {
				batch.Refusals[id] = outcome;
				again.Add(id);
			}
		}
		return again;
	}

	// A budget of its own for the diagnosis: what ran before may have spent the first one, and this is the explanation
	// the caller needs most.
	private static ODataDeleteItemResult Explain(IRecordDeletionService service, string entity, string id,
		RecordDeleteOutcome outcome, DateTimeOffset callDeadline, IReadOnlyList<RecordKey> removed) {
		RecordDeleteDiagnosis diagnosis = service.Diagnose(entity, id, DiagnosisRequestMs, DiagnosisStop(callDeadline));
		return new ODataDeleteItemResult(id, false, outcome.Error, DeleteRefusalText.Explain(entity, id, outcome, diagnosis),
			diagnosis.Referencing.Count > 0 ? diagnosis.Referencing : null, removed);
	}

	// The id the caller passed decides the shape, so `id` with an empty `ids: []` still answers like a single.
	private static ODataWriteResponse BuildDeleteResponse(ODataDeleteArgs args, List<ODataDeleteItemResult> results) {
		if (!string.IsNullOrWhiteSpace(args.Id)) {
			ODataDeleteItemResult only = results[0];
			return new ODataWriteResponse(only.Success, only.Error, only.Success ? only.Id : null) {
				Explanation = only.Explanation,
				Referencing = only.Referencing,
				CascadeDeleted = only.CascadeDeleted,
				CascadeUnconfirmed = only.CascadeUnconfirmed
			};
		}
		int failed = results.Count(result => !result.Success);
		return new ODataWriteResponse(failed == 0,
			failed == 0 ? null : $"{failed} of {results.Count} records were not deleted; results explains each.") {
			Results = results
		};
	}

	private static ODataWriteResponse ValidateTargets(ODataDeleteArgs args, out List<string> ids) {
		ids = [];
		bool hasId = !string.IsNullOrWhiteSpace(args.Id);
		bool hasIds = args.Ids is { Length: > 0 };
		if (hasId == hasIds) {
			ODataWriteResponse invalidEntity = ODataKeyedWrite.ValidateTarget(args.Entity, Guid.Empty.ToString(), DeleteVerb);
			return invalidEntity ?? ODataWriteResponse.Failure(hasId
				? "Pass id or ids, not both."
				: "id is required and must be a record GUID (or pass ids, up to 50); keyless mass delete is not allowed.");
		}
		string[] requested = hasId ? [args.Id] : args.Ids;
		if (requested.Length > MaxIds) {
			return ODataWriteResponse.Failure($"ids takes at most {MaxIds} record GUIDs per call.");
		}
		foreach (string candidate in requested) {
			ODataWriteResponse invalid = ODataKeyedWrite.ValidateTarget(args.Entity, candidate, DeleteVerb);
			if (invalid is not null) {
				return invalid;
			}
		}
		ids = requested.Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		return null;
	}

	// A diagnosis gets DiagnosisBudget, but never so much that the DELETE after it would run past the call's end.
	private static DateTimeOffset DiagnosisStop(DateTimeOffset callDeadline) {
		DateTimeOffset stop = DateTimeOffset.UtcNow + DiagnosisBudget;
		DateTimeOffset latest = callDeadline - IdStartReserve;
		return stop < latest ? stop : latest;
	}

	// The DELETE timeout, never past the call's end; null when too little is left to start one.
	private static int? DeleteBudget(DateTimeOffset callDeadline) {
		double left = (callDeadline - DateTimeOffset.UtcNow).TotalMilliseconds;
		return left < MinimumDeleteMs ? null : (int)Math.Min(RequestTimeoutMs, left);
	}

	// Deletes the plan's owned rows deepest first, then the record.
	private static ODataDeleteItemResult DeleteOne(IRecordDeletionService service, string entity, string id,
		CascadePlan plan, DateTimeOffset callDeadline, ref bool attempted, ref bool received) {
		List<RecordKey> cascadeDeleted = [];
		ODataDeleteItemResult refused = DeleteOwnedChildren(service, new RecordKey(entity, id), plan, callDeadline,
			cascadeDeleted, ref attempted, ref received);
		if (refused is not null) {
			return refused;
		}
		IReadOnlyList<RecordKey> deletedChildren = cascadeDeleted.Count > 0 ? cascadeDeleted : null;
		if (DeleteBudget(callDeadline) is not { } deleteMs) {
			return new ODataDeleteItemResult(id, false, $"{entity}({id}) was not deleted: this call's time budget "
				+ "ran out before its DELETE could be sent" + (deletedChildren is null ? "." : "; cascade-deleted lists "
				+ "the owned rows already deleted."), null, null, deletedChildren);
		}
		RecordDeleteOutcome outcome = service.Delete(entity, id, deleteMs);
		attempted |= outcome.Attempted;
		received |= outcome.Received;
		if (outcome.Success) {
			return new ODataDeleteItemResult(id, true, null, null, null, deletedChildren);
		}
		if (!outcome.Received) {
			// A transport failure has no refusal to explain, and its write may have landed.
			return new ODataDeleteItemResult(id, false, outcome.Error, null, null, deletedChildren);
		}
		return Explain(service, entity, id, outcome, callDeadline, deletedChildren);
	}

	// Deletes the planned owned rows deepest first; a refusal or the first failed child is returned.
	private static ODataDeleteItemResult DeleteOwnedChildren(IRecordDeletionService service, RecordKey target,
		CascadePlan plan, DateTimeOffset callDeadline, List<RecordKey> cascadeDeleted, ref bool attempted,
		ref bool received) {
		if (plan.Refusal is not null) {
			return new ODataDeleteItemResult(target.Id, false, plan.Refusal, plan.Explanation, plan.Referencing, null);
		}
		foreach (RecordKey child in plan.Order) {
			if (DeleteBudget(callDeadline) is not { } childMs) {
				return new ODataDeleteItemResult(target.Id, false,
					$"cascade stopped: this call's time budget ran out before owned row {child.Entity}({child.Id}) "
					+ $"could be deleted, so {target.Entity}({target.Id}) was left in place"
					+ (cascadeDeleted.Count > 0 ? "; cascade-deleted lists the owned rows already deleted." : "."),
					null, plan.Referencing, (cascadeDeleted.Count > 0 ? cascadeDeleted : null));
			}
			RecordDeleteOutcome childOutcome = service.Delete(child.Entity, child.Id, childMs);
			attempted |= childOutcome.Attempted;
			received |= childOutcome.Received;
			if (!childOutcome.Success) {
				return ChildFailure(target, child, childOutcome, plan, cascadeDeleted);
			}
			cascadeDeleted.Add(child);
		}
		return null;
	}

	private static ODataDeleteItemResult ChildFailure(RecordKey target, RecordKey child, RecordDeleteOutcome outcome,
		CascadePlan plan, List<RecordKey> cascadeDeleted) {
		if (outcome.Attempted && !outcome.Received) {
			// No answer: the DELETE may have landed, so the row is neither listed as deleted nor as still there.
			return new ODataDeleteItemResult(target.Id, false,
				$"cascade stopped: the DELETE of owned row {child.Entity}({child.Id}) got no answer, so it may have "
				+ $"been deleted - check with odata-read; {target.Entity}({target.Id}) was left in place: "
				+ outcome.Error, null, plan.Referencing, (cascadeDeleted.Count > 0 ? cascadeDeleted : null)) {
				CascadeUnconfirmed = [child]
			};
		}
		string reason = outcome.ServerDetail is null
			? outcome.Error
			: $"{outcome.Error} The server's own reason: {outcome.ServerDetail}.";
		return new ODataDeleteItemResult(target.Id, false,
			$"cascade stopped: owned row {child.Entity}({child.Id}) was not deleted, so {target.Entity}({target.Id}) "
			+ $"was left in place: {reason}", null, plan.Referencing, (cascadeDeleted.Count > 0 ? cascadeDeleted : null));
	}

	private static (ODataDeletePlanItem Item, CascadePlan Plan) BuildPlan(IRecordDeletionService service, string entity,
		string id, bool cascade, DateTimeOffset stopAt) {
		RecordDeleteDiagnosis diagnosis = service.Diagnose(entity, id, DiagnosisRequestMs, stopAt);
		CascadePlan plan = cascade ? CollectOwned(service, entity, id, stopAt, diagnosis) : null;
		bool blocked = diagnosis.Visible == false || diagnosis.ProcessElementId is not null || !diagnosis.Complete
			|| diagnosis.Referencing.Any(rows => rows.Kind == ReferencingRows.LinkedKind
				|| (!cascade && rows.Kind == ReferencingRows.OwnedKind));
		ODataDeletePlanItem item = new(id, diagnosis.Visible, diagnosis.Referencing,
			plan is { Refusal: null } ? plan.Order : [],
			plan?.Refusal,
			blocked ? DeleteRefusalText.Explain(entity, id, new RecordDeleteOutcome(false, null, null, false, false), diagnosis) : null,
			diagnosis.Complete) {
			PlatformCascade = plan is { Refusal: null, PlatformCascade.Count: > 0 } ? plan.PlatformCascade : null
		};
		return (item, plan);
	}

	// A short, stable digest of what the cascade would delete per id, in the ids' order, each id's rows sorted: the owned rows and the rows
	// the platform deletes with them, or that the plan is refused or was not built.
	internal static string HashPlans(IEnumerable<(string Id, CascadePlan Plan)> plans) {
		System.Text.StringBuilder text = new();
		foreach ((string id, CascadePlan plan) in plans) {
			text.Append(KeyOf(string.Empty, id)).Append('=');
			if (plan is null) {
				text.Append("unchecked");
			}
			else if (plan.Refusal is not null) {
				text.Append("refused");
			}
			else {
				// Sorted: the walk order comes from selects without ORDER BY, so an unchanged tree can list its rows in
				// another order. The deletes still run deepest first; only the digest is order-free.
				text.AppendJoin(',', plan.Order.Select(key => KeyOf(key.Entity, key.Id)).OrderBy(key => key, StringComparer.Ordinal))
					.Append('|')
					.AppendJoin(',', plan.PlatformCascade.Select(key => KeyOf(key.Entity, key.Id)).OrderBy(key => key, StringComparer.Ordinal));
			}
			text.Append(';');
		}
		byte[] digest = System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes(text.ToString().ToLowerInvariant()));
		return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
	}

	internal sealed record CascadePlan(List<RecordKey> Order, List<ReferencingRows> Referencing, string Refusal,
		string Explanation) {
		/// <summary>Rows the platform deletes together with the record or its owned rows, as the check found them.</summary>
		public List<RecordKey> PlatformCascade { get; init; } = [];
	}

	// Owned rows, deepest first, so every row is deleted before the row its required lookup points at. The whole tree
	// is checked BEFORE anything is deleted: a linked row, a live process binding, an unreadable table or an
	// invisible record anywhere refuses the cascade, because deleting children and then failing on the record
	// would leave the children gone for good.
	private static CascadePlan CollectOwned(IRecordDeletionService service, string entity, string id,
		DateTimeOffset stopAt, RecordDeleteDiagnosis rootDiagnosis = null) {
		CascadeWalk walk = new(service, [], [new RecordKey(entity, id)], stopAt);
		RecordDeleteDiagnosis root = rootDiagnosis ?? service.Diagnose(entity, id, DiagnosisRequestMs, stopAt);
		(string refusal, string explanation) = Visit(walk, new RecordKey(entity, id), root, 1);
		return new CascadePlan(walk.Order, root.Referencing, refusal, explanation) { PlatformCascade = walk.PlatformCascade };
	}

	// The state one cascade walk shares across its levels.
	private sealed record CascadeWalk(IRecordDeletionService Service, List<RecordKey> Order, HashSet<RecordKey> Seen,
		DateTimeOffset StopAt) {
		// Platform-cascade rows already checked; kept apart from Seen so such a row can still be queued as owned.
		public HashSet<RecordKey> CheckedCascade { get; } = [];

		// The same rows in the order they were checked, for the plan.
		public List<RecordKey> PlatformCascade { get; } = [];
	}

	private static (string Refusal, string Explanation) Visit(CascadeWalk walk, RecordKey node,
		RecordDeleteDiagnosis diagnosis, int depth) {
		string blocker = DescribeCascadeBlocker(diagnosis);
		if (blocker is not null) {
			return ($"cascade refused: {node.Entity}({node.Id}) {blocker}. Nothing was deleted.",
				DeleteRefusalText.Explain(node.Entity, node.Id, new RecordDeleteOutcome(false, null, null, false, false),
					diagnosis));
		}
		// Rows the platform deletes with the node are not walked into, but a row that still references one of them
		// would make that platform cascade - and so the node's own DELETE - fail after its owned rows were deleted.
		foreach (ReferencingRows rows in diagnosis.Referencing.Where(rows => rows.Kind == ReferencingRows.CascadeKind)) {
			(string refusal, string explanation) = CheckPlatformCascade(walk, node, rows);
			if (refusal is not null) {
				return (refusal, explanation);
			}
		}
		List<ReferencingRows> owned = diagnosis.Referencing.Where(rows => rows.Kind == ReferencingRows.OwnedKind).ToList();
		// A row that requires a lookup value only refers to it: a dictionary value never owns business records.
		if (owned.Count > 0 && walk.Service.IsLookup(node.Entity, DiagnosisRequestMs)) {
			return ($"cascade refused: {node.Entity}({node.Id}) is a lookup (dictionary) value, or could not be shown not "
				+ $"to be one, so the {owned[0].Entity} rows that require it only refer to it. Nothing was deleted.", null);
		}
		foreach (ReferencingRows rows in owned) {
			if (rows.More) {
				return ($"cascade refused: more than {RecordDeletionService.MaxIdsPerPair} {rows.Entity} rows reference "
					+ $"{node.Entity}({node.Id}) through {rows.Column}. Nothing was deleted.", null);
			}
			foreach (string childId in rows.Ids) {
				(string refusal, string explanation) = VisitChild(walk, new RecordKey(rows.Entity, childId), depth);
				if (refusal is not null) {
					return (refusal, explanation);
				}
			}
		}
		return (null, null);
	}

	// A platform-cascade row must itself be free: nothing but other platform-cascade rows may reference it.
	private static (string Refusal, string Explanation) CheckPlatformCascade(CascadeWalk walk, RecordKey node,
		ReferencingRows rows) {
		if (rows.More) {
			return ($"cascade refused: more than {RecordDeletionService.MaxIdsPerPair} {rows.Entity} rows that the "
				+ $"platform deletes with {node.Entity}({node.Id}) reference it through {rows.Column}, so they cannot all "
				+ "be checked. Nothing was deleted.", null);
		}
		foreach (string id in rows.Ids) {
			RecordKey dependent = new(rows.Entity, id);
			if (!walk.CheckedCascade.Add(dependent)) {
				continue;
			}
			walk.PlatformCascade.Add(dependent);
			RecordDeleteDiagnosis diagnosis = walk.Service.Diagnose(dependent.Entity, dependent.Id, DiagnosisRequestMs,
				walk.StopAt);
			string blocker = DescribeCascadeBlocker(diagnosis)
				?? (diagnosis.Referencing.Any(nested => nested.Kind == ReferencingRows.OwnedKind)
					? "is referenced by owned rows that the platform's cascade does not delete"
					: null);
			if (blocker is not null) {
				return ($"cascade refused: {dependent.Entity}({dependent.Id}), which the platform deletes together with "
					+ $"{node.Entity}({node.Id}), {blocker}. Nothing was deleted.",
					DeleteRefusalText.Explain(dependent.Entity, dependent.Id,
						new RecordDeleteOutcome(false, null, null, false, false), diagnosis));
			}
		}
		return (null, null);
	}

	// One owned child: checked (and, above the depth limit, walked) before it is queued for deletion.
	private static (string Refusal, string Explanation) VisitChild(CascadeWalk walk, RecordKey child, int depth) {
		if (!walk.Seen.Add(child)) {
			return (null, null);
		}
		// Shared master data is never deleted as somebody's owned row, whatever lookup points from it.
		if (CascadeRefusedEntities.Contains(child.Entity)) {
			return ($"cascade refused: {child.Entity}({child.Id}) would be deleted as an owned row, and cascade never "
				+ $"deletes {child.Entity} rows. Nothing was deleted.", null);
		}
		RecordDeleteDiagnosis childDiagnosis = walk.Service.Diagnose(child.Entity, child.Id, DiagnosisRequestMs,
			walk.StopAt);
		if (depth < MaxCascadeDepth) {
			(string nested, string nestedExplanation) = Visit(walk, child, childDiagnosis, depth + 1);
			if (nested is not null) {
				return (nested, nestedExplanation);
			}
		}
		else {
			(string leafRefusal, string leafExplanation) = CheckLeaf(walk, child, childDiagnosis);
			if (leafRefusal is not null) {
				return (leafRefusal, leafExplanation);
			}
		}
		walk.Order.Add(child);
		return walk.Order.Count > MaxCascadeRows
			? ($"cascade refused: more than {MaxCascadeRows} owned rows. Nothing was deleted.", null)
			: (null, null);
	}

	// A row at the depth limit is not walked into, so it must have nothing below it but rows the platform deletes with
	// it - and those are checked like the record's own.
	private static (string Refusal, string Explanation) CheckLeaf(CascadeWalk walk, RecordKey child,
		RecordDeleteDiagnosis diagnosis) {
		if (DescribeCascadeBlocker(diagnosis) is { } blocker) {
			return ($"cascade refused: {child.Entity}({child.Id}) {blocker}. Nothing was deleted.",
				DeleteRefusalText.Explain(child.Entity, child.Id, new RecordDeleteOutcome(false, null, null, false, false),
					diagnosis));
		}
		if (diagnosis.Referencing.Any(nested => nested.Kind != ReferencingRows.CascadeKind)) {
			return ($"cascade refused: owned rows go deeper than {MaxCascadeDepth} levels below the record "
				+ $"({child.Entity}({child.Id}) is itself referenced). Nothing was deleted.", null);
		}
		foreach (ReferencingRows rows in diagnosis.Referencing) {
			(string refusal, string explanation) = CheckPlatformCascade(walk, child, rows);
			if (refusal is not null) {
				return (refusal, explanation);
			}
		}
		return (null, null);
	}

	private static string DescribeCascadeBlocker(RecordDeleteDiagnosis diagnosis) {
		if (diagnosis.Visible != true) {
			return "is not visible to this user or could not be read";
		}
		if (diagnosis.ProcessElementId is not null) {
			return $"is held by live process element {diagnosis.ProcessElementId}";
		}
		if (!diagnosis.Complete) {
			return "has referencing tables that could not be checked in time, so its owned rows are not known for certain";
		}
		ReferencingRows linked = diagnosis.Referencing.FirstOrDefault(rows => rows.Kind == ReferencingRows.LinkedKind);
		return linked is null
			? null
			: $"is still referenced by linked rows ({linked.Entity}.{linked.Column}) that cascade never deletes";
	}
}

/// <summary>Arguments for <see cref="ODataDeleteTool"/>.</summary>
public sealed record ODataDeleteArgs {
	/// <summary>Creatio OData entity set name (e.g., Contact, Account).</summary>
	[JsonPropertyName("entity")]
	[Description("Creatio OData entity set name (e.g., Contact, Account, Activity). Call dataforge-find-tables to discover names.")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>GUID of the record to delete.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the record to delete. Pass id or ids - a keyless mass delete is rejected.")]
	public string? Id { get; init; }

	/// <summary>GUIDs of the records to delete, all of the same entity.</summary>
	[JsonPropertyName("ids")]
	[Description("Up to 50 record GUIDs of the same entity, deleted one by one; `results` reports each.")]
	public string[]? Ids { get; init; }

	/// <summary>Also delete owned children.</summary>
	[JsonPropertyName("cascade")]
	[Description("true also deletes OWNED children first - rows whose REQUIRED lookup points at the record, up to 3 "
		+ "levels, 100 rows and 10 ids. The whole tree is checked before anything is deleted: a linked row "
		+ "(optional lookup), a live process binding or an unreadable table anywhere refuses the cascade with "
		+ "nothing deleted. Refused for Contact, Employee, SysAdminUnit, SysUserInRole (as the record or as an owned "
		+ "row) and for any lookup (dictionary) record. Run with dry-run: true first and pass its cascade-plan-hash.")]
	public bool Cascade { get; init; }

	/// <summary>The digest a dry run returned for the cascade the user approved.</summary>
	[JsonPropertyName("cascade-plan-hash")]
	[Description("Required with cascade and confirm: the cascade-plan-hash the dry run of the SAME entity and ids "
		+ "returned. The plan is recomputed and nothing is deleted when it differs from what that dry run showed.")]
	public string? CascadePlanHash { get; init; }

	/// <summary>List, do not delete.</summary>
	[JsonPropertyName("dry-run")]
	[Description("true deletes nothing and needs no confirm: `plan` lists per id whether it is visible, the rows "
		+ "referencing it, the owned rows cascade would delete (deepest first), the rows the platform deletes with "
		+ "them, and what would still block it; with cascade, `cascade-plan-hash` binds the confirm to this plan.")]
	public bool DryRun { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize this destructive delete. When false or omitted, the tool refuses and returns what would be deleted without making any remote call.")]
	public bool Confirm { get; init; }
}

/// <summary>An entity and a record id.</summary>
public sealed record RecordKey(
	[property: JsonPropertyName("entity")] string Entity,
	[property: JsonPropertyName("id")] string Id);

/// <summary>The outcome for one id of an odata-delete call.</summary>
public sealed record ODataDeleteItemResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error,
	[property: JsonPropertyName("explanation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Explanation,
	[property: JsonPropertyName("referencing"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ReferencingRows>? Referencing,
	[property: JsonPropertyName("cascade-deleted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RecordKey>? CascadeDeleted) {

	/// <summary>Owned rows whose DELETE got no answer: they may or may not be gone.</summary>
	[JsonPropertyName("cascade-unconfirmed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<RecordKey>? CascadeUnconfirmed { get; init; }
}

/// <summary>What a dry run found for one id.</summary>
public sealed record ODataDeletePlanItem(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("visible"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Visible,
	[property: JsonPropertyName("referencing")] IReadOnlyList<ReferencingRows> Referencing,
	[property: JsonPropertyName("cascade-would-delete")] IReadOnlyList<RecordKey> CascadeWouldDelete,
	[property: JsonPropertyName("cascade-refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CascadeRefusal,
	[property: JsonPropertyName("blocked-because"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BlockedBecause,
	[property: JsonPropertyName("complete")] bool Complete) {

	/// <summary>Rows the platform deletes together with the record or its owned rows (not deleted by clio).</summary>
	[JsonPropertyName("platform-cascade"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<RecordKey>? PlatformCascade { get; init; }
}
