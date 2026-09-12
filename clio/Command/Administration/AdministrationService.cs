using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Administration;

/// <summary>Preserves native administration semantics and verifies mutation effects.</summary>
public sealed partial class AdministrationService(IAdministrationClient client) : IAdministrationService {

	private const string SysAdminOperationSchema = "SysAdminOperation";
	private const string SysAdminOperationGranteeSchema = "SysAdminOperationGrantee";
	private const string SysAdminUnitSchema = "SysAdminUnit";
	private const string SysAdminUnitIPRangeSchema = "SysAdminUnitIPRange";
	private const string SysLicPackageInRoleSchema = "SysLicPackageInRole";
	private const string ContactColumn = "Contact";
	private const string ActiveColumn = "Active";
	private const string SysAdminUnitTypeValueColumn = "SysAdminUnitTypeValue";
	private const string ParentRoleColumn = "ParentRole";
	private const string ConnectionTypeColumn = "ConnectionType";
	private const string SysUserInRoleSchema = "SysUserInRole";
	private const string SysRoleColumn = "SysRole";
	private const string SysUserColumn = "SysUser";

	private static readonly string[] UnitColumns = [
		"Id", "Name", SysAdminUnitTypeValueColumn, ParentRoleColumn, ActiveColumn, ContactColumn, ConnectionTypeColumn,
		"SynchronizeWithLDAP", "ForceChangePassword"
	];
	private static readonly Guid SystemAdministrators = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");

	/// <inheritdoc />
	public JsonElement ListUnits(Guid? id, string name, int? type, int offset, int limit, bool? rolesOnly = null) {
		Dictionary<string, object> filters = new();
		if (id.HasValue) { RequireId(id.Value); filters["Id"] = id.Value; }
		if (name is not null) { RequireName(name); filters["Name"] = name; }
		if (type.HasValue) { filters[SysAdminUnitTypeValueColumn] = type.Value; }
		if (!type.HasValue && rolesOnly.HasValue) {
			filters[SysAdminUnitTypeValueColumn] = rolesOnly.Value ? new[] { 0, 1, 2, 3, 6 } : new[] { 4, 5, 7 };
		}
		if (rolesOnly == true && type.HasValue && type.Value is not (0 or 1 or 2 or 3 or 6)) {
			throw new ArgumentException("Role inspection accepts only role types 0, 1, 2, 3 or 6.");
		}
		return client.Select(SysAdminUnitSchema, UnitColumns, filters, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement CreateRole(Guid id, string name, int type, Guid parentId) {
		RequireId(id);
		RequireName(name);
		if (type is not (0 or 1 or 3 or 6)) {
			throw new ArgumentException("Role type must be organization (0), division (1), team (3), or functional (6).");
		}
		JsonElement existing = ListUnits(id, null, null, 0, 1);
		if (existing.GetArrayLength() != 0) {
			throw new ArgumentException("The role ID already exists. Read it and use update explicitly.");
		}
		JsonElement parent = GetUnit(parentId);
		ValidateParent(parent, type);
		JsonElement typeRows = client.Select("SysAdminUnitType", ["Id", "Value"],
			new Dictionary<string, object> { ["Value"] = type }, limit: 2);
		if (typeRows.GetArrayLength() != 1) {
			throw new AdministrationStateException("Creatio did not return one matching role type.");
		}
		Dictionary<string, object> values = new() {
			["Id"] = id, ["Name"] = name, [ParentRoleColumn] = parentId.ToString(),
			["SysAdminUnitType"] = typeRows[0].GetProperty("Id").GetString(),
			[ConnectionTypeColumn] = parent.GetProperty(ConnectionTypeColumn).GetInt32(), [ActiveColumn] = true
		};
		SaveRole(values);
		JsonElement result = GetUnit(id);
		if (TypeOf(result) != type || result.GetProperty("Name").GetString() != name
			|| LookupId(result, ParentRoleColumn) != parentId) {
			throw new AdministrationStateException("Role readback differs from the requested state. Inspect it before retrying.");
		}
		return result;
	}

	/// <inheritdoc />
	public JsonElement UpdateRole(Guid id, string name, Guid? parentId) {
		JsonElement current = GetRole(id);
		if (name is null && parentId is null) {
			throw new ArgumentException("Supply a role name or parent to update.");
		}
		Dictionary<string, object> values = new() { ["Id"] = id };
		if (name is not null) { RequireName(name); values["Name"] = name; }
		if (parentId.HasValue) {
			if (TypeOf(current) == 2) {
				throw new ArgumentException("A manager role cannot be moved to another organizational role.");
			}
			JsonElement parent = GetRole(parentId.Value);
			ValidateParent(parent, TypeOf(current));
			ValidateHierarchy(id, parentId.Value);
			if (parent.GetProperty(ConnectionTypeColumn).GetInt32() != current.GetProperty(ConnectionTypeColumn).GetInt32()) {
				throw new ArgumentException("The parent must have the same connection type as the role.");
			}
			values[ParentRoleColumn] = parentId.Value.ToString();
		}
		SaveRole(values);
		Actualize();
		JsonElement result = GetRole(id);
		if ((name is not null && result.GetProperty("Name").GetString() != name)
			|| (parentId.HasValue && LookupId(result, ParentRoleColumn) != parentId.Value)) {
			throw new AdministrationStateException("Role update could not be verified.");
		}
		return result;
	}

	/// <inheritdoc />
	public JsonElement EnsureManager(Guid parentId) {
		JsonElement parent = GetRole(parentId);
		if (TypeOf(parent) is not (0 or 1 or 3)) {
			throw new ArgumentException("Manager roles require an organizational parent.");
		}
		JsonElement children = ManagerChildren(parentId);
		if (children.GetArrayLength() > 1) {
			throw new AdministrationStateException("More than one manager role exists for this parent. Resolve the ambiguity explicitly.");
		}
		if (children.GetArrayLength() == 1) { return children[0].Clone(); }
		if (parent.GetProperty(ConnectionTypeColumn).GetInt32() != 0) {
			throw new ArgumentException("Creating a missing external-user manager role is not verified; use the native administrator UI.");
		}
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationSaveChiefsRole, "SaveChiefsRoleResult",
			new { id = parentId, name = parent.GetProperty("Name").GetString() }, AdministrationResponseKind.SuccessObject);
		children = ManagerChildren(parentId);
		if (children.GetArrayLength() != 1) {
			throw new AdministrationStateException("Manager creation did not produce a unique manager child. Inspect before retrying.");
		}
		return children[0].Clone();
	}

	/// <inheritdoc />
	public void DeleteRole(Guid id) {
		JsonElement role = GetRole(id);
		if (id == SystemAdministrators || LookupId(role, ParentRoleColumn) == Guid.Empty) {
			throw new ArgumentException("Built-in root and system-administrator roles cannot be deleted by this command.");
		}
		JsonElement children = client.Select(SysAdminUnitSchema, ["Id"],
			new Dictionary<string, object> { [ParentRoleColumn] = id }, limit: 1);
		JsonElement members = client.Select(SysUserInRoleSchema, ["Id"],
			new Dictionary<string, object> { [SysRoleColumn] = id }, limit: 1);
		if (children.GetArrayLength() != 0 || members.GetArrayLength() != 0) {
			throw new ArgumentException("Remove or move the role's children and direct members before deleting it.");
		}
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationDeleteRecords, "DeleteRecordsResult",
			new { rootSchema = "VwSysAdminUnit", primaryColumnValues = new[] { id } }, AdministrationResponseKind.SuccessObject);
		Actualize();
		if (ListUnits(id, null, null, 0, 1).GetArrayLength() != 0) {
			throw new AdministrationStateException("The role still exists after deletion.");
		}
	}

