using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>The comparison of the object read back after a save with the plan.</summary>
/// <param name="Critical">Differences in what the call claims: the switch, the grantee's rows and every row the call
/// writes. Any of them means the call's own change did not land, so the call fails.</param>
/// <param name="Differences">Every other difference, reported as a fact: another client may have changed the object
/// between the read and the read-back.</param>
public sealed record ObjectRightsReadBackComparison(IReadOnlyList<string> Critical, IReadOnlyList<string> Differences);

/// <summary>
/// Compares the object read back after a save with the state the plan saved, row by row — grantee, position and
/// operations — so a change that did not land is never reported as done. Pure: no I/O.
/// <para>
/// The switch, the grantee's rows and every row the call writes are what the call claims: a difference there is
/// critical. When the call turns operation permissions on, the All employees row it stores counts as written even when
/// the read only synthesized it. When it turns them off, every row it keeps counts as written; on an object with no
/// stored rows the read synthesizes that row again, so it is not a difference.
/// </para>
/// </summary>
public interface IObjectRightsReadBackVerifier {
	/// <summary>Compares <paramref name="actual"/> with the state <paramref name="plan"/> saved.</summary>
	/// <param name="plan">The plan the save carried out.</param>
	/// <param name="grantee">The SysAdminUnit id of the grantee the call changes; <see cref="Guid.Empty"/> for a call that
	/// only turns the switch.</param>
	/// <param name="actual">The object as read back.</param>
	/// <returns>The critical differences and the others; both empty when the read-back matches the plan.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="actual"/> is null.</exception>
	ObjectRightsReadBackComparison Compare(ObjectRightsPlan plan, Guid grantee, ObjectRightsState actual);
}

/// <inheritdoc />
public sealed class ObjectRightsReadBackVerifier : IObjectRightsReadBackVerifier {

	/// <inheritdoc />
	public ObjectRightsReadBackComparison Compare(ObjectRightsPlan plan, Guid grantee, ObjectRightsState actual) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(actual);
		ObjectRightsState planned = plan.After;
		IReadOnlyList<RoleOperationRights> written = RowsWritten(plan);
		List<string> critical = new();
		List<string> differences = new();
		if (actual.AdministratedByOperations != planned.AdministratedByOperations) {
			critical.Add($"operation permissions are {ObjectRightsSupport.FormatSwitch(actual)}, the plan turned them "
				+ ObjectRightsSupport.FormatSwitch(planned));
		}
		ObjectRightsRowDifference diff = planned.DiffRows(actual);
		List<RoleOperationRights> extra = diff.Extra.Where(row => !IsSynthesizedAfterDisable(plan, actual, row)).ToList();
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
		return new ObjectRightsReadBackComparison(critical, differences);
	}

	// A disable keeps every stored row; when there were none, the read of the object — now off — synthesizes an All
	// employees row with every operation at position 0. That row is the service's rendering of "no stored rows", not a
	// row anyone wrote.
	private static bool IsSynthesizedAfterDisable(ObjectRightsPlan plan, ObjectRightsState actual,
		RoleOperationRights row) =>
		plan.DisablesOperationPermissions && plan.After.Roles.Count == 0 && !actual.AdministratedByOperations
		&& actual.Roles.Count == 1 && row.GranteeId == SysAdminUnitIds.AllEmployees && row.Position == 0
		&& row.CanRead && row.CanCreate && row.CanEdit && row.CanDelete;

	// The rows this call writes: every planned row the object did not have as read, plus — when the call turns operation
	// permissions on — the All employees row it stores (the read may only have synthesized it). If one of them is missing
	// from the read-back, the call's own change did not land; an internal user can be cut off by exactly that row. A
	// disable claims that every row is kept as it is, so every row it keeps counts as written.
	private static IReadOnlyList<RoleOperationRights> RowsWritten(ObjectRightsPlan plan) {
		List<RoleOperationRights> written = plan.Before.DiffRows(plan.After).Extra.ToList();
		if (plan.EnablesOperationPermissions) {
			written.AddRange(plan.After.Roles.Where(row =>
				row.GranteeId == SysAdminUnitIds.AllEmployees && !written.Contains(row)));
		}
		if (plan.DisablesOperationPermissions) {
			written.AddRange(plan.After.Roles.Where(row => !written.Contains(row)));
		}
		return written;
	}
}
