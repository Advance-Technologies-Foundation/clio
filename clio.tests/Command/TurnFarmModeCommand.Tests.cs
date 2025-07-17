using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Clio.Command;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{
    [TestFixture]
    [Category("UnitTests")]
    public class TurnFarmModeCommandTests
    {
        private IValidator<TurnFarmModeOptions> _validator;
        private ISettingsRepository _settingsRepository;
        private ILogger _logger;
        private TurnFarmModeCommand _command;
        private string _testSitePath;
        private string _testWebConfigPath;
        private string _testInternalWebConfigPath;

        [SetUp]
        public void SetUp()
        {
            _validator = Substitute.For<IValidator<TurnFarmModeOptions>>();
            _settingsRepository = Substitute.For<ISettingsRepository>();
            _logger = Substitute.For<ILogger>();
            _command = new TurnFarmModeCommand(_validator, _settingsRepository, _logger);

            // Set up test paths
            _testSitePath = Path.Combine(Path.GetTempPath(), "TestSite");
            _testWebConfigPath = Path.Combine(_testSitePath, "Web.config");
            _testInternalWebConfigPath = Path.Combine(_testSitePath, "Terrasoft.WebApp", "Web.config");

            // Create test directories
            Directory.CreateDirectory(_testSitePath);
            Directory.CreateDirectory(Path.Combine(_testSitePath, "Terrasoft.WebApp"));
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test files
            if (Directory.Exists(_testSitePath))
            {
                Directory.Delete(_testSitePath, true);
            }
        }

        [Test]
        public void Execute_ReturnsError_WhenValidationFails()
        {
            // Arrange
            var options = new TurnFarmModeOptions { TenantId = "1" };
            var validationFailures = new List<ValidationFailure>
            {
                new ValidationFailure("TenantId", "Tenant ID is required") { ErrorCode = "TEST001", Severity = Severity.Error }
            };
            var validationResult = new ValidationResult(validationFailures);
            _validator.Validate(options).Returns(validationResult);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
            _logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("ERROR (TEST001) - Tenant ID is required")));
        }

        [Test]
        public void Execute_ReturnsError_WhenSitePathNotFound()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = "C:\\NonExistentPath" 
            };
            _validator.Validate(options).Returns(new ValidationResult());

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
            _logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("Site path not found")));
        }

        [Test]
        public void Execute_ReturnsSuccess_WhenValidOptionsAndSitePathProvided()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath,
                InstanceId = "Node1"
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfig(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received(1).WriteInfo(Arg.Is<string>(s => s.Contains("Configuring web farm mode for site")));
            _logger.Received(1).WriteInfo("Web farm mode configuration completed successfully");
        }

        [Test]
        public void Execute_UsesEnvironmentPath_WhenSitePathNotProvided()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                Environment = "TestEnv"
            };
            _validator.Validate(options).Returns(new ValidationResult());

            var environmentSettings = new EnvironmentSettings 
            { 
                Uri = "http://localhost:8080" 
            };
            _settingsRepository.GetEnvironment("TestEnv").Returns(environmentSettings);

            var testSite = new IISScannerHandler.UnregisteredSite(
                new IISScannerHandler.SiteBinding("TestSite", "Started", "http/*:8080:localhost", _testSitePath),
                new List<Uri> { new Uri("http://localhost:8080") },
                IISScannerHandler.SiteType.NetFramework
            );

            // Mock the static method call (in real scenario, this would require more setup)
            CreateTestWebConfig(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act & Assert - This test shows the intent, but the static method call makes it harder to test
            // In a real scenario, we would need to refactor to inject the IIS scanner as a dependency
            var result = _command.Execute(options);
            
            // This test verifies the method attempts to get environment settings
            _settingsRepository.Received(1).GetEnvironment("TestEnv");
        }

        [Test]
        public void Execute_ConfiguresTenantId_InBothWebConfigs()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "123", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfig(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            
            // Verify TenantId was added to both configs
            VerifyTenantIdInConfig(_testWebConfigPath, "123");
            VerifyTenantIdInConfig(_testInternalWebConfigPath, "123");
        }

        [Test]
        public void Execute_ConfiguresQuartzClustering_ForAllSchedulers()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath,
                InstanceId = "Node1"
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfigWithQuartz(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            
            // Verify Quartz clustering was configured
            VerifyQuartzClusteringInConfig(_testWebConfigPath, "Node1");
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Found 2 Quartz scheduler configurations to update")));
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Configuring clustering for scheduler: BPMonlineQuartzScheduler")));
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Configuring clustering for scheduler: CampaignQuartzScheduler")));
        }

        [Test]
        public void Execute_UpdatesExistingTenantId_WhenAlreadyExists()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "999", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfigWithExistingTenantId(_testWebConfigPath, "1");
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Updated TenantId from '1' to '999'")));
            VerifyTenantIdInConfig(_testWebConfigPath, "999");
        }

        [Test]
        public void Execute_HandlesException_AndReturnsError()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());
            _validator.When(x => x.Validate(options)).Do(x => throw new Exception("Test exception"));

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(1);
            _logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("Error configuring web farm mode: Test exception")));
        }

        [Test]
        public void Execute_SkipsWebConfig_WhenFileNotFound()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());

            // Don't create web config files

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Web.config not found at")));
            _logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Internal Web.config not found at")));
        }

        [Test]
        public void Execute_LogsSchedulerNames_WhenConfiguringQuartz()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath,
                InstanceId = "CustomNode"
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfigWithQuartz(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteInfo("Configuring clustering for scheduler: BPMonlineQuartzScheduler");
            _logger.Received().WriteInfo("Configuring clustering for scheduler: CampaignQuartzScheduler");
            _logger.Received().WriteInfo("Configured Quartz clustering settings for all schedulers");
        }

        [Test]
        public void Execute_UpdatesExistingQuartzSettings_WhenAlreadyConfigured()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath,
                InstanceId = "NewNode"
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfigWithQuartz(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Updated quartz.jobStore.clustered from 'false' to 'true'")));
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Updated quartz.jobStore.acquireTriggersWithinLock from 'false' to 'true'")));
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Updated quartz.scheduler.instanceId from 'AUTO' to 'NewNode'")));
        }

        [Test]
        public void Execute_HandlesConfigWithoutQuartzSection()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());

            // Create a config with quartzConfig but no quartz child elements
            string webConfigContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""SomeOtherKey"" value=""SomeValue"" />
  </appSettings>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
  <quartzConfig>
    <!-- No quartz child elements -->
  </quartzConfig>
