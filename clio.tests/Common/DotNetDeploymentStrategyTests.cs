using Clio.Command.CreatioInstallCommand;
using Clio.Common.DeploymentStrategies;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class DotNetDeploymentStrategyTests : BaseClioModuleTests {
	private DotNetDeploymentStrategy _sut;

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<DotNetDeploymentStrategy>();
	}

	[Test]
	[Description("Keeps dotnet-hosted deployments on their configured HTTP endpoint when IIS HTTPS was requested.")]
	public void GetApplicationUrl_ShouldUseHttp_WhenHttpsOptionIsSet() {
		// Arrange
		PfInstallerOptions options = new() { SitePort = 40123, UseHttps = true };

		// Act
		string result = _sut.GetApplicationUrl(options);

		// Assert
		result.Should().Be("http://localhost:40123",
			because: "the dotnet deployment strategy configures an HTTP Kestrel endpoint and does not bind an IIS certificate");
	}

	[TestCase(80, "http://localhost")]
	[TestCase(443, "http://localhost:443")]
	[Description("Omits only the default HTTP port from dotnet-hosted application URLs.")]
	public void GetApplicationUrl_ShouldApplyHttpDefaultPortRules(int port, string expected) {
		// Arrange
		PfInstallerOptions options = new() { SitePort = port, UseHttps = true };

		// Act
		string result = _sut.GetApplicationUrl(options);

		// Assert
		result.Should().Be(expected,
			because: "dotnet hosting remains HTTP even when the IIS-only HTTPS option is present");
	}
}
