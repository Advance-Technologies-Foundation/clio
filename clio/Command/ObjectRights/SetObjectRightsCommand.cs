using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>Options of <c>set-object-rights</c>: grant or revoke a role's object operation permissions.</summary>
[Verb("set-object-rights", HelpText =
	"Grant or revoke object operation permissions (read/create/edit/delete) for a role on an object (destructive)")]
public class SetObjectRightsOptions : RemoteCommandOptions {

	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name whose operation permissions are changed")]
	public string EntitySchemaName { get; set; }

	[Option("grantee", Required = true, HelpText =
		"SysAdminUnit id (organizational/functional role or user) to grant/revoke. Names are not unique — pass the id.")]
	public string Grantee { get; set; }

	[Option("operations", Required = false, HelpText =
		"Comma-separated operations: read,create,edit,delete. Default: read,create,edit (delete not granted by default).")]
	public string Operations { get; set; }

	[Option("revoke", Required = false, HelpText =
		"Revoke the operations instead of granting. A role left with no operations is removed.")]
	public bool Revoke { get; set; }

	[Option("disable-operation-permissions", Required = false, HelpText =
		"Allow a revoke to remove the object's LAST rights row, which turns the object's operation permissions "
		+ "OFF and makes it available to ALL internal users. Without this such a revoke is refused.")]
	public bool DisableOperationPermissions { get; set; }

	[Option("include-connected", Required = false, HelpText =
		"Also apply to every object referenced by the root object's own lookup columns")]
	public bool IncludeConnected { get; set; }

	[Option("connected-operations", Required = false, HelpText =
		"Operations applied to the CONNECTED lookup objects with --include-connected. Default: read — picking a "
		+ "lookup value only needs read, so create/edit are not fanned out to shared dictionaries unless passed here.")]
	public string ConnectedOperations { get; set; }

	[Option("allow-security-object", Required = false, HelpText =
		"Allow granting create/edit/delete, or a revoke with --disable-operation-permissions, when the ROOT object is a "
		+ "security or system object (SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, "
		+ "Vw*, *Right/*Rights). Without it such a root may only be granted read.")]
	public bool AllowSecurityObject { get; set; }

	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	[Option("preview", Required = false, HelpText =
		"Write nothing: list every object the call would change, its current state and what it would get, and print "
		+ "a confirmation code for --confirmation-code")]
	public bool Preview { get; set; }

	[Option("confirmation-code", Required = false, HelpText =
		"Code from a --preview run. The change is applied only if the target objects and every role's rights on them "
		+ "are still exactly what that preview showed")]
	public string ConfirmationCode { get; set; }
}

