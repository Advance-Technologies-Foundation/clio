using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Autofac;
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
	[Category("UnitTests")]
	public class LinkCoreSrcCommandTests : BaseCommandTests<LinkCoreSrcOptions> {

		#region Fields: Private

		private IFileSystem _fileSystemMock;
		private ISettingsRepository _settingsRepositoryMock;
		private ISystemServiceManager _systemServiceManagerMock;
		private IValidator<LinkCoreSrcOptions> _validator;

		#endregion

		#region Methods: Public

		[SetUp]
		public void SetUp() {
			_fileSystemMock = Container.Resolve<IFileSystem>();
			_settingsRepositoryMock = Container.Resolve<ISettingsRepository>();
			_systemServiceManagerMock = Container.Resolve<ISystemServiceManager>();
			_validator = Container.Resolve<IValidator<LinkCoreSrcOptions>>();
		}

		protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
			base.AdditionalRegistrations(containerBuilder);
			_fileSystemMock ??= Substitute.For<IFileSystem>();
			_settingsRepositoryMock ??= Substitute.For<ISettingsRepository>();
			_systemServiceManagerMock ??= Substitute.For<ISystemServiceManager>();
			containerBuilder.RegisterInstance(_fileSystemMock).As<IFileSystem>();
			containerBuilder.RegisterInstance(_settingsRepositoryMock).As<ISettingsRepository>();
			containerBuilder.RegisterInstance(_systemServiceManagerMock).As<ISystemServiceManager>();
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
		result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(options.CorePath));
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
		result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(options.Environment));
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

		var command = Container.Resolve<LinkCoreSrcCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because validation failed");
	}

	#endregion

	}

}