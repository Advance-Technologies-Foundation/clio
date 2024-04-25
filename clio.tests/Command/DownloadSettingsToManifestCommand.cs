using System.Collections.Generic;
using ATF.Repository.Mock;
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

		//Arrange
		SaveSettingsToManifestOptions saveSettingsToManifestOptions = new() {
			ManifestFileName = @"web-service-manifest.yaml"
		};
		List<CreatioManifestWebService> webServices = [
			new CreatioManifestWebService {
				Name = "Creatio",
				Url = "https://creatio.com"
			},

			new CreatioManifestWebService {
				Name = "Google",
				Url = "https://google.ca"
			}
		];
		IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
		webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

		DataProviderMock providerMock = new();
		ILogger loggerMock = Substitute.For<ILogger>();
		
		SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
			_container.Resolve<Clio.Common.IFileSystem>(), _container.Resolve<ISerializer>(), webServiceManagerMock);
		
		//Act
		command.Execute(saveSettingsToManifestOptions);


		//Assert
		_fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
		string expectedContent
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-manifest.yaml");
		_fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
			.Be(expectedContent.Trim());
		
		loggerMock.Received(1).WriteInfo("Done");
	}

}