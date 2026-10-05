using System;
using System.Linq;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// <see cref="DefaultRecordRightsPlanner"/>: the pure plan of one <c>set-default-record-rights</c> call — the one rule it
/// changes, the switch, and the policy table that refuses a change the arguments do not name or a list the save would
/// lose data on. No I/O.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class DefaultRecordRightsPlannerTests {

	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");
	private static readonly Guid External = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");

	private const RecordRightLevel N = RecordRightLevel.NotSet;
	private const RecordRightLevel G = RecordRightLevel.Granted;
	private const RecordRightLevel D = RecordRightLevel.Delegated;

	private readonly DefaultRecordRightsPlanner _planner = new();

	private static DefaultRecordRule Rule(Guid author, Guid grantee, RecordRightLevel read, RecordRightLevel edit,
		RecordRightLevel delete, bool doNotApplyForManager = false) =>
		new(author, "author", grantee, "grantee", read, edit, delete, doNotApplyForManager);

	private static DefaultRecordRightsState State(bool on, params DefaultRecordRule[] rules) => new(on, rules);

	private static DefaultRecordRightsChangeRequest Grant(Guid author, Guid grantee, RecordRightLevel level,
		bool? flag = null, bool enable = false, bool disable = false, params RecordOperation[] operations) =>
		new(new DefaultRecordRuleChange(author, "author", grantee, "grantee", operations, level, flag, false), enable,
			disable);

	private static DefaultRecordRightsChangeRequest Revoke(Guid author, Guid grantee, params RecordOperation[] operations) =>
		new(new DefaultRecordRuleChange(author, "author", grantee, "grantee", operations, G, null, true), false, false);

	private static DefaultRecordRightsChangeRequest Switch(bool enable) => new(null, enable, !enable);

	// ---- rule changes ----

	[Test]
	[Description("A grant for a pair with no rule adds one rule with the named operations at the level, the others not set and the manager flag false; every other rule stays as read.")]
	public void Plan_ShouldAddRule_WhenPairHasNone() {
		// Arrange
		DefaultRecordRule other = Rule(External, AllEmployees, G, G, G);
		DefaultRecordRightsState before = State(true, other);

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before,
			Grant(AllEmployees, Admins, G, operations: new[] { RecordOperation.Read, RecordOperation.Edit }));

		// Assert
		plan.Refused.Should().BeFalse(because: "record permissions are on and the list is valid");
		plan.After.Rules.Should().HaveCount(2, because: "one rule is added and none removed");
		plan.After.Rules.Should().Contain(other, because: "a rule the call does not name stays exactly as read");
		plan.RuleAfter.Should().Be(Rule(AllEmployees, Admins, G, G, N), because: "only the named operations are set and a new rule's flag is false");
		plan.ChangesRules.Should().BeTrue(because: "a rule was added");
		plan.Enables.Should().BeFalse(because: "the switch was already on");
	}

	[Test]
	[Description("A grant on an existing rule sets the named operations to the level — lowering delegated to granted too — and leaves the other operations and the manager flag as read when the flag is omitted.")]
	public void Plan_ShouldSetLevelAndKeepFlag_WhenRuleExistsAndFlagOmitted() {
		// Arrange
		DefaultRecordRightsState before = State(true, Rule(AllEmployees, Admins, D, D, D, doNotApplyForManager: true));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before,
			Grant(AllEmployees, Admins, G, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.RuleAfter.Should().Be(Rule(AllEmployees, Admins, G, D, D, doNotApplyForManager: true),
			because: "a grant sets the named operation to the given level and touches nothing else");
	}

	[Test]
	[Description("do-not-apply-for-manager given on a grant sets the rule's flag; repeating the rule's levels with only the flag changed is still a change.")]
	public void Plan_ShouldSetFlag_WhenFlagGiven() {
		// Arrange
		DefaultRecordRightsState before = State(true, Rule(AllEmployees, Admins, G, N, N));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before,
			Grant(AllEmployees, Admins, G, flag: true, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.RuleAfter.DoNotApplyForManager.Should().BeTrue(because: "the flag was given");
		plan.Changes.Should().BeTrue(because: "the flag is a stored field of the rule");
	}

	[Test]
	[Description("A grant the rule already has plans no change, so the command saves nothing (idempotent).")]
	public void Plan_ShouldPlanNoChange_WhenRuleAlreadyHasTheLevels() {
		// Arrange
		DefaultRecordRightsState before = State(true, Rule(AllEmployees, AllEmployees, G, G, N));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before,
			Grant(AllEmployees, AllEmployees, G, operations: new[] { RecordOperation.Read, RecordOperation.Edit }));

		// Assert
		plan.Refused.Should().BeFalse(because: "nothing is wrong with the request");
		plan.Changes.Should().BeFalse(because: "the rule already holds exactly these levels");
	}

	[Test]
	[Description("A revoke sets the named operations to not set and keeps the rule while it still grants something.")]
	public void Plan_ShouldClearOperations_WhenRevokeLeavesARight() {
		// Arrange
		DefaultRecordRightsState before = State(true, Rule(AllEmployees, AllEmployees, G, G, N));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Revoke(AllEmployees, AllEmployees, RecordOperation.Edit));

		// Assert
		plan.RuleAfter.Should().Be(Rule(AllEmployees, AllEmployees, G, N, N), because: "only edit is cleared");
		plan.RuleAfter.Should().NotBeNull(because: "read is still granted");
	}

	[Test]
	[Description("A revoke that leaves the rule with no right removes it from the list (the server would drop an all-not-set rule anyway), and the plan says so.")]
	public void Plan_ShouldRemoveRule_WhenRevokeEmptiesIt() {
		// Arrange
		DefaultRecordRule other = Rule(External, AllEmployees, G, G, G);
		DefaultRecordRightsState before = State(true, Rule(AllEmployees, AllEmployees, G, N, N), other);

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Revoke(AllEmployees, AllEmployees, RecordOperation.Read));

		// Assert
		plan.RuleAfter.Should().BeNull(because: "the rule has no right left");
		plan.After.Rules.Should().Equal(new[] { other }, because: "only the emptied rule is removed");
	}

	[Test]
	[Description("A revoke for a pair with no rule changes nothing.")]
	public void Plan_ShouldPlanNoChange_WhenRevokeFindsNoRule() {
		// Arrange
		DefaultRecordRightsState before = State(true, Rule(External, AllEmployees, G, G, G));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Revoke(AllEmployees, Admins, RecordOperation.Read));

		// Assert
		plan.Changes.Should().BeFalse(because: "there is nothing to revoke");
		plan.RuleBefore.Should().BeNull(because: "the pair has no rule");
	}

	[Test]
	[Description("A revoke while record permissions are OFF is allowed (decision Q2): it cleans a stored rule before an enable brings it into effect, and leaves the switch off.")]
	public void Plan_ShouldAllowRevoke_WhenRecordPermissionsAreOff() {
		// Arrange
		DefaultRecordRightsState before = State(false, Rule(External, AllEmployees, G, G, G));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Revoke(External, AllEmployees,
			RecordOperation.Read, RecordOperation.Edit, RecordOperation.Delete));

		// Assert
		plan.Refused.Should().BeFalse(because: "a revoke while off narrows nothing now and is the clean-up step");
		plan.RuleAfter.Should().BeNull(because: "the stored rule loses every right");
		plan.ChangesRules.Should().BeTrue(because: "the stored list changes");
		plan.After.AdministratedByRecords.Should().BeFalse(because: "a revoke never changes the switch");
	}

	// ---- the switch ----

	[Test]
	[Description("A grant on an object whose record permissions are OFF is refused without enable-record-permissions, and the refusal carries the stored rules that would come into effect.")]
	public void Plan_ShouldRefuseEnableNotRequested_WhenGrantOnObjectThatIsOff() {
		// Arrange
		DefaultRecordRule stored = Rule(External, AllEmployees, G, G, G);
		DefaultRecordRightsState before = State(false, stored);

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Grant(AllEmployees, Admins, G, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.Refusal.Should().Be(DefaultRecordRightsRefusal.EnableNotRequested, because: "the rule would give nobody anything");
		plan.StoredRulesComingIntoEffect.Should().Equal(new[] { stored }, because: "the refusal names what an enable revives");
		plan.After.Should().Be(before, because: "a refused plan writes nothing");
	}

	[Test]
	[Description("A grant with enable-record-permissions turns the switch on and adds the rule; the stored rules that start to apply are listed.")]
	public void Plan_ShouldEnableAndGrant_WhenEnableRequested() {
		// Arrange
		DefaultRecordRightsState before = State(false, Rule(External, AllEmployees, G, G, G));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before,
			Grant(AllEmployees, Admins, G, enable: true, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.Enables.Should().BeTrue(because: "the flag was given on an object that is off");
		plan.After.Rules.Should().HaveCount(2, because: "the stored rule stays and the new one is added");
		plan.StoredRulesComingIntoEffect.Should().HaveCount(2, because: "every rule starts to apply on enable");
	}

	[Test]
	[Description("disable-record-permissions together with a grant is refused: while off the rule would give nobody anything.")]
	public void Plan_ShouldRefuseDisableNotNeeded_WhenDisableWithGrant() {
		// Arrange
		DefaultRecordRightsState before = State(true);

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before,
			Grant(AllEmployees, Admins, G, disable: true, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.Refusal.Should().Be(DefaultRecordRightsRefusal.DisableNotNeeded, because: "a grant and a disable contradict");
	}

	[TestCase(true, false, true)]
	[TestCase(false, true, false)]
	[Description("A switch-only call changes only the switch and plans no rule change; on an object already in that state it changes nothing.")]
	public void Plan_ShouldChangeOnlyTheSwitch_WhenSwitchOnly(bool enable, bool before, bool expected) {
		// Arrange
		DefaultRecordRightsState state = State(before, Rule(External, AllEmployees, G, G, G));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(state, Switch(enable));
		DefaultRecordRightsPlan repeat = _planner.Plan(plan.After, Switch(enable));

		// Assert
		plan.After.AdministratedByRecords.Should().Be(expected, because: "the flag sets the switch");
		plan.ChangesRules.Should().BeFalse(because: "a switch-only call names no rule");
		repeat.Changes.Should().BeFalse(because: "the state the call asks for is already in place");
	}

	[Test]
	[Description("Enabling an object with NO rule is allowed: the built-in 'own records only' default applies, and the plan's rules are empty.")]
	public void Plan_ShouldAllowEnable_WhenObjectHasNoRule() {
		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(State(false), Switch(true));

		// Assert
		plan.Refused.Should().BeFalse(because: "'every user sees only their own records' is a legitimate setup");
		plan.Enables.Should().BeTrue(because: "the switch goes on");
		plan.After.Rules.Should().BeEmpty(because: "no rule is added implicitly");
	}

	// ---- the stored list ----

	[Test]
	[Description("A stored list with two rules for one author+grantee pair is refused when the call changes a rule: the save resends the whole list and the server keeps only the last rule of a pair.")]
	public void Plan_ShouldRefuseDuplicatePairs_WhenRulesChange() {
		// Arrange
		DefaultRecordRule first = Rule(AllEmployees, AllEmployees, G, G, N);
		DefaultRecordRule second = Rule(AllEmployees, AllEmployees, D, N, N);
		DefaultRecordRightsState before = State(true, first, second);

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Grant(External, Admins, G, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.Refusal.Should().Be(DefaultRecordRightsRefusal.DuplicatePairs, because: "any duplicate pair would be resent");
		plan.ProblemRules.Should().BeEquivalentTo(new[] { first, second }, because: "the refusal names both rules of the pair");
	}

	[Test]
	[Description("A stored level outside 0..2 is refused when the call changes a rule, because the save would send it back as is.")]
	public void Plan_ShouldRefuseInvalidStoredLevel_WhenRulesChange() {
		// Arrange
		DefaultRecordRule invalid = Rule(External, AllEmployees, (RecordRightLevel)3, N, N);
		DefaultRecordRightsState before = State(true, invalid);

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Grant(AllEmployees, Admins, G, operations: new[] { RecordOperation.Read }));

		// Assert
		plan.Refusal.Should().Be(DefaultRecordRightsRefusal.InvalidStoredLevel, because: "the level is not one the platform defines");
		plan.ProblemRules.Should().Equal(new[] { invalid }, because: "the refusal names the rule");
	}

	[Test]
	[Description("A switch-only call is not refused for a bad stored list: it sends the list as untouched, so nothing is resent.")]
	public void Plan_ShouldNotRefuseStoredList_WhenSwitchOnly() {
		// Arrange
		DefaultRecordRightsState before = State(false,
			Rule(AllEmployees, AllEmployees, G, G, N), Rule(AllEmployees, AllEmployees, D, N, N));

		// Act
		DefaultRecordRightsPlan plan = _planner.Plan(before, Switch(true));

		// Assert
		plan.Refused.Should().BeFalse(because: "the rule list is not sent by a switch-only call");
	}

	// ---- request shapes the command refuses before any read ----

	[Test]
	[Description("The planner rejects request shapes the command refuses before any read: no rule and no flag, both flags, a rule with no operation.")]
	public void Plan_ShouldThrow_WhenRequestShapeIsInvalid() {
		// Arrange
		DefaultRecordRightsState state = State(true);

		// Act
		Action nothing = () => _planner.Plan(state, new DefaultRecordRightsChangeRequest(null, false, false));
		Action both = () => _planner.Plan(state, new DefaultRecordRightsChangeRequest(null, true, true));
		Action noOperation = () => _planner.Plan(state, Grant(AllEmployees, Admins, G));

		// Assert
		nothing.Should().Throw<ArgumentException>(because: "a call must name a rule or a switch flag");
		both.Should().Throw<ArgumentException>(because: "enable and disable contradict");
		noOperation.Should().Throw<ArgumentException>(because: "nothing is granted by default");
	}

	// ---- the state comparison the read-back uses ----

	[Test]
	[Description("DefaultRecordRightsState.Diff compares rules as a set keyed by author+grantee, every stored field included, ignoring order and names; a lost or extra rule and a changed switch are each reported.")]
	public void Diff_ShouldReportEveryDifference_IgnoringOrderAndNames() {
		// Arrange
		DefaultRecordRule a = Rule(AllEmployees, AllEmployees, G, G, N);
		DefaultRecordRule b = Rule(External, AllEmployees, G, G, G);
		DefaultRecordRightsState planned = State(true, a, b);
		DefaultRecordRightsState reordered = State(true, b with { AuthorName = "other name" }, a);
		DefaultRecordRightsState lost = State(false, a with { DoNotApplyForManager = true }, Rule(Admins, Admins, G, N, N));

		// Act
		var none = planned.Diff(reordered);
		var many = planned.Diff(lost);

		// Assert
		none.Should().BeEmpty(because: "order and display names are not stored rule fields");
		many.Should().HaveCount(4, because: "the switch, the changed flag, the missing rule and the unexpected rule each differ");
	}

}
