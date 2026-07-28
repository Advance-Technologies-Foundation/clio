using System;
using System.IO;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Common.DeploymentStrategies;
using Clio.Common.IIS;
using Clio.Common.ScenarioHandlers;
using Clio.Tests.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class IISDeploymentStrategyTests : BaseClioModuleTests {
	private ICreateIISSiteHandler _handler;
	private IIisCertificateResolver _resolver;
	private ISettingsRepository _settingsRepository;
	private IWindowsFeatureManager _windowsFeatureManager;
	private IISDeploymentStrategy _sut;
	private string _applicationPath;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_handler = Substitute.For<ICreateIISSiteHandler>();
		_resolver = Substitute.For<IIisCertificateResolver>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_windowsFeatureManager = Substitute.For<IWindowsFeatureManager>();
		containerBuilder.AddSingleton(_handler);
		containerBuilder.AddSingleton(_resolver);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_windowsFeatureManager);
	}

	public override void Setup() {
		base.Setup();
		_applicationPath = Path.Combine(Path.GetTempPath(), "clio-iis-strategy-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(_applicationPath, "Terrasoft.Configuration"));
		_handler.Handle(Arg.Any<CreateIISSiteRequest>()).Returns(new CreateIISSiteResponse {
			Status = BaseHandlerResponse.CompletionStatus.Success,
			Description = "created"
		});
		_sut = Container.GetRequiredService<IISDeploymentStrategy>();
	}

	public override void TearDown() {
		if (Directory.Exists(_applicationPath)) {
			Directory.Delete(_applicationPath, recursive: true);
		}
		base.TearDown();
	}

	[Test]
	[Description("Falls back to an HTTP-only IIS request and updates the application URL when HTTPS is requested without a usable certificate.")]
	public async Task Deploy_ShouldFallbackToHttp_WhenNoUsableCertificateExists() {
		// Arrange
		_resolver.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
			.Returns(new IisCertificateSelection(null, false));
		PfInstallerOptions options = new() { SiteName = "https-fallback", SitePort = 40087, UseHttps = true };
		CreateIISSiteRequest captured = null;
		_handler.Handle(Arg.Do<CreateIISSiteRequest>(request => captured = request)).Returns(new CreateIISSiteResponse {
			Status = BaseHandlerResponse.CompletionStatus.Success,
			Description = "created"
		});

		// Act
		int result = await _sut.Deploy(_applicationPath, options);
		string applicationUrl = _sut.GetApplicationUrl(options);

		// Assert
		result.Should().Be(0, because: "missing certificates are explicitly a non-failing HTTPS preference fallback");
		options.UseHttps.Should().BeFalse(because: "downstream registration and browser launch must use the actual HTTP scheme");
		captured.Arguments["protocol"].Should().Be("http", because: "the IIS site must receive one HTTP binding after fallback");
		captured.Arguments["certificateThumbprint"].Should().BeEmpty(because: "HTTP bindings do not attach a certificate");
		applicationUrl.Should().StartWith("http://", because: "the deployment receipt must report the actual reachable scheme");
	}

	[Test]
	[Description("Passes the selected thumbprint into one HTTPS IIS request and preserves the HTTPS application URL.")]
	public async Task Deploy_ShouldUseHttps_WhenUsableCertificateExists() {
		// Arrange
		IisCertificateInfo certificate = new(
			"AABBCCDDEEFF00112233445566778899AABBCCDD", "CN=host", ["host"],
			DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1), true, true);
		_resolver.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
			.Returns(new IisCertificateSelection(certificate, false));
		PfInstallerOptions options = new() { SiteName = "https-site", SitePort = 40087, UseHttps = true };
		CreateIISSiteRequest captured = null;
		_handler.Handle(Arg.Do<CreateIISSiteRequest>(request => captured = request)).Returns(new CreateIISSiteResponse {
			Status = BaseHandlerResponse.CompletionStatus.Success,
			Description = "created"
		});

		// Act
		int result = await _sut.Deploy(_applicationPath, options);

		// Assert
		result.Should().Be(0, because: "a usable certificate makes the HTTPS deployment request valid");
		captured.Arguments["protocol"].Should().Be("https", because: "the IIS site must receive exactly the requested HTTPS binding");
		captured.Arguments["certificateThumbprint"].Should().Be(certificate.Thumbprint,
			because: "the resolved certificate must be attached to the IIS HTTPS binding");
		_sut.GetApplicationUrl(options).Should().StartWith("https://", because: "registration must use the deployed HTTPS scheme");
	}
}
