using System;
using System.Text.Json;

namespace Clio.Command.Administration;

/// <summary>Validated operations over native Creatio user and role administration.</summary>
public interface IAdministrationService {

	/// <summary>Reads safe user/role fields with optional exact filters and paging.</summary>
	JsonElement ListUnits(Guid? id, string name, int? type, int offset, int limit, bool? rolesOnly = null);
	/// <summary>Creates a role with an explicit identity, type and parent.</summary>
	JsonElement CreateRole(Guid id, string name, int type, Guid parentId);
	/// <summary>Updates only the supplied role name or parent.</summary>
	JsonElement UpdateRole(Guid id, string name, Guid? parentId);
	/// <summary>Returns or creates the unique manager child of an organizational role.</summary>
	JsonElement EnsureManager(Guid parentId);
	/// <summary>Deletes an empty leaf role through native administration checks.</summary>
	void DeleteRole(Guid id);
	/// <summary>Adds or removes one direct user membership and actualizes effective roles.</summary>
	JsonElement SetMembership(Guid userId, Guid roleId, bool remove);
	/// <summary>Reads direct or effective role memberships for a user.</summary>
	JsonElement GetMemberships(Guid userId, bool effective, int offset, int limit);
	/// <summary>Reads a role's direct users or effective members with bounded pagination.</summary>
	JsonElement GetMembers(Guid roleId, bool effective, int offset, int limit);
	/// <summary>Creates a local employee or external user linked to an existing contact; membership is a separate operation.</summary>
	JsonElement CreateUser(Guid id, string login, Guid contactId, bool external, string password, bool forceChangePassword);
	/// <summary>Changes only explicitly supplied account properties.</summary>
	JsonElement UpdateUser(Guid id, string login, Guid? contactId, bool? active);
	/// <summary>Changes a local account password without renaming or activating the account.</summary>
	void ChangePassword(Guid id, string password, bool forceChangePassword);
	/// <summary>Reads the native combined password and second-factor lock state.</summary>
	bool IsUserBlocked(Guid id);
	/// <summary>Clears login lockout and verifies the result without activating the account.</summary>
	JsonElement UnlockUser(Guid id);
	/// <summary>Deletes a user through the platform's protected-user checks.</summary>
	void DeleteUser(Guid id);
	/// <summary>Reads functional associations owned by an organizational or manager role.</summary>
	JsonElement GetFunctionalRoles(Guid roleId, int offset, int limit);
	/// <summary>Adds or removes one functional association and verifies direct state.</summary>
	JsonElement SetFunctionalRole(Guid roleId, Guid functionalId, bool remove);
	/// <summary>Reads delegations received by a user.</summary>
	JsonElement GetDelegations(Guid userId, int offset, int limit);
	/// <summary>Delegates a grantor's rights to one user, or removes that exact pair.</summary>
	JsonElement SetDelegation(Guid grantorId, Guid granteeId, bool remove);
	/// <summary>Lists operation definitions using optional exact code matching.</summary>
	JsonElement GetOperations(string code, int offset, int limit);
	/// <summary>Reads grants for an operation, including their priority positions.</summary>
	JsonElement GetOperationGrants(Guid operationId, int offset, int limit);
	/// <summary>Grants, denies or removes an operation permission through RightsService.</summary>
	JsonElement SetOperationGrant(Guid operationId, Guid unitId, bool canExecute, bool remove);
	/// <summary>Sets the native priority of one operation grant.</summary>
	JsonElement SetOperationPosition(Guid grantId, int position);
	/// <summary>Reads IP access ranges attached directly to one user or role.</summary>
	JsonElement GetIpRanges(Guid unitId, int offset, int limit);
	/// <summary>Creates or updates a validated native IPv4 access range.</summary>
	JsonElement SetIpRange(Guid id, Guid unitId, string beginIp, string endIp, bool create);
	/// <summary>Removes an IP restriction belonging to the specified unit.</summary>
	void DeleteIpRange(Guid id, Guid unitId);
	/// <summary>Reads license availability and assignment state for a user.</summary>
	JsonElement GetUserLicenses(Guid userId);
	/// <summary>Assigns or removes one personal license and verifies its checked state.</summary>
	JsonElement SetUserLicense(Guid userId, Guid packageId, bool remove);
	/// <summary>Reads license packages associated with a role.</summary>
	JsonElement GetRoleLicenses(Guid roleId, int offset, int limit);
	/// <summary>Changes one role license association; redistribution is explicitly separate.</summary>
	JsonElement SetRoleLicense(Guid roleId, Guid packageId, bool remove);
	/// <summary>Schedules native role license redistribution; the receipt does not prove eventual assignment.</summary>
	JsonElement RedistributeRoleLicenses(Guid roleId, bool includeManual);
}
