using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATF.Repository.Attributes;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests.Command;

[TestFixture]
internal class SaveSettingsToManifestCommandTest : BaseCommandTests<SaveSettingsToManifestOptions>
{
    protected override MockFileSystem CreateFs()
    {
        MockFileSystem mockFS = base.CreateFs();
        mockFS.MockExamplesFolder("odata_data_examples", "odata_data_examples");
        return mockFS;
    }

    [Test]
    public void SaveWebServiceToFile()
    {
        // Arrange
        SaveSettingsToManifestOptions saveSettingsToManifestOptions =
            new() { ManifestFileName = @"web-service-manifest.yaml" };
        List<CreatioManifestWebService> webServices =
        [
            new() { Name = "Creatio", Url = "https://creatio.com" },

            new() { Name = "Google", Url = "https://google.ca" }
        ];
        IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
        webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

        DataProviderMock providerMock = new();
        ILogger loggerMock = Substitute.For<ILogger>();

        SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
            container.Resolve<IFileSystem>(), container.Resolve<ISerializer>(), webServiceManagerMock,
            container.Resolve<IEnvironmentManager>());

        // Act
        command.Execute(saveSettingsToManifestOptions);

        // Assert
        fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
        string expectedContent
            = TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-manifest.yaml");
        fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
            .Be(expectedContent.Trim());

