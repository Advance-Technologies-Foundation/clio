using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>What the object read back after a save shows against the plan.</summary>
/// <param name="Critical">Differences in what the call claims: the switch, the grantee's rows and every row the call
/// writes. Any of them means the call's own change did not land, so the call fails.</param>
/// <param name="Differences">Every other difference, reported as a fact: another client may have changed the object
/// between the read and the read-back.</param>
public sealed record ObjectRightsReadBack(IReadOnlyList<string> Critical, IReadOnlyList<string> Differences);

/// <summary>
/// Compares the object read back after a save with the state the plan saved, row by row — grantee, position and
/// operations — so a change that did not land is never reported as done. Pure: no I/O.
/// </summary>
public interface IObjectRightsReadBackVerifier {
	/// <summary>Compares <paramref name="actual"/> with the state <paramref name="plan"/> saved.</summary>
	/// <param name="plan">The plan the save carried out.</param>
	/// <param name="grantee">The SysAdminUnit id of the grantee the call changes.</param>
	/// <param name="actual">The object as read back.</param>
	/// <returns>The critical differences and the others; both empty when the read-back matches the plan.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="actual"/> is null.</exception>
	ObjectRightsReadBack Compare(ObjectRightsPlan plan, Guid grantee, ObjectRightsState actual);
}

/// <inheritdoc />
public sealed class ObjectRightsReadBackVerifier : IObjectRightsReadBackVerifier {

	/// <inheritdoc />
	public ObjectRightsReadBack Compare(ObjectRightsPlan plan, Guid grantee, ObjectRightsState actual) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(actual);
		ObjectRightsState planned = plan.After;
		IReadOnlyList<RoleOperationRights> written = RowsWritten(plan);
		List<string> critical = new();
		List<string> differences = new();
		if (actual.AdministratedByOperations != planned.AdministratedByOperations) {
			critical.Add($"operation permissions are {OnOff(actual)}, the plan turned them {OnOff(planned)}");
		}
		ObjectRightsRowDifference diff = planned.DiffRows(actual);
		List<RoleOperationRights> extra = diff.Extra.Where(row => !IsSynthesizedRow(planned, actual, row)).ToList();
		foreach (RoleOperationRights missing in diff.Missing) {
			RoleOperationRights read = extra.FirstOrDefault(row =>
				row.GranteeId == missing.GranteeId && row.Position == missing.Position);
			if (read is not null) {
				extra.Remove(read);
			}
			string text = read is null
				? $"{ObjectRightsSupport.FormatRow(missing)} is missing"
				: $"{ObjectRightsSupport.FormatRow(read)}, the plan wrote {ObjectRightsSupport.FormatOperations(missing)}";
			bool claimed = missing.GranteeId == grantee || written.Any(row => row.SameRowAs(missing));
			(claimed ? critical : differences).Add(text);
		}
		foreach (RoleOperationRights row in extra) {
			(row.GranteeId == grantee ? critical : differences).Add($"{ObjectRightsSupport.FormatRow(row)} is not in the plan");
		}
		return new ObjectRightsReadBack(critical, differences);
	}

	// The rows this call writes: every planned row the object did not have as read, plus — when the call turns operation
	// permissions on — the All employees row it stores (the read may only have synthesized it). If one of them is missing
	// from the read-back, the call's own change did not land; an internal user can be cut off by exactly that row.
	private static IReadOnlyList<RoleOperationRights> RowsWritten(ObjectRightsPlan plan) {
		List<RoleOperationRights> written = plan.Before.DiffRows(plan.After).Extra.ToList();
		if (plan.EnablesOperationPermissions) {
			written.AddRange(plan.After.Roles.Where(row =>
				row.GranteeId == SysAdminUnitIds.AllEmployees && !written.Contains(row)));
		}
		return written;
	}

	// A disable that leaves no stored rows is read back with the All employees row the service synthesizes for an
	// object with no stored rows; nobody saved it.
	private static bool IsSynthesizedRow(ObjectRightsState planned, ObjectRightsState actual, RoleOperationRights row) =>
		!planned.AdministratedByOperations && planned.Roles.Count == 0 && !actual.AdministratedByOperations
		&& actual.Roles.Count == 1 && row.GranteeId == SysAdminUnitIds.AllEmployees && row.Position == 0
		&& row.CanRead && row.CanCreate && row.CanEdit && row.CanDelete;

	private static string OnOff(ObjectRightsState state) => state.AdministratedByOperations ? "ON" : "OFF";
}
