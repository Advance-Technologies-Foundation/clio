using System;
using System.Collections.Generic;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The read-back comparison is pure: each case is "planned state, state read back → which differences fail the call
/// and which are only reported". The switch, the grantee's rows and every row the call writes are what the call
/// claims, so a difference there is critical; a difference on another role's row is a fact.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ObjectRightsReadBackVerifierTests {

	private static readonly Guid Grantee = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Other = Guid.Parse("197c2267-236b-4dac-97ec-13e4f34aa38d");

	private readonly ObjectRightsReadBackVerifier _verifier = new();

	private static RoleOperationRights Row(Guid grantee, int position, string ops) =>
		new(grantee, grantee == AllEmployees ? "All employees" : grantee == Grantee ? "Grantee" : "Other", position,
			ops.Contains('R'), ops.Contains('C'), ops.Contains('E'), ops.Contains('D'));

	private static ObjectRightsState State(bool administered, params RoleOperationRights[] rows) => new(administered, rows);

	// A plan built directly, so each case pins the comparison alone and not the planner that would produce it.
	private static ObjectRightsPlan PlanOf(ObjectRightsState before, ObjectRightsState after) =>
		new(before, after, ObjectRightsRefusal.None,
			EnablesOperationPermissions: !before.AdministratedByOperations && after.AdministratedByOperations,
			DisablesOperationPermissions: before.AdministratedByOperations && !after.AdministratedByOperations,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: false,
			RowsBecomingEffective: Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: Array.Empty<RoleOperationRights>(),
			DuplicatePositions: Array.Empty<int>());

	private static IEnumerable<TestCaseData> ReadBacks() {
		ObjectRightsState employeesOnly = State(true, Row(AllEmployees, 0, "RCED"));
		ObjectRightsState granted = State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R"));
		yield return new TestCaseData(employeesOnly, granted, granted, null, null)
			.SetName("Compare_ShouldReportNothing_WhenTheReadBackMatchesThePlan");
		yield return new TestCaseData(employeesOnly, granted, employeesOnly, "[1] Grantee: read is missing", null)
			.SetName("Compare_ShouldFailTheCall_WhenTheGrantedRowIsMissing");
		yield return new TestCaseData(employeesOnly, granted,
				State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R"), Row(Grantee, 2, "R")),
				"[2] Grantee: read is not in the plan", null)
			.SetName("Compare_ShouldFailTheCall_WhenTheReadBackHasAnExtraGranteeRow");
		ObjectRightsState beforeRevoke = State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "RC"));
		yield return new TestCaseData(beforeRevoke, State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R")),
				beforeRevoke, "[1] Grantee: read/create, the plan wrote read", null)
			.SetName("Compare_ShouldFailTheCall_WhenARevokeDidNotLand");
		yield return new TestCaseData(State(true, Row(Grantee, 0, "R")), State(false, Row(Grantee, 0, "")),
				State(true, Row(Grantee, 0, "")), "operation permissions are ON, the plan turned them OFF", null)
			.SetName("Compare_ShouldFailTheCall_WhenADisableReadsBackOn");
		yield return new TestCaseData(State(false, Row(Other, 0, "R")),
				State(true, Row(Other, 0, "R"), Row(AllEmployees, 1, "RCED"), Row(Grantee, 2, "R")),
				State(true, Row(Other, 0, "R"), Row(Grantee, 2, "R")),
				"[1] All employees: read/create/edit/delete is missing", null)
			.SetName("Compare_ShouldFailTheCall_WhenTheAllEmployeesRowAnEnableWritesIsMissing");
		yield return new TestCaseData(State(true, Row(AllEmployees, 0, "RCED"), Row(Other, 1, "R")),
				State(true, Row(AllEmployees, 0, "RCED"), Row(Other, 1, "R"), Row(Grantee, 2, "R")),
				State(true, Row(AllEmployees, 0, "RCED"), Row(Other, 1, "RC"), Row(Grantee, 2, "R")),
				null, "[1] Other: read/create, the plan wrote read")
			.SetName("Compare_ShouldReportAFact_WhenAnotherRoleRowDiffers");
		ObjectRightsState synthesized = State(false, Row(AllEmployees, 0, "RCED"));
		ObjectRightsState enabled = State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R"));
		yield return new TestCaseData(synthesized, enabled, State(true, Row(Grantee, 1, "R")),
				"[0] All employees: read/create/edit/delete is missing", null)
			.SetName("Compare_ShouldFailTheCall_WhenTheSynthesizedAllEmployeesRowAnEnableKeepsIsMissing");
		yield return new TestCaseData(synthesized, enabled, State(false, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R")),
				"operation permissions are OFF, the plan turned them ON", null)
			.SetName("Compare_ShouldFailTheCall_WhenAnEnableReadsBackOff");
		yield return new TestCaseData(State(true, Row(AllEmployees, 0, "RCED"), Row(Other, 1, "R")),
				State(true, Row(AllEmployees, 0, "RCED"), Row(Other, 1, "R"), Row(Grantee, 2, "R")),
				State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 2, "R")),
				null, "[1] Other: read is missing")
			.SetName("Compare_ShouldReportAFact_WhenAnotherRoleRowIsMissing");
		yield return new TestCaseData(employeesOnly, granted,
				State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R"), Row(Other, 2, "R")),
				null, "[2] Other: read is not in the plan")
			.SetName("Compare_ShouldReportAFact_WhenTheReadBackHasAnExtraRowOfAnotherRole");
		yield return new TestCaseData(State(true, Row(Grantee, 0, "R")), State(false, Row(Grantee, 0, "")),
				State(false, Row(AllEmployees, 0, "RCED")),
				"[0] Grantee: no operations is missing", "[0] All employees: read/create/edit/delete is not in the plan")
			.SetName("Compare_ShouldFailTheCall_WhenADisableThatKeptRowsReadsBackOnlyTheSynthesizedRow");
	}

	[TestCaseSource(nameof(ReadBacks))]
	[Description("Each difference between the read-back and the plan is sorted: a difference in what the call claims (the switch, the grantee's rows, a row it writes) fails the call, and any other is reported as a fact.")]
	public void Compare_ShouldSortEachDifference_WhenTheReadBackIsCompared(ObjectRightsState before,
		ObjectRightsState planned, ObjectRightsState actual, string expectedCritical, string expectedDifference) {
		// Arrange
		ObjectRightsPlan plan = PlanOf(before, planned);

		// Act
		ObjectRightsReadBackComparison comparison = _verifier.Compare(plan, Grantee, actual);

		// Assert
		if (expectedCritical is null) {
			comparison.Critical.Should().BeEmpty(because: "the read-back shows everything the call claims");
		} else {
			comparison.Critical.Should().Equal(new[] { expectedCritical }, because: "the call's own change did not land");
		}
		if (expectedDifference is null) {
			comparison.Differences.Should().BeEmpty(because: "no other row differs from the plan");
		} else {
			comparison.Differences.Should().Equal(new[] { expectedDifference },
				because: "a row the call does not write is reported, not failed");
		}
	}

	[Test]
	[Description("A missing plan or read-back is a caller bug, not an empty comparison.")]
	public void Compare_ShouldThrow_WhenAnInputIsNull() {
		// Arrange
		ObjectRightsState state = State(true, Row(AllEmployees, 0, "RCED"));

		// Act
		Action noPlan = () => _verifier.Compare(null, Grantee, state);
		Action noReadBack = () => _verifier.Compare(PlanOf(state, state), Grantee, null);

		// Assert
		noPlan.Should().ThrowExactly<ArgumentNullException>(because: "a comparison needs the plan")
			.WithParameterName("plan", because: "the plan is what is missing");
		noReadBack.Should().ThrowExactly<ArgumentNullException>(because: "a comparison needs the read-back")
			.WithParameterName("actual", because: "the read-back is what is missing");
	}
}
