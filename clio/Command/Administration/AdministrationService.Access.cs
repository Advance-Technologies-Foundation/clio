using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Administration;

public sealed partial class AdministrationService {
	private static readonly string[] FunctionalColumns = ["Id", "OrgRole", "FuncRole"];
	private static readonly string[] DelegationColumns = ["Id", "GrantorSysAdminUnit", "GranteeSysAdminUnit"];
	private static readonly string[] OperationGrantColumns = ["Id", SysAdminOperationSchema, SysAdminUnitSchema, "CanExecute", "Position"];

	/// <inheritdoc />
	public JsonElement GetFunctionalRoles(Guid roleId, int offset, int limit) {
		GetOrganizationalRole(roleId);
		return client.Select("SysFuncRoleInOrgRole", FunctionalColumns,
			new Dictionary<string, object> { ["OrgRole"] = roleId }, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement SetFunctionalRole(Guid roleId, Guid functionalId, bool remove) {
		JsonElement org = GetOrganizationalRole(roleId);
		JsonElement functional = GetRole(functionalId);
		if (TypeOf(functional) != 6) { throw new ArgumentException("The associated role must be functional (type 6)."); }
		if (org.GetProperty(ConnectionTypeColumn).GetInt32() != functional.GetProperty(ConnectionTypeColumn).GetInt32()) {
			throw new ArgumentException("Associated roles must have matching connection types.");
		}
		Dictionary<string, object> filters = new() { ["OrgRole"] = roleId, ["FuncRole"] = functionalId };
		JsonElement rows = client.Select("SysFuncRoleInOrgRole", FunctionalColumns, filters, limit: 2);
		if (rows.GetArrayLength() > 1) { throw new AdministrationStateException("Duplicate functional associations require explicit repair."); }
		JsonElement? receipt = null;
		bool alreadyAbsent = remove && rows.GetArrayLength() == 0;
		if (remove && rows.GetArrayLength() == 1) {
			receipt = client.Post(ServiceUrlBuilder.KnownRoute.AdministrationRemoveFunctionalRole, "RemoveFunctionalRoleAssociationResult",
				new { associationId = rows[0].GetProperty("Id").GetGuid() }, AdministrationResponseKind.SuccessObject);
		} else if (!remove && rows.GetArrayLength() == 0) {
			client.Post(ServiceUrlBuilder.KnownRoute.AdministrationAddFunctionalRoles, "AddFuncRolesInOrgRoleResult",
				new { orgRoleId = roleId, funcRoleIds = JsonSerializer.Serialize(new[] { functionalId }) }, AdministrationResponseKind.ErrorString);
		}
		if (receipt is null) { Actualize(); }
		rows = client.Select("SysFuncRoleInOrgRole", FunctionalColumns, filters, limit: 2);
		RequireExpectedCount(rows, remove ? 0 : 1);
		return JsonSerializer.SerializeToElement(new {
			associations = rows, redistributionReceipt = receipt,
			completed = receipt is null || (receipt.Value.TryGetProperty("completed", out JsonElement completed) && completed.GetBoolean()),
			licenseReconciliationRequired = alreadyAbsent || (receipt is not null
				&& receipt.Value.TryGetProperty("licenseReconciliationRequired", out JsonElement reconcile) && reconcile.GetBoolean()),
			userAssignmentsVerified = false
		});
	}

	/// <inheritdoc />
	public JsonElement GetDelegations(Guid userId, int offset, int limit) {
		GetUser(userId);
		return client.Select("SysAdminUnitGrantedRight", DelegationColumns,
			new Dictionary<string, object> { ["GranteeSysAdminUnit"] = userId }, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement SetDelegation(Guid grantorId, Guid granteeId, bool remove) {
		JsonElement grantor = GetUnit(grantorId);
		JsonElement grantee = GetUser(granteeId);
		if (TypeOf(grantor) == 7 || TypeOf(grantee) == 7 || grantorId == granteeId) {
			throw new ArgumentException("Delegation requires distinct identities and does not support technical users.");
		}
		Dictionary<string, object> filters = new() { ["GrantorSysAdminUnit"] = grantorId, ["GranteeSysAdminUnit"] = granteeId };
		JsonElement rows = client.Select("SysAdminUnitGrantedRight", DelegationColumns, filters, limit: 2);
		if (rows.GetArrayLength() > 1) { throw new AdministrationStateException("Duplicate delegations require explicit repair."); }
		if (remove && rows.GetArrayLength() == 1) {
			client.Post(ServiceUrlBuilder.KnownRoute.AdministrationRemoveDelegation, "RemoveSysAdminUnitGrantedRightsResult",
				new { selectedRecords = JsonSerializer.Serialize(new[] { rows[0].GetProperty("Id").GetGuid() }) }, AdministrationResponseKind.ErrorString);
		} else if (!remove && rows.GetArrayLength() == 0) {
			client.Post(ServiceUrlBuilder.KnownRoute.AdministrationAddDelegation, "AddSysAdminUnitGrantedRightsResult",
				new { masterRecordId = grantorId, selectedRecords = JsonSerializer.Serialize(new[] { granteeId }) }, AdministrationResponseKind.ErrorString);
		}
		Actualize();
		rows = client.Select("SysAdminUnitGrantedRight", DelegationColumns, filters, limit: 2);
		RequireExpectedCount(rows, remove ? 0 : 1);
		return rows;
	}

	/// <inheritdoc />
	public JsonElement GetOperations(string code, int offset, int limit) => client.Select(SysAdminOperationSchema,
		["Id", "Name", "Code", "Description"], code is null ? new Dictionary<string, object>()
			: new Dictionary<string, object> { ["Code"] = code }, offset, limit);

	/// <inheritdoc />
	public JsonElement GetOperationGrants(Guid operationId, int offset, int limit) {
		RequireOperation(operationId);
		return client.Select(SysAdminOperationGranteeSchema, OperationGrantColumns,
			new Dictionary<string, object> { [SysAdminOperationSchema] = operationId }, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement SetOperationGrant(Guid operationId, Guid unitId, bool canExecute, bool remove) {
		RequireOperation(operationId);
		GetUnit(unitId);
		Dictionary<string, object> filters = new() { [SysAdminOperationSchema] = operationId, [SysAdminUnitSchema] = unitId };
		JsonElement rows = client.Select(SysAdminOperationGranteeSchema, OperationGrantColumns, filters, limit: 2);
		if (rows.GetArrayLength() > 1) { throw new AdministrationStateException("Duplicate operation grants require explicit repair."); }
		if (remove && rows.GetArrayLength() == 1) {
			client.Post(ServiceUrlBuilder.KnownRoute.RightsDeleteOperationGrantee, "DeleteAdminOperationGranteeResult",
				new { recordIds = new[] { rows[0].GetProperty("Id").GetGuid() } }, AdministrationResponseKind.SuccessObject);
		} else if (!remove) {
			client.Post(ServiceUrlBuilder.KnownRoute.RightsSetOperationGrantee, "SetAdminOperationGranteeResult",
				new { adminOperationId = operationId, adminUnitIds = new[] { unitId }, canExecute }, AdministrationResponseKind.SuccessObject);
		}
		rows = client.Select(SysAdminOperationGranteeSchema, OperationGrantColumns, filters, limit: 2);
		RequireExpectedCount(rows, remove ? 0 : 1);
		if (!remove && rows[0].GetProperty("CanExecute").GetBoolean() != canExecute) {
			throw new AdministrationStateException("Operation permission readback differs from the request.");
		}
		return rows;
	}

	/// <inheritdoc />
	public JsonElement SetOperationPosition(Guid grantId, int position) {
		RequireId(grantId);
		if (position < 0) { throw new ArgumentException("Operation grant position must be nonnegative."); }
		Dictionary<string, object> filters = new() { ["Id"] = grantId };
		JsonElement rows = client.Select(SysAdminOperationGranteeSchema, OperationGrantColumns, filters, limit: 2);
		RequireExpectedCount(rows, 1);
		// Validate bridge authorization and native cache support before persisting a new priority.
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationInvalidateRightsCache, "InvalidateAdministrationRightsCacheResult",
			new { }, AdministrationResponseKind.True);
		client.Post(ServiceUrlBuilder.KnownRoute.RightsSetOperationPosition, "SetAdminOperationGranteePositionResult",
			new { granteeId = grantId, position }, AdministrationResponseKind.SuccessObject);
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationInvalidateRightsCache, "InvalidateAdministrationRightsCacheResult",
			new { }, AdministrationResponseKind.True);
		rows = client.Select(SysAdminOperationGranteeSchema, OperationGrantColumns, filters, limit: 2);
		RequireExpectedCount(rows, 1);
		if (rows[0].GetProperty("Position").GetInt32() != position) {
			throw new AdministrationStateException("Operation priority readback differs from the request.");
		}
		return rows[0].Clone();
	}

	private JsonElement GetOrganizationalRole(Guid id) {
		JsonElement role = GetRole(id);
		if (TypeOf(role) is not (0 or 1 or 2 or 3)) { throw new ArgumentException("An organizational or manager role is required."); }
		return role;
	}

	private void RequireOperation(Guid id) {
		RequireId(id);
		RequireExpectedCount(client.Select(SysAdminOperationSchema, ["Id"], new Dictionary<string, object> { ["Id"] = id }, limit: 2), 1);
	}

	private static void RequireExpectedCount(JsonElement rows, int count) {
		if (rows.GetArrayLength() != count) { throw new AdministrationStateException("Administration record count differs from the expected state. Inspect before retrying."); }
	}
}
