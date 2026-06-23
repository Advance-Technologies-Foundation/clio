using System.Text.Json;
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
internal sealed class CreateServerToServerOAuthAppCommandTests : BaseCommandTests<CreateServerToServerOAuthAppOptions>
{
	private const string SelectUrl = "http://localhost/select";
	private const string Secret = "super-secret-value";
	private CreateServerToServerOAuthAppCommand _command;
	private IIdentityServiceCreatioClient _creatioClient;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateServerToServerOAuthAppCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_creatioClient = Substitute.For<IIdentityServiceCreatioClient>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		containerBuilder.AddSingleton(_creatioClient);
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("CreateApp uses the supplied systemUserId directly and returns the client credentials from the REST endpoint.")]
	public void CreateApp_ShouldUseSuppliedSystemUserId_WhenSystemUserIdProvided() {
		// Arrange
		const string userId = "44444444-4444-4444-4444-444444444444";
		_creatioClient.CreateClioClient(Arg.Any<DeployIdentityOptions>(), userId)
			.Returns(new OAuthClientCredentials("client-id-1", Secret));
		CreateServerToServerOAuthAppOptions options = new() { SystemUserId = userId };

		// Act
		CreateServerToServerOAuthAppResult result = _command.CreateApp(options);

		// Assert
		result.ClientId.Should().Be("client-id-1",
			because: "the client id from the REST endpoint must be surfaced");
		result.ClientSecret.Should().Be(Secret,
			because: "the client secret must be returned in the structured result");
		result.GrantType.Should().Be("client_credentials",
			because: "this primitive always creates a server-to-server (client_credentials) app");
		_creatioClient.Received(1).CreateClioClient(Arg.Any<DeployIdentityOptions>(), userId);
	}

	[Test]
	[Description("CreateApp resolves the system user by name (default Supervisor) over REST when no systemUserId is supplied.")]
	public void CreateApp_ShouldResolveSystemUserByName_WhenSystemUserIdOmitted() {
		// Arrange
		const string resolvedId = "11111111-1111-1111-1111-111111111111";
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysAdminUnit")))
			.Returns(JsonSerializer.Serialize(new { success = true, rows = new[] { new { Id = resolvedId, Name = "Supervisor" } } }));
		_creatioClient.CreateClioClient(Arg.Any<DeployIdentityOptions>(), resolvedId)
			.Returns(new OAuthClientCredentials("client-id-2", Secret));
		CreateServerToServerOAuthAppOptions options = new();

		// Act
		CreateServerToServerOAuthAppResult result = _command.CreateApp(options);

		// Assert
		result.SystemUserId.Should().Be(resolvedId,
			because: "the Supervisor user resolved over REST must be the bound system user");
		_creatioClient.Received(1).CreateClioClient(Arg.Any<DeployIdentityOptions>(), resolvedId);
	}

	[Test]
	[Description("CreateApp throws an actionable error when the system user cannot be resolved.")]
	public void CreateApp_ShouldThrow_WhenSystemUserNotFound() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysAdminUnit")))
			.Returns(JsonSerializer.Serialize(new { success = true, rows = System.Array.Empty<object>() }));
		CreateServerToServerOAuthAppOptions options = new() { SystemUser = "Ghost" };

		// Act
		System.Action act = () => _command.CreateApp(options);

		// Assert
		act.Should().Throw<System.InvalidOperationException>()
			.WithMessage("*Ghost*",
				because: "an unresolved system user must produce an actionable error naming the user");
	}

	[Test]
	[Description("Execute logs the client id but never logs the client secret, and returns exit code 0.")]
	public void Execute_ShouldLogClientIdButNeverSecret_WhenAppCreated() {
		// Arrange
		const string userId = "44444444-4444-4444-4444-444444444444";
		_creatioClient.CreateClioClient(Arg.Any<DeployIdentityOptions>(), userId)
			.Returns(new OAuthClientCredentials("client-id-3", Secret));
		CreateServerToServerOAuthAppOptions options = new() { SystemUserId = userId };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "creating the OAuth app is a success");
		_logger.Received(1).WriteInfo(Arg.Is<string>(line => line.Contains("client-id-3")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains(Secret)));
		_logger.DidNotReceive().WriteError(Arg.Is<string>(line => line.Contains(Secret)));
	}
}