</configuration>";
            File.WriteAllText(_testWebConfigPath, webConfigContent);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteWarning("No quartz scheduler configurations found in quartzConfig section");
        }

        [Test]
        public void Execute_HandlesConfigWithoutQuartzConfigSection()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());

            // Create a config without quartzConfig section at all
            string webConfigContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""SomeOtherKey"" value=""SomeValue"" />
  </appSettings>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
</configuration>";
            File.WriteAllText(_testWebConfigPath, webConfigContent);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteWarning("quartzConfig section not found in Web.config");
        }

        [Test]
        public void Execute_CreatesAppSettingsSection_WhenNotExists()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath
            };
            _validator.Validate(options).Returns(new ValidationResult());

            // Create config without appSettings section
            string webConfigContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
</configuration>";
            File.WriteAllText(_testWebConfigPath, webConfigContent);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);
            _logger.Received().WriteInfo("Created appSettings section");
            VerifyTenantIdInConfig(_testWebConfigPath, "1");
        }

        [Test]
        public void Execute_CreatesBackupFiles_BeforeModifyingConfigs()
        {
            // Arrange
            var options = new TurnFarmModeOptions 
            { 
                TenantId = "1", 
                SitePath = _testSitePath,
                InstanceId = "Node1"
            };
            _validator.Validate(options).Returns(new ValidationResult());

            CreateTestWebConfig(_testWebConfigPath);
            CreateTestInternalWebConfig(_testInternalWebConfigPath);

            // Act
            int result = _command.Execute(options);

            // Assert
            result.Should().Be(0);

            // Check if backup files were created
            var backupFiles = Directory.GetFiles(_testSitePath, "before-tfm-*-Web.config");
            backupFiles.Should().HaveCount(1, "Main Web.config backup should be created");

            var internalBackupFiles = Directory.GetFiles(Path.Combine(_testSitePath, "Terrasoft.WebApp"), "before-tfm-*-Web.config");
            internalBackupFiles.Should().HaveCount(1, "Internal Web.config backup should be created");

            // Verify backup content is same as original
            var backupContent = File.ReadAllText(backupFiles[0]);
            backupContent.Should().Contain("SomeOtherKey"); // Original content should be preserved
            
            _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Backup created:")));
        }

        #region Helper Methods

        private void CreateTestWebConfig(string path)
        {
            string webConfigContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""SomeOtherKey"" value=""SomeValue"" />
  </appSettings>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
  <quartzConfig>
    <quartz isActive=""true"">
      <add key=""quartz.scheduler.instanceName"" value=""BPMonlineQuartzScheduler"" />
      <add key=""quartz.jobStore.clustered"" value=""false"" />
      <add key=""quartz.jobStore.acquireTriggersWithinLock"" value=""false"" />
      <add key=""quartz.scheduler.instanceId"" value=""AUTO"" />
    </quartz>
  </quartzConfig>
</configuration>";
            File.WriteAllText(path, webConfigContent);
        }

        private void CreateTestWebConfigWithQuartz(string path)
        {
            string webConfigContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""SomeOtherKey"" value=""SomeValue"" />
  </appSettings>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
  <quartzConfig>
    <quartz isActive=""true"">
      <add key=""quartz.scheduler.instanceName"" value=""BPMonlineQuartzScheduler"" />
      <add key=""quartz.jobStore.clustered"" value=""false"" />
      <add key=""quartz.jobStore.acquireTriggersWithinLock"" value=""false"" />
      <add key=""quartz.scheduler.instanceId"" value=""AUTO"" />
    </quartz>
    <quartz isActive=""true"">
      <add key=""quartz.scheduler.instanceName"" value=""CampaignQuartzScheduler"" />
      <add key=""quartz.jobStore.clustered"" value=""false"" />
      <add key=""quartz.jobStore.acquireTriggersWithinLock"" value=""false"" />
      <add key=""quartz.scheduler.instanceId"" value=""AUTO"" />
    </quartz>
  </quartzConfig>
</configuration>";
            File.WriteAllText(path, webConfigContent);
        }

        private void CreateTestWebConfigWithExistingTenantId(string path, string existingTenantId)
        {
            string webConfigContent = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""TenantId"" value=""{existingTenantId}"" />
    <add key=""SomeOtherKey"" value=""SomeValue"" />
  </appSettings>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
</configuration>";
            File.WriteAllText(path, webConfigContent);
        }

        private void CreateTestInternalWebConfig(string path)
        {
            string webConfigContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""InternalKey"" value=""InternalValue"" />
  </appSettings>
  <system.web>
    <httpRuntime maxRequestLength=""51200"" executionTimeout=""600"" />
  </system.web>
</configuration>";
            File.WriteAllText(path, webConfigContent);
        }

        private void VerifyTenantIdInConfig(string configPath, string expectedTenantId)
        {
            var doc = new XmlDocument();
            doc.Load(configPath);
            
            var tenantIdNode = doc.SelectSingleNode("//appSettings/add[@key='TenantId']");
            tenantIdNode.Should().NotBeNull();
            tenantIdNode.Attributes["value"].Value.Should().Be(expectedTenantId);
        }

        private void VerifyQuartzClusteringInConfig(string configPath, string expectedInstanceId)
        {
            var doc = new XmlDocument();
            doc.Load(configPath);
            
            var quartzNodes = doc.SelectNodes("//quartzConfig/quartz");
            quartzNodes.Should().NotBeNull();
            quartzNodes.Count.Should().BeGreaterThan(0);

            foreach (XmlNode quartzNode in quartzNodes)
            {
                var clusteredNode = quartzNode.SelectSingleNode("add[@key='quartz.jobStore.clustered']");
                clusteredNode.Should().NotBeNull();
                clusteredNode.Attributes["value"].Value.Should().Be("true");

                var acquireTriggersNode = quartzNode.SelectSingleNode("add[@key='quartz.jobStore.acquireTriggersWithinLock']");
                acquireTriggersNode.Should().NotBeNull();
                acquireTriggersNode.Attributes["value"].Value.Should().Be("true");

                var instanceIdNode = quartzNode.SelectSingleNode("add[@key='quartz.scheduler.instanceId']");
                instanceIdNode.Should().NotBeNull();
                instanceIdNode.Attributes["value"].Value.Should().Be(expectedInstanceId);
            }
        }

        #endregion
    }
}

