using Autofac;
using Clio.Command;
using Clio.Tests.Command;
using Clio.Tests.Common;
using Clio.Tests.Extensions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;

namespace Clio.Tests
{
	[TestFixture]
	internal class CommonProgramTest: BaseClioModuleTests
	{
		[Test]
		public void ApplyManifestOptionsTest() {
			var optionsFromFile = new EnvironmentOptions() {
				Uri = "http://file"
			};
			var optionsFromCommandLine = new EnvironmentOptions() {
				Uri = "http://commandline"
			};
			var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
			resultOptions.Uri.Should().Be("http://commandline");
		}

		[Test]
		public void ApplyManifestOptionsOnlyFromFileTest() {
			var optionsFromFile = new EnvironmentOptions {
				Uri = "http://file",
				Login = "fileLogin",
				Password = "filePassword",
				ClientId = "fileClientId",
				IsNetCore = true,
				ClientSecret = "fileClientSecret",
				AuthAppUri = "fileAuthAppUri",
			};
			var optionsFromCommandLine = new EnvironmentOptions();
			var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
			optionsFromFile.Uri.Should().Be(resultOptions.Uri);
			optionsFromFile.Login.Should().Be(resultOptions.Login);
			optionsFromFile.Password.Should().Be(resultOptions.Password);
			optionsFromFile.ClientId.Should().Be(resultOptions.ClientId);
			optionsFromFile.ClientSecret.Should().Be(resultOptions.ClientSecret);
			optionsFromFile.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
			optionsFromFile.IsNetCore.Should().Be(resultOptions.IsNetCore);
		}

		[Test]
		public void ApplyEnvManifestOptionsTest() {
			var optionsFromFile = new EnvironmentOptions() {
				Uri = "http://file",
				Login = "fileLogin",
				Password = "filePassword",
				ClientId = "fileClientId",
				IsNetCore = true,
				ClientSecret = "fileClientSecret",
				AuthAppUri = "fileAuthAppUri",
			};
			var optionsFromCommandLine = new EnvironmentOptions() {
				Environment = "myEnv"
			};
			var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
			optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
			optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
			optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
			optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
			optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
			optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
		}

        [Test]
        public void ApplyEnvManifestOptionsWhenOptionInFileNullTest() {
			EnvironmentOptions optionsFromFile = null;
            var optionsFromCommandLine = new EnvironmentOptions() {
                Environment = "myEnv"
            };
            var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
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
            var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
			resultOptions.Should().BeNull();
        }

        [Test]
        public void ApplyEnvManifestOptionsWhenOptionInFileNullAndCommandLineIsEmpty() {
            EnvironmentOptions optionsFromFile = null;
			var optionsFromCommandLine = new EnvironmentOptions();
            var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
			optionsFromCommandLine.Uri.Should().Be(resultOptions.Uri);
			optionsFromCommandLine.Login.Should().Be(resultOptions.Login);
			optionsFromCommandLine.Password.Should().Be(resultOptions.Password);
			optionsFromCommandLine.ClientId.Should().Be(resultOptions.ClientId);
			optionsFromCommandLine.ClientSecret.Should().Be(resultOptions.ClientSecret);
			optionsFromCommandLine.AuthAppUri.Should().Be(resultOptions.AuthAppUri);
        }

        [Test]
		public void ReadEnvironmentOptionsFromManifestFile() {
			FileSystem.MockExamplesFolder("deployments-manifest");
			var manifestFileName = "full-creatio-config.yaml";
			var environmentManager = Container.Resolve<IEnvironmentManager>();
			var manifestFilePath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), manifestFileName);
			EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
			var commonFileSystem = new Clio.Common.FileSystem(FileSystem);
			var environmentOptionsFromFile = Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
			envSettingsFromFile.Uri.Should().Be(environmentOptionsFromFile.Uri);
			envSettingsFromFile.Login.Should().Be(environmentOptionsFromFile.Login);
			envSettingsFromFile.Password.Should().Be(environmentOptionsFromFile.Password);
		}

        [Test]
        public void ReadEnvironmentOptionsFromOnlySettingsManifestFile() {
            FileSystem.MockExamplesFolder("deployments-manifest");
            var manifestFileName = "only-settings.yaml";
            var environmentManager = Container.Resolve<IEnvironmentManager>();
            var manifestFilePath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), manifestFileName);
            EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
            var commonFileSystem = new Clio.Common.FileSystem(FileSystem);
            var environmnetOptionsFromFile = Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
			environmnetOptionsFromFile.Should().BeNull();
        }

		[Test]
		public void IsCfgOpenCommand_WithCfgOpenArguments_ShouldBeTrue()
		{
			// Arrange
			string[] args = new[] { "cfg", "open" };
			Program.IsCfgOpenCommand = false; // Reset the value before the test
			
			// Act
			Program.Main(args);
			
			// Assert
			Program.IsCfgOpenCommand.Should().BeTrue("because 'cfg' and 'open' arguments were provided");
		}

		[Test]
		public void IsCfgOpenCommand_WithEmptyArguments_ShouldBeFalse()
		{
			// Arrange
			string[] args = new string[0];
			Program.IsCfgOpenCommand = false; // Reset the value before the test
			
			// Act
			Program.Main(args);
			
			// Assert
			Program.IsCfgOpenCommand.Should().BeFalse("because no arguments were provided");
		}

		[Test]
		public void IsCfgOpenCommand_WithOnlyCfgArgument_ShouldBeFalse()
		{
			// Arrange
			string[] args = new[] { "cfg" };
			Program.IsCfgOpenCommand = false; // Reset the value before the test
			
			// Act
			Program.Main(args);
			
			// Assert
			Program.IsCfgOpenCommand.Should().BeFalse("because only 'cfg' argument was provided without 'open'");
		}

		[Test]
		public void IsCfgOpenCommand_WithDifferentArguments_ShouldBeFalse()
		{
			// Arrange
			string[] args = new[] { "other", "command" };
			Program.IsCfgOpenCommand = false; // Reset the value before the test
			
			// Act
			Program.Main(args);
			
			// Assert
			Program.IsCfgOpenCommand.Should().BeFalse("because different arguments were provided instead of 'cfg' and 'open'");
		}

		[Test]
		public void IsCfgOpenCommand_WithCfgAndDifferentSubcommand_ShouldBeFalse()
		{
			// Arrange
			string[] args = new[] { "cfg", "other" };
			Program.IsCfgOpenCommand = false; // Reset the value before the test
			
			// Act
			Program.Main(args);
			
			// Assert
			Program.IsCfgOpenCommand.Should().BeFalse("because 'cfg' was provided with a subcommand different from 'open'");
		}
    }
}
