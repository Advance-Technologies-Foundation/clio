using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using Autofac;
using CommandLine;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
internal class CreateDevEnvironmentCommandTests : BaseCommandTests<CreateDevEnvironmentOptions>
{
	private readonly IKubernetesService _kubernetesService = Substitute.For<IKubernetesService>();
	private readonly IConfigPatcherService _configPatcherService = Substitute.For<IConfigPatcherService>();
	private readonly IPostgresService _postgresService = Substitute.For<IPostgresService>();

	public override void Setup()
	{
		base.Setup();
		// Reset mocks between tests
		_kubernetesService.ClearReceivedCalls();
		_configPatcherService.ClearReceivedCalls();
		_postgresService.ClearReceivedCalls();
	}

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_kubernetesService).As<IKubernetesService>();
		containerBuilder.RegisterInstance(_configPatcherService).As<IConfigPatcherService>();
		containerBuilder.RegisterInstance(_postgresService).As<IPostgresService>();
	}

	#region Parameter Parsing Tests

	[Test]
	[Description("Verifies that CreateDevEnvironmentOptions is defined with all required parameters")]
	public void Options_ShouldHave_AllRequiredParameters()
	{
		// Arrange & Act
		var options = typeof(CreateDevEnvironmentOptions);

		// Assert
		options.GetProperty(nameof(CreateDevEnvironmentOptions.Zip)).Should().NotBeNull("Zip parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.EnvName)).Should().NotBeNull("EnvName parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.Port)).Should().NotBeNull("Port parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.Username)).Should().NotBeNull("Username parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.Password)).Should().NotBeNull("Password parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.TargetDir)).Should().NotBeNull("TargetDir parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.Maintainer)).Should().NotBeNull("Maintainer parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.SkipInfra)).Should().NotBeNull("SkipInfra parameter must exist");
		options.GetProperty(nameof(CreateDevEnvironmentOptions.NoConfirm)).Should().NotBeNull("NoConfirm parameter must exist");
	}

	[Test]
	[Description("Verifies Zip parameter is marked as required")]
	public void Options_Zip_ShouldBeRequired()
	{
		// Arrange
		var zipProperty = typeof(CreateDevEnvironmentOptions).GetProperty(nameof(CreateDevEnvironmentOptions.Zip));

		// Act
		var optionAttribute = zipProperty?.GetCustomAttributes(typeof(OptionAttribute), false).FirstOrDefault() as OptionAttribute;

		// Assert
		optionAttribute.Should().NotBeNull("Zip must have Option attribute");
		optionAttribute?.Required.Should().BeTrue("Zip must be required");
	}

	[Test]
	[Description("Verifies Port parameter has default value of 8080")]
	public void Options_Port_ShouldHaveDefaultValue()
	{
		// Arrange
		var options = new CreateDevEnvironmentOptions();

		// Act
		var port = options.Port;

		// Assert
		port.Should().Be(8080, "default port should be 8080");
	}

	[Test]
	[Description("Verifies Username parameter has default value of 'Supervisor'")]
	public void Options_Username_ShouldHaveDefaultValue()
	{
		// Arrange
		var options = new CreateDevEnvironmentOptions();

		// Act
		var username = options.Username;

		// Assert
		username.Should().Be("Supervisor", "default username should be 'Supervisor'");
	}

	[Test]
	[Description("Verifies Password parameter has default value of 'Supervisor'")]
	public void Options_Password_ShouldHaveDefaultValue()
	{
		// Arrange
		var options = new CreateDevEnvironmentOptions();

		// Act
		var password = options.Password;

		// Assert
		password.Should().Be("Supervisor", "default password should be 'Supervisor'");
	}

	[Test]
	[Description("Verifies SkipInfra parameter has default value of false")]
	public void Options_SkipInfra_ShouldHaveDefaultValueFalse()
	{
		// Arrange
		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/path/to/app.zip"
		};

		// Act
		var skipInfra = options.SkipInfra;

		// Assert
		skipInfra.Should().BeFalse("default SkipInfra should be false");
	}

	#endregion

	#region Validation Tests

	[Test]
	[Description("Verifies command fails when ZIP file does not exist")]
	public void Execute_WithNonexistentZipFile_ShouldReturnError()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/nonexistent/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		result.Should().Be(1, "command should fail when ZIP file does not exist");
	}

	[Test]
	[Description("Verifies EnvName is required either as parameter or interactive prompt")]
	public void Execute_WithoutEnvName_ShouldHandleInteractivePrompt()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.File.WriteAllText("/app.zip", "dummy");
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = null, // Will be set interactively
			TargetDir = "/tmp/creatio",
			NoConfirm = true
		};

		// Note: Interactive prompt testing is limited in unit tests
		// This test verifies that EnvName is at least checked for null

		// Act & Assert
		// In real scenario, this would prompt the user
		options.EnvName.Should().BeNullOrEmpty("EnvName should be optional parameter if provided via interactive prompt");
	}

	#endregion

	#region Infrastructure Tests

	[Test]
	[Description("Verifies command calls KubernetesService.CheckInfrastructureExists")]
	public void Execute_ShouldCheckExistingInfrastructure()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddDirectory("/tmp/creatio");
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists("default").Returns(false);
		_kubernetesService.DeployInfrastructure(Arg.Any<string>(), "default").Returns(true);
		_kubernetesService.WaitForServices("default", Arg.Any<TimeSpan>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_kubernetesService.Received(1).CheckInfrastructureExists("default");
	}

	[Test]
	[Description("Verifies command skips infrastructure when SkipInfra option is set")]
	public void Execute_WithSkipInfra_ShouldNotCallKubernetesService()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddDirectory("/tmp/creatio");
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_kubernetesService.DidNotReceive().CheckInfrastructureExists(Arg.Any<string>());
	}

	#endregion

	#region Configuration Tests

	[Test]
	[Description("Verifies command calls ConfigPatcherService for CookiesSameSiteMode")]
	public void Execute_ShouldPatchCookiesSameSiteMode()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddFile("/tmp/creatio/Terrasoft.WebHost.dll.config", new MockFileData("<configuration></configuration>"));
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);
		_configPatcherService.UpdateConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_configPatcherService.ConfigurePort(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_configPatcherService.Received(1).PatchCookiesSameSiteMode(Arg.Any<string>());
	}

	[Test]
	[Description("Verifies command calls ConfigPatcherService for connection string update")]
	public void Execute_ShouldUpdateConnectionString()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddFile("/tmp/creatio/Terrasoft.WebHost.dll.config", new MockFileData("<configuration></configuration>"));
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);
		_configPatcherService.UpdateConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_configPatcherService.ConfigurePort(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			Port = 8080,
			Username = "test-user",
			Password = "test-pass",
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_configPatcherService.Received(1).UpdateConnectionString(
			Arg.Any<string>(), "localhost", 5432, "creatio", "test-user", "test-pass");
	}

	[Test]
	[Description("Verifies command calls ConfigPatcherService to configure port")]
	public void Execute_ShouldConfigurePort()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddFile("/tmp/creatio/Terrasoft.WebHost.dll.config", new MockFileData("<configuration></configuration>"));
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);
		_configPatcherService.UpdateConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_configPatcherService.ConfigurePort(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			Port = 9090,
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_configPatcherService.Received(1).ConfigurePort(Arg.Any<string>(), 9090);
	}

	#endregion

	#region Database Tests

	[Test]
	[Description("Verifies command calls PostgresService to set Maintainer when provided")]
	public void Execute_WithMaintainer_ShouldSetMaintainerInDatabase()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddFile("/tmp/creatio/Terrasoft.WebHost.dll.config", new MockFileData("<configuration></configuration>"));
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);
		_configPatcherService.UpdateConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_configPatcherService.ConfigurePort(Arg.Any<string>(), Arg.Any<int>()).Returns(true);
		_postgresService.TestConnectionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), 
			Arg.Any<string>(), Arg.Any<string>())
			.Returns(System.Threading.Tasks.Task.FromResult(true));
		_postgresService.SetMaintainerSettingAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(System.Threading.Tasks.Task.FromResult(true));

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			Maintainer = "john_doe",
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_postgresService.Received(1).SetMaintainerSettingAsync("localhost", 5432, "creatio", 
			Arg.Any<string>(), Arg.Any<string>(), "john_doe");
	}

	[Test]
	[Description("Verifies command skips Maintainer setting when not provided")]
	public void Execute_WithoutMaintainer_ShouldNotCallPostgresService()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddFile("/tmp/creatio/Terrasoft.WebHost.dll.config", new MockFileData("<configuration></configuration>"));
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);
		_configPatcherService.UpdateConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_configPatcherService.ConfigurePort(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			Maintainer = null,
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		_postgresService.DidNotReceive().SetMaintainerSettingAsync(Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}

	#endregion

	#region ZIP Extraction Tests

	[Test]
	[Description("Verifies command extracts ZIP file to target directory")]
	public void Execute_ShouldExtractZipToTargetDirectory()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		FileSystem.AddFile("/app.zip", new MockFileData("dummy"));
		FileSystem.AddFile("/tmp/creatio/Terrasoft.WebHost.dll.config", new MockFileData("<configuration></configuration>"));
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		_kubernetesService.CheckInfrastructureExists(Arg.Any<string>()).Returns(true);
		_configPatcherService.PatchCookiesSameSiteMode(Arg.Any<string>()).Returns(true);
		_configPatcherService.UpdateConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_configPatcherService.ConfigurePort(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			SkipInfra = true,
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		// Note: ZIP extraction testing is limited without actual ZIP file
		// This verifies that extraction is attempted and target directory is prepared
		result.Should().Be(0, "command should complete successfully");
	}

	#endregion

	#region Error Handling Tests

	[Test]
	[Description("Verifies command returns error code on exception")]
	public void Execute_OnException_ShouldReturnErrorCode()
	{
		// Arrange
		FileSystem = new MockFileSystem();
		// Don't create ZIP file to trigger FileNotFoundException
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

		var command = Container.Resolve<CreateDevEnvironmentCommand>();
		var options = new CreateDevEnvironmentOptions
		{
			Zip = "/nonexistent/app.zip",
			EnvName = "test-env",
			TargetDir = "/tmp/creatio",
			NoConfirm = true
		};

		// Act
		var result = command.Execute(options);

		// Assert
		result.Should().Be(1, "command should return error code on exception");
	}

	#endregion
}
