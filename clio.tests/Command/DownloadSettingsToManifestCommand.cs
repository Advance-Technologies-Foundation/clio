using Autofac;
using Clio.Command;
using Clio.Tests.Infrastructure;
using NUnit.Framework;
using FluentAssertions;
using Clio.Common;
using YamlDotNet.Serialization;

namespace Clio.Tests.Command
{
	[TestFixture]
	internal class SaveSettingsToManifestCommandTest:BaseCommandTests<SaveSettingsToManifestOptions>
	{
		System.IO.Abstractions.IFileSystem _fileSystem = TestFileSystem.MockFileSystem();
		IContainer _container;

		[SetUp]
		public void Setup() {

			var bindingModule = new BindingsModule(_fileSystem);
			_container = bindingModule.Register();
		}

		[Test]
		public void SaveWebServiceToFile() {

			SaveSettingsToManifestOptions saveSettingsToManifestOptions = new SaveSettingsToManifestOptions() { 
				ManifestFileName = @"web-service-manifest.yaml"
			};
			SaveSettingsToManifestCommand command = new SaveSettingsToManifestCommand(null, null, _container.Resolve<IFileSystem>(), _container.Resolve<ISerializer>());
			command.Execute(saveSettingsToManifestOptions);
			_fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
			var expectedContent = TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-manifest.yaml");
			_fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should().Be(expectedContent.Trim());
		}
	}
}
