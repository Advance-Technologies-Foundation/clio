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

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<VerifyOAuthAppCommand>();
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
		_identityServerProbe.RunBearerDataServiceSmokeTest(SelectUrl, Token).Returns(200);
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
		_identityServerProbe.DidNotReceive().RunBearerDataServiceSmokeTest(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Verify uses an explicit identity-server-url override instead of deriving from the host.")]
	public void Verify_ShouldUseExplicitIdentityUrl_WhenOverrideSupplied() {
		// Arrange
		_identityServerProbe.AcquireClientCredentialsToken("https://explicit-is.example.com", "cid", "secret").Returns(Token);
		_identityServerProbe.RunBearerDataServiceSmokeTest(SelectUrl, Token).Returns(200);
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
		_identityServerProbe.RunBearerDataServiceSmokeTest(SelectUrl, Token).Returns(200);
		VerifyOAuthAppOptions options = new() { ClientId = "cid", ClientSecret = "secret" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a verified app is a success");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(line => line.Contains(Token)));
	}
}
