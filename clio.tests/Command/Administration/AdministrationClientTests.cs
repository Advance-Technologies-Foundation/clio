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

[TestFixture]
[Property("Module", "Command")]
public sealed class AdministrationClientTests : BaseClioModuleTests {
	private IApplicationClient _transport;
	private IAdministrationClient _client;

	public override void Setup() {
		base.Setup();
		_client = Container.GetRequiredService<IAdministrationClient>();
	}

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_transport = Substitute.For<IApplicationClient>();
		services.AddSingleton(_transport);
	}

	public override void TearDown() {
		_transport.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("{\"SaveRoleResult\":\"{\\\"success\\\":false,\\\"message\\\":\\\"secret-marker\\\"}\"}")]
	[TestCase("{\"SaveRoleResult\":\"{}\"}")]
	[TestCase("{\"SaveRoleResult\":true}")]
	[TestCase("{\"OtherResult\":\"{\\\"success\\\":true}\"}")]
	[TestCase("<html>secret-marker</html>")]
	[Description("Rejects malformed or negative wrapped native responses without exposing server prose.")]
	public void Post_RejectsInvalidSuccessContract(string response) {
		// Arrange
		Reply(response);
		// Act
		Action action = () => _client.Post(ServiceUrlBuilder.KnownRoute.AdministrationSaveRole, "SaveRoleResult",
			new { }, AdministrationResponseKind.SuccessObject);
		// Assert
		action.Should().Throw<InvalidOperationException>(because: "an HTTP response alone is not proof of successful administration")
			.Which.ToString().Should().NotContain("secret-marker", because: "server text may contain credentials");
	}

	[Test]
	[Description("Accepts encoded native success and explicitly disables transport retries.")]
	public void Post_AcceptsSuccessWithoutRetry() {
		// Arrange
		Reply("{\"SaveRoleResult\":\"{\\\"success\\\":true,\\\"roleId\\\":\\\"test\\\"}\"}");
		// Act
		JsonElement result = _client.Post(ServiceUrlBuilder.KnownRoute.AdministrationSaveRole, "SaveRoleResult",
			new { jsonObject = "{}" }, AdministrationResponseKind.SuccessObject);
		// Assert
		result.GetProperty("roleId").GetString().Should().Be("test", because: "the decoded result must survive document disposal");
		object[] args = _transport.ReceivedCalls().Single().GetArguments();
		args[3].Should().Be(1, because: "retrying an ambiguous mutation can duplicate native side effects");
	}

	[Test]
	[Description("Preserves false as a valid lock-state result while rejecting it as an actualization result.")]
	public void Post_DistinguishesBooleanStateFromSuccess() {
		// Arrange
		Reply("{\"Result\":false}");
		// Act
		JsonElement state = _client.Post(ServiceUrlBuilder.KnownRoute.AdministrationGetIsUserBlocked, "Result", new { }, AdministrationResponseKind.Boolean);
		Action mutation = () => _client.Post(ServiceUrlBuilder.KnownRoute.AdministrationActualize, "Result", new { }, AdministrationResponseKind.True);
		// Assert
		state.GetBoolean().Should().BeFalse(because: "an unlocked account has false lock state");
		mutation.Should().Throw<InvalidOperationException>(because: "failed actualization must not be reported as success");
	}

	[Test]
	[Description("Sanitizes transport exceptions, including ArgumentException, before command logging.")]
	public void Post_DoesNotLeakTransportException() {
		// Arrange
		_transport.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new ArgumentException("secret-marker"));
		// Act
		Action action = () => _client.Post(ServiceUrlBuilder.KnownRoute.AdministrationSaveUser, "Result", new { }, AdministrationResponseKind.ErrorString);
		// Assert
		action.Should().Throw<InvalidOperationException>(because: "the transport must fail closed")
			.Which.ToString().Should().NotContain("secret-marker", because: "neither nested exceptions nor their messages may reveal a password");
	}

	[TestCase("{\"success\":true,\"rows\":[{\"Id\":\"a\"}],\"notFoundColumns\":[\"Name\"]}")]
	[TestCase("{\"success\":true,\"rows\":[{\"Id\":\"a\"}],\"notFoundColumns\":[]}")]
	[TestCase("{\"success\":false,\"rows\":[]}")]
	[Description("Rejects incomplete DataService rows and silently omitted columns.")]
	public void Select_RejectsIncompleteReadback(string response) {
		// Arrange
		Reply(response);
		// Act
		Action action = () => _client.Select("SysAdminUnit", ["Id", "Name"], new Dictionary<string, object>());
		// Assert
		action.Should().Throw<InvalidOperationException>(because: "missing identity or state fields cannot verify a mutation");
	}

	[Test]
	[Description("Creates exact identity filters for deletes and rejects empty IDs before transport.")]
	public void DeleteEntity_RequiresExactIdentity() {
		// Arrange
		Reply("{\"success\":true,\"rowsAffected\":1}");
		Guid id = Guid.NewGuid();
		// Act
		_client.DeleteEntity("SysAdminUnitIPRange", id);
		Action invalid = () => _client.DeleteEntity("SysAdminUnitIPRange", Guid.Empty);
		// Assert
		invalid.Should().Throw<ArgumentException>(because: "a delete without identity could remove unrelated access rules");
		object[] args = _transport.ReceivedCalls().Single().GetArguments();
		using JsonDocument body = JsonDocument.Parse((string)args[1]);
		body.RootElement.GetProperty("filters").GetProperty("items").GetProperty("identity")
			.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetGuid()
			.Should().Be(id, because: "the server must receive the exact requested record identity");
	}

	private void Reply(string response) => _transport.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
		Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(response);
}
