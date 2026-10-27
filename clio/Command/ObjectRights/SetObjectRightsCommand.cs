using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Options of <c>set-object-rights</c>: grant or revoke one role's operation permissions on one object, move or remove
/// that role's row, or turn the object's operation permissions on or off alone.
/// </summary>
[Verb("set-object-rights", HelpText =
	"Grant or revoke object operation permissions (read/create/edit/delete) for one role on one object, move or remove "
	+ "that role's row, or turn the object's operation permissions on or off (destructive)")]
public class SetObjectRightsOptions : RemoteCommandOptions {

	/// <summary>The object whose operation permissions change, by its code (entity schema name).</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object whose operation permissions are changed, by its code (entity schema name). One object per call. A title "
		+ "is refused, naming the code it belongs to; the output shows the object's title next to its code.")]
	public string EntitySchemaName { get; set; }

	/// <summary>
	/// The SysAdminUnit id of the role or user whose row changes. Required for a grant or revoke; not given when the call
	/// only turns operation permissions on or off.
	/// </summary>
	[Option("grantee", Required = false, HelpText =
		"SysAdminUnit id (organizational/functional role or user) to grant/revoke. Names are not unique — pass the id. "
		+ "Required for a grant or revoke; not given with --disable-operation-permissions, or with "
		+ "--enable-operation-permissions alone.")]
	public string Grantee { get; set; }

	/// <summary>
	/// Comma-separated operations to grant or revoke. Required for a grant or revoke: no operation is granted by default.
	/// </summary>
	[Option("operations", Required = false, HelpText =
		"Comma-separated operations to grant or revoke: read,create,edit,delete. Required for a grant or revoke - no "
		+ "operation is granted by default. Not given with --disable-operation-permissions, or with "
		+ "--enable-operation-permissions alone.")]
	public string Operations { get; set; }

	/// <summary>Revoke the operations instead of granting them.</summary>
	[Option("revoke", Required = false, HelpText =
		"Revoke the operations named in --operations instead of granting them. The role's row is kept, with those "
		+ "operations cleared, as a deny: a revoke never removes the row (--remove-row does).")]
	public bool Revoke { get; set; }

	/// <summary>Move the grantee's row to this position, 0 the highest; a call of its own.</summary>
	[Option("position", Required = false, HelpText =
		"Move the grantee's row to this position, 0 the highest priority. The other rows keep their relative order and "
		+ "are renumbered 0..n-1, as the Object permissions designer does. A call of its own, with --grantee: not with "
		+ "--operations, --revoke, --above, --remove-row or a switch flag.")]
	public int? Position { get; set; }

	/// <summary>Move the grantee's row directly above the row of this role; a call of its own.</summary>
	[Option("above", Required = false, HelpText =
		"Move the grantee's row directly above the row of this role (SysAdminUnit id), which must have exactly one row "
		+ "on the object; for example above All employees, so that the grantee's row decides first for its members. A "
		+ "call of its own, like --position.")]
	public string Above { get; set; }

	/// <summary>Remove the grantee's row; a call of its own.</summary>
	[Option("remove-row", Required = false, HelpText =
		"Remove the grantee's row. Its members are then decided by the next matching row (often All employees), which "
		+ "can WIDEN their access - unlike --revoke, which keeps the row as a deny. Refused when no row would grant any "
		+ "operation on an object that uses operation permissions. A call of its own, like --position.")]
	public bool RemoveRow { get; set; }

	/// <summary>Turn the object's operation permissions on: with a grant, or alone.</summary>
	[Option("enable-operation-permissions", Required = false, HelpText =
		"Turn the object's operation permissions ON; from then on its rows decide who can reach it. With a grant "
		+ "(--grantee, --operations) it lets the grant turn them on; alone it turns them on with the stored rows as they "
		+ "are. Either way the All employees row is kept, or added with read/create/edit/delete when the object has "
		+ "stored rows but none for All employees. Without this, a grant on an object that does not use operation "
		+ "permissions is refused.")]
	public bool EnableOperationPermissions { get; set; }

	/// <summary>Turn the object's operation permissions off; a call of its own that changes no row.</summary>
	[Option("disable-operation-permissions", Required = false, HelpText =
		"Turn the object's operation permissions OFF, keeping every row as it is, operations included: the object "
		+ "becomes available to ALL internal users, and the rows apply again if operation permissions are turned back "
		+ "on. A call of its own: not with --grantee, --operations or --revoke.")]
	public bool DisableOperationPermissions { get; set; }

	/// <summary>Apply the change without a prompt.</summary>
	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	/// <summary>Write nothing; show what the call would change.</summary>
	[Option("preview", Required = false, HelpText =
		"Write nothing: show what the call would change, the rows it affects, and whether it would be refused")]
	public bool Preview { get; set; }

	/// <summary>
	/// The time the whole call may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// Every request gets at most what is left of it, and the save is sent only while there is time left for it and
	/// for the read-back. <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? CallBudget { get; set; }
}

