using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SettingsRepositoryGetEnvironmentTests {

	private MockFileSystem _fileSystem;

	[SetUp]
	public void SetUp() {
		_fileSystem = TestFileSystem.MockFileSystem();
		_fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(
			File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-netcore-active-env.json"))));
		_ = new SettingsRepository(_fileSystem);
	}

	[Test]
	[Description("GetEnvironment with only Uri and no env name should NOT inherit IsNetCore from the active environment.")]
	public void GetEnvironment_WithDirectUri_DoesNotInheritIsNetCore_FromActiveEnvironment() {
		SettingsRepository sut = new(_fileSystem);
		var options = new EnvironmentOptions {
			Uri = "http://ts1-core-dev04:88/sae",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		EnvironmentSettings result = sut.GetEnvironment(options);

		result.IsNetCore.Should().BeFalse(
			because: "when no environment name is specified and only a URI is provided, IsNetCore must not be inherited from the active environment and defaults to false");
	}

	[Test]
	[Description("GetEnvironment with no env name and no Uri should inherit IsNetCore from the active environment.")]
	public void GetEnvironment_WithNoUriAndNoEnv_InheritsIsNetCore_FromActiveEnvironment() {
		SettingsRepository sut = new(_fileSystem);
		var options = new EnvironmentOptions {
			Login = "Supervisor",
			Password = "Supervisor"
		};

		EnvironmentSettings result = sut.GetEnvironment(options);

		result.IsNetCore.Should().BeTrue(
			because: "when no environment name and no URI are provided, settings should be resolved from the active environment which has IsNetCore=true");
	}

	[Test]
	[Description("GetEnvironment with an explicit environment name should inherit IsNetCore from that environment.")]
	public void GetEnvironment_WithExplicitEnvName_InheritsIsNetCore_FromThatEnvironment() {
		SettingsRepository sut = new(_fileSystem);
		var options = new EnvironmentOptions {
			Environment = "framework-env"
		};

		EnvironmentSettings result = sut.GetEnvironment(options);

		result.IsNetCore.Should().BeFalse(
			because: "when an explicit environment name is provided, IsNetCore must be resolved from that environment's stored settings");
	}

	[Test]
	[Description("GetEnvironment with explicit IsNetCore=false option should override the stored environment value.")]
	public void GetEnvironment_WithExplicitIsNetCoreOption_OverridesStoredValue() {
		SettingsRepository sut = new(_fileSystem);
		var options = new EnvironmentOptions {
			Environment = "netcore-env",
			IsNetCore = false
		};

		EnvironmentSettings result = sut.GetEnvironment(options);

		result.IsNetCore.Should().BeFalse(
			because: "an explicit IsNetCore option value must override the stored environment value");
	}

	[Test]
	[Description("GetEnvironment with direct URI and explicit IsNetCore=true should use the explicit value.")]
	public void GetEnvironment_WithDirectUriAndExplicitIsNetCore_UsesExplicitValue() {
		SettingsRepository sut = new(_fileSystem);
		var options = new EnvironmentOptions {
			Uri = "http://remote-host:88/site",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};

		EnvironmentSettings result = sut.GetEnvironment(options);

		result.IsNetCore.Should().BeTrue(
			because: "an explicit IsNetCore=true option must be respected even when only a URI is provided");
	}
}
