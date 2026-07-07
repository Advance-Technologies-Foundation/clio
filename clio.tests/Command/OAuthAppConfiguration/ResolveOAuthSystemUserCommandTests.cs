using System.Text.Json;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class ResolveOAuthSystemUserCommandTests : BaseCommandTests<ResolveOAuthSystemUserOptions>
{
	private const string SelectUrl = "http://localhost/select";
	private ResolveOAuthSystemUserCommand _command;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ResolveOAuthSystemUserCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("ResolveSystemUser returns the found user with id and name when the SelectQuery returns a row for the default Supervisor.")]
	public void ResolveSystemUser_ShouldReturnFoundUser_WhenSupervisorRowReturned() {
		// Arrange
		ArrangeRows([new UserRow("11111111-1111-1111-1111-111111111111", "Supervisor")]);
		ResolveOAuthSystemUserOptions options = new();

		// Act
		ResolveOAuthSystemUserResult result = _command.ResolveSystemUser(options);

		// Assert
		result.Found.Should().BeTrue(
			because: "a matching SysAdminUnit row must be reported as found");
		result.SystemUserId.Should().Be("11111111-1111-1111-1111-111111111111",
			because: "the Id column maps to systemUserId");
		result.Name.Should().Be("Supervisor",
			because: "the Name column maps to the resolved name");
	}

	[Test]
	[Description("ResolveSystemUser filters by Name with the default Supervisor when neither id nor name is supplied.")]
	public void ResolveSystemUser_ShouldFilterByDefaultSupervisorName_WhenNoCriteriaSupplied() {
		// Arrange
		ArrangeRows([new UserRow("11111111-1111-1111-1111-111111111111", "Supervisor")]);
		ResolveOAuthSystemUserOptions options = new();

		// Act
		_command.ResolveSystemUser(options);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl, Arg.Is<string>(body =>
			body.Contains("SysAdminUnit") && body.Contains("Supervisor")));
	}

	[Test]
	[Description("ResolveSystemUser filters by Id (not Name) when an id is supplied, taking precedence over name.")]
	public void ResolveSystemUser_ShouldFilterById_WhenIdSupplied() {
		// Arrange
		const string id = "22222222-2222-2222-2222-222222222222";
		ArrangeRows([new UserRow(id, "Some User")]);
		ResolveOAuthSystemUserOptions options = new() { Id = id, Name = "Ignored" };

		// Act
		_command.ResolveSystemUser(options);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl, Arg.Is<string>(body =>
			body.Contains(id) && !body.Contains("Ignored")));
	}

	[Test]
	[Description("ResolveSystemUser reports not found when the SelectQuery returns no rows.")]
	public void ResolveSystemUser_ShouldReportNotFound_WhenNoRowsReturned() {
		// Arrange
		ArrangeRows([]);
		ResolveOAuthSystemUserOptions options = new() { Name = "Ghost" };

		// Act
		ResolveOAuthSystemUserResult result = _command.ResolveSystemUser(options);

		// Assert
		result.Found.Should().BeFalse(
			because: "no SysAdminUnit row means the user could not be resolved");
		result.SystemUserId.Should().BeNull(
			because: "an unresolved user carries no id");
	}

	[Test]
	[Description("Execute returns exit code 0 and logs the JSON result when the user is found.")]
	public void Execute_ShouldReturnZeroAndLogJson_WhenUserFound() {
		// Arrange
		ArrangeRows([new UserRow("11111111-1111-1111-1111-111111111111", "Supervisor")]);
		ResolveOAuthSystemUserOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a resolved user is a success");
		_logger.Received(1).WriteInfo(Arg.Is<string>(line => line.Contains("systemUserId")));
	}

	[Test]
	[Description("Execute returns exit code 1 when the user is not found.")]
	public void Execute_ShouldReturnOne_WhenUserNotFound() {
		// Arrange
		ArrangeRows([]);
		ResolveOAuthSystemUserOptions options = new() { Name = "Ghost" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a missing user is a non-zero exit code so callers can branch on it");
	}

	private void ArrangeRows(System.Collections.Generic.IEnumerable<UserRow> rows) {
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysAdminUnit")))
			.Returns(JsonSerializer.Serialize(new { success = true, rows }));
	}

	private sealed record UserRow(string Id, string Name);
}
