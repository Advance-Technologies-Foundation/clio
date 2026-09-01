using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using Clio.Command;
using Clio.Common;
using Clio.Common.ScenarioHandlers;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;
using OneOf;

namespace Clio.Tests.Command {

	[TestFixture]
	[Category("Unit")]
	[Property("Module", "Command")]
	public class LinkCoreSrcCommandTests : BaseCommandTests<LinkCoreSrcOptions> {

		#region Fields: Private

		private IFileSystem _fileSystemMock;
		private ISettingsRepository _settingsRepositoryMock;
		private ISystemServiceManager _systemServiceManagerMock;
		private ICreatioHostEnvironmentStore _environmentStoreMock;
		private IUpdateIISSitePhysicalPathHandler _updateIISSitePhysicalPathHandlerMock;
		private IValidator<LinkCoreSrcOptions> _validator;
		private LinkCoreSrcCommand _command;

		#endregion

		#region Methods: Public

		[SetUp]
		public void SetUp() {
			_fileSystemMock = Container.GetRequiredService<IFileSystem>();
			_settingsRepositoryMock = Container.GetRequiredService<ISettingsRepository>();
			_systemServiceManagerMock = Container.GetRequiredService<ISystemServiceManager>();
			_updateIISSitePhysicalPathHandlerMock = Container.GetRequiredService<IUpdateIISSitePhysicalPathHandler>();
			_validator = Container.GetRequiredService<IValidator<LinkCoreSrcOptions>>();
			_command = Container.GetRequiredService<LinkCoreSrcCommand>();
		}

		protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
			base.AdditionalRegistrations(containerBuilder);
			_fileSystemMock ??= Substitute.For<IFileSystem>();
			_settingsRepositoryMock ??= Substitute.For<ISettingsRepository>();
			_systemServiceManagerMock ??= Substitute.For<ISystemServiceManager>();
			_environmentStoreMock ??= Substitute.For<ICreatioHostEnvironmentStore>();
			_updateIISSitePhysicalPathHandlerMock ??= Substitute.For<IUpdateIISSitePhysicalPathHandler>();
			containerBuilder.AddSingleton<IFileSystem>(_fileSystemMock);
			containerBuilder.AddSingleton<ISettingsRepository>(_settingsRepositoryMock);
			containerBuilder.AddSingleton<ISystemServiceManager>(_systemServiceManagerMock);
			containerBuilder.AddSingleton<ICreatioHostEnvironmentStore>(_environmentStoreMock);
			containerBuilder.AddSingleton<IUpdateIISSitePhysicalPathHandler>(_updateIISSitePhysicalPathHandlerMock);
		}

	#endregion

	#region Tests: Kestrel configuration

