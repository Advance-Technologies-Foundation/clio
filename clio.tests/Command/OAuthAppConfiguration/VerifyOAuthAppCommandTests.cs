using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class VerifyOAuthAppCommandTests : BaseCommandTests<VerifyOAuthAppOptions>
{
	private const string Token = "an-access-token";
	private const string SelectUrl = "http://localhost/select";
	private VerifyOAuthAppCommand _command;
	private ISysSettingsManager _sysSettingsManager;
	private IIdentityServerProbe _identityServerProbe;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private EnvironmentSettings _environment;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<VerifyOAuthAppCommand>();
		_environment = Container.GetRequiredService<EnvironmentSettings>();
	}

	public override void TearDown() {
		_environment.ClientId = null;
		_environment.ClientSecret = null;
		_environment.AuthAppUri = null;
		_identityServerProbe.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("")]
	[TestCase("/connect/token")]
	[Description("An environment-only invocation uses saved OAuth credentials and URL without querying CRM settings or mutating options.")]
	public void Verify_ShouldUseRegisteredCredentials_WhenOverridesAreOmitted(string endpointPath) {
		// Arrange
		_environment.ClientId = "saved-client";
		_environment.ClientSecret = "saved-secret";
		_environment.AuthAppUri = "https://saved-identity.example" + endpointPath;
		_identityServerProbe.AcquireClientCredentialsToken("https://saved-identity.example", "saved-client", "saved-secret").Returns(Token);
		_identityServerProbe.RunBearerDataServiceSmokeTest(_environment, SelectUrl, Token).Returns(200);
		VerifyOAuthAppOptions options = new() { Environment = "DEV" };

		// Act
		VerifyOAuthAppResult result = _command.Verify(options);

		// Assert
		result.Ok.Should().BeTrue(because: "saved credentials should support an environment-only invocation");
		options.ClientSecret.Should().BeNull(because: "verification must not copy stored secrets into command options");
		_sysSettingsManager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
	}

	[TestCase(401)]
	[TestCase(403)]
	[TestCase(500)]
	[Description("Verification fails when CRM does not accept the bearer smoke request, without interpreting permissions.")]
	public void Execute_ShouldReturnFailure_WhenCrmRequestFails(int status) {
		// Arrange
		_identityServerProbe.AcquireClientCredentialsToken(Arg.Any<string>(), "cid", "secret").Returns(Token);
		_identityServerProbe.RunBearerDataServiceSmokeTest(Arg.Any<EnvironmentSettings>(), SelectUrl, Token).Returns(status);

		// Act
		int exitCode = _command.Execute(new VerifyOAuthAppOptions { ClientId = "cid", ClientSecret = "secret" });

		// Assert
		exitCode.Should().Be(1, because: "token issuance alone cannot pass OAuth verification");
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_identityServerProbe = Substitute.For<IIdentityServerProbe>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_sysSettingsManager.GetSysSettingValueByCode(Arg.Any<string>()).Returns(string.Empty);
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		containerBuilder.AddSingleton(_sysSettingsManager);
		containerBuilder.AddSingleton(_identityServerProbe);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("Verify reports ok=true when a token is acquired and the bearer DataService smoke test returns HTTP 200.")]
	public void Verify_ShouldReportOk_WhenTokenAcquiredAndDataServiceReturns200() {
		// Arrange
		_identityServerProbe.AcquireClientCredentialsToken(Arg.Any<string>(), "cid", "secret").Returns(Token);
		_identityServerProbe.RunBearerDataServiceSmokeTest(Arg.Any<EnvironmentSettings>(), SelectUrl, Token)
			.Returns(200);
		VerifyOAuthAppOptions options = new() { ClientId = "cid", ClientSecret = "secret" };

		// Act
		VerifyOAuthAppResult result = _command.Verify(options);

		// Assert
		result.TokenAcquired.Should().BeTrue(
			because: "the probe returned a non-empty access token");
		result.DataServiceStatus.Should().Be(200,
			because: "the bearer smoke test status must be surfaced");
		result.Ok.Should().BeTrue(
			because: "a token plus an HTTP 200 smoke test means the app is verified end to end");
	}

	[Test]
	[Description("Verify reports ok=false and skips the smoke test when no token can be acquired.")]
	public void Verify_ShouldReportNotOkAndSkipSmokeTest_WhenTokenNotAcquired() {
		// Arrange
		_identityServerProbe.AcquireClientCredentialsToken(Arg.Any<string>(), "cid", "secret").Returns(string.Empty);
		VerifyOAuthAppOptions options = new() { ClientId = "cid", ClientSecret = "secret" };

		// Act
		VerifyOAuthAppResult result = _command.Verify(options);

		// Assert
		result.TokenAcquired.Should().BeFalse(
			because: "an empty token means acquisition failed");
		result.DataServiceStatus.Should().Be(0,
			because: "the smoke test must be skipped when no token is available");
		result.Ok.Should().BeFalse(
			because: "verification cannot succeed without a token");
		_identityServerProbe.DidNotReceive().RunBearerDataServiceSmokeTest(
			Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Verify uses an explicit identity-server-url override instead of deriving from the host.")]
	public void Verify_ShouldUseExplicitIdentityUrl_WhenOverrideSupplied() {
		// Arrange
		_identityServerProbe.AcquireClientCredentialsToken("https://explicit-is.example.com", "cid", "secret").Returns(Token);
		_identityServerProbe.RunBearerDataServiceSmokeTest(Arg.Any<EnvironmentSettings>(), SelectUrl, Token)
			.Returns(200);
		VerifyOAuthAppOptions options = new() {
			ClientId = "cid",
			ClientSecret = "secret",
			IdentityServerUrl = "https://explicit-is.example.com"
		};

		// Act
		VerifyOAuthAppResult result = _command.Verify(options);

		// Assert
		result.IdentityServerUrl.Should().Be("https://explicit-is.example.com",
			because: "an explicit override must take precedence over setting/derived URLs");
	}

	[Test]
	[Description("Verify throws when client id or secret is missing.")]
	public void Verify_ShouldThrow_WhenCredentialsMissing() {
		// Arrange
		VerifyOAuthAppOptions options = new() { ClientId = "cid" };

		// Act
		System.Action act = () => _command.Verify(options);

		// Assert
		act.Should().Throw<System.ArgumentException>(
			because: "verification requires both a client id and a client secret");
	}

	[Test]
	[Description("Execute returns exit code 0 when verification succeeds and never logs the access token text.")]
	public void Execute_ShouldReturnZeroAndNeverLogToken_WhenVerified() {
		// Arrange
		_identityServerProbe.AcquireClientCredentialsToken(Arg.Any<string>(), "cid", "secret").Returns(Token);
		_identityServerProbe.RunBearerDataServiceSmokeTest(Arg.Any<EnvironmentSettings>(), SelectUrl, Token)
			.Returns(200);
		VerifyOAuthAppOptions options = new() { ClientId = "cid", ClientSecret = "secret" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a verified app is a success");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains(Token)));
	}
}
