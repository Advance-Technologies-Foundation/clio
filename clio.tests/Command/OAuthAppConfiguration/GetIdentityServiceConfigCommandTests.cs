using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class GetIdentityServiceConfigCommandTests : BaseCommandTests<GetIdentityServiceConfigOptions>
{
	private GetIdentityServiceConfigCommand _command;
	private ISysSettingsManager _sysSettingsManager;
	private IIdentityServerProbe _identityServerProbe;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetIdentityServiceConfigCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_identityServerProbe = Substitute.For<IIdentityServerProbe>();
		_logger = Substitute.For<ILogger>();
		// The URL resolver stays real because its logic (host derivation) is under test here too.
		containerBuilder.AddSingleton(_sysSettingsManager);
		containerBuilder.AddSingleton(_identityServerProbe);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("GetConfig reports source=setting and the configured URL when OAuth20IdentityServerUrl is set.")]
	public void GetConfig_ShouldReportSettingSource_WhenIdentityUrlSettingIsSet() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("OAuth20IdentityServerUrl")
			.Returns("https://configured-is.creatio.com");
		_sysSettingsManager.GetSysSettingValueByCode("OAuth20IdentityServerClientId").Returns("client-abc");
		_identityServerProbe.IsDiscoveryReachable(Arg.Any<string>()).Returns(true);
		GetIdentityServiceConfigOptions options = new();

		// Act
		GetIdentityServiceConfigResult result = _command.GetConfig(options);

		// Assert
		result.Source.Should().Be("setting",
			because: "a populated OAuth20IdentityServerUrl setting is the authoritative source");
		result.IdentityServerUrl.Should().Be("https://configured-is.creatio.com",
			because: "the configured setting value must be returned verbatim");
		result.ClientId.Should().Be("client-abc",
			because: "the OAuth20IdentityServerClientId setting maps to clientId");
		result.TokenEndpoint.Should().Be("https://configured-is.creatio.com/connect/token",
			because: "the token endpoint is derived from the configured base URL");
		result.Reachable.Should().BeTrue(
			because: "the probe reported the discovery document as reachable");
	}

	[Test]
	[Description("GetConfig derives the -is host from the Creatio host when OAuth20IdentityServerUrl is empty.")]
	public void GetConfig_ShouldDeriveIsHost_WhenIdentityUrlSettingIsEmpty() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("OAuth20IdentityServerUrl").Returns(string.Empty);
		_sysSettingsManager.GetSysSettingValueByCode("OAuth20IdentityServerClientId").Returns(string.Empty);
		_identityServerProbe.IsDiscoveryReachable(Arg.Any<string>()).Returns(false);
		GetIdentityServiceConfigOptions options = new();

		// Act
		GetIdentityServiceConfigResult result = _command.GetConfig(options);

		// Assert
		result.Source.Should().Be("derived",
			because: "an empty setting forces derivation from the Creatio host");
		result.IdentityServerUrl.Should().Be("http://localhost-is",
			because: "the test environment Uri http://localhost derives to host localhost-is");
		result.Reachable.Should().BeFalse(
			because: "the probe reported the derived discovery document as unreachable");
	}

	[Test]
	[Description("GetConfig degrades a sys-setting read failure to an empty value and still derives a URL rather than throwing.")]
	public void GetConfig_ShouldDegradeGracefully_WhenSettingReadThrows() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(Arg.Any<string>())
			.Returns(_ => throw new System.InvalidOperationException("setting unavailable"));
		GetIdentityServiceConfigOptions options = new();

		// Act
		GetIdentityServiceConfigResult result = _command.GetConfig(options);

		// Assert
		result.Source.Should().Be("derived",
			because: "a failed setting read must degrade to derivation, not abort the whole read");
		result.IdentityServerUrl.Should().Be("http://localhost-is",
			because: "derivation from the Creatio host still works when the setting read fails");
	}

	[Test]
	[Description("Execute returns exit code 0 and logs the JSON config.")]
	public void Execute_ShouldReturnZeroAndLogJson_WhenConfigRead() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode(Arg.Any<string>()).Returns(string.Empty);
		GetIdentityServiceConfigOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "reading the identity service config is a successful operation");
		_logger.Received(1).WriteInfo(Arg.Is<string>(line => line.Contains("identityServerUrl")));
	}
}