        loggerMock.Received(1).WriteInfo("Done");
    }

    [Test]
    public void SaveSysSettingsFromEnvironmentToFile()
    {
        IContainer container = GetContainer();
        ISysSettingsManager sysSettingsManager = container.Resolve<ISysSettingsManager>();
        List<SysSettings> sysSettingsWithValues = sysSettingsManager.GetAllSysSettingsWithValues();

        sysSettingsWithValues.Should().NotBeNull();
        sysSettingsWithValues.Count.Should().BeGreaterThan(0);
    }

    [TestCase("ShowWidgetOnIntroPage", "true")]
    [TestCase("Maintainer", "Customer")]
    [TestCase("QueryExecutionTimeout", "10")]
    [TestCase("LastAgeActualizationDate", "2024-05-16")]
    [TestCase("AutomaticAgeActualizationTime", "05:30:00")]
    [TestCase("AutomaticAgeActualizationTime", "05:30:00")]
    [TestCase("SyncMemoryLimitToDeallocate", "100.1")]
    [TestCase("PrimaryCurrency", "915e8a55-98d6-df11-9b2a-001d60e938c6")]
    public void SaveSysSettingsToFile(string sysSettingsCode, string sysSettingsStringValue)
    {
        IContainer container = GetContainer();
        IFileSystem fileSystem = container.Resolve<IFileSystem>();

        // Arrange
        SaveSettingsToManifestOptions saveSettingsToManifestOptions =
            new() { ManifestFileName = @"save-syssettings-manifest.yaml" };
        List<CreatioManifestWebService> webServices = [];

        DataProviderMock providerMock = new();

        IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
        webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

        ILogger loggerMock = Substitute.For<ILogger>();

        SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
            fileSystem, container.Resolve<ISerializer>(), webServiceManagerMock,
            container.Resolve<IEnvironmentManager>(),
            container.Resolve<ISysSettingsManager>());

        // Act
        command.Execute(saveSettingsToManifestOptions);

        // Assert
        fileSystem.ExistsFile(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();

        IEnvironmentManager envManager = container.Resolve<IEnvironmentManager>();
        IEnumerable<CreatioManifestSetting> actualSettings =
            envManager.GetSettingsFromManifest(saveSettingsToManifestOptions.ManifestFileName);
        CreatioManifestSetting? settings = actualSettings.FirstOrDefault(s => s.Code == sysSettingsCode);
        sysSettingsStringValue.Should().Be(settings.Value);
        loggerMock.Received(1).WriteInfo("Done");
    }

    public void SaveSysSettingsToFile()
    {
        IContainer container = GetContainer();
        IFileSystem fileSystem = container.Resolve<IFileSystem>();

        // Arrange
        SaveSettingsToManifestOptions saveSettingsToManifestOptions =
            new() { ManifestFileName = @"save-syssettings-manifest.yaml" };
        List<CreatioManifestWebService> webServices = [];

        DataProviderMock providerMock = new();
        MockSysPackage(providerMock, true);

        IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
        webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

        ILogger loggerMock = Substitute.For<ILogger>();

        SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
            fileSystem, container.Resolve<ISerializer>(), webServiceManagerMock,
            container.Resolve<IEnvironmentManager>(),
            container.Resolve<ISysSettingsManager>());

        // Act
        command.Execute(saveSettingsToManifestOptions);

        // Assert
        fileSystem.ExistsFile(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
        string expectedContent
            = TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-full-manifest.yaml");

        IEnvironmentManager envManager = container.Resolve<IEnvironmentManager>();
        IEnumerable<CreatioManifestSetting> actualSettings =
            envManager.GetSettingsFromManifest(saveSettingsToManifestOptions.ManifestFileName);
        actualSettings.Should().NotBeNull();
        actualSettings.Count().Should().Be(434);
        loggerMock.Received(1).WriteInfo("Done");
    }

    private IContainer GetContainer() => MockDataContainer.GetContainer(fileSystem);

    [TestCase(true)]
    [TestCase(false)]
    public void SaveEnvironmentSettingsEmptyPackagesToFile(bool accending)
    {
        // Arrange
        SaveSettingsToManifestOptions saveSettingsToManifestOptions =
            new() { ManifestFileName = @"web-service-manifest.yaml" };
        List<CreatioManifestWebService> webServices =
        [
            new() { Name = "Creatio", Url = "https://creatio.com" },

            new() { Name = "Google", Url = "https://google.ca" }
        ];

        DataProviderMock providerMock = new();
        MockSysPackage(providerMock, accending);

        IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
        webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

        ILogger loggerMock = Substitute.For<ILogger>();

        SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
            container.Resolve<IFileSystem>(), container.Resolve<ISerializer>(), webServiceManagerMock,
            container.Resolve<IEnvironmentManager>());

        // Act
        command.Execute(saveSettingsToManifestOptions);

        // Assert
        fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
        string expectedContent
            = TestFileSystem.ReadExamplesFile(
                "deployments-manifest",
                "expected-saved-full-manifest-WithoutSchemas.yaml");
        fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim().Should()
            .Be(expectedContent.Trim());

        loggerMock.Received(1).WriteInfo("Done");
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void SaveEnvironmentSettingsPackagesWithSchemasToFile(bool packageAccending, bool schemaAccending)
    {
        // Arrange
        SaveSettingsToManifestOptions saveSettingsToManifestOptions =
            new() { ManifestFileName = @"web-service-manifest.yaml" };
        List<CreatioManifestWebService> webServices =
        [
            new() { Name = "Creatio", Url = "https://creatio.com" },

            new() { Name = "Google", Url = "https://google.ca" }
        ];

        DataProviderMock providerMock = new();
        MockSysPackage(providerMock, packageAccending, true, schemaAccending);

        IWebServiceManager webServiceManagerMock = Substitute.For<IWebServiceManager>();
        webServiceManagerMock.GetCreatioManifestWebServices().Returns(webServices);

        ILogger loggerMock = Substitute.For<ILogger>();

        SaveSettingsToManifestCommand command = new(providerMock, loggerMock,
            container.Resolve<IFileSystem>(), container.Resolve<ISerializer>(), webServiceManagerMock,
            container.Resolve<IEnvironmentManager>());

        // Act
        command.Execute(saveSettingsToManifestOptions);

        // Assert
        fileSystem.File.Exists(saveSettingsToManifestOptions.ManifestFileName).Should().BeTrue();
        string expectedContent
            = TestFileSystem.ReadExamplesFile("deployments-manifest", "expected-saved-full-manifest.yaml");
        string actualContent = fileSystem.File.ReadAllText(saveSettingsToManifestOptions.ManifestFileName).Trim();
        actualContent.Should()
            .Be(expectedContent.Trim());

        loggerMock.Received(1).WriteInfo("Done");
    }

    [Test]
    public void TestFormatDateTime()
    {
        DateTime dateTime = new(2024, 12, 10, 0, 0, 0, DateTimeKind.Utc);
        string expectedString = "12/10/2024 12:00:00 AM";
        expectedString.Should().Be(dateTime.ToString("M/dd/yyyy hh:mm:ss tt").ToUpper());
    }

    private void MockSysPackage(DataProviderMock providerMock, bool packageAccending, bool withSchemas = false,
        bool schemaAccending = false)
    {
        IItemsMock? mock = providerMock.MockItems(nameof(SysPackage));
        Guid packageId1 = Guid.NewGuid();
        Guid packageId2 = Guid.NewGuid();
        List<Dictionary<string, object>> list =
        [
            new()
            {
                { "Id", packageId1 },
                { "Name", "CrtBase" },
                { "ModifiedOn", new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "Maintainer", "Creatio" }
            },
            new()
            {
                { "Id", packageId2 },
                { "Name", "CrtUI" },
                { "ModifiedOn", new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "Maintainer", "ATF" }
            }
        ];
        if (!packageAccending)
        {
            list.Reverse();
        }

        mock.Returns(list);
        if (withSchemas)
        {
            MockSysSchemasForPackage(packageId1, providerMock, 2, schemaAccending);
            MockSysSchemasForPackage(packageId2, providerMock, 2, schemaAccending);
        }
    }

    private void MockSysSchemasForPackage(Guid packageId, DataProviderMock providerMock, int count,
        bool schemaAccending)
    {
        IItemsMock? mock = providerMock.MockItems(nameof(SysSchema));
        List<Dictionary<string, object>> list =
        [
            new()
            {
                { "Id", Guid.NewGuid() },
                { "Name", "Contact" },
                { "ModifiedOn", new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "SysPackageId", packageId },
                { "Checksum", "ContactHash" },
                { "UId", Guid.Parse("DE86FE03-3508-4F94-A50E-34A335B1F9F2") }
            },
            new()
            {
                { "Id", Guid.NewGuid() },
                { "Name", "Account" },
                { "ModifiedOn", new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "SysPackageId", packageId },
                { "Checksum", "AccountHash" },
                { "UId", Guid.Parse("DE86FE03-3508-4F94-A50E-34A335B1F9F3") }
            }
        ];
        mock.FilterHas(packageId);
        if (!schemaAccending)
        {
            list.Reverse();
        }

        mock.Returns(list);
    }

    [Test(Description = "Validate that we can mock from OData Json response and get all SysSettings with values.")]
    public void GetAllSysSettingsWithValues_ReturnsMockValues()
    {
        // Arrange
        IContainer container = GetContainer();

        // Getting SysSettingsManager from the container, we real deps but mock data provider
        ISysSettingsManager sysSettingsManager = container.Resolve<ISysSettingsManager>();

        // Act
        List<SysSettings> settings = sysSettingsManager.GetAllSysSettingsWithValues();

        // Assert
        settings.Should().HaveCount(434);
        settings.Any(s => s.ValueTypeName == "Binary").Should().BeFalse();
        int sysettingsValueCount = 0;
        foreach (SysSettings setting in settings)
        {
            sysettingsValueCount += setting.SysSettingsValues.Count;
        }

        sysettingsValueCount.Should().Be(430);
    }

    [Test(Description = "Validate that we can mock from OData Json response and get all SysSettings with values.")]
    public void SaveSysSettingsToManifest()
    {
        // Arrange
        IContainer container = GetContainer();

        // Getting SysSettingsManager from the container, we real deps but mock data provider
        ISysSettingsManager sysSettingsManager = container.Resolve<ISysSettingsManager>();

        // Act
        List<SysSettings> settings = sysSettingsManager.GetAllSysSettingsWithValues();

        // Assert
        settings.Should().HaveCount(434);
        settings.Any(s => s.ValueTypeName == "Binary").Should().BeFalse();
        int sysettingsValueCount = 0;
        foreach (SysSettings setting in settings)
        {
            sysettingsValueCount += setting.SysSettingsValues.Count;
        }

        sysettingsValueCount.Should().Be(430);
    }
}

