using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using ATF.Repository;
using ATF.Repository.Attributes;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using CreatioModel;
using DocumentFormat.OpenXml.Drawing;
using FluentAssertions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Common.Json;
using Terrasoft.Core;
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
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-manifest.yaml");
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
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-full-manifest-WithoutSchemas.yaml");
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
			= TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-full-manifest.yaml");
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
	
	
	[Test(Description = "Validate that we can mock from OData Json response and get all SysSettings with values.")]
	public void GetAllSysSettingsWithValues_ReturnsMockValues(){

		//Arrange
		var container = GetContainer();
		// Getting SysSettingsManager from the container, we real deps but mock data provider
		ISysSettingsManager sysSettingsManager = container.Resolve<ISysSettingsManager>();
		
		//Act
		List<SysSettings> settings = sysSettingsManager.GetAllSysSettingsWithValues();
		
		//Assert
		settings.Should().HaveCount(434);
		settings.Any(s => s.ValueTypeName == "Binary").Should().BeFalse();
		var sysettingsValueCount = 0;
		foreach(var setting in settings) {
			sysettingsValueCount += setting.SysSettingsValues.Count;
		}
		sysettingsValueCount.Should().Be(430);
	}


	[Test(Description = "Validate that we can mock from OData Json response and get all SysSettings with values.")]
	public void SaveSysSettingsToManifest() {

		//Arrange
		var container = GetContainer();
		// Getting SysSettingsManager from the container, we real deps but mock data provider
		ISysSettingsManager sysSettingsManager = container.Resolve<ISysSettingsManager>();

		//Act
		List<SysSettings> settings = sysSettingsManager.GetAllSysSettingsWithValues();

		//Assert
		settings.Should().HaveCount(434);
		settings.Any(s => s.ValueTypeName == "Binary").Should().BeFalse();
		var sysettingsValueCount = 0;
		foreach (var setting in settings) {
			sysettingsValueCount += setting.SysSettingsValues.Count;
		}
		sysettingsValueCount.Should().Be(430);
	}

	private IContainer GetContainer() {
		var dataProviderMock = GetMockSysSettingsData();
		MockFileSystem mockFileSystem = TestFileSystem.MockFileSystem();
		BindingsModule bm = new(mockFileSystem);
		EnvironmentSettings environmentSettings = new() {
			Uri = "http://localhost",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = false
		};
		IContainer container = bm.Register(
		settings: environmentSettings,
		registerNullSettingsForTest: false,
		additionalRegistrations: builder => {
			builder.RegisterInstance(dataProviderMock).As<IDataProvider>();
		});
		return container;
	}

	private DataProviderMock GetMockSysSettingsData() {
		DataProviderMock dataProviderMock = new();
		List<ODataResponse> responses = GetOdataResponses("odata_data_examples");
		List<Dictionary<string, object>> mockSysSettingsRecords = responses
			.Where(r => r.SchemaName == "SysSettings")
			.SelectMany(r => r.Records)
			.Where(s => s["ValueTypeName"].ToString() != "Binary")
			.ToList();

		List<Dictionary<string, object>> mockSysSettingsValueRecords = responses
			.Where(r => r.SchemaName == "SysSettingsValue")
			.SelectMany(r => r.Records)
			.ToList();

		MockSysSettingsItems("SysSettings", dataProviderMock, mockSysSettingsRecords);
		MockSysSettingsValueItems("SysSettingsValue", dataProviderMock, mockSysSettingsValueRecords);
		

		return dataProviderMock;
		// Let's create a real container but with mock Items, see additionalRegistrations
		// Autofac returns last registration, so we can override the real data provider with the mock one
		
	}

	private List<ODataResponse> GetOdataResponses(string folderName) {
		var files = _fileSystem.Directory.GetFiles(folderName);
		List<ODataResponse> odataResponses = new();
		foreach (var file in files) {
			var content = _fileSystem.File.ReadAllText(file);
			var oDataResponse = ParseOdataResponse(content);
			odataResponses.Add(oDataResponse);
		}
		return odataResponses;
	}
	
	private void MockSysSettingsItems(string schemaName,DataProviderMock dataProviderMock, List<Dictionary<string, object>> records){
		IItemsMock mock = dataProviderMock.MockItems(schemaName);
		mock.FilterHas("Binary");
		
		// We don't have a way to get the type of the model from the schemaName
		// I decided to use reflection to get the type of the model. There is a better way but its mych longer
		SysSettings sysSettings = new ();
		
		//We need to convert records odata collection into collection of expected Types
		foreach(Dictionary<string, object> record in records) {
			foreach(string key in record.Keys) {
				
				//We also need to make sure that when OData feed missing propertyValue,
				// for instance ReferenceSchemaUId, then we either remove it from the model or set it to default value
				// in case we do nothing it throws an exception, because its casing null into Guid.
				// For now I simply commented out the ReferenceSchemaUId property in SysSettings models
				PropertyInfo p = sysSettings.GetType().GetProperty(key);
				if(p is null) {
					record.Remove(key);
					continue;
				}
				
				if (p.PropertyType.IsAssignableFrom(typeof(string)))
				{
					record[key] = record[key].ToString();
				}
				else if (p.PropertyType.IsAssignableFrom(typeof(Guid)))
				{
					bool isGuid = Guid.TryParse(record[key].ToString(), out Guid value);
					if(isGuid) {
						record[key] = value;
					}else {
						record[key] = Guid.Empty;
					}
				}
				else if (p.PropertyType.IsAssignableFrom(typeof(bool)))
				{
					record[key]= bool.Parse(record[key].ToString() ?? "False");
				}
				else if (p.PropertyType.IsAssignableFrom(typeof(int)))
				{
					record[key] = int.Parse(record[key].ToString() ?? "0");
				}
				else if (p.PropertyType.IsAssignableFrom(typeof(DateTime)))
				{
					record[key] =  DateTime.Parse(record[key].ToString() ?? "1970-01-01T00:00:0.000000Z");
				}
				else if (p.PropertyType.IsAssignableFrom(typeof(decimal)))
				{
					record[key] =  decimal.Parse(record[key].ToString() ?? "0.00");
				}
				else if (p.PropertyType.IsAssignableFrom(typeof(float)))
				{
					record[key] =  float.Parse(record[key].ToString() ?? "0.00");
				}
			}
		}
		mock.Returns(records);
	}

	private void MockSysSettingsValueItems(string schemaName, DataProviderMock dataProviderMock, List<Dictionary<string, object>> records) {
		IItemsMock mock = dataProviderMock.MockItems(schemaName);

		// We don't have a way to get the type of the model from the schemaName
		// I decided to use reflection to get the type of the model. There is a better way but its mych longer
		SysSettingsValue sysSettingsValue = new();

		//We need to convert records odata collection into collection of expected Types
		List<Dictionary<string, object>> resultRecords = new();
		foreach (Dictionary<string, object> record in records) {
			var resultRecord = new Dictionary<string, object>();
			foreach (string key in record.Keys) {

				//We also need to make sure that when OData feed missing propertyValue,
				// for instance ReferenceSchemaUId, then we either remove it from the model or set it to default value
				// in case we do nothing it throws an exception, because its casing null into Guid.
				// For now I simply commented out the ReferenceSchemaUId property in SysSettings models
				PropertyInfo p = sysSettingsValue.GetType().GetProperty(key);
				if (p is null) {
					record.Remove(key);
					continue;
				}

				if (p.PropertyType.IsAssignableFrom(typeof(string))) {
					record[key] = record[key].ToString();
				} else if (p.PropertyType.IsAssignableFrom(typeof(Guid))) {
					bool isGuid = Guid.TryParse(record[key].ToString(), out Guid value);
					if (isGuid) {
						record[key] = value;
					} else {
						record[key] = Guid.Empty;
					}
				} else if (p.PropertyType.IsAssignableFrom(typeof(bool))) {
					record[key] = bool.Parse(record[key].ToString() ?? "False");
				} else if (p.PropertyType.IsAssignableFrom(typeof(int))) {
					record[key] = int.Parse(record[key].ToString() ?? "0");
				} else if (p.PropertyType.IsAssignableFrom(typeof(DateTime))) {
					record[key] = DateTime.Parse(record[key].ToString() ?? "1970-01-01T00:00:0.000000Z");
				} else if (p.PropertyType.IsAssignableFrom(typeof(decimal))) {
					record[key] = decimal.Parse(record[key].ToString() ?? "0.00");
				} else if (p.PropertyType.IsAssignableFrom(typeof(float))) {
					record[key] = float.Parse(record[key].ToString() ?? "0.00");
				}
				
				IEnumerable<CustomAttributeData> customAttributes = p.CustomAttributes;
				var c = customAttributes
					.FirstOrDefault(c=> c.AttributeType == typeof(SchemaPropertyAttribute));
				if (c!= null) {
					var entitySchemaColumnName = c.ConstructorArguments[0].Value.ToString();
					resultRecord[entitySchemaColumnName] = record[key];
				}
			}
			
			resultRecords.Add(resultRecord);
		}
		mock.Returns(resultRecords);
		//mock.Returns(records);
	}
}