	[Test]
	[Description("Constrains existing HTTP and HTTPS Kestrel endpoint hosts to loopback while updating the development HTTP port.")]
	public void UpdateConfigWithPort_ShouldConstrainAllKestrelEndpointsToLoopback() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://[::]:5000" },
			      "PublicHttp": { "Url": "http://10.0.0.5:5001" },
			      "Https": { "Url": "https://0.0.0.0:5002", "Certificate": { "Path": "server.pfx" } }
			    }
			  },
			  "CustomSetting": { "Enabled": true }
			}
			""";

		// Act
		string result = LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "the linked development HTTP endpoint must use the environment port and loopback");
		GetJsonString(result, "Kestrel", "Endpoints", "PublicHttp", "Url").Should().Be("http://localhost:5001",
			because: "additional HTTP endpoints must not remain exposed on a non-loopback host");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:5002",
			because: "preserved HTTPS endpoints must also be constrained to loopback");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be("server.pfx",
			because: "link-core-src must preserve the existing HTTPS certificate configuration");
		HasJsonProperty(result, "CustomSetting").Should().BeTrue(
			because: "unrelated application configuration must survive the port update");
	}

	[Test]
	[Description("Adds a loopback HTTP endpoint without replacing existing Kestrel configuration when no HTTP endpoint exists.")]
	public void UpdateConfigWithPort_ShouldAddLoopbackHttpEndpointAndPreserveExistingConfiguration() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Certificates": { "Default": { "Path": "server.pfx" } },
			    "Endpoints": {
			      "Https": { "Url": "https://[::]:5002", "Certificate": { "Path": "server.pfx" } }
			    }
			  }
			}
			""";

		// Act
		string result = LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "link-core-src needs a deterministic local HTTP endpoint when none exists");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:5002",
			because: "an existing HTTPS endpoint must not be discarded by the HTTP fallback");
		GetJsonString(result, "Kestrel", "Certificates", "Default", "Path").Should().Be("server.pfx",
			because: "existing certificate settings must remain available to Kestrel");
	}

	[Test]
	[Description("Updates the canonical Http endpoint even when another HTTP endpoint appears first in the Kestrel JSON object.")]
	public void UpdateConfigWithPort_ShouldPreferNamedHttpEndpointWhenEndpointOrderDiffers() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "PublicHttp": { "Url": "http://0.0.0.0:5001" },
			      "Http": { "Url": "http://localhost:5000" }
			    }
			  }
			}
			""";

		// Act
		string result = LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "the named Http endpoint is the canonical development endpoint regardless of JSON property order");
		GetJsonString(result, "Kestrel", "Endpoints", "PublicHttp", "Url").Should().Be("http://localhost:5001",
			because: "other HTTP endpoints must keep their ports while being constrained to loopback");
	}

	[Test]
	[Description("Updates the canonical HTTPS endpoint when the registered environment uses an HTTPS URI instead of creating a plaintext endpoint.")]
	public void UpdateConfigWithPort_ShouldPreferHttpsEndpoint_WhenTargetSchemeIsHttps() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Https": { "Url": "https://[::]:5002", "Certificate": { "Path": "server.pfx" } }
			    }
			  }
			}
			""";

		// Act
		string result = LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json", Uri.UriSchemeHttps);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:40123",
			because: "an HTTPS-registered environment must keep its secure endpoint and update its selected port");
		HasJsonProperty(result, "Kestrel", "Endpoints", "Http").Should().BeFalse(
			because: "link-core-src must not introduce a plaintext listener for an HTTPS-registered environment");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Certificate", "Path").Should().Be("server.pfx",
			because: "the existing HTTPS certificate configuration must remain attached to the endpoint");
	}

	[Test]
	[Description("Preserves an HTTPS endpoint whose legacy configuration name is Http when link-core-src targets an HTTPS environment.")]
	public void UpdateConfigWithPort_ShouldPreserveHttpsEndpointNamedHttp_WhenTargetSchemeIsHttps() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "https://[::]:5002", "Certificate": { "Path": "server.pfx" } }
			    }
			  }
			}
			""";

		// Act
		string result = LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json", Uri.UriSchemeHttps);

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("https://localhost:40123",
			because: "the endpoint URL scheme, rather than the legacy endpoint name, determines whether it is secure");
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Certificate", "Path").Should().Be("server.pfx",
			because: "link-core-src must preserve the certificate attached to the selected HTTPS endpoint");
	}

	[Test]
	[Description("Removes every HTTP Kestrel endpoint when link-core-src targets an HTTPS environment.")]
	public void UpdateConfigWithPort_ShouldRemoveHttpEndpoints_WhenTargetSchemeIsHttps() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://localhost:5000" },
			      "PublicHttp": { "Url": "http://0.0.0.0:5001" },
			      "Https": { "Url": "https://localhost:5002", "Certificate": { "Path": "server.pfx" } }
			    }
			  }
			}
			""";

		// Act
		string result = LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json", Uri.UriSchemeHttps);

		// Assert
		HasJsonProperty(result, "Kestrel", "Endpoints", "Http").Should().BeFalse(
			because: "an HTTPS-linked environment must not retain its canonical plaintext endpoint");
		HasJsonProperty(result, "Kestrel", "Endpoints", "PublicHttp").Should().BeFalse(
			because: "an HTTPS-linked environment must not retain an additional plaintext endpoint");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:40123",
			because: "the selected HTTPS endpoint must receive the linked environment port");
	}

	[Test]
	[Description("Rejects HTTPS link-core-src configuration when neither the selected endpoint nor Kestrel's default certificate is configured.")]
	public void UpdateConfigWithPort_ShouldRejectHttpsWithoutCertificate() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://localhost:5000" }
			    }
			  }
			}
			""";

		// Act
		Action action = () => LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json", Uri.UriSchemeHttps);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("HTTPS link-core-src requires a certificate on the selected endpoint or in Kestrel.Certificates:Default.",
			because: "linking an HTTPS environment must not write an endpoint that Kestrel cannot start");
	}

	[Test]
	[Description("Rejects linking a Kestrel configuration that would preserve a plaintext certificate password.")]
	public void UpdateConfigWithPort_ShouldRejectPlaintextCertificatePassword() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Https": {
			        "Url": "https://localhost:5002",
			        "Certificate": { "Path": "server.pfx", "Password": "secret" }
			      }
			    }
			  }
			}
			""";

		// Act
		Action action = () => LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json", "https");

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("link-core-src cannot preserve plaintext Kestrel certificate passwords.*",
			because: "link-core-src must not write an existing certificate secret back to appsettings.json");
	}

	[Test]
	[Description("Rejects linked HTTPS configuration when the selected certificate file cannot be loaded before appsettings.json is written.")]
	public void ValidateHttpsConfiguration_ShouldRejectInvalidCertificateMaterial() {
		// Arrange
		string temporaryDirectory = Path.Combine(Path.GetTempPath(), "clio-link-core-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temporaryDirectory);
		try
		{
			string certificatePath = Path.Combine(temporaryDirectory, "server.pfx");
			File.WriteAllText(certificatePath, "not a certificate");
			string configuration = $$"""
				{
				  "Kestrel": {
				    "Endpoints": {
				      "Https": {
				        "Url": "https://localhost:40123",
				        "Certificate": { "Path": "{{Path.GetFileName(certificatePath)}}" }
				      }
				    }
				  }
				}
				""";

			// Act
			Action action = () => _command.ValidateHttpsConfiguration(
				configuration,
				Path.Combine(temporaryDirectory, "appsettings.json"),
				Path.Combine(temporaryDirectory, "environment"));

			// Assert
			action.Should().Throw<InvalidOperationException>()
				.WithMessage("The certificate specified by --cert-path is invalid or cannot be loaded: *",
				because: "link-core-src must fail before writing an HTTPS configuration that Kestrel cannot load");
		}
		finally
		{
			if (Directory.Exists(temporaryDirectory))
			{
				Directory.Delete(temporaryDirectory, recursive: true);
			}
		}
	}

	[Test]
	[Description("Rejects a link-core-src port that would make the preserved HTTP and HTTPS Kestrel endpoints collide.")]
	public void UpdateConfigWithPort_ShouldRejectHttpHttpsPortConflict() {
		// Arrange
		const string existingJson = """
			{
			  "Kestrel": {
			    "Endpoints": {
			      "Http": { "Url": "http://localhost:5000" },
			      "Https": { "Url": "https://localhost:5002", "Certificate": { "Path": "server.pfx" } }
			    }
			  }
			}
			""";

		// Act
		Action action = () => LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 5002, "/tmp/appsettings.json");

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The Kestrel HTTP and HTTPS endpoints both use port 5002.*",
			because: "link-core-src must fail before writing a configuration that Kestrel cannot bind");
	}

	[Test]
	[Description("Preserves a valid JSON configuration error instead of misreporting it as an unsupported XML format.")]
	public void UpdateConfigWithPort_ShouldRejectMalformedJsonConfiguration() {
		// Arrange
		const string existingJson = "{\"Kestrel\":[]}";

		// Act
		Action action = () => LinkCoreSrcCommand.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

		// Assert
		action.Should().Throw<JsonException>()
			.WithMessage("Configuration property 'Kestrel' must be a JSON object.",
			because: "a valid JSON file with an invalid Kestrel shape should report its structural error directly");
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

	#endregion

	#region Tests: Validation

	[Test]
	[Description("Should validate that CorePath is required")]
	public void Validate_ShouldFail_WhenCorePathIsEmpty() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = ""
		};

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because CorePath is required");
		result.Errors.Should().Contain(e => e.PropertyName == nameof(options.CorePath));
	}

	[Test]
	[Description("Should validate that Environment is required")]
	public void Validate_ShouldFail_WhenEnvironmentIsEmpty() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "",
			CorePath = "/path/to/core"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because Environment is required");
		result.Errors.Should().Contain(e => e.PropertyName == nameof(options.Environment));
	}

	[Test]
	[Description("Should validate that CorePath directory exists")]
	public void Validate_ShouldFail_WhenCorePathDoesNotExist() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = "/nonexistent/path"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(false);

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because CorePath directory must exist");
	}

	[Test]
	[Description("Should validate that Environment is registered in clio config")]
	public void Validate_ShouldFail_WhenEnvironmentNotRegistered() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "nonexistent",
			CorePath = "/path/to/core"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_settingsRepositoryMock.GetEnvironment(Arg.Any<string>()).Returns((EnvironmentSettings)null);

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because environment must be registered");
	}

	[Test]
	[Description("Should validate that ConnectionStrings.config exists in application")]
	public void Validate_ShouldFail_WhenConnectionStringsConfigNotFound() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = "/path/to/core"
		};

		var envSettings = new EnvironmentSettings {
			EnvironmentPath = "/path/to/app",
			Uri = "http://localhost:82"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_settingsRepositoryMock.GetEnvironment("test").Returns(envSettings);
		
		// Mock GetFiles to return different values based on path and filename
		_fileSystemMock.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(callArgs => {
				string path = (string)callArgs[0];
				string pattern = (string)callArgs[1];
				
				// No ConnectionStrings.config in app
				if (pattern == "ConnectionStrings.config") {
					return Array.Empty<string>();
				}
				
				// Return empty for other patterns
				return Array.Empty<string>();
			});
		
		_fileSystemMock.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(new[] { "/path/to/core/Terrasoft.WebHost" });

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because ConnectionStrings.config must exist in application");
	}

	[Test]
	[Description("Should validate that appsettings.config exists in core")]
	public void Validate_ShouldFail_WhenAppSettingsConfigNotFound() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = "/path/to/core"
		};

		var envSettings = new EnvironmentSettings {
			EnvironmentPath = "/path/to/app",
			Uri = "http://localhost:82"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_settingsRepositoryMock.GetEnvironment("test").Returns(envSettings);
		
		_fileSystemMock.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(callArgs => {
				string pattern = (string)callArgs[1];
				
				// Return ConnectionStrings.config in app
				if (pattern == "ConnectionStrings.config") {
					return new[] { "/path/to/app/ConnectionStrings.config" };
				}
				
				// No appsettings.json in core
				if (pattern == "appsettings.json") {
					return Array.Empty<string>();
				}
				
				return Array.Empty<string>();
			});

		_fileSystemMock.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(new[] { "/path/to/core/Terrasoft.WebHost" });

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because appsettings.json must exist in core");
	}

	[Test]
	[Description("Should validate that app.config exists in core")]
	public void Validate_ShouldFail_WhenAppConfigNotFound() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = "/path/to/core"
		};

		var envSettings = new EnvironmentSettings {
			EnvironmentPath = "/path/to/app",
			Uri = "http://localhost:82"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_settingsRepositoryMock.GetEnvironment("test").Returns(envSettings);
		
		_fileSystemMock.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(callArgs => {
				string pattern = (string)callArgs[1];
				
				// Return ConnectionStrings.config in app
				if (pattern == "ConnectionStrings.config") {
					return new[] { "/path/to/app/ConnectionStrings.config" };
				}
				
				// Return appsettings.json in core
				if (pattern == "appsettings.json") {
					return new[] { "/path/to/core/appsettings.json" };
				}
				
				// No Terrasoft.WebHost.dll.config
				if (pattern == "Terrasoft.WebHost.dll.config") {
					return Array.Empty<string>();
				}
				
				return Array.Empty<string>();
			});

		_fileSystemMock.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(new[] { "/path/to/core/Terrasoft.WebHost" });

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because Terrasoft.WebHost.dll.config must exist in core");
	}

	[Test]
	[Description("Should validate that Terrasoft.WebHost exists in core")]
	public void Validate_ShouldFail_WhenTerrasoftWebHostNotFound() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = "/path/to/core"
		};

		var envSettings = new EnvironmentSettings {
			EnvironmentPath = "/path/to/app",
			Uri = "http://localhost:82"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_settingsRepositoryMock.GetEnvironment("test").Returns(envSettings);
		
		_fileSystemMock.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(callArgs => {
				string pattern = (string)callArgs[1];
				
				if (pattern == "ConnectionStrings.config") {
					return new[] { "/path/to/app/ConnectionStrings.config" };
				}
				if (pattern == "appsettings.json") {
					return new[] { "/path/to/core/appsettings.json" };
				}
				if (pattern == "Terrasoft.WebHost.dll.config") {
					return new[] { "/path/to/core/Terrasoft.WebHost.dll.config" };
				}
				
				return Array.Empty<string>();
			});
		
		// No Terrasoft.WebHost directory
		_fileSystemMock.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(Array.Empty<string>());

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(false, "because Terrasoft.WebHost directory must exist in core");
	}

	[Test]
	[Description("Should validate successfully with correct options")]
	public void Validate_ShouldSucceed_WhenAllConditionsMet() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "test",
			CorePath = "/path/to/core"
		};

		var envSettings = new EnvironmentSettings {
			EnvironmentPath = "/path/to/app",
			Uri = "http://localhost:82"
		};

		_fileSystemMock.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_settingsRepositoryMock.GetEnvironment("test").Returns(envSettings);
		
		_fileSystemMock.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(callArgs => {
				string pattern = (string)callArgs[1];
				
				if (pattern == "ConnectionStrings.config") {
					return new[] { "/path/to/app/ConnectionStrings.config" };
				}
				if (pattern == "appsettings.json") {
					return new[] { "/path/to/core/appsettings.json" };
				}
				if (pattern == "Terrasoft.WebHost.dll.config") {
					return new[] { "/path/to/core/Terrasoft.WebHost.dll.config" };
				}
				
				return Array.Empty<string>();
			});
		
		_fileSystemMock.GetDirectories(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
			.Returns(new[] { "/path/to/core/Terrasoft.WebHost" });

		// Act
		var result = _validator.Validate(options);

		// Assert
		result.IsValid.Should().Be(true, "because all required files and directories exist");
	}

	#endregion

	#region Tests: Execution

	[Test]
	[Description("Should return 1 when validation fails")]
	public void Execute_ShouldReturnOne_WhenValidationFails() {
		// Arrange
		var options = new LinkCoreSrcOptions {
			Environment = "",
			CorePath = ""
		};

		var command = Container.GetRequiredService<LinkCoreSrcCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because validation failed");
	}

	[Test]
	[Description("Moves protected dotnet certificate values when link-core-src changes the registered environment path.")]
	public void MigrateHostEnvironment_ShouldMoveProtectedValuesToNewEnvironmentPath() {
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};
		_environmentStoreMock.Load(Path.GetFullPath("/tmp/creatio-app")).Returns(environmentVariables);
		_environmentStoreMock.Load(Path.GetFullPath("/tmp/creatio-core")).Returns(
			new Dictionary<string, string>());

		// Act
		_command.MigrateHostEnvironment("/tmp/creatio-app", "/tmp/creatio-core");

		// Assert
		_environmentStoreMock.Received(1).Save(
			Path.GetFullPath("/tmp/creatio-core"),
			environmentVariables);
		_environmentStoreMock.Received(1).Save(
			Path.GetFullPath("/tmp/creatio-app"),
			Arg.Is<IReadOnlyDictionary<string, string>>(variables => variables.Count == 0));
	}

	[Test]
	[Description("Restores the previous IIS physical path when link-core-src must roll back an environment update")]
	public void RestoreIISPhysicalPath_ShouldApplyPreviousEnvironmentPath() {
		// Arrange
		_updateIISSitePhysicalPathHandlerMock.Handle(Arg.Any<UpdateIISSitePhysicalPathRequest>())
			.Returns(Task.FromResult<OneOf<BaseHandlerResponse, HandlerError>>(
				new UpdateIISSitePhysicalPathResponse {
					Status = BaseHandlerResponse.CompletionStatus.Success,
					Description = "restored"
				}));

		// Act
		_command.RestoreIISPhysicalPath("production", "/tmp/creatio-app");

		// Assert
		_updateIISSitePhysicalPathHandlerMock.Received(1).Handle(
			Arg.Is<UpdateIISSitePhysicalPathRequest>(request =>
				request.Arguments["siteName"] == "production"
				&& request.Arguments["physicalPath"] == "/tmp/creatio-app"));
	}

	#endregion

	}

}
