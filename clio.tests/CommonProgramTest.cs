using Autofac;
using Clio.Command;
using Clio.Tests.Command;
using Clio.Tests.Common;
using Clio.Tests.Extensions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
			_fileSystem.MockExamplesFolder("deployments-manifest");
			var manifestFileName = "full-creatio-config.yaml";
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
			var commonFileSystem = new Clio.Common.FileSystem(_fileSystem);
			var environmentOptionsFromFile = Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
			envSettingsFromFile.Uri.Should().Be(environmentOptionsFromFile.Uri);
			envSettingsFromFile.Login.Should().Be(environmentOptionsFromFile.Login);
			envSettingsFromFile.Password.Should().Be(environmentOptionsFromFile.Password);
		}

        [Test]
        public void ReadEnvironmentOptionsFromOnlySettingsManifestFile() {
            _fileSystem.MockExamplesFolder("deployments-manifest");
            var manifestFileName = "only-settings.yaml";
            var environmentManager = _container.Resolve<IEnvironmentManager>();
            var manifestFilePath = $"C:\\{manifestFileName}";
            EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
            var commonFileSystem = new Clio.Common.FileSystem(_fileSystem);
            var environmnetOptionsFromFile = Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
			environmnetOptionsFromFile.Should().BeNull();
        }
    }
}
