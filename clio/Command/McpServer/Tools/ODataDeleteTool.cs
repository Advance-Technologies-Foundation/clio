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
	private const string DeleteVerb = "delete";
	private const int DiagnosisRequestMs = 10_000;
	private static readonly TimeSpan DiagnosisBudget = TimeSpan.FromSeconds(25);

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
		"first, after checking the whole tree; anything that would still block refuses it with nothing deleted. `dry-run: true` lists what would be deleted and what " +
		"blocks it, deletes nothing and needs no confirm. " +
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
			if (args.DryRun) {
				// No top-level id: a dry run deleted nothing, and an id there would read like a deletion.
				return new ODataWriteResponse(true) {
					Plan = ids.Select(id => BuildPlan(service, entity, id, args.Cascade,
						DateTimeOffset.UtcNow + DiagnosisBudget)).ToList()
				};
			}
			List<ODataDeleteItemResult> results = [];
			foreach (string id in ids) {
				results.Add(DeleteOne(service, entity, id, args.Cascade, DateTimeOffset.UtcNow + DiagnosisBudget,
					ref attempted, ref received));
			}
			if (args.Ids is null) {
				ODataDeleteItemResult only = results[0];
				return new ODataWriteResponse(only.Success, only.Error, only.Success ? only.Id : null) {
					Explanation = only.Explanation,
					Referencing = only.Referencing,
					CascadeDeleted = only.CascadeDeleted
				};
			}
			int failed = results.Count(result => !result.Success);
			return new ODataWriteResponse(failed == 0,
				failed == 0 ? null : $"{failed} of {results.Count} records were not deleted; results explains each.") {
				Results = results
			};
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
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

	private static ODataDeleteItemResult DeleteOne(IRecordDeletionService service, string entity, string id,
		bool cascade, DateTimeOffset stopAt, ref bool attempted, ref bool received) {
		List<RecordKey> cascadeDeleted = [];
		if (cascade) {
			ODataDeleteItemResult refused = DeleteOwnedChildren(service, new RecordKey(entity, id), stopAt, cascadeDeleted,
				ref attempted, ref received);
			if (refused is not null) {
				return refused;
			}
		}
		RecordDeleteOutcome outcome = service.Delete(entity, id, RequestTimeoutMs);
		attempted |= outcome.Attempted;
		received |= outcome.Received;
		IReadOnlyList<RecordKey> removed = cascadeDeleted.Count > 0 ? cascadeDeleted : null;
		if (outcome.Success) {
			return new ODataDeleteItemResult(id, true, null, null, null, removed);
		}
		if (!outcome.Received) {
			// A transport failure has no refusal to explain, and its write may have landed.
			return new ODataDeleteItemResult(id, false, outcome.Error, null, null, removed);
		}
		RecordDeleteDiagnosis diagnosis = service.Diagnose(entity, id, DiagnosisRequestMs, stopAt);
		return new ODataDeleteItemResult(id, false, outcome.Error, DeleteRefusalText.Explain(entity, id, outcome, diagnosis),
			diagnosis.Referencing.Count > 0 ? diagnosis.Referencing : null, removed);
	}

	// Plans the whole tree, then deletes its owned rows deepest first; a refusal or the first failed child is returned.
	private static ODataDeleteItemResult DeleteOwnedChildren(IRecordDeletionService service, RecordKey target,
		DateTimeOffset stopAt, List<RecordKey> cascadeDeleted, ref bool attempted, ref bool received) {
		CascadePlan plan = CollectOwned(service, target.Entity, target.Id, stopAt);
		if (plan.Refusal is not null) {
			return new ODataDeleteItemResult(target.Id, false, plan.Refusal, plan.Explanation, plan.Referencing, null);
		}
		foreach (RecordKey child in plan.Order) {
			RecordDeleteOutcome childOutcome = service.Delete(child.Entity, child.Id, RequestTimeoutMs);
			attempted |= childOutcome.Attempted;
			received |= childOutcome.Received;
			if (!childOutcome.Success) {
				string reason = childOutcome.ServerDetail is null
					? childOutcome.Error
					: $"{childOutcome.Error} The server's own reason: {childOutcome.ServerDetail}.";
				return new ODataDeleteItemResult(target.Id, false,
					$"cascade stopped: owned row {child.Entity}({child.Id}) was not deleted, so {target.Entity}({target.Id}) "
					+ $"was left in place: {reason}", null, plan.Referencing, cascadeDeleted);
			}
			cascadeDeleted.Add(child);
		}
		return null;
	}

	private static ODataDeletePlanItem BuildPlan(IRecordDeletionService service, string entity, string id,
		bool cascade, DateTimeOffset stopAt) {
		RecordDeleteDiagnosis diagnosis = service.Diagnose(entity, id, DiagnosisRequestMs, stopAt);
		CascadePlan plan = cascade ? CollectOwned(service, entity, id, stopAt, diagnosis) : null;
		bool blocked = diagnosis.Visible == false || diagnosis.ProcessElementId is not null || !diagnosis.Complete
			|| diagnosis.Referencing.Any(rows => rows.Kind == ReferencingRows.LinkedKind
				|| (!cascade && rows.Kind == ReferencingRows.OwnedKind));
		return new ODataDeletePlanItem(id, diagnosis.Visible, diagnosis.Referencing,
			plan is { Refusal: null } ? plan.Order : [],
			plan?.Refusal,
			blocked ? DeleteRefusalText.Explain(entity, id, new RecordDeleteOutcome(false, null, null, false, false), diagnosis) : null,
			diagnosis.Complete);
	}

	private sealed record CascadePlan(List<RecordKey> Order, List<ReferencingRows> Referencing, string Refusal,
		string Explanation);

	// Owned rows, deepest first, so every row is deleted before the row its required lookup points at. The whole tree
	// is checked BEFORE anything is deleted: a linked row, a live process binding, an unreadable table or an
	// invisible record anywhere refuses the cascade, because deleting children and then failing on the record
	// would leave the children gone for good.
	private static CascadePlan CollectOwned(IRecordDeletionService service, string entity, string id,
		DateTimeOffset stopAt, RecordDeleteDiagnosis rootDiagnosis = null) {
		CascadeWalk walk = new(service, [], [new RecordKey(entity, id)], stopAt);
		RecordDeleteDiagnosis root = rootDiagnosis ?? service.Diagnose(entity, id, DiagnosisRequestMs, stopAt);
		(string refusal, string explanation) = Visit(walk, new RecordKey(entity, id), root, 1);
		return new CascadePlan(walk.Order, root.Referencing, refusal, explanation);
	}

	// The state one cascade walk shares across its levels.
	private sealed record CascadeWalk(IRecordDeletionService Service, List<RecordKey> Order, HashSet<RecordKey> Seen,
		DateTimeOffset StopAt);

	private static (string Refusal, string Explanation) Visit(CascadeWalk walk, RecordKey node,
		RecordDeleteDiagnosis diagnosis, int depth) {
		string blocker = DescribeCascadeBlocker(diagnosis);
		if (blocker is not null) {
			return ($"cascade refused: {node.Entity}({node.Id}) {blocker}. Nothing was deleted.",
				DeleteRefusalText.Explain(node.Entity, node.Id, new RecordDeleteOutcome(false, null, null, false, false),
					diagnosis));
		}
		foreach (ReferencingRows rows in diagnosis.Referencing.Where(rows => rows.Kind == ReferencingRows.OwnedKind)) {
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

	// One owned child: checked (and, above the depth limit, walked) before it is queued for deletion.
	private static (string Refusal, string Explanation) VisitChild(CascadeWalk walk, RecordKey child, int depth) {
		if (!walk.Seen.Add(child)) {
			return (null, null);
		}
		RecordDeleteDiagnosis childDiagnosis = walk.Service.Diagnose(child.Entity, child.Id, DiagnosisRequestMs,
			walk.StopAt);
		if (depth < MaxCascadeDepth) {
			(string nested, string nestedExplanation) = Visit(walk, child, childDiagnosis, depth + 1);
			if (nested is not null) {
				return (nested, nestedExplanation);
			}
		}
		else if (childDiagnosis.Referencing.Any(nested => nested.Kind != ReferencingRows.CascadeKind)) {
			return ($"cascade refused: owned rows go deeper than {MaxCascadeDepth} levels below the record "
				+ $"({child.Entity}({child.Id}) is itself referenced). Nothing was deleted.", null);
		}
		walk.Order.Add(child);
		return walk.Order.Count > MaxCascadeRows
			? ($"cascade refused: more than {MaxCascadeRows} owned rows. Nothing was deleted.", null)
			: (null, null);
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
		+ "nothing deleted. Refused for Contact, Employee, SysAdminUnit, SysUserInRole. Run with dry-run: true first.")]
	public bool Cascade { get; init; }

	/// <summary>List, do not delete.</summary>
	[JsonPropertyName("dry-run")]
	[Description("true deletes nothing and needs no confirm: `plan` lists per id whether it is visible, the rows "
		+ "referencing it, the owned rows cascade would delete (deepest first), and what would still block it.")]
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
	[property: JsonPropertyName("cascade-deleted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RecordKey>? CascadeDeleted);

/// <summary>What a dry run found for one id.</summary>
public sealed record ODataDeletePlanItem(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("visible"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Visible,
	[property: JsonPropertyName("referencing")] IReadOnlyList<ReferencingRows> Referencing,
	[property: JsonPropertyName("cascade-would-delete")] IReadOnlyList<RecordKey> CascadeWouldDelete,
	[property: JsonPropertyName("cascade-refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CascadeRefusal,
	[property: JsonPropertyName("blocked-because"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BlockedBecause,
	[property: JsonPropertyName("complete")] bool Complete);
