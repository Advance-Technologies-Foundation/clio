using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.Administration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.Administration;

/// <summary>Guards native identity, password and manager semantics independently of transport.</summary>
[TestFixture, Property("Module", "Command")]
public sealed class AdministrationServiceTests : BaseClioModuleTests {
	private IAdministrationClient _client;
	private IAdministrationService _service;
	private readonly Dictionary<Guid, JsonElement> _units = new();
	private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
	private static readonly Guid RoleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

	public override void Setup() { _units.Clear(); base.Setup(); _service = Container.GetRequiredService<IAdministrationService>(); }
	public override void TearDown() { _client.ClearReceivedCalls(); base.TearDown(); }
	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IAdministrationClient>();
		services.AddSingleton(_client);
	}

	[Test]
	[Description("Reuses an automatically created manager child without issuing SaveChiefsRole.")]
	public void EnsureManager_ReusesExistingChild() {
		// Arrange
		Seed(Unit(RoleId, 1));
		JsonElement manager = Unit(UserId, 2, RoleId);
		ManagerRows(Rows(manager));
		// Act
		JsonElement result = _service.EnsureManager(RoleId);
		// Assert
		result.GetProperty("Id").GetGuid().Should().Be(UserId, because: "native creation already owns the manager identity");
		Writes().Should().BeEmpty(because: "ensuring an existing manager must not create a duplicate");
	}

	[Test]
	[Description("Rejects ambiguous manager children instead of picking or deleting one.")]
	public void EnsureManager_RejectsDuplicates() {
		// Arrange
		Seed(Unit(RoleId, 1));
		ManagerRows(Rows(Unit(UserId, 2, RoleId), Unit(Guid.NewGuid(), 2, RoleId)));
		// Act
		Action action = () => _service.EnsureManager(RoleId);
		// Assert
		action.Should().Throw<InvalidOperationException>(because: "multiple manager children require an explicit repair decision");
		Writes().Should().BeEmpty(because: "ambiguous manager state must not trigger another creation");
	}

	[TestCase(4)]
	[TestCase(6)]
	[Description("Rejects a user or functional role as the parent of a manager group.")]
	public void EnsureManager_RejectsWrongParentKind(int type) {
		// Arrange
		Seed(Unit(RoleId, type));
		// Act
		Action action = () => _service.EnsureManager(RoleId);
		// Assert
		action.Should().Throw<ArgumentException>(because: "manager groups belong to organizational roles");
		Writes().Should().BeEmpty(because: "invalid parent types must be rejected before mutation");
	}

	[Test]
	[Description("Sends only the password and force-change flag when resetting an existing account password.")]
	public void ChangePassword_DoesNotRenameOrActivate() {
		// Arrange
		Seed(Unit(UserId, 4, active: false));
		// Act
		_service.ChangePassword(UserId, "unit-test-secret", false);
		// Assert
		object[] call = Writes().Should().ContainSingle(because: "password reset is one native mutation").Which;
		call[0].Should().Be(ServiceUrlBuilder.KnownRoute.AdministrationSaveUser, because: "password validation belongs to native administration");
		JsonElement outer = JsonSerializer.SerializeToElement(call[2]);
		using JsonDocument payload = JsonDocument.Parse(outer.GetProperty("jsonObject").GetString());
		payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
			new[] { "Id", "UserPassword", "ForceChangePassword" }, because: "password reset must not silently change identity, contact or activation");
		outer.GetProperty("roleId").GetString().Should().BeEmpty(because: "password reset cannot assign a role");
	}

	[Test]
	[Description("Refuses password resets for LDAP-synchronized accounts before reaching the credential endpoint.")]
	public void ChangePassword_RejectsLdapAccount() {
		// Arrange
		Seed(Unit(UserId, 4, ldap: true));
		// Act
		Action action = () => _service.ChangePassword(UserId, "unit-test-secret", true);
		// Assert
		action.Should().Throw<ArgumentException>(because: "the external identity provider owns an LDAP password");
		Writes().Should().BeEmpty(because: "the native local password path must not receive an LDAP credential");
	}

	[TestCase("1.2.3", "1.2.3.4")]
	[TestCase("::1", "::1")]
	[TestCase("10.0.1.250", "10.0.2.10")]
	[TestCase("192.0.2.2", "192.0.2.1")]
	[TestCase("not-an-ip", "192.0.2.1")]
	[Description("Rejects ambiguous, inverted and unsupported address ranges before writing a native IP restriction.")]
	public void SetIpRange_RejectsUnsafeRepresentation(string begin, string end) {
		// Arrange
		Seed(Unit(UserId, 4));
		// Act
		Action action = () => _service.SetIpRange(Guid.NewGuid(), UserId, begin, end, true);
		// Assert
		action.Should().Throw<ArgumentException>(because: "native IP enforcement silently mishandles malformed or incompatible ranges");
		_client.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IAdministrationClient.WriteEntity))
			.Should().BeEmpty(because: "an invalid access rule must not reach storage");
	}

	[Test]
	[Description("Uses AddUserRoles so native second-factor checks apply when assigning System administrators.")]
	public void SetMembership_UsesGuardedNativeEndpoint() {
		// Arrange
		Seed(Unit(UserId, 4));
		Seed(Unit(RoleId, 1));
		_client.Select("SysUserInRole", Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, object>>(), 0, 2)
			.Returns(Rows(JsonSerializer.SerializeToElement(new { Id = Guid.NewGuid() })));
		// Act
		_service.SetMembership(UserId, RoleId, false);
		// Assert
		object[] mutation = Writes().First();
		mutation[0].Should().Be(ServiceUrlBuilder.KnownRoute.AdministrationAddUserRoles,
			because: "AddUsersInRole omits the native system-administrator second-factor guard");
		JsonElement body = JsonSerializer.SerializeToElement(mutation[2]);
		body.GetProperty("userId").GetGuid().Should().Be(UserId, because: "the target member must be explicit");
		JsonSerializer.Deserialize<Guid[]>(body.GetProperty("roleIds").GetString()).Should().Equal(new[] { RoleId },
			because: "the request must change only the selected role");
	}

	[Test]
	[Description("Refuses deleting a root role even if it appears to have no members.")]
	public void DeleteRole_ProtectsRoot() {
		// Arrange
		Seed(Unit(RoleId, 1));
		// Act
		Action action = () => _service.DeleteRole(RoleId);
		// Assert
		action.Should().Throw<ArgumentException>(because: "root administering units are protected platform identities");
		Writes().Should().BeEmpty(because: "the delete endpoint must not receive a root role");
	}

	[Test]
	[Description("User and role lists push kind filters into the server query before pagination.")]
	public void ListUnits_FiltersKindBeforePaging() {
		// Arrange
		_client.Select(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, object>>(), 10, 5).Returns(Rows());
		// Act
		_service.ListUnits(null, null, null, 10, 5, rolesOnly: false);
		// Assert
		object[] args = _client.ReceivedCalls().Single().GetArguments();
		((IReadOnlyDictionary<string, object>)args[2])["SysAdminUnitTypeValue"].Should().BeEquivalentTo(new[] { 4, 5, 7 },
			because: "user inspection must include supported account kinds and exclude role rows before applying the page limit");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Functional-role removal retains the bridge's completion and scheduling receipt after verifying deletion.")]
	public void SetFunctionalRole_PreservesPartialOutcome(bool completed) {
		// Arrange
		Seed(Unit(RoleId, 1));
		Seed(Unit(UserId, 6));
		_client.Select("SysFuncRoleInOrgRole", Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, object>>(), 0, 2)
			.Returns(Rows(JsonSerializer.SerializeToElement(new { Id = Guid.NewGuid() })), Rows());
		JsonElement receipt = JsonSerializer.SerializeToElement(new {
			success = true, completed, associationDeleted = true, licenseReconciliationRequired = !completed
		});
		_client.Post(ServiceUrlBuilder.KnownRoute.AdministrationRemoveFunctionalRole, "RemoveFunctionalRoleAssociationResult",
			Arg.Any<object>(), AdministrationResponseKind.SuccessObject).Returns(receipt);
		// Act
		JsonElement result = _service.SetFunctionalRole(RoleId, UserId, true);
		// Assert
		result.GetProperty("completed").GetBoolean().Should().Be(completed, because: "deletion must not hide a later processing failure");
		result.GetProperty("redistributionReceipt").GetProperty("associationDeleted").GetBoolean().Should().BeTrue(
			because: "the agent needs to know the mutation committed before choosing recovery");
		result.GetProperty("licenseReconciliationRequired").GetBoolean().Should().Be(!completed,
			because: "the receipt must preserve the need for explicit license recovery");
		Writes().Should().ContainSingle(because: "the bridge owns actualization and its outcome must not be overwritten by a second attempt");
	}

	[Test]
	[Description("An already-absent association is not evidence that a previous redistribution completed.")]
	public void SetFunctionalRole_AbsentDoesNotClaimLicenseCompletion() {
		// Arrange
		Seed(Unit(RoleId, 1));
		Seed(Unit(UserId, 6));
		_client.Select("SysFuncRoleInOrgRole", Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, object>>(), 0, 2).Returns(Rows());
		// Act
		JsonElement result = _service.SetFunctionalRole(RoleId, UserId, true);
		// Assert
		result.GetProperty("licenseReconciliationRequired").GetBoolean().Should().BeTrue(
			because: "a retry cannot infer that post-deletion license processing succeeded");
		result.GetProperty("userAssignmentsVerified").GetBoolean().Should().BeFalse(
			because: "only user-license readback can prove distribution completion");
	}

	[Test]
	[Description("Membership addition rejects an internal/external connection-type mismatch before a native write.")]
	public void SetMembership_RejectsCrossConnectionAddition() {
		// Arrange
		Seed(Unit(UserId, 4, connectionType: 1));
		Seed(Unit(RoleId, 1));
		// Act
		Action action = () => _service.SetMembership(UserId, RoleId, false);
		// Assert
		action.Should().Throw<ArgumentException>(because: "native UI filters disallow cross-connection membership");
		Writes().Should().BeEmpty(because: "the native add endpoint does not enforce this UI constraint itself");
	}

	[Test]
	[Description("Priority changes invalidate the native rights cache without overwriting grant values.")]
	public void SetOperationPosition_InvalidatesCacheAfterReorder() {
		// Arrange
		_client.Select("SysAdminOperationGrantee", Arg.Any<IReadOnlyList<string>>(),
			Arg.Any<IReadOnlyDictionary<string, object>>(), 0, 2)
			.Returns(Rows(JsonSerializer.SerializeToElement(new { Id = RoleId, Position = 0 })));
		// Act
		JsonElement result = _service.SetOperationPosition(RoleId, 0);
		// Assert
		Writes().Select(call => (ServiceUrlBuilder.KnownRoute)call[0]).Should().Equal(
			new[] { ServiceUrlBuilder.KnownRoute.AdministrationInvalidateRightsCache, ServiceUrlBuilder.KnownRoute.RightsSetOperationPosition, ServiceUrlBuilder.KnownRoute.AdministrationInvalidateRightsCache },
			because: "native ordering must be followed by cache invalidation without reasserting potentially concurrent grants");
		result.GetProperty("Position").GetInt32().Should().Be(0, because: "the persisted priority still needs readback verification");
	}

	[Test]
	[Description("A failed bridge permission preflight cannot leave a changed priority with stale permissions.")]
	public void SetOperationPosition_RefusedPreflightDoesNotReorder() {
		// Arrange
		_client.Select("SysAdminOperationGrantee", Arg.Any<IReadOnlyList<string>>(),
			Arg.Any<IReadOnlyDictionary<string, object>>(), 0, 2)
			.Returns(Rows(JsonSerializer.SerializeToElement(new { Id = RoleId, Position = 1 })));
		_client.Post(ServiceUrlBuilder.KnownRoute.AdministrationInvalidateRightsCache, "InvalidateAdministrationRightsCacheResult",
			Arg.Any<object>(), AdministrationResponseKind.True).Returns(_ => throw new InvalidOperationException("Denied"));
		// Act
		Action action = () => _service.SetOperationPosition(RoleId, 0);
		// Assert
		action.Should().Throw<InvalidOperationException>(because: "bridge authorization must precede priority mutation");
		Writes().Select(call => (ServiceUrlBuilder.KnownRoute)call[0]).Should().NotContain(ServiceUrlBuilder.KnownRoute.RightsSetOperationPosition,
			because: "a deterministic cache authorization failure must not change persisted access ordering");
	}

	private void Seed(JsonElement unit) {
		Guid id = unit.GetProperty("Id").GetGuid();
		_units[id] = unit;
		_client.Select("SysAdminUnit", Arg.Any<IReadOnlyList<string>>(),
			Arg.Any<IReadOnlyDictionary<string, object>>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call => {
			IReadOnlyDictionary<string, object> filters = call.ArgAt<IReadOnlyDictionary<string, object>>(2);
			return filters.TryGetValue("Id", out object value) && _units.TryGetValue((Guid)value, out JsonElement found)
				? Rows(found) : Rows();
		});
	}
	private void ManagerRows(JsonElement rows) => _client.Select("SysAdminUnit", Arg.Any<IReadOnlyList<string>>(),
		Arg.Is<IReadOnlyDictionary<string, object>>(filters => filters.ContainsKey("ParentRole")), 0, 2).Returns(rows);
	private object[][] Writes() => _client.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IAdministrationClient.Post)).Select(call => call.GetArguments()).ToArray();
	private static JsonElement Rows(params JsonElement[] rows) => JsonSerializer.SerializeToElement(rows);
	private static JsonElement Unit(Guid id, int type, Guid parent = default, bool active = true, bool ldap = false, int connectionType = 0) => JsonSerializer.SerializeToElement(new {
		Id = id, Name = "Fixture", SysAdminUnitTypeValue = type, ParentRole = parent.ToString(), Contact = "",
		Active = active, ConnectionType = connectionType, SynchronizeWithLDAP = ldap, ForceChangePassword = false
	});
}
