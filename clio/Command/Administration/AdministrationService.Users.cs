using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Administration;

public sealed partial class AdministrationService {

	/// <inheritdoc />
	public JsonElement CreateUser(Guid id, string login, Guid contactId, bool external, string password,
		bool forceChangePassword) {
		RequireId(id);
		RequireName(login);
		RequirePassword(password);
		RequireContact(contactId);
		if (ListUnits(id, null, null, 0, 1).GetArrayLength() != 0
			|| ListUnits(null, login, null, 0, 1).GetArrayLength() != 0) {
			throw new ArgumentException("The account ID or login already exists. Inspect it and use update explicitly.");
		}
		// Membership is deliberately separate: native creation saves the account before assigning a role.
		SaveUser(new Dictionary<string, object> {
			["Id"] = id, ["Name"] = login, [ContactColumn] = contactId.ToString(),
			["SysAdminUnitType"] = "472e97c7-6bd7-df11-9b2a-001d60e938c6",
			[ConnectionTypeColumn] = external ? 1 : 0, [ActiveColumn] = true,
			["UserPassword"] = password, ["ForceChangePassword"] = forceChangePassword
		});
		JsonElement user = GetUser(id);
		if (user.GetProperty("Name").GetString() != login || LookupId(user, ContactColumn) != contactId
			|| user.GetProperty(ConnectionTypeColumn).GetInt32() != (external ? 1 : 0)) {
			throw new AdministrationStateException("Created account readback differs from the request. Inspect before retrying.");
		}
		return user;
	}

	/// <inheritdoc />
	public JsonElement UpdateUser(Guid id, string login, Guid? contactId, bool? active) {
		GetUser(id);
		if (login is null && contactId is null && active is null) {
			throw new ArgumentException("Supply a login, contact ID or active state to update.");
		}
		Dictionary<string, object> values = new() { ["Id"] = id };
		if (login is not null) { RequireName(login); values["Name"] = login; }
		if (contactId.HasValue) { RequireContact(contactId.Value); values[ContactColumn] = contactId.Value.ToString(); }
		if (active.HasValue) { values[ActiveColumn] = active.Value; }
		SaveUser(values);
		JsonElement user = GetUser(id);
		if ((login is not null && user.GetProperty("Name").GetString() != login)
			|| (contactId.HasValue && LookupId(user, ContactColumn) != contactId.Value)
			|| (active.HasValue && user.GetProperty(ActiveColumn).GetBoolean() != active.Value)) {
			throw new AdministrationStateException("Account update could not be verified.");
		}
		return user;
	}

	/// <inheritdoc />
	public void ChangePassword(Guid id, string password, bool forceChangePassword) {
		JsonElement before = GetUser(id);
		if (before.GetProperty("SynchronizeWithLDAP").GetBoolean()) {
			throw new ArgumentException("Change an LDAP-managed account password in its identity provider.");
		}
		RequirePassword(password);
		SaveUser(new Dictionary<string, object> {
			["Id"] = id, ["UserPassword"] = password, ["ForceChangePassword"] = forceChangePassword
		});
		JsonElement after = GetUser(id);
		if (before.GetProperty("Name").GetString() != after.GetProperty("Name").GetString()
			|| before.GetProperty(ActiveColumn).GetBoolean() != after.GetProperty(ActiveColumn).GetBoolean()
			|| LookupId(before, ContactColumn) != LookupId(after, ContactColumn)
			|| after.GetProperty("ForceChangePassword").GetBoolean() != forceChangePassword) {
			throw new AdministrationStateException("Password service accepted the request but account readback differs. Inspect before retrying.");
		}
	}

	/// <inheritdoc />
	public bool IsUserBlocked(Guid id) {
		GetUser(id);
		return client.Post(ServiceUrlBuilder.KnownRoute.AdministrationGetIsUserBlocked, "GetIsUserBlockedResult",
			new { userId = id }, AdministrationResponseKind.Boolean).GetBoolean();
	}

	/// <inheritdoc />
	public JsonElement UnlockUser(Guid id) {
		JsonElement before = GetUser(id);
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationUnblockUser, "UnblockUserResult",
			new { userId = id }, AdministrationResponseKind.SuccessObject);
		if (IsUserBlocked(id)) { throw new AdministrationStateException("The account remains blocked after unlocking."); }
		JsonElement after = GetUser(id);
		if (before.GetProperty(ActiveColumn).GetBoolean() != after.GetProperty(ActiveColumn).GetBoolean()) {
			throw new AdministrationStateException("The account active state changed during unlock. Inspect it before proceeding.");
		}
		return after;
	}

	/// <inheritdoc />
	public void DeleteUser(Guid id) {
		GetUser(id);
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationDeleteUser, "DeleteUserResult",
			new { userId = id }, AdministrationResponseKind.SuccessObject);
		if (ListUnits(id, null, null, 0, 1).GetArrayLength() != 0) {
			throw new AdministrationStateException("The account still exists after deletion.");
		}
	}

	private JsonElement GetUser(Guid id) {
		JsonElement user = GetUnit(id);
		if (TypeOf(user) is not (4 or 5 or 7)) { throw new ArgumentException("The ID must identify a user account."); }
		return user;
	}

	private void RequireContact(Guid id) {
		RequireId(id);
		if (client.Select(ContactColumn, ["Id"], new Dictionary<string, object> { ["Id"] = id }, limit: 2).GetArrayLength() != 1) {
			throw new ArgumentException("The contact ID must identify one existing contact.");
		}
	}

	private void SaveUser(Dictionary<string, object> values) => client.Post(
		ServiceUrlBuilder.KnownRoute.AdministrationSaveUser, "UpdateOrCreateUserResult",
		new { jsonObject = JsonSerializer.Serialize(values), roleId = string.Empty }, AdministrationResponseKind.ErrorString);

	private static void RequirePassword(string password) {
		if (string.IsNullOrEmpty(password)) { throw new ArgumentException("Provide a nonempty password through the secret source."); }
	}
}
