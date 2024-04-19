using System.Collections.Generic;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using YamlDotNet.Serialization;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
internal class SaveSettingsToManifestCommandTest : BaseCommandTests<SaveSettingsToManifestOptions>
{

	#region Setup/Teardown

	[SetUp]
	public void Setup(){
		BindingsModule bindingModule = new(_fileSystem);
		_container = bindingModule.Register();
	}

	#endregion

	#region Fields: Private

	private readonly IFileSystem _fileSystem = TestFileSystem.MockFileSystem();
	private IContainer _container;

	#endregion

	[Test]
	public void SaveWebServiceToFile(){
		SaveSettingsToManifestOptions saveSettingsToManifestOptions = new() {
			ManifestFileName = @"web-service-manifest.yaml"
		};
		List<CreatioManifestWebService> webServices = new List<CreatioManifestWebService> {
			new() {
				Name = "Creatio",
				Url = "https://creatio.com"
			},
			new() {
				Name = "Google",
				Url = "https://google.ca"
			}
		};
		IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
		webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

		SaveSettingsToManifestCommand command = new(null, null,
			_container.Resolve<Clio.Common.IFileSystem>(), _container.Resolve<ISerializer>(), webServiceManagerMock);
		command.Execute(saveSettingsToManifestOptions);
		_fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
		string expectedContent
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-manifest.yaml");
		_fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
			.Be(expectedContent.Trim());
	}

}