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
			Assert.AreEqual("http://commandline", resultOptions.Uri);
		}

		[Test]
		public void ApplyManifestOptionsOnlyFromFileTest() {
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
			};
			var resultOptions = Program.CombinedOption(optionsFromFile, optionsFromCommandLine);
			Assert.AreEqual(optionsFromFile.Uri, resultOptions.Uri);
			Assert.AreEqual(optionsFromFile.Login, resultOptions.Login);
			Assert.AreEqual(optionsFromFile.Password, resultOptions.Password);
			Assert.AreEqual(optionsFromFile.ClientId, resultOptions.ClientId);
			Assert.AreEqual(optionsFromFile.ClientSecret, resultOptions.ClientSecret);
			Assert.AreEqual(optionsFromFile.AuthAppUri, resultOptions.AuthAppUri);
			Assert.AreEqual(optionsFromFile.IsNetCore, resultOptions.IsNetCore);
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
			Assert.AreEqual(optionsFromCommandLine.Uri, resultOptions.Uri);
			Assert.AreEqual(optionsFromCommandLine.Login, resultOptions.Login);
			Assert.AreEqual(optionsFromCommandLine.Password, resultOptions.Password);
			Assert.AreEqual(optionsFromCommandLine.ClientId, resultOptions.ClientId);
			Assert.AreEqual(optionsFromCommandLine.ClientSecret, resultOptions.ClientSecret);
			Assert.AreEqual(optionsFromCommandLine.AuthAppUri, resultOptions.AuthAppUri);
		}

		[Test]
		public void ReadEnvironmentOptionsFromManifestFile() {
			_fileSystem.MockExamplesFolder("deployments-manifest");
			var manifestFileName = "full-creatio-config.yaml";
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			EnvironmentSettings envSettingsFromFile = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
			var commonFileSystem = new Clio.Common.FileSystem(_fileSystem);
			var environmnetOptionsFromFile = Program.ReadEnvironmentOptionsFromManifestFile(manifestFilePath, commonFileSystem);
			Assert.AreEqual(envSettingsFromFile.Uri, environmnetOptionsFromFile.Uri);
			Assert.AreEqual(envSettingsFromFile.Login, environmnetOptionsFromFile.Login);
			Assert.AreEqual(envSettingsFromFile.Password, environmnetOptionsFromFile.Password);
		}

	}
}