/// <summary>
/// <c>set-object-rights</c>: changes one role's operations on ONE object, or turns the object's "Use operation
/// permissions" switch on or off, like the "Object permissions" designer, with every access-changing transition named
/// in the arguments. The switch and the rows change separately: a revoke never turns the switch off, and turning it
/// off keeps every row. The flow is read → plan → policy → confirm → save → read back; the output and the exit code
/// come from the plan and the read-back. Destructive and confirm-gated.
/// </summary>
public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly IObjectRightsReader _reader;
	private readonly IObjectRightsWriter _writer;
	private readonly IObjectRightsPlanner _planner;
	private readonly IGranteeLookup _granteeLookup;
	private readonly IObjectRightsReadBackVerifier _readBackVerifier;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public SetObjectRightsCommand(IObjectRightsReader reader, IObjectRightsWriter writer, IObjectRightsPlanner planner,
		IGranteeLookup granteeLookup, IObjectRightsReadBackVerifier readBackVerifier, IInteractiveConsole console,
		ILogger logger) {
		_reader = reader;
		_writer = writer;
		_planner = planner;
		_granteeLookup = granteeLookup;
		_readBackVerifier = readBackVerifier;
		_console = console;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(SetObjectRightsOptions options) {
		if (!TryParseInputs(options, out string named, out Guid grantee,
				out IReadOnlyCollection<ObjectOperation> operations, out RowLayoutRequest layout)) {
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.CallBudget);
		ObjectRightsChangeRequest request = new(grantee, null, operations, options.Revoke,
			options.EnableOperationPermissions, options.DisableOperationPermissions, layout);
		// A call that only turns the switch names no grantee: there is no row of a role to change, so none is looked up.
		if (!request.SwitchOnly) {
			if (!ObjectRightsCommandInput.TryResolveUnitName(_granteeLookup, grantee, "grantee", requestOptions, _logger,
				out string granteeName)) {
				return 1;
			}
			request = request with { GranteeName = granteeName };
		}
		if (!TryReadObject(named, requestOptions, out string schemaName, out ObjectRightsInfo before)) {
			return 1;
		}
		// The effect of a move or a removal is named per role, external ones as such. While operation permissions are off
		// the rows decide nothing, so there is no effect to name and nothing to look up.
		IReadOnlySet<Guid> externalUnits = null;
		if (request.ChangesLayout && before.AdministratedByOperations
			&& !TryFindExternalUnits(before, schemaName, requestOptions, out externalUnits)) {
			return 1;
		}
		ObjectRightsPlan plan = _planner.Plan(before.State, request, externalUnits);
		Change change = new(schemaName, before.Caption,
			request.SwitchOnly ? null : $"'{ObjectRightsSupport.Display(request.GranteeName)}' ({grantee})", request, plan,
			AnchorLabel(before, layout));
		if (plan.Refused) {
			_logger.WriteError($"Error: {RefusalMessage(change)} Nothing was changed.");
			return 1;
		}
		if (!plan.Changes) {
			_logger.WriteInfo($"{change.Label}: {NoChangeReason(change)} (no change).");
			return 0;
		}
		IReadOnlyList<string> facts = DescribePlan(change);
		if (options.Preview) {
			_logger.WriteInfo($"PREVIEW — nothing was changed. {change.Summary}");
			WriteFacts(facts);
			return 0;
		}
		switch (ObjectRightsCommandInput.Confirm(options.Confirm, _console, _logger, new ConfirmRequest(
					"set-object-rights", "object permissions", change.Summary, facts,
					"Object-permissions change cancelled."))) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		if (!ObjectRightsCommandInput.HasTimeToSave(requestOptions, change.SchemaName, _logger)) {
			return 1;
		}
		ObjectRightsSaveResult save = plan.RewritesAllRows
			? _writer.SaveRowLayout(before.Snapshot, plan.After, requestOptions)
			: _writer.Save(before.Snapshot, plan.After, requestOptions);
		// After a failed save the read-back is one attempt as well: the object is checked once, and on MCP the answer
		// still arrives within the call's budget.
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName,
			save.Succeeded ? requestOptions : requestOptions with { MaxAttempts = 1 });
		return save.Succeeded
			? ReportSaved(change, facts, actual)
			: ReportFailedSave(change, facts, save, actual);
	}

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string Caption, string GranteeLabel, ObjectRightsChangeRequest Request,
		ObjectRightsPlan Plan, string AnchorLabel) {

		// The object by its title and its code, so the developer sees which object the code names.
		public string Label => ObjectRightsSupport.FormatObject(SchemaName, Caption);

		public string Summary {
			get {
				if (Request.SwitchOnly) {
					string state = Request.DisableOperationPermissions ? "OFF" : "ON";
					return $"Turn operation permissions {state} on {Label}.";
				}
				if (Request.ChangesLayout) {
					return Request.Layout.Kind switch {
						RowLayoutKind.Remove => $"Remove the row of grantee {GranteeLabel} on {Label}.",
						RowLayoutKind.MoveToPosition =>
							$"Move the row of grantee {GranteeLabel} to position {Request.Layout.Position} on {Label}.",
						_ => $"Move the row of grantee {GranteeLabel} above the row of {AnchorLabel} on {Label}."
					};
				}
				string verb = Request.Revoke ? "Revoke" : "Grant";
				return $"{verb} [{Operations}] for grantee {GranteeLabel} on {Label}.";
			}
		}

		public string Operations => ObjectRightsSupport.FormatOperations(Request.Operations);

		public RoleOperationRights GranteeRowBefore =>
			Plan.Before.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);

		public RoleOperationRights GranteeRowAfter =>
			Plan.After.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);

		// The planner refuses a grantee with several rows, so each side has at most one row for it.
		public bool GranteeRowChanges => GranteeRowAfter is not null && !GranteeRowAfter.SameRowAs(GranteeRowBefore);
	}

	private bool TryParseInputs(SetObjectRightsOptions options, out string named, out Guid grantee,
		out IReadOnlyCollection<ObjectOperation> operations, out RowLayoutRequest layout) {
		grantee = Guid.Empty;
		operations = Array.Empty<ObjectOperation>();
		layout = null;
		if (!ObjectRightsCommandInput.TryReadObjectName(options.EntitySchemaName, _logger, out named)) {
			return false;
		}
		if (options.Preview && options.Confirm) {
			_logger.WriteError("Error: --preview writes nothing, so it cannot be combined with --confirm.");
			return false;
		}
		if (options.Position.HasValue || options.Above is not null || options.RemoveRow) {
			return TryReadLayout(options, out grantee, out layout);
		}
		if (!TryReadCallShape(options, out bool switchOnly)) {
			return false;
		}
		if (switchOnly) {
			return true;
		}
		if (options.Revoke && options.EnableOperationPermissions) {
			_logger.WriteError("Error: --enable-operation-permissions applies to a grant; a revoke never turns operation "
				+ "permissions on.");
			return false;
		}
		if (!TryReadGrantee(options.Grantee, out grantee)) {
			return false;
		}
		// The arguments show the whole effect: every grant or revoke names its operations; none is implied.
		if (options.Operations is null) {
			_logger.WriteError("Error: --operations is required: name the operations to grant or revoke "
				+ "(read,create,edit,delete).");
			return false;
		}
		if (!TryParseOperations(options.Operations, out operations, out string opError)) {
			_logger.WriteError(opError);
			return false;
		}
		return true;
	}

	private bool TryReadGrantee(string raw, out Guid grantee) {
		if (Guid.TryParse(raw, out grantee) && grantee != Guid.Empty) {
			return true;
		}
		_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
		return false;
	}

	// A move or a removal is a call of its own: it changes no operation and no switch, so an argument about either beside
	// it would read as part of the change while nothing of it happens — and one call naming two row changes could not
	// show a single before and after.
	private bool TryReadLayout(SetObjectRightsOptions options, out Guid grantee, out RowLayoutRequest layout) {
		grantee = Guid.Empty;
		layout = null;
		int changes = (options.Position.HasValue ? 1 : 0) + (options.Above is null ? 0 : 1)
			+ (options.RemoveRow ? 1 : 0);
		if (changes > 1) {
			_logger.WriteError("Error: --position, --above and --remove-row each name one change of the "
				+ "grantee's row; pass one of them.");
			return false;
		}
		if (options.Operations is not null || options.Revoke || options.EnableOperationPermissions
			|| options.DisableOperationPermissions) {
			_logger.WriteError("Error: --position, --above and --remove-row are a call of their own: they move or "
				+ "remove the grantee's row and change no operation, so --operations, --revoke, "
				+ "--enable-operation-permissions and --disable-operation-permissions do not go with them. Make the grant, "
				+ "the revoke or the switch change in a separate call.");
			return false;
		}
		if (!TryReadGrantee(options.Grantee, out grantee)) {
			return false;
		}
		if (options.RemoveRow) {
			layout = new RowLayoutRequest(RowLayoutKind.Remove);
			return true;
		}
		if (options.Position is { } position) {
			if (position < 0) {
				_logger.WriteError("Error: --position must be 0 (the highest priority) or more.");
				return false;
			}
			layout = new RowLayoutRequest(RowLayoutKind.MoveToPosition, position);
			return true;
		}
		if (!Guid.TryParse(options.Above, out Guid anchor) || anchor == Guid.Empty) {
			_logger.WriteError("Error: --above must be the SysAdminUnit id (GUID) of a role with a row on the object.");
			return false;
		}
		if (anchor == grantee) {
			_logger.WriteError("Error: --above names the grantee itself; name the role whose row the grantee's row goes "
				+ "above.");
			return false;
		}
		layout = new RowLayoutRequest(RowLayoutKind.MoveAbove, Anchor: anchor);
		return true;
	}

	// A failed lookup fails the call: an external role reported as internal would hide exactly the change to portal
	// users that the operator must see.
	private bool TryFindExternalUnits(ObjectRightsInfo before, string schemaName, CreatioRequestOptions requestOptions,
		out IReadOnlySet<Guid> externalUnits) {
		try {
			externalUnits = _granteeLookup.FindExternalUnits(
				before.Roles.Select(row => row.GranteeId).Distinct().ToArray(), requestOptions);
			return true;
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			externalUnits = null;
			_logger.WriteError($"Error: '{schemaName}': could not tell which roles with a row are external, so the effect "
				+ $"of the change cannot be shown: {ObjectRightsSupport.DisplayFailure(ex)}. Nothing was changed.");
			return false;
		}
	}

	private static string AnchorLabel(ObjectRightsInfo before, RowLayoutRequest layout) {
		if (layout is not { Kind: RowLayoutKind.MoveAbove }) {
			return null;
		}
		RoleOperationRights row = before.Roles.FirstOrDefault(role => role.GranteeId == layout.Anchor);
		return row is null
			? $"role {layout.Anchor}"
			: $"'{ObjectRightsSupport.Display(row.GranteeName)}' ({layout.Anchor})";
	}

	// Which kind of call the arguments describe. A switch-only call (--enable- or --disable-operation-permissions
	// alone) needs nothing else; any other call goes on to name a grantee and operations.
	private bool TryReadCallShape(SetObjectRightsOptions options, out bool switchOnly) {
		switchOnly = false;
		if (options.EnableOperationPermissions && options.DisableOperationPermissions) {
			_logger.WriteError("Error: --enable-operation-permissions and --disable-operation-permissions turn the switch "
				+ "opposite ways; pass one of them.");
			return false;
		}
		bool namesRow = options.Grantee is not null || options.Operations is not null;
		// Turning operation permissions off changes no row, so it is a call of its own: an argument about a row beside it
		// would read as part of the change while nothing of it happens.
		if (options.DisableOperationPermissions) {
			if (namesRow || options.Revoke) {
				_logger.WriteError("Error: --disable-operation-permissions is a call of its own: it turns operation "
					+ "permissions OFF and keeps every row as it is, so --grantee, --operations and --revoke do not go with "
					+ "it. To clear a role's operations, revoke them in a separate call.");
				return false;
			}
			switchOnly = true;
			return true;
		}
		if (options.EnableOperationPermissions && !namesRow && !options.Revoke) {
			switchOnly = true;
			return true;
		}
		if (!namesRow && !options.Revoke) {
			_logger.WriteError("Error: name the change: --grantee and --operations for a grant or a revoke, or "
				+ "--enable-operation-permissions / --disable-operation-permissions alone to turn operation permissions on "
				+ "or off.");
			return false;
		}
		return true;
	}

	// The write's own policy for a title (ObjectRightsCommandInput.TryResolveObjectName owns the code-first rule): the
	// object a write changes is named by its CODE, which the approval shows. A title is not unique, and the same word can
	// be one object's code and another's title, so a name that is no object's code but is a title is refused with the
	// code(s) it belongs to, as run-process refuses a process caption. Returns false after writing the error.
	private bool TryReadObject(string named, CreatioRequestOptions requestOptions, out string schemaName,
		out ObjectRightsInfo before) {
		schemaName = null;
		before = null;
		if (!ObjectRightsCommandInput.TryResolveObjectName(named, code => _reader.GetObjectRights(code, requestOptions),
				_reader, requestOptions, _logger, out ObjectNameResolution resolution)) {
			return false;
		}
		if (resolution.TitleMatches.Count > 0) {
			_logger.WriteError($"Error: {ObjectRightsCommandInput.NotACode(named, resolution.TitleMatches)}. Pass the code — "
				+ "a title is not unique, and this call changes access rights. Nothing was changed.");
			return false;
		}
		// Not found, or found but not read: either way nothing is known that a change could be planned on.
		if (!resolution.ByCode.IsRead) {
			_logger.WriteError($"Error: '{resolution.Code}': {resolution.ByCode.FailureReason}. Nothing was changed.");
			return false;
		}
		schemaName = resolution.Code;
		before = resolution.ByCode;
		return true;
	}

	private static string RefusalMessage(Change change) {
		string label = change.Label;
		ObjectRightsPlan plan = change.Plan;
		return plan.Refusal switch {
			ObjectRightsRefusal.EnableNotRequested =>
				$"{label} does not use operation permissions yet. Granting would turn them ON, and from then on its rows "
				+ "decide who can reach it"
				+ (plan.RowsBecomingEffective.Count > 0
					? $" ({ObjectRightsSupport.FormatRows(plan.RowsBecomingEffective)})"
					: "")
				+ (plan.AddsAllEmployeesRow
					? "; an 'All employees' row with read/create/edit/delete would be added below them"
					: "")
				+ ". Re-run with --enable-operation-permissions if that is intended.",
			ObjectRightsRefusal.RevokeOnNotAdministered =>
				$"{label} is not administered by operation permissions (they are OFF), so the tool does not revoke on "
				+ "it. To limit access, turn operation permissions on first with a grant and "
				+ "--enable-operation-permissions; see get-guidance object-rights.",
			ObjectRightsRefusal.LeavesNoGrantingRow when change.Request.SwitchOnly =>
				$"turning operation permissions on would leave no row on {label} that grants any operation "
				+ $"({ObjectRightsSupport.FormatRows(plan.RowsBecomingEffective)}), so every internal user would be cut "
				+ "off. Grant the operations in the same call instead (--grantee, --operations and "
				+ "--enable-operation-permissions); for internal users grant them to All employees, whose row decides "
				+ "before a new row of any other role, which goes at the lowest priority.",
			ObjectRightsRefusal.LeavesNoGrantingRow when change.Request.ChangesLayout =>
				$"removing the row of grantee {change.GranteeLabel} would leave no row on {label} that grants any "
				+ $"operation ({ObjectRightsSupport.FormatRows(plan.Before.Roles.Where(row => row.GranteeId != change.Request.Grantee))}), "
				+ "so only the holders of the '…any data' system operations could reach it. Grant operations to another "
				+ "role first, or turn operation permissions OFF with --disable-operation-permissions alone.",
			ObjectRightsRefusal.NoGranteeRow =>
				$"grantee {change.GranteeLabel} has no row on {label}, so there is no row to "
				+ $"{(change.Request.Layout.Kind == RowLayoutKind.Remove ? "remove" : "move")}. A grant adds a row, at the "
				+ "lowest priority.",
			ObjectRightsRefusal.AnchorHasNoRow =>
				$"{change.AnchorLabel} has no row on {label}, so there is no row to move the grantee's row above. "
				+ $"Rows: {ObjectRightsSupport.FormatRows(plan.Before.Roles)}.",
			ObjectRightsRefusal.AnchorHasSeveralRows =>
				$"{change.AnchorLabel} has {plan.DuplicatePositions.Count} rows on {label} (positions "
				+ $"{string.Join(", ", plan.DuplicatePositions)}), so the place above it is ambiguous. Remove "
				+ "the duplicates in the Object permissions designer, or move the row with --position.",
			ObjectRightsRefusal.PositionOutOfRange =>
				$"position {change.Request.Layout.Position} is past the last row: {label} has "
				+ $"{plan.Before.Roles.Count} rows, so the positions are 0..{plan.Before.Roles.Count - 1}.",
			ObjectRightsRefusal.LeavesNoGrantingRow =>
				$"after this revoke no row on {label} would grant any operation. A revoke never turns operation "
				+ "permissions off: to turn them OFF instead — the object becomes available to ALL internal users and "
				+ "every row is kept as it is — call set-object-rights with --entity-schema-name and "
				+ "--disable-operation-permissions only.",
			ObjectRightsRefusal.DuplicateGranteeRows =>
				$"grantee {change.GranteeLabel} has {plan.DuplicatePositions.Count} rows on {label} (positions "
				+ $"{string.Join(", ", plan.DuplicatePositions)}). Which of them decides depends on the other rows, so "
				+ "the tool changes none of them. Remove the duplicates in the Object permissions designer, then re-run.",
			_ => $"the change on {label} is refused ({plan.Refusal})."
		};
	}

	// Why a call changes nothing. A disable that finds the switch already off — typically a retry — says what OFF means,
	// so "(no change)" never reads as "nobody has access".
	private static string NoChangeReason(Change change) {
		if (!change.Request.SwitchOnly) {
			return RowNoChangeReason(change);
		}
		return change.Request.DisableOperationPermissions
			? "operation permissions are already OFF — the object is available to all internal users"
			: "operation permissions are already ON";
	}

	// A fact about the grantee's row only: other rows may still decide for the grantee's members.
	private static string RowNoChangeReason(Change change) {
		RoleOperationRights row = change.GranteeRowBefore;
		if (row is null) {
			return $"grantee {change.GranteeLabel} has no row, so there is nothing to revoke";
		}
		string granteeRow = $"the row of grantee {change.GranteeLabel} at position {row.Position}";
		if (change.Request.Layout?.Kind == RowLayoutKind.Remove) {
			return $"{granteeRow} is the row the service shows for an object that is off with no stored rows; it is "
				+ "not stored, so there is nothing to remove";
		}
		if (change.Request.ChangesLayout) {
			return $"{granteeRow} is already where the call puts it";
		}
		return change.Request.Revoke
			? $"{granteeRow} already has none of [{change.Operations}]"
			: $"{granteeRow} already has [{change.Operations}]";
	}

	// The facts the operator approves: what turns on or off, which rows start to decide, where the grantee's row sits
	// and which rows decide before it. Facts only — no conclusion about who can actually reach the object.
	private static IReadOnlyList<string> DescribePlan(Change change) {
		ObjectRightsPlan plan = change.Plan;
		string label = change.Label;
		List<string> facts = new();
		if (plan.EnablesOperationPermissions) {
			facts.Add($"Operation permissions on {label} are turned ON: from then on its rows decide, in priority order, "
				+ "who can reach it.");
			if (plan.RowsBecomingEffective.Count > 0) {
				facts.Add($"Rows that start to decide: {ObjectRightsSupport.FormatRows(plan.RowsBecomingEffective)}.");
			}
			if (plan.AddsAllEmployeesRow) {
				RoleOperationRights allEmployees = plan.After.Roles.First(row => row.GranteeId == SysAdminUnitIds.AllEmployees);
				facts.Add($"An '{ObjectRightsSupport.Display(allEmployees.GranteeName)}' row with read/create/edit/delete is "
					+ $"added at position {allEmployees.Position}, below the existing rows.");
			}
		}
		if (plan.DisablesOperationPermissions) {
			facts.Add($"Operation permissions on {label} are turned OFF: it becomes available to ALL internal users. "
				+ "Every row is kept as it is, operations included, and applies again if operation permissions are turned "
				+ "back on"
				+ (plan.Before.Roles.Count > 0
					? $": {ObjectRightsSupport.FormatRows(plan.Before.Roles)}."
					: "; it has no stored rows."));
		}
		if (change.Request.SwitchOnly) {
			return facts;
		}
		if (change.Request.ChangesLayout) {
			return DescribeLayout(change);
		}
		facts.Add(DescribeGranteeRow(change));
		if (plan.RowsAboveGrantee.Count > 0) {
			facts.Add("Rows above the grantee's row, which decide first for a user who is also in those roles: "
				+ $"{ObjectRightsSupport.FormatRows(plan.RowsAboveGrantee)}.");
		}
		return facts;
	}

	// While operation permissions are off the rows decide nothing, so no one's access changes until they are turned on.
	private static IReadOnlyList<string> DescribeLayout(Change change) {
		ObjectRightsPlan plan = change.Plan;
		List<string> facts = new() {
			$"Rows before: {ObjectRightsSupport.FormatRows(plan.Before.Roles)}.",
			$"Rows after: {ObjectRightsSupport.FormatRows(plan.After.Roles)}."
		};
		if (!plan.Before.AdministratedByOperations) {
			facts.Add($"Operation permissions on {change.Label} are OFF: the rows decide nothing until they are turned on "
				+ "(--enable-operation-permissions), so no one's access changes now.");
			return facts;
		}
		if (plan.RoleAccessChanges.Count == 0) {
			facts.Add("No role's operations change.");
			return facts;
		}
		facts.Add("What changes, for a member who is in no other role with a row (a member of several listed roles, or "
			+ "of a parent role, can be decided by another row):");
		facts.AddRange(plan.RoleAccessChanges.Select(DescribeAccessChange));
		return facts;
	}

	private static string DescribeAccessChange(RoleAccessChange change) {
		string role = $"{(change.External ? "external role" : "role")} "
			+ $"'{ObjectRightsSupport.Display(change.GranteeName)}' ({change.GranteeId})";
		string decided = $"decided by {DecidingRow(change.DecidingBefore)} before, by {DecidingRow(change.DecidingAfter)} "
			+ "after";
		HashSet<string> before = change.OperationsBefore.ToHashSet();
		HashSet<string> after = change.OperationsAfter.ToHashSet();
		if (before.SetEquals(after)) {
			return $"  {role}: unchanged, {FormatAccess(change.OperationsAfter)} — {decided}.";
		}
		string direction = after.IsSupersetOf(before) ? "WIDENS"
			: after.IsSubsetOf(before) ? "NARROWS"
			: "CHANGES";
		// A narrowing for portal users is the external-access revoke the operator must not miss.
		string revoke = change.External && direction == "NARROWS" ? " (a revoke of external access)" : "";
		return $"  {role}: access {direction}{revoke} from {FormatAccess(change.OperationsBefore)} to "
			+ $"{FormatAccess(change.OperationsAfter)} — {decided}.";
	}

	private static string DecidingRow(RoleOperationRights row) =>
		row is null ? "no row" : ObjectRightsSupport.FormatRow(row);

	private static string FormatAccess(IReadOnlyList<string> operations) =>
		operations.Count == 0 ? "no access" : string.Join("/", operations);

	private static string DescribeGranteeRow(Change change) {
		RoleOperationRights before = change.GranteeRowBefore;
		RoleOperationRights after = change.GranteeRowAfter;
		if (change.Plan.AddsGranteeRow && after is not null) {
			return $"A row for the grantee is added at position {after.Position}, the lowest priority, with "
				+ $"{ObjectRightsSupport.FormatOperations(after)}.";
		}
		if (change.GranteeRowChanges && after is not null) {
			string operationsNow = ObjectRightsSupport.FormatOperations(after);
			return change.Request.Revoke
				? $"The grantee's row stays at position {after.Position} with [{change.Operations}] cleared; it now has "
					+ $"{operationsNow}."
				: $"The grantee's row at position {after.Position} now has {operationsNow}.";
		}
		return before is null
			? "The grantee has no row; no row changes."
			: $"The grantee's row at position {before.Position} is unchanged "
				+ $"({ObjectRightsSupport.FormatOperations(before)}).";
	}

	// The call's own result line, from the plan: a row that changed, or — when only the switch changes — that.
	private static string DoneLine(Change change) {
		if (change.Request.SwitchOnly) {
			return $"{change.Label}: operation permissions turned "
				+ $"{(change.Plan.DisablesOperationPermissions ? "OFF; every row is kept as it is" : "ON")}.";
		}
		if (change.Request.ChangesLayout) {
			return change.Request.Layout.Kind == RowLayoutKind.Remove
				? $"{change.Label}: removed the row of grantee {change.GranteeLabel}."
				: $"{change.Label}: moved the row of grantee {change.GranteeLabel} to position "
					+ $"{change.GranteeRowAfter.Position}.";
		}
		if (change.GranteeRowChanges) {
			return $"{change.Label}: {(change.Request.Revoke ? "revoked" : "granted")} [{change.Operations}] for "
				+ $"grantee {change.GranteeLabel}.";
		}
		// A grant that changed no row: only its enable did something. A grant or revoke never turns the switch off.
		string granteeRow = change.GranteeRowAfter is null
			? $"grantee {change.GranteeLabel} has no row"
			: $"the row of grantee {change.GranteeLabel} is unchanged";
		return $"{change.Label}: operation permissions turned ON; {granteeRow}.";
	}

	// The save succeeded; the read-back is compared with the plan, so a change that did not land is never reported as
	// done. When the read-back itself fails, nothing shows that the rows this call writes landed — a retry would even
	// plan "no change" once the switch and the grantee's row are in place — so the call fails and says so.
	private int ReportSaved(Change change, IReadOnlyList<string> facts, ObjectRightsInfo actual) {
		string label = change.Label;
		if (!actual.IsRead) {
			_logger.WriteError($"Error: {label}: saved, but NOT verified — on the read-back "
				+ $"{actual.FailureReason}. Check it with get-object-rights before retrying: the plan was:");
			WriteFacts(facts);
			return 1;
		}
		ObjectRightsReadBackComparison comparison =
			_readBackVerifier.Compare(change.Plan, change.Request.Grantee, actual.State);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: {label}: the save reported success, but the object read back does not match "
				+ $"the plan: {string.Join("; ", comparison.Critical)}. Check it with get-object-rights.");
			return 1;
		}
		ReportDone(change, facts, actual.State, comparison);
		return 0;
	}

	// A save that reported an error — a timeout, for example — may still have been committed, so the object is read
	// back before the call is reported as failed. A save that got no answer (a hang, or a connection that broke after
	// the request went out) can even land AFTER the read-back, so that failure says the change may still be applied
	// instead of that it did not happen.
	private int ReportFailedSave(Change change, IReadOnlyList<string> facts, ObjectRightsSaveResult save,
		ObjectRightsInfo actual) {
		string label = change.Label;
		string failure = save.OutcomeUnknown
			? $"the save got no answer ({save.Error}) and may still be applied"
			: $"the save failed ({save.Error})";
		string recheck = save.OutcomeUnknown
			? " Re-read the object with get-object-rights before retrying or reporting a failure."
			: " Read the object with get-object-rights before retrying.";
		if (!actual.IsRead) {
			_logger.WriteError($"Error: {label}: {failure}, and on the read-back {actual.FailureReason}.{recheck}");
			return 1;
		}
		ObjectRightsReadBackComparison comparison =
			_readBackVerifier.Compare(change.Plan, change.Request.Grantee, actual.State);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: {label}: {failure}. The object now: operation permissions "
				+ $"{ObjectRightsSupport.FormatSwitch(actual.State)}; rows "
				+ $"{ObjectRightsSupport.FormatRows(actual.State.Roles)}.{(save.OutcomeUnknown ? recheck : "")}");
			return 1;
		}
		_logger.WriteWarning($"{label}: the save reported an error ({save.Error}), but the object read back "
			+ (comparison.Differences.Count == 0
				? "matches the plan."
				: "shows the switch and the rows this call writes as planned."));
		ReportDone(change, facts, actual.State, comparison);
		return 0;
	}

	private void ReportDone(Change change, IReadOnlyList<string> facts, ObjectRightsState state,
		ObjectRightsReadBackComparison comparison) {
		_logger.WriteInfo(DoneLine(change));
		WriteFacts(facts);
		_logger.WriteInfo($"  Rows now: {ObjectRightsSupport.FormatRows(state.Roles)}.");
		foreach (string difference in comparison.Differences) {
			_logger.WriteWarning($"  Differs from the plan: {difference} — another client may have changed the object "
				+ "meanwhile.");
		}
	}

	private void WriteFacts(IEnumerable<string> facts) {
		foreach (string fact in facts) {
			_logger.WriteInfo($"  {fact}");
		}
	}

	private static bool TryParseOperations(string raw, out IReadOnlyCollection<ObjectOperation> operations,
		out string error) {
		error = null;
		List<ObjectOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (!ObjectOperationNames.TryParse(token, out ObjectOperation operation)) {
				operations = Array.Empty<ObjectOperation>();
				error = $"Error: --operations: unknown operation '{ObjectRightsSupport.Display(token)}'. Use "
					+ $"{ObjectOperationNames.Accepted}.";
				return false;
			}
			parsed.Add(operation);
		}
		if (parsed.Count == 0) {
			// Given but empty ("", " ", ","): the value the approval shows names no operation.
			operations = Array.Empty<ObjectOperation>();
			error = $"Error: --operations: no operation given. Use {ObjectOperationNames.Accepted}.";
			return false;
		}
		operations = parsed.Distinct().OrderBy(op => op).ToArray();
		return true;
	}
}
