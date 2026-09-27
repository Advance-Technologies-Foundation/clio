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
		"Allow granting create/edit/delete when the ROOT object is a security or system object (SysAdmin*, SysUser*, "
		+ "SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, Vw*, *Right/*Rights). Without it such a root may only be granted read.")]
	public bool AllowSecurityObject { get; set; }

	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	[Option("preview", Required = false, HelpText =
		"Write nothing: list every object the call would change, its current state and what it would get, and print "
		+ "a confirmation code for --confirmation-code")]
	public bool Preview { get; set; }

	[Option("confirmation-code", Required = false, HelpText =
		"Token from a --preview run. The change is applied only if the target objects and their rights are still "
		+ "exactly what that preview showed")]
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
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return 1;
		}
		if (!Guid.TryParse(options.Grantee, out Guid grantee) || grantee == Guid.Empty) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		if (!TryParseOperations("--operations", options.Operations, DefaultOperations,
				out IReadOnlyCollection<ObjectOperation> operations, out string opError)) {
			_logger.WriteError(opError);
			return 1;
		}
		if (!TryParseOperations("--connected-operations", options.ConnectedOperations, DefaultConnectedOperations,
				out IReadOnlyCollection<ObjectOperation> connectedOperations, out string connectedError)) {
			_logger.WriteError(connectedError);
			return 1;
		}

		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

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

		// Resolve the full target set BEFORE confirming, so the destructive prompt names every object that will
		// be written (with --include-connected the fan-out can span several permission objects).
		ConnectedObjectsResolution resolution = _connectedObjects.Resolve(options.EntitySchemaName, fanOut);
		if (resolution.EnumerationError is not null) {
			// The caller asked for the root AND its lookups. Writing the root alone would report success while the
			// lookups stay unreachable, so nothing is written.
			_logger.WriteError(
				$"Error: could not enumerate the connected objects of '{options.EntitySchemaName}' "
				+ $"({resolution.EnumerationError}). Nothing was changed. Check that the object name exists; if it "
				+ "does, re-run, or drop --include-connected to change the root object only.");
			return 1;
		}
		foreach (string excluded in resolution.Excluded) {
			_logger.WriteWarning(
				$"  {excluded}: security/system object — not included in the fan-out. Pass it as "
				+ "--entity-schema-name to change it explicitly.");
		}
		IReadOnlyList<string> objects = resolution.Objects;

		// The grantee must exist: granting to an unknown id would turn operation permissions on for a principal
		// nobody holds — cutting access for everyone else — and still report "granted".
		string granteeName;
		try {
			granteeName = _granteeLookup.ResolveGranteeName(grantee, requestOptions);
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: could not check grantee {grantee}: {ex.Message}");
			return 1;
		}
		if (granteeName is null) {
			_logger.WriteError($"Error: grantee {grantee} was not found in SysAdminUnit. Nothing was changed — pass the "
				+ "id of an existing role or user.");
			return 1;
		}
		string granteeLabel = $"'{granteeName}' ({grantee})";

		// The fan-out never reaches security/system objects; naming one as the root is the explicit way to change
		// it. Even then a grant beyond read is a privilege-escalation path, so it needs its own opt-in.
		if (!options.Revoke && ConnectedObjectsResolver.IsSecurityOrSystemObject(objects[0])
			&& operations.Any(op => op != ObjectOperation.Read) && !options.AllowSecurityObject) {
			_logger.WriteError($"Error: '{objects[0]}' is a security/system object, so only read may be granted on it "
				+ "without --allow-security-object. Nothing was changed.");
			return 1;
		}

		string verb = options.Revoke ? "Revoke" : "Grant";
		string opList = FormatOperations(operations);
		string connectedOpList = FormatOperations(connectedOperations);
		string change = $"{verb} object operations [{opList}] for grantee {granteeLabel} on '{objects[0]}'";
		if (objects.Count > 1) {
			change += $", and [{connectedOpList}] on {objects.Count - 1} connected object(s) "
				+ $"({string.Join(", ", objects.Skip(1))})";
		}
		change += ".";
		if (options.Revoke && options.DisableOperationPermissions) {
			// The operator approves the access WIDENING here, not just the revoke — so the prompt has to say it.
			change += $" If '{objects[0]}' is left with no rights rows, its operation permissions are turned OFF"
				+ " and it becomes available to ALL internal users (connected objects are never turned off).";
		}
		if (!options.Revoke) {
			// The mirror-image transition: granting to an object not administered yet can NARROW it for everyone else.
			change += " An object that does not use operation permissions yet has them turned ON, after which only"
				+ " the roles listed on it can reach it (the result names them).";
		}

		// Preview and code confirmation. On MCP the command cannot ask anyone, so the change is split in two calls:
		// a preview that writes nothing and returns a code for the exact targets and their current rights, and a
		// confirmed call that must carry that code. It is called a CODE, not a token, on purpose: the MCP output
		// redactor masks the value after any "...-token:" key, which would hide it from the agent. A code that no longer matches (a new lookup, rights changed
		// by someone else) means the user approved something other than what would now be written.
		if (options.Preview || !string.IsNullOrWhiteSpace(options.ConfirmationCode)) {
			List<string> state = DescribeTargets(objects, grantee, operations, connectedOperations, options, requestOptions);
			string code = ComputeCode(state, grantee, operations, connectedOperations, options);
			if (options.Preview) {
				_logger.WriteInfo($"PREVIEW — nothing was changed. {change}");
				foreach (string line in state) {
					_logger.WriteInfo($"  {line}");
				}
				_logger.WriteInfo($"confirmation-code: {code}");
				return 0;
			}
			if (!string.Equals(options.ConfirmationCode.Trim(), code, StringComparison.OrdinalIgnoreCase)) {
				_logger.WriteError("Error: the confirmation code does not match — the target objects or their rights "
					+ "changed since the preview, or the arguments differ. Nothing was changed; run a new preview.");
				return 1;
			}
		} else {
			ConfirmDecision decision = ConfirmApply(options, change);
			if (decision == ConfirmDecision.Cancelled) {
				return 0;
			}
			if (decision == ConfirmDecision.Refused) {
				return 1;
			}
		}

		try {
			bool anyFailure = false;
			for (int index = 0; index < objects.Count; index++) {
				string schemaName = objects[index];
				bool isRoot = index == 0;
				IReadOnlyCollection<ObjectOperation> objectOperations = isRoot ? operations : connectedOperations;
				string objectOpList = isRoot ? opList : connectedOpList;
				// Turning operation permissions OFF is only ever approved for the object the caller named: a shared
				// lookup made available to all internal users as a side effect would be a silent widening.
				ObjectRightsChange result = _rightsWriter.SetObjectRights(
					schemaName, grantee, objectOperations, options.Revoke,
					isRoot && options.DisableOperationPermissions, requestOptions);
				anyFailure |= Report(result, schemaName, isRoot, grantee, granteeLabel, objectOpList, options.Revoke);
				if (isRoot && anyFailure && objects.Count > 1) {
					// The root change did not happen, so changing its lookups would leave a half-applied state.
					_logger.WriteWarning(
						$"  The root object was not changed, so the {objects.Count - 1} connected object(s) were not attempted.");
					break;
				}
			}
			return anyFailure ? 1 : 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// One line per target for the preview, and the input of the confirmation code: the object, its role in the
	// call, whether operation permissions are on, and what the grantee holds now. Any change to these between the
	// preview and the confirmed call changes the code.
	private List<string> DescribeTargets(IReadOnlyList<string> objects, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, IReadOnlyCollection<ObjectOperation> connectedOperations,
		SetObjectRightsOptions options, CreatioRequestOptions requestOptions) {
		List<string> lines = new();
		for (int index = 0; index < objects.Count; index++) {
			string schemaName = objects[index];
			string role = index == 0 ? "root" : "connected";
			string ops = FormatOperations(index == 0 ? operations : connectedOperations);
			ObjectRightsInfo info = _rightsReader.GetObjectRights(schemaName, requestOptions);
			string current;
			if (info.ReadError is not null) {
				current = "could not read its rights";
			} else if (!info.Found) {
				current = "not found";
			} else if (!info.AdministratedByOperations) {
				current = options.Revoke
					? "not administered by operation permissions (a revoke cannot restrict it)"
					: "not administered by operation permissions — they will be turned ON";
			} else {
				RoleOperationRights row = info.Roles.FirstOrDefault(r => r.GranteeId == grantee);
				current = row is null ? "administered; grantee has NO grant" : $"administered; grantee holds {Operations(row)}";
			}
			lines.Add($"{schemaName} ({role}): {(options.Revoke ? "revoke" : "grant")} [{ops}]. Now: {current}.");
		}
		return lines;
	}

	private static string Operations(RoleOperationRights role) {
		string[] ops = new[] {
			role.CanRead ? "read" : null,
			role.CanCreate ? "create" : null,
			role.CanEdit ? "edit" : null,
			role.CanDelete ? "delete" : null
		}.Where(op => op != null).ToArray();
		return ops.Length == 0 ? "no operations" : string.Join("/", ops);
	}

	// A short, stable fingerprint of the arguments and the described target state. It is not a secret and not a
	// signature: it only proves the confirmed call is about the same targets, in the same state, that were shown.
	private static string ComputeCode(IEnumerable<string> state, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, IReadOnlyCollection<ObjectOperation> connectedOperations,
		SetObjectRightsOptions options) {
		string material = string.Join("\n", new[] {
			grantee.ToString(), FormatOperations(operations.OrderBy(op => op)),
			FormatOperations(connectedOperations.OrderBy(op => op)), options.Revoke.ToString(),
			options.DisableOperationPermissions.ToString(), options.AllowSecurityObject.ToString()
		}.Concat(state));
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
		return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
	}

	// Reports one object's result and returns whether it counts as a failure of the run. One outcome, one branch:
	// the switch over ObjectRightsOutcome replaces an order-dependent chain of boolean checks.
	private bool Report(ObjectRightsChange result, string schemaName, bool isRoot, Guid grantee, string granteeLabel,
		string objectOpList, bool revoke) {
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
					+ (isRoot
						? "Nothing was changed — re-run with --disable-operation-permissions if that is what you want."
						: "Nothing was changed — a connected object is never turned off by a fan-out; name it as "
							+ "--entity-schema-name to do that explicitly."));
				return true;
			case ObjectRightsOutcome.NoChange:
				_logger.WriteInfo($"  {schemaName}: already in the requested state (no change).");
				return false;
			case ObjectRightsOutcome.ChangedAndDisabled:
				_logger.WriteInfo($"  {schemaName}: {(revoke ? "revoked" : "granted")} [{objectOpList}] for grantee "
					+ $"{granteeLabel}. Operation permissions are now OFF on this object — it is available to ALL internal users.");
				return false;
			case ObjectRightsOutcome.ChangedAndEnabled:
				return ReportEnabled(result, schemaName, grantee, granteeLabel, objectOpList);
			default:
				_logger.WriteInfo($"  {schemaName}: {(revoke ? "revoked" : "granted")} [{objectOpList}] for grantee {granteeLabel}.");
				return false;
		}
	}

	// Turning operation permissions on can cut every other internal role off the object. The writer read the object
	// back, so report who actually holds rights now, and fail loudly when only the grantee does.
	private bool ReportEnabled(ObjectRightsChange result, string schemaName, Guid grantee, string granteeLabel,
		string objectOpList) {
		string head = $"  {schemaName}: granted [{objectOpList}] for grantee {granteeLabel}. Operation permissions were "
			+ "turned ON for this object";
		if (result.RolesAfterEnable is null) {
			_logger.WriteWarning($"{head}, but reading it back failed ({result.ReadBackError}) — check with "
				+ "get-object-rights which roles can still reach it.");
			return false;
		}
		IReadOnlyList<RoleOperationRights> others = result.RolesAfterEnable
			.Where(role => role.GranteeId != grantee && (role.CanRead || role.CanCreate || role.CanEdit || role.CanDelete))
			.ToList();
		if (others.Count == 0) {
			_logger.WriteError($"{head}, and only the grantee holds rights now — every other internal user LOST access to "
				+ $"'{schemaName}'. Grant the roles that should keep it (for example All employees).");
			return true;
		}
		_logger.WriteInfo($"{head}; roles with rights now: {string.Join(", ", others.Select(Describe))}.");
		return false;
	}

	private static string Describe(RoleOperationRights role) {
		string[] ops = new[] {
			role.CanRead ? "read" : null,
			role.CanCreate ? "create" : null,
			role.CanEdit ? "edit" : null,
			role.CanDelete ? "delete" : null
		}.Where(op => op != null).ToArray();
		return $"{role.GranteeName} ({string.Join("/", ops)})";
	}

	// Least-privilege default for the ROOT object: read/create/edit (the access a role needs to work with an
	// object). delete is NOT granted by default — pass it in --operations explicitly.
	private static readonly ObjectOperation[] DefaultOperations =
		{ ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit };

	// Least-privilege default for CONNECTED lookup objects: read only. A role picks a lookup value with read;
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
