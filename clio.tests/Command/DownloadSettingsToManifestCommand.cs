using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using CreatioModel;
using FluentAssertions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Common.Json;
using Terrasoft.Core.Entities;
using YamlDotNet.Serialization;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
internal class SaveSettingsToManifestCommandTest : BaseCommandTests<SaveSettingsToManifestOptions>
{
	protected override MockFileSystem CreateFs() {
		var mockFS = base.CreateFs();
		mockFS.MockExamplesFolder("odata_data_examples", "odata_data_examples");
		return mockFS;
	}

	[Test]
	public void SaveWebServiceToFile() {

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
			_container.Resolve<Clio.Common.IFileSystem>(), _container.Resolve<ISerializer>(), webServiceManagerMock, _container.Resolve<IEnvironmentManager>());

		//Act
		command.Execute(saveSettingsToManifestOptions);


		//Assert
		_fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
		string expectedContent
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expectedCreatioManifestValue-saved-manifest.yaml");
		_fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
			.Be(expectedContent.Trim());

		loggerMock.Received(1).WriteInfo("Done");
	}

	[Test]
	public void SaveSysSettingsFromEnvironmentToFile() {

		// mock syssettings from odata files
		var appUrl = "";
		var login = "";
		var password = "";
		var remoteDataProvider = new RemoteDataProvider(appUrl, login, password);
		//MockDataFromFolder(providerMock, "odata_data_examples");
		SysSettingsManager sysSettingsManager = new(remoteDataProvider);
		var sysSettingsWithValues = sysSettingsManager.GetAllSysSettingsWithValues();
		Assert.NotNull(sysSettingsWithValues);
		Assert.NotZero(sysSettingsWithValues.Count);

		// get syssettings from syssettingsmanager and convert to manifest setting

		// save manifest settings

		// read manu=ifest settings and compare with expected syssettings value from origin odata files		
	}


	[TestCase(true)]
	[TestCase(false)]
	public void SaveEnvironmentSettingsEmptyPackagesToFile(bool accending) {

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

		DataProviderMock providerMock = new();
		MockSysPackage(providerMock, accending);

		IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
		webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

		ILogger loggerMock = Substitute.For<ILogger>();

		SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
			_container.Resolve<Clio.Common.IFileSystem>(), _container.Resolve<ISerializer>(), webServiceManagerMock, _container.Resolve<IEnvironmentManager>());

		//Act
		command.Execute(saveSettingsToManifestOptions);

		//Assert
		_fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
		string expectedContent
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expectedCreatioManifestValue-saved-full-manifest-WithoutSchemas.yaml");
		_fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
			.Be(expectedContent.Trim());

		loggerMock.Received(1).WriteInfo("Done");
	}

	[TestCase(true, true)]
	[TestCase(false, false)]
	[TestCase(false, true)]
	[TestCase(true, false)]
	public void SaveEnvironmentSettingsPackagesWithSchemasToFile(bool packageAccending, bool schemaAccending) {

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

		DataProviderMock providerMock = new();
		MockSysPackage(providerMock, packageAccending, true, schemaAccending);
		

		IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
		webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

		ILogger loggerMock = Substitute.For<ILogger>();

		SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
			_container.Resolve<Clio.Common.IFileSystem>(), _container.Resolve<ISerializer>(), webServiceManagerMock, _container.Resolve<IEnvironmentManager>());

		//Act
		command.Execute(saveSettingsToManifestOptions);

		//Assert
		_fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
		string expectedContent
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expectedCreatioManifestValue-saved-full-manifest.yaml");
		_fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
			.Be(expectedContent.Trim());

		loggerMock.Received(1).WriteInfo("Done");
	}

	private void MockSysPackage(DataProviderMock providerMock, bool packageAccending, bool withSchemas = false, bool schemaAccending = false) {
		var mock = providerMock.MockItems(nameof(SysPackage));
		Guid packageId1 = Guid.NewGuid();
		Guid packageId2 = Guid.NewGuid();
		var list = new List<Dictionary<string, object>>() {
			new Dictionary<string, object>() {
				{"Id", packageId1},
				{"Name", "CrtBase"},
				{"ModifiedOn", new DateTime(2024,5,10, 0, 0, 0, DateTimeKind.Utc)}
			},
			new Dictionary<string, object>() {
				{"Id", packageId2},
				{"Name", "CrtUI"},
				{"ModifiedOn", new DateTime(2024,5,10, 0, 0, 0, DateTimeKind.Utc)}
			}
		};
		if (!packageAccending) {
			list.Reverse();
		}
		mock.Returns(list);
		if (withSchemas) {
			MockSysSchemasForPackage(packageId1, providerMock, 2, schemaAccending);
			MockSysSchemasForPackage(packageId2, providerMock, 2, schemaAccending);
		}

	}

	private void MockSysSchemasForPackage(Guid packageId, DataProviderMock providerMock, int count, bool schemaAccending) {
		var mock = providerMock.MockItems(nameof(SysSchema));
		var list = new List<Dictionary<string, object>>() {
			new Dictionary<string, object>() {
				{"Id", Guid.NewGuid()},
				{"Name", "Contact"},
				{"ModifiedOn", new DateTime(2024,5,10, 0, 0, 0, DateTimeKind.Utc)},
				{"SysPackageId", packageId },
				{"Checksum", "ContactHash" },
				{"UId", Guid.Parse("DE86FE03-3508-4F94-A50E-34A335B1F9F2") }
			},
			new Dictionary<string, object>() {
				{"Id", Guid.NewGuid()},
				{"Name", "Account"},
				{"ModifiedOn", new DateTime(2024,5,10, 0, 0, 0, DateTimeKind.Utc)},
				{"SysPackageId", packageId },
				{"Checksum", "AccountHash" },
				{"UId", Guid.Parse("DE86FE03-3508-4F94-A50E-34A335B1F9F3") }
			}
		};
		mock.FilterHas(packageId);
		if (!schemaAccending) {
			list.Reverse();
		}
		mock.Returns(list);
	}
	private void MockDataFromFolder(DataProviderMock providerMock, string folderName) {
		var files = _fileSystem.Directory.GetFiles(folderName);
		foreach(var file in files) {
			var content = _fileSystem.File.ReadAllText(file);
			var oDataResponse = ParseOdataResponse(content);
			var mock = providerMock.MockItems(oDataResponse.SchemaName);
			var list = new List<Dictionary<string, object>>();
			foreach (var record in oDataResponse.Records) {
				list.Add(record);
			}
			mock.Returns(list);
		}
	}

	private ODataResponse ParseOdataResponse(string content) {
		return Json.Deserialize<ODataResponse>(content);
	}

	[Test]
	public void TestParseOdataResponse() {
		var odataFoldferName = "odata_data_examples";
		var files = _fileSystem.Directory.GetFiles(odataFoldferName);
		List<ODataResponse> odataResponses = new();
		foreach (var file in files) {
			var content = _fileSystem.File.ReadAllText(file);
			var oDataResponse = ParseOdataResponse(content);
			odataResponses.Add(oDataResponse);
		}
		Assert.AreEqual(odataResponses.Count, files.Length);
		Assert.IsTrue(odataResponses.Any(x => x.SchemaName == "SysSettings"));
		Assert.IsTrue(odataResponses.Any(x => x.SchemaName == "SysSettingsValue"));
	}
}