/// <summary>
/// <c>set-object-rights</c>: grants or revokes a role's object operation permissions on an object and, with
/// <c>--include-connected</c>, on the object's own lookup objects. Destructive and confirm-gated.
/// </summary>
public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly IObjectRightsWriter _rightsWriter;
	private readonly IObjectRightsReader _rightsReader;
	private readonly IConnectedObjectsResolver _connectedObjects;
	private readonly IGranteeLookup _granteeLookup;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public SetObjectRightsCommand(IObjectRightsWriter rightsWriter, IObjectRightsReader rightsReader,
		IConnectedObjectsResolver connectedObjects, IGranteeLookup granteeLookup, IInteractiveConsole console,
		ILogger logger) {
		_rightsWriter = rightsWriter;
		_rightsReader = rightsReader;
		_connectedObjects = connectedObjects;
		_granteeLookup = granteeLookup;
		_console = console;
		_logger = logger;
	}

	public override int Execute(SetObjectRightsOptions options) {
		if (!TryParseInputs(options, out Guid grantee, out IReadOnlyCollection<ObjectOperation> operations,
				out IReadOnlyCollection<ObjectOperation> connectedOperations)) {
			return 1;
		}
		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};
		if (!TryResolveTargets(options, out IReadOnlyList<string> objects)
			|| !TryResolveGranteeLabel(grantee, requestOptions, out string granteeLabel)
			|| IsRefusedBySecurityGate(objects[0], operations, options)) {
			return 1;
		}
		RightsChangeRequest request = new(objects, grantee, granteeLabel, operations, connectedOperations, options,
			requestOptions);
		try {
			return ConfirmChange(request) ?? ApplyChange(request);
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// Everything one call changes, resolved and validated before the confirmation.
	private sealed record RightsChangeRequest(
		IReadOnlyList<string> Objects,
		Guid Grantee,
		string GranteeLabel,
		IReadOnlyCollection<ObjectOperation> Operations,
		IReadOnlyCollection<ObjectOperation> ConnectedOperations,
		SetObjectRightsOptions Options,
		CreatioRequestOptions RequestOptions) {

		public IReadOnlyCollection<ObjectOperation> OperationsFor(bool isRoot) => isRoot ? Operations : ConnectedOperations;
	}

	private bool TryParseInputs(SetObjectRightsOptions options, out Guid grantee,
		out IReadOnlyCollection<ObjectOperation> operations, out IReadOnlyCollection<ObjectOperation> connectedOperations) {
		grantee = Guid.Empty;
		operations = connectedOperations = Array.Empty<ObjectOperation>();
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return false;
		}
		if (!Guid.TryParse(options.Grantee, out grantee) || grantee == Guid.Empty) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return false;
		}
		if (!TryParseOperations("--operations", options.Operations, DefaultOperations, out operations,
				out string opError)) {
			_logger.WriteError(opError);
			return false;
		}
		if (!TryParseOperations("--connected-operations", options.ConnectedOperations, DefaultConnectedOperations,
				out connectedOperations, out string connectedError)) {
			_logger.WriteError(connectedError);
			return false;
		}
		return true;
	}

	// Resolves the full target set BEFORE confirming, so the destructive prompt names every object that will be
	// written (with --include-connected the fan-out can span several permission objects).
	private bool TryResolveTargets(SetObjectRightsOptions options, out IReadOnlyList<string> objects) {
		// A revoke fans out to the connected lookups only when the caller names the operations to take away there.
		// The read-only default is a least-privilege default for a GRANT; applied to a revoke it would strip READ
		// from shared dictionaries (Contact, Account, Currency) that other sections of the same audience still need.
		bool explicitConnected = !string.IsNullOrWhiteSpace(options.ConnectedOperations);
		bool fanOut = options.IncludeConnected && (!options.Revoke || explicitConnected);
		if (options.IncludeConnected && !fanOut) {
			_logger.WriteWarning(
				"--include-connected with --revoke leaves the connected objects untouched unless "
				+ "--connected-operations names what to revoke there; only the root object is changed.");
		}
		ConnectedObjectsResolution resolution = _connectedObjects.Resolve(options.EntitySchemaName, fanOut);
		objects = resolution.Objects;
		if (resolution.EnumerationError is not null) {
			// The caller asked for the root AND its lookups. Writing the root alone would report success while the
			// lookups stay unreachable, so nothing is written.
			_logger.WriteError(
				$"Error: could not enumerate the connected objects of '{options.EntitySchemaName}' "
				+ $"({resolution.EnumerationError}). Nothing was changed. Check that the object name exists; if it "
				+ "does, re-run, or drop --include-connected to change the root object only.");
			return false;
		}
		foreach (string excluded in resolution.Excluded) {
			_logger.WriteWarning(
				$"  {excluded}: security/system object — not included in the fan-out. Pass it as "
				+ "--entity-schema-name to change it explicitly.");
		}
		return true;
	}

	// The grantee must exist: granting to an unknown id would turn operation permissions on for a principal nobody
	// holds — cutting access for everyone else — and still report "granted".
	private bool TryResolveGranteeLabel(Guid grantee, CreatioRequestOptions requestOptions, out string granteeLabel) {
		granteeLabel = null;
		string granteeName;
		try {
			granteeName = _granteeLookup.ResolveGranteeName(grantee, requestOptions);
		}
		catch (Exception ex) when (RightManagementServiceClient.IsServiceFailure(ex)) {
			_logger.WriteError($"Error: could not check grantee {grantee}: {ex.Message}");
			return false;
		}
		if (granteeName is null) {
			_logger.WriteError($"Error: grantee {grantee} was not found in SysAdminUnit. Nothing was changed — pass the "
				+ "id of an existing role or user.");
			return false;
		}
		granteeLabel = $"'{granteeName}' ({grantee})";
		return true;
	}

	// The fan-out never reaches security/system objects; naming one as the root is the explicit way to change it.
	// Even then two changes widen access to it and need their own opt-in: a grant beyond read, and a revoke that
	// may turn its operation permissions OFF (which opens it to every internal user).
	private bool IsRefusedBySecurityGate(string root, IReadOnlyCollection<ObjectOperation> operations,
		SetObjectRightsOptions options) {
		if (!ConnectedObjectsResolver.IsSecurityOrSystemObject(root) || options.AllowSecurityObject) {
			return false;
		}
		if (!options.Revoke && operations.Any(op => op != ObjectOperation.Read)) {
			_logger.WriteError($"Error: '{root}' is a security/system object, so only read may be granted on it "
				+ "without --allow-security-object. Nothing was changed.");
			return true;
		}
		if (options.Revoke && options.DisableOperationPermissions) {
			_logger.WriteError($"Error: '{root}' is a security/system object. Turning its operation permissions off "
				+ "would make it available to ALL internal users, so --disable-operation-permissions on it needs "
				+ "--allow-security-object. Nothing was changed.");
			return true;
		}
		return false;
	}

	// The change as the confirmation prompt and the preview name it.
	private static string DescribeChange(RightsChangeRequest request) {
		SetObjectRightsOptions options = request.Options;
		string root = request.Objects[0];
		string change = $"{(options.Revoke ? "Revoke" : "Grant")} object operations [{FormatOperations(request.Operations)}] "
			+ $"for grantee {request.GranteeLabel} on '{root}'";
		if (request.Objects.Count > 1) {
			change += $", and [{FormatOperations(request.ConnectedOperations)}] on {request.Objects.Count - 1} "
				+ $"connected object(s) ({string.Join(", ", request.Objects.Skip(1))})";
		}
		change += ".";
		if (options.Revoke && options.DisableOperationPermissions) {
			// The operator approves the access WIDENING here, not just the revoke — so the prompt has to say it.
			change += $" If '{root}' is left with no rights rows, its operation permissions are turned OFF"
				+ " and it becomes available to ALL internal users (connected objects are never turned off).";
		}
		if (!options.Revoke) {
			// The mirror-image transition: granting to an object not administered yet can NARROW it for everyone else.
			change += " An object that does not use operation permissions yet has them turned ON, after which only"
				+ " the roles listed on it can reach it (the result names them).";
		}
		return change;
	}

	// Returns the exit code when the call ends here (a preview, a cancelled or refused prompt, a code that does not
	// match), or null when the change is approved and has to be applied.
	// On MCP the command cannot ask anyone, so the change is split in two calls: a preview that writes nothing and
	// returns a code for the exact targets and every role's current rights on them, and a confirmed call that must
	// carry that code. A code that no longer matches (a new lookup, rights changed by someone else) means the user
	// approved something other than what would now be written. It is called a CODE, not a token, on purpose: the
	// MCP output redactor masks the value after any "...-token:" key, which would hide it from the agent.
	private int? ConfirmChange(RightsChangeRequest request) {
		SetObjectRightsOptions options = request.Options;
		string change = DescribeChange(request);
		if (!options.Preview && string.IsNullOrWhiteSpace(options.ConfirmationCode)) {
			return ConfirmApply(options, change) switch {
				ConfirmDecision.Cancelled => 0,
				ConfirmDecision.Refused => 1,
				_ => null
			};
		}
		List<TargetState> targets = DescribeTargets(request);
		if (targets[0].Problem is not null) {
			// A preview of an object that could not be read shows nothing the user can approve.
			_logger.WriteError($"Error: '{request.Objects[0]}' {targets[0].Problem}. Nothing was changed and no "
				+ "confirmation code was issued.");
			return 1;
		}
		string code = ComputeCode(targets, request);
		if (options.Preview) {
			_logger.WriteInfo($"PREVIEW — nothing was changed. {change}");
			foreach (TargetState target in targets) {
				_logger.WriteInfo($"  {target.Line}");
			}
			_logger.WriteInfo($"confirmation-code: {code}");
			return 0;
		}
		if (!string.Equals(options.ConfirmationCode.Trim(), code, StringComparison.OrdinalIgnoreCase)) {
			_logger.WriteError("Error: the confirmation code does not match — the target objects or their rights "
				+ "changed since the preview, or the arguments differ. Nothing was changed; run a new preview.");
			return 1;
		}
		return null;
	}

	private int ApplyChange(RightsChangeRequest request) {
		IReadOnlyList<string> objects = request.Objects;
		bool anyFailure = false;
		for (int index = 0; index < objects.Count; index++) {
			string schemaName = objects[index];
			bool isRoot = index == 0;
			// Turning operation permissions OFF is only ever approved for the object the caller named: a shared lookup
			// made available to all internal users as a side effect would be a silent widening.
			ObjectRightsChange result = _rightsWriter.SetObjectRights(
				schemaName, request.Grantee, request.OperationsFor(isRoot), request.Options.Revoke,
				isRoot && request.Options.DisableOperationPermissions, request.RequestOptions);
			bool failed = Report(result, schemaName, isRoot, request);
			anyFailure |= failed;
			if (isRoot && failed && !result.Changed && objects.Count > 1) {
				// The root change did not happen, so changing its lookups would leave a half-applied state. A root that
				// WAS written but reported a problem (who lost access) still gets its lookups: stopping there would
				// leave exactly that half-applied state.
				_logger.WriteWarning(
					$"  The root object was not changed, so the {objects.Count - 1} connected object(s) were not attempted.");
				break;
			}
		}
		return anyFailure ? 1 : 0;
	}

	// One target of the preview: the line shown to the user, the state fingerprint the confirmation code is computed
	// from, and why the object could not be described (null when it could).
	private sealed record TargetState(string Line, string Fingerprint, string Problem);

	// Describes every target for the preview and for the confirmation code: the object, its role in the call,
	// whether operation permissions are on, and EVERY role's rights on it. The other roles decide whether a revoke
	// is refused or turns the object off, and who keeps access after a grant turns operation permissions on.
	private List<TargetState> DescribeTargets(RightsChangeRequest request) =>
		request.Objects.Select((schemaName, index) => DescribeTarget(schemaName, index == 0, request)).ToList();

	private TargetState DescribeTarget(string schemaName, bool isRoot, RightsChangeRequest request) {
		string role = isRoot ? "root" : "connected";
		string ops = FormatOperations(request.OperationsFor(isRoot).OrderBy(op => op));
		string head = $"{schemaName} ({role}): {(request.Options.Revoke ? "revoke" : "grant")} [{ops}]. Now: ";
		ObjectRightsInfo info = _rightsReader.GetObjectRights(schemaName, request.RequestOptions);
		if (info.ReadError is not null) {
			return new TargetState(head + "could not read its rights.", $"{schemaName}|{role}|unreadable",
				$"could not be read ({info.ReadError})");
		}
		if (!info.Found) {
			return new TargetState(head + "not found.", $"{schemaName}|{role}|not-found", "was not found");
		}
		IReadOnlyList<RoleOperationRights> roles = info.Roles ?? Array.Empty<RoleOperationRights>();
		string rows = string.Join(";", roles
			.Select(r => $"{r.GranteeId}:{string.Join("/", r.OperationNames())}")
			.OrderBy(row => row, StringComparer.Ordinal));
		string fingerprint = $"{schemaName}|{role}|{(info.AdministratedByOperations ? "on" : "off")}|{rows}";
		return new TargetState(head + DescribeCurrentState(info.AdministratedByOperations, roles, request) + ".",
			fingerprint, null);
	}

	private static string DescribeCurrentState(bool administered, IReadOnlyList<RoleOperationRights> roles,
		RightsChangeRequest request) {
		if (!administered) {
			return request.Options.Revoke
				? "not administered by operation permissions (a revoke cannot restrict it)"
				: "not administered by operation permissions — they will be turned ON";
		}
		// A grantee can hold several rows; the writer changes all of them, so show what they add up to.
		RoleOperationRights[] granteeRows = roles.Where(r => r.GranteeId == request.Grantee).ToArray();
		string granteeHolds = granteeRows.Length == 0
			? "grantee has NO grant"
			: $"grantee holds {HeldOperations(granteeRows)}";
		string[] others = roles.Where(r => r.GranteeId != request.Grantee && r.HasAnyOperation).Select(Describe).ToArray();
		string otherRoles = others.Length == 0
			? "no other role holds rights"
			: $"other roles with rights: {string.Join(", ", others)}";
		return $"administered; {granteeHolds}; {otherRoles}";
	}

	private static readonly string[] OperationOrder = { "read", "create", "edit", "delete" };

	private static string HeldOperations(IEnumerable<RoleOperationRights> rows) {
		string[] held = rows.SelectMany(r => r.OperationNames()).Distinct()
			.OrderBy(op => Array.IndexOf(OperationOrder, op)).ToArray();
		return held.Length == 0 ? "no operations" : string.Join("/", held);
	}

	// A short, stable fingerprint of the arguments and the target state. It is not a secret and not a signature:
	// it only proves the confirmed call is about the same targets, in the same state, that the preview showed.
	private static string ComputeCode(IEnumerable<TargetState> targets, RightsChangeRequest request) {
		SetObjectRightsOptions options = request.Options;
		string material = string.Join("\n", new[] {
			request.Grantee.ToString(), FormatOperations(request.Operations.OrderBy(op => op)),
			FormatOperations(request.ConnectedOperations.OrderBy(op => op)), options.Revoke.ToString(),
			options.DisableOperationPermissions.ToString(), options.AllowSecurityObject.ToString()
		}.Concat(targets.Select(target => target.Fingerprint)));
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
		return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
	}

	// Reports one object's result and returns whether it counts as a failure of the run. One outcome, one branch:
	// the switch over ObjectRightsOutcome replaces an order-dependent chain of boolean checks.
	private bool Report(ObjectRightsChange result, string schemaName, bool isRoot, RightsChangeRequest request) {
		string granteeLabel = request.GranteeLabel;
		string objectOpList = FormatOperations(request.OperationsFor(isRoot));
		string done = request.Options.Revoke ? "revoked" : "granted";
		switch (result.Outcome) {
			case ObjectRightsOutcome.Failed:
				_logger.WriteError($"  {schemaName}: {result.Error}");
				return true;
			case ObjectRightsOutcome.NotFound when isRoot:
				// The object the caller NAMED does not exist (typically a typo): nothing was written, so this is
				// neither applied, a no-op nor cancelled — it must not report success.
				_logger.WriteError($"  {schemaName}: schema not found — nothing was changed. Check the object name.");
				return true;
			case ObjectRightsOutcome.NotFound:
				_logger.WriteWarning($"  {schemaName}: schema not found (skipped).");
				return false;
			case ObjectRightsOutcome.RevokeOnNotAdministered: {
				string message = $"  {schemaName}: not administered by operation permissions — every internal user "
					+ "can reach it, so a revoke cannot restrict it. Nothing was changed. To limit access, grant the "
					+ "roles that should keep it (that turns operation permissions on).";
				if (isRoot) {
					_logger.WriteError(message);
					return true;
				}
				_logger.WriteWarning(message);
				return false;
			}
			case ObjectRightsOutcome.RefusedLastRowRemoval:
				// Nothing was written, so this is not a success: the revoke the operator asked for did not happen.
				_logger.WriteError(
					$"  {schemaName}: grantee {granteeLabel} holds the object's LAST effective grant. Removing it would "
					+ $"turn operation permissions OFF and make '{schemaName}' available to ALL internal users. "
					+ RefusalHint(schemaName, isRoot, request.Options));
				return true;
			case ObjectRightsOutcome.NoChange:
				_logger.WriteInfo($"  {schemaName}: already in the requested state (no change).");
				return false;
			case ObjectRightsOutcome.ChangedAndDisabled:
				_logger.WriteInfo($"  {schemaName}: {done} [{objectOpList}] for grantee {granteeLabel}. Operation "
					+ "permissions are now OFF on this object — it is available to ALL internal users.");
				return false;
			case ObjectRightsOutcome.ChangedAndEnabled:
				return ReportEnabled(result, schemaName, request.Grantee, granteeLabel, objectOpList);
			default:
				_logger.WriteInfo($"  {schemaName}: {done} [{objectOpList}] for grantee {granteeLabel}.");
				return false;
		}
	}

	// What to do after a refused last-grant revoke. No hint towards turning a security/system table off: that
	// opens it to every internal user.
	private static string RefusalHint(string schemaName, bool isRoot, SetObjectRightsOptions options) {
		if (!isRoot) {
			return "Nothing was changed — a connected object is never turned off by a fan-out; name it as "
				+ "--entity-schema-name to do that explicitly.";
		}
		if (ConnectedObjectsResolver.IsSecurityOrSystemObject(schemaName) && !options.AllowSecurityObject) {
			return "Nothing was changed.";
		}
		return "Nothing was changed — re-run with --disable-operation-permissions if that is what you want.";
	}

	// Turning operation permissions on can cut every other internal role off the object. The writer read the object
	// back, so report who actually holds rights now, and fail loudly when internal users lost access.
	private bool ReportEnabled(ObjectRightsChange result, string schemaName, Guid grantee, string granteeLabel,
		string objectOpList) {
		string head = $"  {schemaName}: granted [{objectOpList}] for grantee {granteeLabel}. Operation permissions were "
			+ "turned ON for this object";
		if (result.RolesAfterEnable is null) {
			_logger.WriteWarning($"{head}, but reading it back failed ({result.ReadBackError}) — check with "
				+ "get-object-rights which roles can still reach it.");
			return false;
		}
		IReadOnlyList<RoleOperationRights> holders = result.RolesAfterEnable.Where(role => role.HasAnyOperation).ToList();
		if (holders.Count == 0) {
			_logger.WriteError($"{head}, but the read-back shows NO role with rights on '{schemaName}', so nobody can "
				+ "reach it. The change is already saved; check it with get-object-rights and grant the roles that "
				+ "should have access.");
			return true;
		}
		IReadOnlyList<RoleOperationRights> others = holders.Where(role => role.GranteeId != grantee).ToList();
		// Granting All employees itself keeps every internal user in, even when its row is the only one.
		if (others.Count == 0 && grantee != SysAdminUnitIds.AllEmployees) {
			_logger.WriteError($"{head}, and only the grantee holds rights now — every other internal user LOST access to "
				+ $"'{schemaName}'. The change is already saved. Grant the roles that should keep it (for example All employees).");
			return true;
		}
		IEnumerable<RoleOperationRights> shown = others.Count > 0 ? others : holders;
		_logger.WriteInfo($"{head}; roles with rights now: {string.Join(", ", shown.Select(Describe))}.");
		return false;
	}

	private static string Describe(RoleOperationRights role) =>
		$"{role.GranteeName} ({string.Join("/", role.OperationNames())})";

	// Least-privilege default for the ROOT object: read/create/edit (the access a role needs to work with an
	// object). delete is NOT granted by default — pass it in --operations explicitly.
	private static readonly ObjectOperation[] DefaultOperations =
		{ ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit };

	// Least-privilege default for CONNECTED lookup objects: read only. A role picks a lookup value with read, and
	// fanning create/edit out to every shared dictionary (status/type lookups, Currency, Contact, Account) would
	// hand a broad audience write access it never needed.
	private static readonly ObjectOperation[] DefaultConnectedOperations = { ObjectOperation.Read };

	private static string FormatOperations(IEnumerable<ObjectOperation> operations) =>
		string.Join("/", operations.Select(op => op.ToString().ToLowerInvariant()));

	private static bool TryParseOperations(string optionName, string raw, IReadOnlyCollection<ObjectOperation> defaults,
		out IReadOnlyCollection<ObjectOperation> operations, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			operations = defaults;
			return true;
		}
		List<ObjectOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			switch (token.ToLowerInvariant()) {
				case "read": parsed.Add(ObjectOperation.Read); break;
				case "create": case "append": parsed.Add(ObjectOperation.Create); break;
				case "edit": case "write": parsed.Add(ObjectOperation.Edit); break;
				case "delete": parsed.Add(ObjectOperation.Delete); break;
				default:
					operations = Array.Empty<ObjectOperation>();
					error = $"Error: {optionName}: unknown operation '{token}'. Use read,create,edit,delete.";
					return false;
			}
		}
		if (parsed.Count == 0) {
			// Only separators (for example ","): an empty set would write an empty row on a grant, and could turn
			// operation permissions ON for an object while granting nothing.
			operations = Array.Empty<ObjectOperation>();
			error = $"Error: {optionName}: no operation given. Use read,create,edit,delete.";
			return false;
		}
		operations = parsed.Distinct().ToArray();
		return true;
	}

	// Destructive gate, mirroring other destructive clio commands: --confirm applies without a prompt; without
	// it an interactive run asks y/n, and a non-interactive run refuses (rather than silently applying).
	private ConfirmDecision ConfirmApply(SetObjectRightsOptions options, string change) {
		if (options.Confirm) {
			return ConfirmDecision.Approved;
		}
		if (!_console.IsInteractive) {
			_logger.WriteError(
				"Error: set-object-rights is destructive and needs confirmation. Re-run with --confirm to apply "
				+ $"the change: {change}");
			return ConfirmDecision.Refused;
		}
		_logger.WriteWarning($"About to change object permissions: {change}");
		if (!_console.Prompt("Apply this change?")) {
			_logger.WriteInfo("Object-permissions change cancelled.");
			return ConfirmDecision.Cancelled;
		}
		return ConfirmDecision.Approved;
	}

	private enum ConfirmDecision {
		Approved,
		Cancelled,
		Refused
	}
}
