using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.DeploymentStrategies;
using Clio.Common.SystemServices;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class DotNetDeploymentStrategyTests : BaseClioModuleTests {
	private ICreatioHostService _creatioHostService;
	private ISystemServiceManager _serviceManager;
	private DotNetDeploymentStrategy _sut;
	private string _temporaryDirectory;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_creatioHostService = Substitute.For<ICreatioHostService>();
		_serviceManager = Substitute.For<ISystemServiceManager>();
		containerBuilder.AddSingleton(_creatioHostService);
		containerBuilder.AddSingleton(_serviceManager);
	}

	public override void Setup() {
		base.Setup();
		_temporaryDirectory = Path.Combine(Path.GetTempPath(), "clio-dotnet-strategy-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_temporaryDirectory);
		_sut = Container.GetRequiredService<DotNetDeploymentStrategy>();
	}

	public override void TearDown() {
		if (Directory.Exists(_temporaryDirectory)) {
			Directory.Delete(_temporaryDirectory, recursive: true);
		}
		base.TearDown();
	}

	[TestCase(false, 80, "http://localhost")]
	[TestCase(false, 443, "http://localhost:443")]
	[TestCase(true, 80, "https://localhost:80")]
	[TestCase(true, 443, "https://localhost")]
	[Description("Reports the selected dotnet protocol and omits only its matching default port.")]
	public void GetApplicationUrl_ShouldUseSelectedProtocolAndDefaultPortRules(bool useHttps, int port, string expected) {
		// Arrange
		PfInstallerOptions options = new() { SitePort = port, UseHttps = useHttps };

		// Act
		string result = _sut.GetApplicationUrl(options);

		// Assert
		result.Should().Be(expected,
			because: "the deployment receipt must match the Kestrel protocol and loopback endpoint selected by the options");
	}

	[Test]
	[Description("Keeps the registered dotnet control URL local while the explicitly requested listener uses all interfaces.")]
	public void GetApplicationUrl_ShouldRemainLocalWhenAllInterfacesAreEnabled() {
		// Arrange
		PfInstallerOptions options = new() { SitePort = 40123, UseHttps = true, BindAllInterfaces = true };

		// Act
		string result = _sut.GetApplicationUrl(options);

		// Assert
		result.Should().Be("https://localhost:40123",
			because: "wildcard listener addresses are not valid client destinations and registration/readiness use the local control URL");
	}

	[Test]
	[Description("Binds a new dotnet HTTP configuration to loopback instead of exposing every network interface.")]
	public void BuildApplicationConfiguration_ShouldBindHttpToLoopbackByDefault() {
		// Arrange
		PfInstallerOptions options = new() { SitePort = 40123 };

		// Act
		string result = _sut.BuildApplicationConfiguration(null, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "a default dotnet deployment must be reachable only from the local machine");
		result.Should().NotContain("http://[::]",
			because: "a new default configuration must not retain the all-interface wildcard");
	}

	[Test]
	[Description("Uses the explicit bind-all-interfaces option when a dotnet deployment must receive network traffic.")]
	public void BuildApplicationConfiguration_ShouldBindAllInterfacesWhenExplicitlyRequested() {
		// Arrange
		string certificatePath = CreateTemporaryPfx("server.pfx");
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			BindAllInterfaces = true,
			CertificatePath = certificatePath
		};

		// Act
		string result = _sut.BuildApplicationConfiguration(null, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://[::]:40123",
			because: "the wildcard binding must remain available only through explicit opt-in");
	}

	[Test]
	[Description("Rejects plaintext dotnet deployment when all network interfaces were requested.")]
	public void BuildApplicationConfiguration_ShouldRejectPlaintextAllInterfaceBinding() {
		// Arrange
		PfInstallerOptions options = new() { SitePort = 40123, BindAllInterfaces = true };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(null, options);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("--bind-all-interfaces requires --use-https for dotnet deployment.",
			because: "a network-facing dotnet listener must not be exposed as plaintext by default");
	}

	[Test]
	[Description("Configures a dotnet HTTPS endpoint from a PFX certificate and removes the plaintext endpoint when HTTPS is explicit.")]
	public void BuildApplicationConfiguration_ShouldConfigureHttpsFromPfx() {
		// Arrange
		string certificatePath = CreateTemporaryPfx("server.pfx", "secret");
		string passwordFile = CreateTemporaryPasswordFile("secret");
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			CertificatePath = certificatePath,
			CertificatePasswordFile = passwordFile
		};

		// Act
		DotNetApplicationConfiguration configuration = _sut.BuildApplicationConfigurationWithEnvironment(null, options);
		string result = configuration.Json;

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:40123",
			because: "the HTTPS endpoint must use the secure loopback address and requested port");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be(Path.GetFullPath(certificatePath),
			because: "Kestrel must load the certificate selected by the operator");
		HasJsonProperty(result, "Kestrel", "Endpoints", "Https", "Certificate", "Password").Should().BeFalse(
			because: "certificate passwords must not be persisted in the generated appsettings.json");
		configuration.EnvironmentVariables.Should().ContainKey("Kestrel__Endpoints__Https__Certificate__Password",
			because: "Kestrel must receive the certificate password through the child process environment");
		configuration.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"].Should().Be("secret",
			because: "the selected child process must receive the operator-provided certificate password");
		HasJsonProperty(result, "Kestrel", "Endpoints", "Http").Should().BeFalse(
			because: "explicit HTTPS deployment must not leave a parallel plaintext listener");
	}

	[Test]
	[Description("Does not restore certificate secrets from an HTTP endpoint that explicit HTTPS deployment removes.")]
	public void BuildApplicationConfiguration_ShouldNotExportRemovedHttpCertificatePassword() {
		// Arrange
		string certificatePath = CreateTemporaryPfx("replacement.pfx", "https-secret");
		string passwordFile = CreateTemporaryPasswordFile("https-secret");
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": {
			        "Url": "http://localhost:5000",
			        "Certificate": { "Path": "removed.pfx", "Password": "http-secret" }
			      },
			      "Https": { "Url": "https://localhost:5001" }
			    }
			  }
			}
			""";
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			CertificatePath = certificatePath,
			CertificatePasswordFile = passwordFile
		};

		// Act
		DotNetApplicationConfiguration configuration = _sut.BuildApplicationConfigurationWithEnvironment(existingJson, options);

		// Assert
		configuration.EnvironmentVariables.Should().NotContainKey(
			"Kestrel__Endpoints__Http__Certificate__Password",
			because: "a removed HTTP endpoint must not leak its certificate password into the HTTPS host environment");
		configuration.Json.Should().NotContain("http-secret",
			because: "a removed endpoint's certificate password must not remain in the generated configuration");
	}

	[Test]
	[Description("Uses the explicit all-interface binding for dotnet HTTPS when the operator selects that topology.")]
	public void BuildApplicationConfiguration_ShouldBindHttpsToAllInterfacesWhenExplicitlyRequested() {
		// Arrange
		string certificatePath = CreateTemporaryPfx("server.pfx");
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			BindAllInterfaces = true,
			CertificatePath = certificatePath
		};

		// Act
		string result = _sut.BuildApplicationConfiguration(null, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://[::]:40123",
			because: "the explicit network-facing topology must bind the HTTPS endpoint to all interfaces");
	}

	[Test]
	[Description("Configures a PEM certificate with its separate private key file for dotnet HTTPS hosting.")]
	public void BuildApplicationConfiguration_ShouldConfigureHttpsFromPemAndKey() {
		// Arrange
		(string certificatePath, string keyPath) = CreateTemporaryPemCertificate();
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			CertificatePath = certificatePath,
			CertificateKeyPath = keyPath
		};

		// Act
		string result = _sut.BuildApplicationConfiguration(null, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be(Path.GetFullPath(certificatePath),
			because: "the PEM certificate path must be forwarded to Kestrel");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "KeyPath").Should().Be(Path.GetFullPath(keyPath),
			because: "Kestrel needs the matching private key path for a PEM certificate");
	}

	[Test]
	[Description("Preserves existing HTTPS certificate settings while constraining their bind address when HTTP remains the selected protocol.")]
	public void BuildApplicationConfiguration_ShouldPreserveExistingHttpsConfiguration() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://[::]:5000" },
			      "PublicHttp": { "Url": "http://0.0.0.0:5001" },
			      "Https": {
			        "Url": "https://[::]:5002",
			        "Certificate": { "Path": "existing.pfx", "Password": "existing-secret" }
			      }
			    }
			  },
			  "CustomSetting": { "Enabled": true }
			}
			""";
		PfInstallerOptions options = new() { SitePort = 40123 };

		// Act
		DotNetApplicationConfiguration configuration = _sut.BuildApplicationConfigurationWithEnvironment(existingJson, options);
		string result = configuration.Json;

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "the selected HTTP endpoint must use the secure loopback default");
		GetJsonString(result, "Kestrel", "Endpoints", "PublicHttp", "Url").Should().Be("http://localhost:5001",
			because: "every preserved HTTP endpoint must be constrained by the secure loopback default");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:5002",
			because: "preserved HTTPS configuration must not remain exposed on the wildcard address");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be("existing.pfx",
			because: "existing operator certificate configuration must not be discarded");
		HasJsonProperty(result, "Kestrel", "Endpoints", "Https", "Certificate", "Password").Should().BeFalse(
			because: "existing certificate passwords must not remain persisted in appsettings.json");
		configuration.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"].Should().Be("existing-secret",
			because: "an existing certificate password must be supplied through the controlled host environment");
		HasJsonProperty(result, "CustomSetting").Should().BeTrue(
			because: "unrelated application configuration must survive endpoint updates");
	}

	[Test]
	[Description("Rewrites an unbracketed IPv6 endpoint without mistaking the final address segment for a port.")]
	public void BuildApplicationConfiguration_ShouldPreserveUnbracketedIpv6AddressWithoutPort() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://::1" },
			      "Https": { "Url": "https://::1", "Certificate": { "Path": "existing.pfx" } }
			    }
			  }
			}
			""";
		PfInstallerOptions options = new() { SitePort = 40123 };

		// Act
		string result = _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "an IPv6 address without an explicit port must be rewritten before the selected HTTP port is applied");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost",
			because: "an unbracketed IPv6 HTTPS address without an explicit port must use the HTTPS default port");
	}

	[TestCase("http://[::1]", "http://localhost")]
	[TestCase("https://[::1]", "https://localhost")]
	[Description("Supports bracketed IPv6 endpoint hosts when the address has no explicit port.")]
	public void ReplaceHost_ShouldSupportBracketedIpv6WithoutPort(string url, string expected) {
		// Arrange

		// Act
		string result = KestrelEndpointUrl.ReplaceHost(url, "localhost");

		// Assert
		result.Should().Be(expected,
			because: "bracketed IPv6 host syntax without a port is a valid Kestrel endpoint form");
	}

	[TestCase("http://::1:5000")]
	[TestCase("http://2001:db8::1:5000")]
	[TestCase("http://fe80::1:5000")]
	[Description("Rejects ambiguous unbracketed IPv6 endpoint strings that could silently lose an explicit port.")]
	public void BuildApplicationConfiguration_ShouldRejectAmbiguousUnbracketedIpv6Port(string url) {
		// Arrange
		string existingJson = $"{{\"Kestrel\":{{\"Endpoints\":{{\"Http\":{{\"Url\":\"http://localhost:5000\"}},\"PublicHttp\":{{\"Url\":\"{url}\"}}}}}}}}";
		PfInstallerOptions options = new() { SitePort = 40123 };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("Kestrel endpoint 'PublicHttp' has an unsupported URL: *",
			because: "an ambiguous unbracketed IPv6 port must be rejected instead of being rewritten incorrectly");
	}

	[Test]
	[Description("Rejects an HTTP deployment that would preserve an HTTPS endpoint on the same Kestrel port.")]
	public void BuildApplicationConfiguration_ShouldRejectHttpHttpsPortConflict() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://localhost:40123" },
			      "Https": {
			        "Url": "https://localhost:5002",
			        "Certificate": { "Path": "existing.pfx" }
			      }
			    }
			  }
			}
			""";
		PfInstallerOptions options = new() { SitePort = 5002 };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The existing Kestrel HTTP and HTTPS endpoints both use port 5002.*",
			because: "preserving both protocols on one binding would make Kestrel fail at startup");
	}

	[Test]
	[Description("Rejects an HTTP deployment that would leave two same-scheme Kestrel endpoints on one binding.")]
	public void BuildApplicationConfiguration_ShouldRejectDuplicateHttpPort() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://localhost:5000" },
			      "PublicHttp": { "Url": "http://0.0.0.0:40123" }
			    }
			  }
			}
			""";
		PfInstallerOptions options = new() { SitePort = 40123 };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The Kestrel HTTP endpoints 'Http' and 'PublicHttp' both use port 40123.*",
			because: "Kestrel cannot start when rewritten HTTP endpoints share the same host and port");
	}

	[Test]
	[Description("Reuses an existing Kestrel HTTPS certificate when HTTPS is explicitly selected without a replacement certificate path.")]
	public void BuildApplicationConfiguration_ShouldReuseExistingHttpsCertificate() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
				  "Endpoints": {
				    "Http": { "Url": "http://localhost:5000" },
				    "Https": {
				      "Url": "https://[::]:5002",
				      "Certificate": { "Path": "existing.pfx" }
				    },
				    "PublicHttps": {
				      "Url": "https://0.0.0.0:5003",
				      "Certificate": { "Path": "public.pfx" }
				    }
				  }
			  }
			}
			""";
		PfInstallerOptions options = new() { SitePort = 40123, UseHttps = true };

		// Act
		string result = _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:40123",
			because: "explicit HTTPS must rebind the preserved endpoint to the selected port and bind policy");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be("existing.pfx",
			because: "an operator-provided Kestrel certificate must be reused when no replacement is supplied");
		GetJsonString(result, "Kestrel", "Endpoints", "PublicHttps", "Url").Should().Be("https://localhost:5003",
			because: "every preserved HTTPS endpoint must follow the selected loopback binding policy");
		HasJsonProperty(result, "Kestrel", "Endpoints", "Http").Should().BeFalse(
			because: "explicit HTTPS must remove the old plaintext endpoint");
	}

	[Test]
	[Description("Rejects dotnet HTTPS deployment when no certificate path or existing Kestrel certificate is available.")]
	public void BuildApplicationConfiguration_ShouldFailWithoutHttpsCertificate() {
		// Arrange
		PfInstallerOptions options = new() { SitePort = 40123, UseHttps = true };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(null, options);

		// Assert
			action.Should().Throw<InvalidOperationException>()
			.WithMessage("Dotnet HTTPS requires --cert-path or an existing Kestrel certificate configuration.",
			because: "an HTTPS listener without a certificate would fail after deployment and hide the configuration error");
	}

	[TestCase("{\"Kestrel\": []}")]
	[TestCase("{\"Kestrel\": {\"Endpoints\": []}}")]
	[TestCase("{\"Kestrel\": {\"Endpoints\": {\"Http\": []}}}")]
	[Description("Fails closed when an existing Kestrel configuration node has the wrong JSON type.")]
	public void BuildApplicationConfiguration_ShouldRejectNonObjectKestrelNodes(string existingJson) {
		// Arrange
		PfInstallerOptions options = new() { SitePort = 40123 };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		action.Should().Throw<JsonException>()
			.WithMessage("Configuration property '*' must be a JSON object.",
			because: "invalid Kestrel configuration must not be replaced with a potentially less secure default");
	}

	[Test]
	[Description("Rejects an explicit dotnet HTTPS certificate file that cannot be loaded as a certificate with a private key.")]
	public void BuildApplicationConfiguration_ShouldFailForInvalidHttpsCertificate() {
		// Arrange
		string certificatePath = Path.Combine(_temporaryDirectory, "invalid.pfx");
		File.WriteAllText(certificatePath, "not a certificate");
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			CertificatePath = certificatePath
		};

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(null, options);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The certificate specified by --cert-path is invalid or cannot be loaded: *",
			because: "deployment must fail before starting Kestrel when explicit certificate material is invalid");
	}

	[Test]
	[Description("Rejects an existing dotnet HTTPS endpoint whose certificate object has no certificate source.")]
	public void BuildApplicationConfiguration_ShouldFailForMalformedExistingHttpsCertificate() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Certificates": { "Default": { "Path": "default.pfx" } },
			    "Endpoints": {
			      "Https": { "Url": "https://localhost:5002", "Certificate": { "Password": "secret" } }
			    }
			  }
			}
			""";
		PfInstallerOptions options = new() { SitePort = 40123, UseHttps = true };

		// Act
		Action action = () => _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The existing Kestrel HTTPS endpoint certificate configuration is incomplete.*",
			because: "an incomplete endpoint certificate must not silently fall back to a valid default certificate");
	}

	[Test]
	[Description("Deploys dotnet with a certificate password supplied through a file and forwards it only to the host and service environment.")]
	public async Task Deploy_ShouldPassCertificatePasswordThroughEnvironment() {
		// Arrange
		string appDirectory = Path.Combine(_temporaryDirectory, "app");
		Directory.CreateDirectory(appDirectory);
		string certificatePath = CreateTemporaryPfx("deploy.pfx", "deploy-secret");
		string passwordFile = CreateTemporaryPasswordFile("deploy-secret");
		int sitePort = GetAvailablePort();
		PfInstallerOptions options = new() {
			SiteName = "deploy-test",
			SitePort = sitePort,
			UseHttps = true,
			CertificatePath = certificatePath,
			CertificatePasswordFile = passwordFile,
			AutoRun = true
		};
		_creatioHostService.StartInBackground(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>()).Returns(42);
		_serviceManager.CreateOrUpdateService(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<IReadOnlyDictionary<string, string>>()).Returns(Task.FromResult(true));
		_serviceManager.StartService(Arg.Any<string>()).Returns(Task.FromResult(true));

		// Act
		int exitCode = await _sut.Deploy(appDirectory, options);

		// Assert
		exitCode.Should().Be(0,
			because: "a valid dotnet HTTPS deployment should complete after writing configuration and starting the host");
		string generatedJson = File.ReadAllText(Path.Combine(appDirectory, "appsettings.json"));
		generatedJson.Should().NotContain("deploy-secret",
			because: "certificate passwords must not be written to the deployed appsettings.json");
		_creatioHostService.Received(1).PersistEnvironmentVariables(
			appDirectory,
			Arg.Is<IReadOnlyDictionary<string, string>>(variables =>
				variables["Kestrel__Endpoints__Https__Certificate__Password"] == "deploy-secret"));
		_creatioHostService.Received(1).StartInBackground(
			appDirectory,
			Arg.Is<IReadOnlyDictionary<string, string>>(variables =>
				variables["Kestrel__Endpoints__Https__Certificate__Password"] == "deploy-secret"));
		if (!OperatingSystem.IsWindows()) {
			await _serviceManager.Received(1).CreateOrUpdateService(
				"creatio-deploy-test", Arg.Any<string>(), appDirectory, "/usr/bin/dotnet", "Terrasoft.WebHost.dll", true,
				Arg.Is<IReadOnlyDictionary<string, string>>(variables =>
					variables["Kestrel__Endpoints__Https__Certificate__Password"] == "deploy-secret"));
		}
	}

	private string CreateTemporaryPfx(string fileName, string? password = null) {
		using RSA key = RSA.Create(2048);
		CertificateRequest request = new("CN=clio-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
		string path = Path.Combine(_temporaryDirectory, fileName);
		File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
		return path;
	}

	private string CreateTemporaryPasswordFile(string password) {
		string path = Path.Combine(_temporaryDirectory, Guid.NewGuid().ToString("N") + ".password");
		File.WriteAllText(path, password + Environment.NewLine);
		return path;
	}

	private static int GetAvailablePort() {
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private (string CertificatePath, string KeyPath) CreateTemporaryPemCertificate() {
		using RSA key = RSA.Create(2048);
		CertificateRequest request = new("CN=clio-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
		string certificatePath = Path.Combine(_temporaryDirectory, "server.pem");
		string keyPath = Path.Combine(_temporaryDirectory, "server.key");
		File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
		File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
		return (certificatePath, keyPath);
	}

	private static string GetJsonString(string json, params string[] path) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement current = document.RootElement;
		foreach (string propertyName in path) {
			current = current.GetProperty(propertyName);
		}
		return current.GetString() ?? string.Empty;
	}

	private static bool HasJsonProperty(string json, params string[] path) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement current = document.RootElement;
		for (int index = 0; index < path.Length - 1; index++) {
			current = current.GetProperty(path[index]);
		}
		return current.TryGetProperty(path[^1], out _);
	}
}
