using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml;
using Clio.Command;
using Clio.Common;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command {

	[TestFixture]
	[Category("Unit")]
	[Property("Module", "Command")]
	public class LinkCoreSrcCommandTests : BaseCommandTests<LinkCoreSrcOptions> {

		#region Fields: Private

		private IFileSystem _fileSystemMock;
		private ISettingsRepository _settingsRepositoryMock;
		private ISystemServiceManager _systemServiceManagerMock;
		private IValidator<LinkCoreSrcOptions> _validator;
		private LinkCoreSrcCommand _command;

		#endregion

		#region Methods: Public

		[SetUp]
		public void SetUp() {
			_fileSystemMock = Container.GetRequiredService<IFileSystem>();
			_settingsRepositoryMock = Container.GetRequiredService<ISettingsRepository>();
			_systemServiceManagerMock = Container.GetRequiredService<ISystemServiceManager>();
			_validator = Container.GetRequiredService<IValidator<LinkCoreSrcOptions>>();
			_command = Container.GetRequiredService<LinkCoreSrcCommand>();
		}

		protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
			base.AdditionalRegistrations(containerBuilder);
			_fileSystemMock ??= Substitute.For<IFileSystem>();
			_settingsRepositoryMock ??= Substitute.For<ISettingsRepository>();
			_systemServiceManagerMock ??= Substitute.For<ISystemServiceManager>();
			containerBuilder.AddSingleton<IFileSystem>(_fileSystemMock);
			containerBuilder.AddSingleton<ISettingsRepository>(_settingsRepositoryMock);
			containerBuilder.AddSingleton<ISystemServiceManager>(_systemServiceManagerMock);
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
		string result = _command.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

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
		string result = _command.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

		// Assert
		GetJsonString(result, "Kestrel", "Endpoints", "Http", "Url").Should().Be("http://localhost:40123",
			because: "link-core-src needs a deterministic local HTTP endpoint when none exists");
		GetJsonString(result, "Kestrel", "Endpoints", "Https", "Url").Should().Be("https://localhost:5002",
			because: "an existing HTTPS endpoint must not be discarded by the HTTP fallback");
		GetJsonString(result, "Kestrel", "Certificates", "Default", "Path").Should().Be("server.pfx",
			because: "existing certificate settings must remain available to Kestrel");
	}

	[Test]
	[Description("Preserves a valid JSON configuration error instead of misreporting it as an unsupported XML format.")]
	public void UpdateConfigWithPort_ShouldRejectMalformedJsonConfiguration() {
		// Arrange
		const string existingJson = "{\"Kestrel\":[]}";

		// Act
		Action action = () => _command.UpdateConfigWithPort(existingJson, 40123, "/tmp/appsettings.json");

		// Assert
		action.Should().Throw<JsonException>()
			.WithMessage("Configuration property '*' must be a JSON object.",
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

	#endregion

	}

}