	/// <inheritdoc />
	public JsonElement SetMembership(Guid userId, Guid roleId, bool remove) {
		JsonElement user = GetUnit(userId);
		if (TypeOf(user) is not (4 or 5 or 7)) { throw new ArgumentException("The member ID must identify a user."); }
		JsonElement role = GetRole(roleId);
		if (!remove && user.GetProperty(ConnectionTypeColumn).GetInt32() != role.GetProperty(ConnectionTypeColumn).GetInt32()) {
			throw new ArgumentException("The user and role must have matching connection types.");
		}
		if (remove) {
			client.Post(ServiceUrlBuilder.KnownRoute.AdministrationRemoveUsersInRoles, "RemoveUsersInRolesResult",
				new { roleIds = JsonSerializer.Serialize(new[] { roleId }), userIds = JsonSerializer.Serialize(new[] { userId }),
					recordIds = "[]" }, AdministrationResponseKind.SuccessObject);
		} else {
			client.Post(ServiceUrlBuilder.KnownRoute.AdministrationAddUserRoles, "AddUserRolesResult",
				new { userId, roleIds = JsonSerializer.Serialize(new[] { roleId }) }, AdministrationResponseKind.ErrorString);
		}
		Actualize();
		JsonElement direct = client.Select(SysUserInRoleSchema, ["Id", SysUserColumn, SysRoleColumn],
			new Dictionary<string, object> { [SysUserColumn] = userId, [SysRoleColumn] = roleId }, limit: 2);
		if (direct.GetArrayLength() != (remove ? 0 : 1)) {
			throw new AdministrationStateException("Direct membership does not match the requested state.");
		}
		return direct;
	}

