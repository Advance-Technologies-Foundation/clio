using System;
using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Autofac;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
internal class ConfigPatcherServiceTests
{
	private IConfigPatcherService _configPatcherService;
	private MockFileSystem _fileSystem;

	[SetUp]
	public void Setup()
	{
		// Create a new MockFileSystem for each test with directories pre-created
		var fileSystem = new MockFileSystem();
		fileSystem.Directory.CreateDirectory("/config");
		fileSystem.Directory.CreateDirectory("/connectionStrings");
		_fileSystem = fileSystem;
		_configPatcherService = new ConfigPatcherService(_fileSystem);
	}

	#region PatchCookiesSameSiteMode Tests

	[Test]
	[Description("Verifies PatchCookiesSameSiteMode adds SameSite attribute when not present")]
	public void PatchCookiesSameSiteMode_WithoutSameSiteAttribute_ShouldAdd()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<system.webServer>
					<httpCookies httpOnlyCookies="true" requireSSL="false" />
				</system.webServer>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.PatchCookiesSameSiteMode(filePath);

		// Assert
		result.Should().BeTrue("patching should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("sameSite=\"Lax\"", "sameSite attribute should be added");
	}

	[Test]
	[Description("Verifies PatchCookiesSameSiteMode updates existing SameSite attribute")]
	public void PatchCookiesSameSiteMode_WithExistingSameSiteAttribute_ShouldUpdate()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<system.webServer>
					<httpCookies httpOnlyCookies="true" sameSite="None" requireSSL="false" />
				</system.webServer>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.PatchCookiesSameSiteMode(filePath);

		// Assert
		result.Should().BeTrue("patching should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("sameSite=\"Lax\"", "sameSite attribute should be updated to Lax");
		fileContent.Should().NotContain("sameSite=\"None\"", "old sameSite value should be removed");
	}

	[Test]
	[Description("Verifies PatchCookiesSameSiteMode creates httpCookies element if missing")]
	public void PatchCookiesSameSiteMode_WithoutHttpCookiesElement_ShouldCreate()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<system.webServer>
				</system.webServer>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.PatchCookiesSameSiteMode(filePath);

		// Assert
		result.Should().BeTrue("patching should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("httpCookies", "httpCookies element should be created");
		fileContent.Should().Contain("sameSite=\"Lax\"", "sameSite attribute should be set");
	}

	#endregion

	#region UpdateConnectionString Tests

	[Test]
	[Description("Verifies UpdateConnectionString updates existing connection string")]
	public void UpdateConnectionString_WithExistingConnectionString_ShouldUpdate()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<connectionStrings>
					<add name="db" connectionString="Server=oldserver,5433;Database=olddb;User Id=olduser;Password=oldpass" />
				</connectionStrings>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.UpdateConnectionString(filePath, "localhost", 5432, "creatio", "admin", "password123");

		// Assert
		result.Should().BeTrue("connection string update should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("Server=localhost,5432", "connection string should contain server and port");
		fileContent.Should().Contain("Database=creatio");
		fileContent.Should().Contain("User Id=admin");
		fileContent.Should().Contain("Password=password123");
	}

	[Test]
	[Description("Verifies UpdateConnectionString creates connectionStrings section if missing")]
	public void UpdateConnectionString_WithoutConnectionStringsSection_ShouldCreate()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.UpdateConnectionString(filePath, "localhost", 5432, "creatio", "admin", "password123");

		// Assert
		result.Should().BeTrue("connection string update should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("connectionStrings");
		fileContent.Should().Contain("Server=localhost,5432");
		fileContent.Should().Contain("Database=creatio");
	}

	[Test]
	[Description("Verifies UpdateConnectionString handles special characters in password")]
	public void UpdateConnectionString_WithSpecialCharactersInPassword_ShouldHandleCorrectly()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<connectionStrings>
					<add name="db" connectionString="Server=oldserver,5433;Database=olddb;User Id=olduser;Password=oldpass" />
				</connectionStrings>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);
		const string specialPassword = "Pass@word!123;#$%";

		// Act
		var result = _configPatcherService.UpdateConnectionString(filePath, "localhost", 5432, "creatio", "admin", specialPassword);

		// Assert
		result.Should().BeTrue("connection string update should succeed with special characters");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("Pass@word!123");
		fileContent.Should().Contain("Server=localhost,5432");
	}



	#endregion

	#region ConfigurePort Tests

	[Test]
	[Description("Verifies ConfigurePort updates appSettings port value")]
	public void ConfigurePort_WithExistingPortSetting_ShouldUpdate()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<appSettings>
					<add key="Port" value="8080" />
				</appSettings>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.ConfigurePort(filePath, 9090);

		// Assert
		result.Should().BeTrue("port configuration should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("value=\"9090\"");
		fileContent.Should().NotContain("value=\"8080\"");
	}

	[Test]
	[Description("Verifies ConfigurePort creates appSettings section if missing")]
	public void ConfigurePort_WithoutAppSettingsSection_ShouldCreate()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.ConfigurePort(filePath, 8080);

		// Assert
		result.Should().BeTrue("port configuration should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("appSettings");
		fileContent.Should().Contain("key=\"Port\"");
		fileContent.Should().Contain("value=\"8080\"");
	}

	[Test]
	[Description("Verifies ConfigurePort adds Port setting if missing in appSettings")]
	public void ConfigurePort_WithoutPortSetting_ShouldAdd()
	{
		// Arrange
		const string filePath = "/config/Terrasoft.WebHost.dll.config";
		const string originalContent = """
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<appSettings>
					<add key="OtherSetting" value="value" />
				</appSettings>
			</configuration>
			""";

		_fileSystem.File.WriteAllText(filePath, originalContent);

		// Act
		var result = _configPatcherService.ConfigurePort(filePath, 8080);

		// Assert
		result.Should().BeTrue("port configuration should succeed");
		var fileContent = _fileSystem.File.ReadAllText(filePath);
		fileContent.Should().Contain("key=\"Port\"");
		fileContent.Should().Contain("value=\"8080\"");
	}

	#endregion
}
