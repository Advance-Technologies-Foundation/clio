using Clio.Command.IdentityServiceDeployment;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class CreateOAuthTechnicalUserCommandTests : BaseCommandTests<CreateOAuthTechnicalUserOptions>
{
	private CreateOAuthTechnicalUserCommand _command;
	private IIdentityServiceCreatioClient _creatioClient;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateOAuthTechnicalUserCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_creatioClient = Substitute.For<IIdentityServiceCreatioClient>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_creatioClient);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("CreateTechnicalUser returns the systemUserId from the REST endpoint and defaults the user name when none supplied.")]
	public void CreateTechnicalUser_ShouldReturnSystemUserIdWithDefaultName_WhenNameOmitted() {
		// Arrange
		_creatioClient.CreateTechnicalUser("clio_oauth_technical_user")
			.Returns("33333333-3333-3333-3333-333333333333");
		CreateOAuthTechnicalUserOptions options = new();

		// Act
		CreateOAuthTechnicalUserResult result = _command.CreateTechnicalUser(options);

		// Assert
		result.SystemUserId.Should().Be("33333333-3333-3333-3333-333333333333",
			because: "the REST endpoint's systemUserId must be surfaced");
		result.Name.Should().Be("clio_oauth_technical_user",
			because: "the default technical user name must be used when none is supplied");
	}

	[Test]
	[Description("CreateTechnicalUser reports roleGranted=false and a deferral notice because the role grant is database-direct and cannot run remotely.")]
	public void CreateTechnicalUser_ShouldReportRoleGrantDeferred_WhenUserCreated() {
		// Arrange
		_creatioClient.CreateTechnicalUser(Arg.Any<string>()).Returns("33333333-3333-3333-3333-333333333333");
		CreateOAuthTechnicalUserOptions options = new() { Name = "svc_user" };

		// Act
		CreateOAuthTechnicalUserResult result = _command.CreateTechnicalUser(options);

		// Assert
		result.RoleGranted.Should().BeFalse(
			because: "the REST-only path never grants a Creatio role to the new user");
		result.RoleGrantNotice.Should().Contain("deferred",
			because: "the deferral must be explained so callers know to grant roles manually");
	}

	[Test]
	[Description("Execute returns exit code 0, logs the JSON result, and writes the role-grant deferral warning.")]
	public void Execute_ShouldReturnZeroAndWarnAboutRoleGrant_WhenUserCreated() {
		// Arrange
		_creatioClient.CreateTechnicalUser(Arg.Any<string>()).Returns("33333333-3333-3333-3333-333333333333");
		CreateOAuthTechnicalUserOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "creating the technical user is a success even with a deferred role grant");
		_logger.Received(1).WriteWarning(Arg.Is<string>(msg => msg.Contains("Role grant deferred")));
	}
}
