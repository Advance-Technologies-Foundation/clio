using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
	private string _temporaryDirectory;

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
		PfInstallerOptions options = new() { SitePort = 40123, BindAllInterfaces = true };

		// Act
		string result = _sut.GetApplicationUrl(options);

		// Assert
		result.Should().Be("http://localhost:40123",
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
		PfInstallerOptions options = new() { SitePort = 40123, BindAllInterfaces = true };

		// Act
		string result = _sut.BuildApplicationConfiguration(null, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://[::]:40123",
			because: "the wildcard binding must remain available only through explicit opt-in");
	}

	[Test]
	[Description("Configures a dotnet HTTPS endpoint from a PFX certificate and removes the plaintext endpoint when HTTPS is explicit.")]
	public void BuildApplicationConfiguration_ShouldConfigureHttpsFromPfx() {
		// Arrange
		string certificatePath = CreateTemporaryPfx("server.pfx", "secret");
		PfInstallerOptions options = new() {
			SitePort = 40123,
			UseHttps = true,
			CertificatePath = certificatePath,
			CertificatePassword = "secret"
		};

		// Act
		string result = _sut.BuildApplicationConfiguration(null, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:40123",
			because: "the HTTPS endpoint must use the secure loopback address and requested port");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be(Path.GetFullPath(certificatePath),
			because: "Kestrel must load the certificate selected by the operator");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Password").Should().Be("secret",
			because: "the configured certificate password must be available to Kestrel");
		HasJsonProperty(result, "Kestrel", "Endpoints", "Http").Should().BeFalse(
			because: "explicit HTTPS deployment must not leave a parallel plaintext listener");
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
		string result = _sut.BuildApplicationConfiguration(existingJson, options);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "the selected HTTP endpoint must use the secure loopback default");
		GetJsonString(result, "Kestrel", "Endpoints", "PublicHttp", "Url").Should().Be("http://localhost:5001",
			because: "every preserved HTTP endpoint must be constrained by the secure loopback default");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:5002",
			because: "preserved HTTPS configuration must not remain exposed on the wildcard address");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be("existing.pfx",
			because: "existing operator certificate configuration must not be discarded");
		HasJsonProperty(result, "CustomSetting").Should().BeTrue(
			because: "unrelated application configuration must survive endpoint updates");
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
			.WithMessage("Dotnet HTTPS requires --cert-path or an existing Kestrel certificate configuration.",
			because: "a password-only certificate object cannot configure a working HTTPS listener");
	}

	private string CreateTemporaryPfx(string fileName, string? password = null) {
		using RSA key = RSA.Create(2048);
		CertificateRequest request = new("CN=clio-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
		string path = Path.Combine(_temporaryDirectory, fileName);
		File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
		return path;
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
