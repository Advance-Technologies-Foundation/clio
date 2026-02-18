using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class CommonProgramTest : BaseClioModuleTests{
	#region Methods: Public

	[Test]
	public void ApplyEnvManifestOptionsTest() {
		EnvironmentOptions optionsFromFile = new() {
			Uri = "http://file",
			Login = "fileLogin",
			Password = "filePassword",
			ClientId = "fileClientId",
			IsNetCore = true,
			ClientSecret = "fileClientSecret",
			AuthAppUri = "fileAuthAppUri"
		};
		EnvironmentOptions optionsFromCommandLine = new() {
			Environment = "myEnv"
		};
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
		optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
		optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
		optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
	}

	[Test]
	public void ApplyEnvManifestOptionsWhenOptionInFileNullAndCommandLineIsEmpty() {
		EnvironmentOptions optionsFromFile = null;
		EnvironmentOptions optionsFromCommandLine = new();
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
		optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
		optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
		optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
	}

	[Test]
	public void ApplyEnvManifestOptionsWhenOptionInFileNullAndCommandLineOptionsIsNullTest() {
		EnvironmentOptions optionsFromFile = null;
		EnvironmentOptions optionsFromCommandLine = null;
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		resultOptions.Should().BeNull();
	}

	[Test]
	public void ApplyEnvManifestOptionsWhenOptionInFileNullTest() {
		EnvironmentOptions optionsFromFile = null;
		EnvironmentOptions optionsFromCommandLine = new() {
			Environment = "myEnv"
		};
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
		optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
		optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
		optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
	}

	[Test]
	public void ApplyManifestOptionsOnlyFromFileTest() {
		EnvironmentOptions optionsFromFile = new() {
			Uri = "http://file",
			Login = "fileLogin",
			Password = "filePassword",
			ClientId = "fileClientId",
			IsNetCore = true,
			ClientSecret = "fileClientSecret",
			AuthAppUri = "fileAuthAppUri"
		};
		EnvironmentOptions optionsFromCommandLine = new();
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		optionsFromFile.Uri.Should().Be(resultOptions.Uri);
		optionsFromFile.Login.Should().Be(resultOptions.Login);
		optionsFromFile.Password.Should().Be(resultOptions.Password);
		optionsFromFile.ClientId.Should().Be(resultOptions.ClientId);
		optionsFromFile.ClientSecret.Should().Be(resultOptions.ClientSecret);
		optionsFromFile.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
		optionsFromFile.IsNetCore.Should().Be(resultOptions.IsNetCore);
	}

	[Test]
	public void ApplyManifestOptionsTest() {
		EnvironmentOptions optionsFromFile = new() {
			Uri = "http://file"
		};
		EnvironmentOptions optionsFromCommandLine = new() {
			Uri = "http://commandline"
		};
		EnvironmentOptions resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
		resultOptions.Uri.Should().Be("http://commandline");
	}

	[Test]
	public void IsCfgOpenCommand_WithCfgAndDifferentSubcommand_ShouldBeFalse() {
		// Arrange
		string[] args = ["cfg", "other"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeFalse("because 'cfg' was provided with a subcommand different from 'open'");
	}

	[Test]
	public void IsCfgOpenCommand_WithCfgOpenArguments_ShouldBeTrue() {
		// Arrange
		string[] args = new[] { "cfg", "open" };
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeTrue("because 'cfg' and 'open' arguments were provided");
	}

	[Test]
	public void IsCfgOpenCommand_WithDifferentArguments_ShouldBeFalse() {
		// Arrange
		string[] args = ["other", "command"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should()
			   .BeFalse("because different arguments were provided instead of 'cfg' and 'open'");
	}

	[Test]
	public void IsCfgOpenCommand_WithEmptyArguments_ShouldBeFalse() {
		// Arrange
		string[] args = [];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeFalse("because no arguments were provided");
	}

	[Test]
	public void IsCfgOpenCommand_WithOnlyCfgArgument_ShouldBeFalse() {
		// Arrange
		string[] args = ["cfg"];
		Program.IsCfgOpenCommand = false; // Reset the value before the test

		// Act
		Program.Main(args);

		// Assert
		Program.IsCfgOpenCommand.Should().BeFalse("because only 'cfg' argument was provided without 'open'");
	}

	[Test]
	public void ReadEnvironmentOptionsFromManifestFile() {
		FileSystem.MockExamplesFolder("deployments-manifest");
		string manifestFileName = "full-creatio-config.yaml";
		IEnvironmentManager environmentManager = Container.Resolve<IEnvironmentManager>();
		string manifestFilePath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), manifestFileName);
		EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
		FileSystem commonFileSystem = new(FileSystem);
		EnvironmentOptions environmentOptionsFromManifestFile
			= Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
		envSettingsFromFile.Uri.Should().Be(environmentOptionsFromManifestFile.Uri);
		envSettingsFromFile.Login.Should().Be(environmentOptionsFromManifestFile.Login);
		envSettingsFromFile.Password.Should().Be(environmentOptionsFromManifestFile.Password);
	}

	[Test]
	public void ReadEnvironmentOptionsFromOnlySettingsManifestFile() {
		FileSystem.MockExamplesFolder("deployments-manifest");
		string manifestFileName = "only-settings.yaml";
		IEnvironmentManager environmentManager = Container.Resolve<IEnvironmentManager>();
		string manifestFilePath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), manifestFileName);
		EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
		FileSystem commonFileSystem = new(FileSystem);
		EnvironmentOptions environmentOptionsFromManifestFile
			= Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
		environmentOptionsFromManifestFile.Should().BeNull();
	}

	#endregion
}