internal class MockDataContainer(MockFileSystem fileSystem)
{
    protected MockFileSystem _fileSystem = fileSystem;

    private ODataResponse ParseOdataResponse(string content) =>
        Terrasoft.Common.Json.Json.Deserialize<ODataResponse>(content);

    public static IContainer GetContainer(MockFileSystem fileSystem)
    {
        MockDataContainer instance = new(fileSystem);
        return instance.InternalGetContainer();
    }

    private IContainer InternalGetContainer()
    {
        DataProviderMock dataProviderMock = GetMockSysSettingsData();
        BindingsModule bm = new(_fileSystem);
        EnvironmentSettings environmentSettings = new()
        {
            Uri = "http://localhost", Login = "Supervisor", Password = "Supervisor", IsNetCore = false
        };
        IContainer container = bm.Register(
            environmentSettings,
            builder => { builder.RegisterInstance(dataProviderMock).As<IDataProvider>(); });
        return container;
    }

    private DataProviderMock GetMockSysSettingsData()
    {
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

    private List<ODataResponse> GetOdataResponses(string folderName)
    {
        string[] files = _fileSystem.Directory.GetFiles(folderName);
        List<ODataResponse> odataResponses = [];
        foreach (string file in files)
        {
            string content = _fileSystem.File.ReadAllText(file);
            ODataResponse oDataResponse = ParseOdataResponse(content);
            odataResponses.Add(oDataResponse);
        }

        return odataResponses;
    }

    private void MockSysSettingsItems(string schemaName, DataProviderMock dataProviderMock,
        List<Dictionary<string, object>> records)
    {
        IItemsMock mock = dataProviderMock.MockItems(schemaName);
        mock.FilterHas("Binary");

        // We don't have a way to get the type of the model from the schemaName
        // I decided to use reflection to get the type of the model. There is a better way but its mych longer
        SysSettings sysSettings = new();

        // We need to convert records odata collection into collection of expected Types
        List<Dictionary<string, object>> resultRecords = [];
        foreach (Dictionary<string, object> record in records)
        {
            Dictionary<string, object> resultRecord = [];
            foreach (string key in record.Keys)
            {
                // We also need to make sure that when OData feed missing propertyValue,
                // for instance ReferenceSchemaUId, then we either remove it from the model or set it to default value
                // in case we do nothing it throws an exception, because its casing null into Guid.
                // For now I simply commented out the ReferenceSchemaUId property in SysSettings models
                PropertyInfo p = sysSettings.GetType().GetProperty(key);
                if (p is null)
                {
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
                    if (isGuid)
                    {
                        record[key] = value;
                    }
                    else
                    {
                        record[key] = Guid.Empty;
                    }
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(bool)))
                {
                    record[key] = bool.Parse(record[key].ToString() ?? "False");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(int)))
                {
                    record[key] = int.Parse(record[key].ToString() ?? "0");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(DateTime)))
                {
                    record[key] = DateTime.Parse(record[key].ToString() ?? "1970-01-01T00:00:0.000000Z");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(decimal)))
                {
                    record[key] = decimal.Parse(record[key].ToString() ?? "0.00");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(float)))
                {
                    record[key] = float.Parse(record[key].ToString() ?? "0.00");
                }

                IEnumerable<CustomAttributeData> customAttributes = p.CustomAttributes;
                CustomAttributeData? c = customAttributes
                    .FirstOrDefault(c => c.AttributeType == typeof(SchemaPropertyAttribute));
                if (c != null)
                {
                    string? entitySchemaColumnName = c.ConstructorArguments[0].Value.ToString();
                    resultRecord[entitySchemaColumnName] = record[key];
                }
            }

            resultRecords.Add(resultRecord);
        }

        mock.Returns(resultRecords);
    }

    private void MockSysSettingsValueItems(string schemaName, DataProviderMock dataProviderMock,
        List<Dictionary<string, object>> records)
    {
        IItemsMock mock = dataProviderMock.MockItems(schemaName);

        // We don't have a way to get the type of the model from the schemaName
        // I decided to use reflection to get the type of the model. There is a better way but its mych longer
        SysSettingsValue sysSettingsValue = new();

        // We need to convert records odata collection into collection of expected Types
        List<Dictionary<string, object>> resultRecords = [];
        foreach (Dictionary<string, object> record in records)
        {
            Dictionary<string, object> resultRecord = [];
            foreach (string key in record.Keys)
            {
                // We also need to make sure that when OData feed missing propertyValue,
                // for instance ReferenceSchemaUId, then we either remove it from the model or set it to default value
                // in case we do nothing it throws an exception, because its casing null into Guid.
                // For now I simply commented out the ReferenceSchemaUId property in SysSettings models
                PropertyInfo p = sysSettingsValue.GetType().GetProperty(key);
                if (p is null)
                {
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
                    if (isGuid)
                    {
                        record[key] = value;
                    }
                    else
                    {
                        record[key] = Guid.Empty;
                    }
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(bool)))
                {
                    record[key] = bool.Parse(record[key].ToString() ?? "False");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(int)))
                {
                    record[key] = int.Parse(record[key].ToString() ?? "0");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(DateTime)))
                {
                    record[key] = DateTime.Parse(record[key].ToString() ?? "1970-01-01T00:00:0.000000Z");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(decimal)))
                {
                    record[key] = decimal.Parse(record[key].ToString() ?? "0.00");
                }
                else if (p.PropertyType.IsAssignableFrom(typeof(float)))
                {
                    record[key] = float.Parse(record[key].ToString() ?? "0.00");
                }

                IEnumerable<CustomAttributeData> customAttributes = p.CustomAttributes;
                CustomAttributeData? c = customAttributes
                    .FirstOrDefault(c => c.AttributeType == typeof(SchemaPropertyAttribute));
                if (c != null)
                {
                    string? entitySchemaColumnName = c.ConstructorArguments[0].Value.ToString();
                    resultRecord[entitySchemaColumnName] = record[key];
                }
            }

            resultRecords.Add(resultRecord);
        }

        mock.Returns(resultRecords);
    }
}

public class JsonDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? dateTimeString = reader.GetString();
            if (DateTime.TryParse(dateTimeString, out DateTime dateTime))
            {
                return dateTime;
            }
        }

        throw new JsonException("Invalid date format");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
}