	/// <inheritdoc />
	public JsonElement GetMemberships(Guid userId, bool effective, int offset, int limit) {
		JsonElement user = GetUnit(userId);
		if (TypeOf(user) is not (4 or 5 or 7)) { throw new ArgumentException("The member ID must identify a user."); }
		return effective
			? client.Select("SysAdminUnitInRole", ["Id", SysAdminUnitSchema, "SysAdminUnitRoleId"],
				new Dictionary<string, object> { [SysAdminUnitSchema] = userId }, offset, limit)
			: client.Select(SysUserInRoleSchema, ["Id", SysUserColumn, SysRoleColumn],
				new Dictionary<string, object> { [SysUserColumn] = userId }, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement GetMembers(Guid roleId, bool effective, int offset, int limit) {
		GetRole(roleId);
		return effective
			? client.Select("SysAdminUnitInRole", ["Id", SysAdminUnitSchema, "SysAdminUnitRoleId"],
				new Dictionary<string, object> { ["SysAdminUnitRoleId"] = roleId,
					["SysAdminUnit.SysAdminUnitTypeValue"] = new[] { 4, 5, 7 } }, offset, limit)
			: client.Select(SysUserInRoleSchema, ["Id", SysUserColumn, SysRoleColumn],
				new Dictionary<string, object> { [SysRoleColumn] = roleId }, offset, limit);
	}

	private void ValidateHierarchy(Guid id, Guid parentId) {
		HashSet<Guid> visited = [id];
		Guid cursor = parentId;
		while (cursor != Guid.Empty) {
			if (!visited.Add(cursor) || visited.Count > 200) {
				throw new ArgumentException("The requested role hierarchy is cyclic or exceeds the supported depth.");
			}
			cursor = LookupId(GetRole(cursor), ParentRoleColumn);
		}
	}

	private JsonElement ManagerChildren(Guid parentId) => client.Select(SysAdminUnitSchema, UnitColumns,
		new Dictionary<string, object> { [ParentRoleColumn] = parentId, [SysAdminUnitTypeValueColumn] = 2 }, limit: 2);

	private void SaveRole(Dictionary<string, object> values) => client.Post(
		ServiceUrlBuilder.KnownRoute.AdministrationSaveRole, "SaveRoleResult",
		new { jsonObject = JsonSerializer.Serialize(values) }, AdministrationResponseKind.SuccessObject);

	private void Actualize() => client.Post(ServiceUrlBuilder.KnownRoute.AdministrationActualize,
		"ActualizeAdminUnitInRoleResult", new { }, AdministrationResponseKind.True);

	private JsonElement GetUnit(Guid id) {
		JsonElement rows = ListUnits(id, null, null, 0, 2);
		if (rows.GetArrayLength() != 1) { throw new ArgumentException("The administration ID does not identify one existing user or role."); }
		return rows[0].Clone();
	}

	private JsonElement GetRole(Guid id) {
		JsonElement role = GetUnit(id);
		if (TypeOf(role) is not (0 or 1 or 2 or 3 or 6)) { throw new ArgumentException("The ID must identify a role."); }
		return role;
	}

	private static int TypeOf(JsonElement unit) => unit.GetProperty(SysAdminUnitTypeValueColumn).GetInt32();

	private static Guid LookupId(JsonElement unit, string column) {
		JsonElement value = unit.GetProperty(column);
		if (value.ValueKind == JsonValueKind.Null) { return Guid.Empty; }
		if (value.ValueKind == JsonValueKind.Object) { value = value.GetProperty("value"); }
		return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out Guid id) ? id : Guid.Empty;
	}

	private static void ValidateParent(JsonElement parent, int childType) {
		int parentType = TypeOf(parent);
		Guid parentId = parent.GetProperty("Id").GetGuid();
		if (childType == 6 && parentType != 6
			&& parentId != Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008")
			&& parentId != Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f")) {
			throw new ArgumentException("A functional role must belong to another functional role or the All employees/All external users anchor.");
		}
		if (parentType is not (0 or 1 or 3 or 6) || (childType != 6 && parentType == 6)) {
			throw new ArgumentException("The parent role type is incompatible with the requested role.");
		}
	}

	private static void RequireId(Guid id) {
		if (id == Guid.Empty) { throw new ArgumentException("Administration IDs must be nonempty GUIDs."); }
	}

	private static void RequireName(string name) {
		if (string.IsNullOrWhiteSpace(name) || name.Length > 250) { throw new ArgumentException("Provide a nonblank name of at most 250 characters."); }
	}
}
