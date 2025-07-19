using Clio.UserEnvironment;
using Clio.Command;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System;
using System.Xml;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Clio.Tests.Command
{
    /// <summary>
    /// Unit tests for <see cref="SetFsmConfigCommand"/>.
    /// </summary>
    [TestFixture]
    public class SetFsmConfigCommandTests : BaseCommandTests<SetFsmConfigOptions>
    {
        private IValidator<SetFsmConfigOptions> _validator;
        private ISettingsRepository _settingsRepository;
        private SetFsmConfigCommand _command;

        /// <summary>
        /// Testable version of SetFsmConfigCommand for mocking IIS site scanning.
        /// </summary>
        [SetUp]
        public void Setup()
        {
            _validator = Substitute.For<IValidator<SetFsmConfigOptions>>();
            _settingsRepository = Substitute.For<ISettingsRepository>();
            _command = new SetFsmConfigCommand(_validator, _settingsRepository);
        }

        /// <summary>
        /// Should return error when validation fails.
        /// </summary>
        [Test, Category("Unit")]
        public void Execute_ReturnsError_WhenValidationFails()
        {
            // Arrange
            var options = new SetFsmConfigOptions { IsFsm = "on" };
            _validator.Validate(options).Returns(new ValidationResult(new List<ValidationFailure>
            {
                new ValidationFailure("IsFsm", "Error message")
            }));

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(1, "because validation failed");
        }

        /// <summary>
        /// Should update config file and return success when validation passes and config exists (Windows path).
        /// </summary>
        [Test, Category("Unit")]
        public void Execute_ReturnsSuccess_WhenValidationPasses_AndConfigExists_WindowsPath()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var configPath = Path.Combine(tempDir, "Web.config");
            File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

            var options = new SetFsmConfigOptions { IsFsm = "on", PhysicalPath = tempDir };
            _validator.Validate(options).Returns(new ValidationResult());

            var env = new EnvironmentSettings { Uri = "http://test.com", IsNetCore = false };
            _settingsRepository.GetEnvironment(options).Returns(env);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0, "because config exists and should be updated");

            var doc = new XmlDocument();
            doc.Load(configPath);
            var fileDesignMode = doc.SelectSingleNode("//terrasoft/fileDesignMode").Attributes["enabled"].Value;
            var useStaticFileContent = doc.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']").Attributes["value"].Value;

            fileDesignMode.Should().Be("true", "because IsFsm=on sets fileDesignMode enabled to true");
            useStaticFileContent.Should().Be("false", "because IsFsm=on sets UseStaticFileContent to false");

            Directory.Delete(tempDir, true);
        }

        /// <summary>
        /// Should update config file and return success when validation passes and config exists (Linux path).
        /// </summary>
        [Test, Category("Unit")]
        public void Execute_ReturnsSuccess_WhenValidationPasses_AndConfigExists_LinuxPath()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var configPath = Path.Combine(tempDir, "Web.config");
            File.WriteAllText(configPath, GetSampleWebConfig("true", "false"));

            var options = new SetFsmConfigOptions { IsFsm = "off", PhysicalPath = tempDir };
            _validator.Validate(options).Returns(new ValidationResult());

            var env = new EnvironmentSettings { Uri = "http://test.com", IsNetCore = false };
            _settingsRepository.GetEnvironment(options).Returns(env);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0, "because config exists and should be updated");

            var doc = new XmlDocument();
            doc.Load(configPath);
            var fileDesignMode = doc.SelectSingleNode("//terrasoft/fileDesignMode").Attributes["enabled"].Value;
            var useStaticFileContent = doc.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']").Attributes["value"].Value;

            fileDesignMode.Should().Be("false", "because IsFsm=off sets fileDesignMode enabled to false");
            useStaticFileContent.Should().Be("true", "because IsFsm=off sets UseStaticFileContent to true");

            Directory.Delete(tempDir, true);
        }

        /// <summary>
        /// Should use correct config file name for .NET Core environments.
        /// </summary>
        [Test, Category("Unit")]
        public void Execute_UsesCorrectWebConfigFileName_ForNetCore()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var configPath = Path.Combine(tempDir, "Terrasoft.WebHost.dll.config");
            File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

            var options = new SetFsmConfigOptions { IsFsm = "on", PhysicalPath = tempDir };
            _validator.Validate(options).Returns(new ValidationResult());

            var env = new EnvironmentSettings { Uri = "http://test.com", IsNetCore = true };
            _settingsRepository.GetEnvironment(options).Returns(env);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0, "because .NET Core config exists and should be updated");

            var doc = new XmlDocument();
            doc.Load(configPath);
            var fileDesignMode = doc.SelectSingleNode("//terrasoft/fileDesignMode").Attributes["enabled"].Value;
            var useStaticFileContent = doc.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']").Attributes["value"].Value;

            fileDesignMode.Should().Be("true", "because IsFsm=on sets fileDesignMode enabled to true");
            useStaticFileContent.Should().Be("false", "because IsFsm=on sets UseStaticFileContent to false");

            Directory.Delete(tempDir, true);
        }

        /// <summary>
        /// Should return error if config file does not exist.
        /// </summary>
        [Test, Category("Unit")]
        public void Execute_ReturnsError_WhenConfigDoesNotExist()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            var options = new SetFsmConfigOptions { IsFsm = "on", PhysicalPath = tempDir };
            _validator.Validate(options).Returns(new ValidationResult());

            var env = new EnvironmentSettings { Uri = "http://test.com", IsNetCore = false };
            _settingsRepository.GetEnvironment(options).Returns(env);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(1, "because config file does not exist");

            Directory.Delete(tempDir, true);
        }

        
       

        /// <summary>
        /// Returns a sample web.config XML string.
        /// </summary>
        private static string GetSampleWebConfig(string fileDesignModeEnabled, string useStaticFileContent)
        {
            return $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <terrasoft>
                        <fileDesignMode enabled="{fileDesignModeEnabled}" />
                      </terrasoft>
                      <appSettings>
                        <add key="UseStaticFileContent" value="{useStaticFileContent}" />
                      </appSettings>
                    </configuration>
                    """;
        }
    }
}
