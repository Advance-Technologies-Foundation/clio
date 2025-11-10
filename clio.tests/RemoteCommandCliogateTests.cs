using System;
using System.Net.Http;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests
{
    [TestFixture]
    [Description("Unit tests for cliogate presence and version check logic in RemoteCommand.")]
    public class RemoteCommandCliogateTests
    {
        private class TestOptions : RemoteCommandOptions { }

        private class TestRemoteCommand : RemoteCommand<TestOptions>
        {
            public bool VersionChecked { get; private set; }
            public string LastError { get; private set; }
            
            public TestRemoteCommand(string servicePath, string clioGateMinVersion, IClioGateway clioGateway)
            {
                ServicePath = servicePath;
                ClioGateMinVersion = clioGateMinVersion;
                ClioGateWay = clioGateway;
                Logger = Substitute.For<ILogger>();
                Logger
                    .When(x => x.WriteError(Arg.Any<string>()))
                    .Do(call => LastError = call.Arg<string>());
            }
            
            public TestRemoteCommand(EnvironmentSettings environmentSettings, string servicePath)
            {
                EnvironmentSettings = environmentSettings;
                ServicePath = servicePath;
                Logger = Substitute.For<ILogger>();
            }
            
            // Expose ServiceUri for testing
            public string GetServiceUri() => ServiceUri;
            
            protected override void ExecuteRemoteCommand(TestOptions options) { }
            protected override void ProceedResponse(string response, TestOptions options) { }
            protected override string ClioGateMinVersion { get; } = "0.0.0.0";
            protected override string ServicePath { get; set; }
        }

        [Test]
        [Description("Should return error if cliogate is required by ServicePath and not installed.")]
        public void Execute_ShouldReturnError_WhenCliogateRequiredByServicePathAndNotInstalled()
        {
            var cmd = new TestRemoteCommand("/rest/CreatioApiGateway/GetSysInfo", "0.0.0.0", null);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(1, "cliogate is required but not installed");
            cmd.LastError.Should().Contain("cliogate is not installed", "error message should be correct");
        }

        [Test]
        [Description("Should return error if cliogate is required by ClioGateMinVersion and not installed.")]
        public void Execute_ShouldReturnError_WhenCliogateRequiredByMinVersionAndNotInstalled()
        {
            var cmd = new TestRemoteCommand("", "2.0.0.32", null);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(1, "cliogate is required by version but not installed");
            cmd.LastError.Should().Contain("cliogate is not installed", "error message should be correct");
        }

        [Test]
        [Description("Should check version if cliogate is installed and ClioGateMinVersion is set.")]
        public void Execute_ShouldCheckVersion_WhenCliogateInstalledAndMinVersionSet()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("", "2.0.0.32", gateway);
            cmd.Execute(new TestOptions());
            gateway.Received(1).CheckCompatibleVersion("2.0.0.32");
        }

        [Test]
        [Description("Should not check version if ClioGateMinVersion is default.")]
        public void Execute_ShouldNotCheckVersion_WhenMinVersionIsDefault()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("", "0.0.0.0", gateway);
            cmd.Execute(new TestOptions());
            gateway.DidNotReceive().CheckCompatibleVersion(Arg.Any<string>());
        }

        [Test]
        [Description("Should execute command if cliogate is present and requirements are met.")]
        public void Execute_ShouldRunCommand_WhenCliogatePresentAndRequirementsMet()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("/rest/CreatioApiGateway/GetSysInfo", "2.0.0.32", gateway);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(0, "cliogate is present and requirements are met");
        }

        [Test]
        [Description("Should execute command if ServicePath is not empty and does not require cliogate.")]
        public void Execute_ShouldRunCommand_WhenServicePathIsNotEmptyAndDoesNotRequireCliogate()
        {
            var cmd = new TestRemoteCommand("/api/SomeOtherService", "0.0.0.0", null);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(0, "cliogate is not required for this ServicePath");
        }

        [Test]
        [Description("Should execute command if ServicePath is not empty and does not require cliogate, even if gateway is present.")]
        public void Execute_ShouldRunCommand_WhenServicePathIsNotEmptyAndDoesNotRequireCliogateWithGateway()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("/api/SomeOtherService", "0.0.0.0", gateway);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(0, "cliogate is not required for this ServicePath, gateway present");
            gateway.DidNotReceive().CheckCompatibleVersion(Arg.Any<string>());
        }
    }

    [TestFixture]
    [Description("Unit tests for ServiceUri property in RemoteCommand.")]
    public class RemoteCommandServiceUriTests
    {
        private class TestOptions : RemoteCommandOptions { }

        private class TestRemoteCommand : RemoteCommand<TestOptions>
        {
            public TestRemoteCommand(EnvironmentSettings environmentSettings, string servicePath)
            {
                EnvironmentSettings = environmentSettings;
                ServicePath = servicePath;
                Logger = Substitute.For<ILogger>();
            }

            // Expose ServiceUri for testing
            public string GetServiceUri() => ServiceUri;

            protected override void ExecuteRemoteCommand(TestOptions options) { }
            protected override void ProceedResponse(string response, TestOptions options) { }
            protected override string ServicePath { get; set; }
        }

        [Test]
        [Description("ServiceUri should return absolute HTTP URL when ServicePath is absolute HTTP URL.")]
        public void ServiceUri_ShouldReturnAbsoluteUrl_WhenServicePathIsAbsoluteHttpUrl()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost",
                IsNetCore = true
            };
            var servicePath = "http://example.com/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://example.com/api/service", "absolute HTTP URLs should be returned as-is");
        }

        [Test]
        [Description("ServiceUri should return absolute HTTPS URL when ServicePath is absolute HTTPS URL.")]
        public void ServiceUri_ShouldReturnAbsoluteUrl_WhenServicePathIsAbsoluteHttpsUrl()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost",
                IsNetCore = true
            };
            var servicePath = "https://example.com/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("https://example.com/api/service", "absolute HTTPS URLs should be returned as-is");
        }

        [Test]
        [Description("ServiceUri should combine RootPath and ServicePath when ServicePath is relative and IsNetCore is true.")]
        public void ServiceUri_ShouldCombineRootPathAndServicePath_WhenServicePathIsRelativeAndIsNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080",
                IsNetCore = true
            };
            var servicePath = "/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080/api/service", "relative paths should be combined with RootPath for NetCore");
        }

        [Test]
        [Description("ServiceUri should combine RootPath with /0 and ServicePath when ServicePath is relative and IsNetCore is false.")]
        public void ServiceUri_ShouldCombineRootPathWithZeroAndServicePath_WhenServicePathIsRelativeAndIsNotNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080",
                IsNetCore = false
            };
            var servicePath = "/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080/0/api/service", "relative paths should be combined with RootPath/0 for non-NetCore");
        }

        [Test]
        [Description("ServiceUri should handle ServicePath without leading slash when IsNetCore is true.")]
        public void ServiceUri_ShouldHandleServicePathWithoutLeadingSlash_WhenIsNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080",
                IsNetCore = true
            };
            var servicePath = "api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080api/service", "ServicePath without leading slash should be appended directly to RootPath");
        }

        [Test]
        [Description("ServiceUri should handle ServicePath without leading slash when IsNetCore is false.")]
        public void ServiceUri_ShouldHandleServicePathWithoutLeadingSlash_WhenIsNotNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080",
                IsNetCore = false
            };
            var servicePath = "api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080/0api/service", "ServicePath without leading slash should be appended directly to RootPath/0");
        }

        [Test]
        [Description("ServiceUri should handle Uri with trailing slash and ServicePath with leading slash for NetCore.")]
        public void ServiceUri_ShouldHandleTrailingAndLeadingSlashes_WhenIsNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080/",
                IsNetCore = true
            };
            var servicePath = "/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080//api/service", "trailing and leading slashes should result in double slash");
        }

        [Test]
        [Description("ServiceUri should handle Uri with trailing slash and ServicePath with leading slash for non-NetCore.")]
        public void ServiceUri_ShouldHandleTrailingAndLeadingSlashes_WhenIsNotNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080/",
                IsNetCore = false
            };
            var servicePath = "/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080//0/api/service", "trailing slash on Uri should be preserved");
        }

        [Test]
        [Description("ServiceUri should not treat FTP URLs as absolute URLs.")]
        public void ServiceUri_ShouldNotTreatFtpAsAbsolute_WhenServicePathIsFtpUrl()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost",
                IsNetCore = true
            };
            var servicePath = "ftp://example.com/file";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhostftp://example.com/file", "FTP URLs should not be treated as absolute and should be appended to RootPath");
        }

        [Test]
        [Description("ServiceUri should handle empty ServicePath for NetCore.")]
        public void ServiceUri_ShouldHandleEmptyServicePath_WhenIsNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080",
                IsNetCore = true
            };
            var servicePath = "";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080", "empty ServicePath should return RootPath for NetCore");
        }

        [Test]
        [Description("ServiceUri should handle empty ServicePath for non-NetCore.")]
        public void ServiceUri_ShouldHandleEmptyServicePath_WhenIsNotNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost:8080",
                IsNetCore = false
            };
            var servicePath = "";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://localhost:8080/0", "empty ServicePath should return RootPath/0 for non-NetCore");
        }

        [Test]
        [Description("ServiceUri should handle complex relative paths with query strings for NetCore.")]
        public void ServiceUri_ShouldHandleComplexRelativePathsWithQueryStrings_WhenIsNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "https://example.com",
                IsNetCore = true
            };
            var servicePath = "/api/service?param1=value1&param2=value2";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("https://example.com/api/service?param1=value1&param2=value2", "query strings should be preserved in relative paths");
        }

        [Test]
        [Description("ServiceUri should handle complex relative paths with query strings for non-NetCore.")]
        public void ServiceUri_ShouldHandleComplexRelativePathsWithQueryStrings_WhenIsNotNetCore()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "https://example.com",
                IsNetCore = false
            };
            var servicePath = "/api/service?param1=value1&param2=value2";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("https://example.com/0/api/service?param1=value1&param2=value2", "query strings should be preserved in relative paths for non-NetCore");
        }

        [Test]
        [Description("ServiceUri should handle absolute URLs with port numbers.")]
        public void ServiceUri_ShouldHandleAbsoluteUrlsWithPortNumbers()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost",
                IsNetCore = true
            };
            var servicePath = "http://example.com:8080/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://example.com:8080/api/service", "absolute URLs with port numbers should be returned as-is");
        }

        [Test]
        [Description("ServiceUri should handle absolute URLs with authentication.")]
        public void ServiceUri_ShouldHandleAbsoluteUrlsWithAuthentication()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost",
                IsNetCore = true
            };
            var servicePath = "http://user:pass@example.com/api/service";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("http://user:pass@example.com/api/service", "absolute URLs with authentication should be returned as-is");
        }

        [Test]
        [Description("ServiceUri should handle absolute URLs with fragments.")]
        public void ServiceUri_ShouldHandleAbsoluteUrlsWithFragments()
        {
            // Arrange
            var environmentSettings = new EnvironmentSettings
            {
                Uri = "http://localhost",
                IsNetCore = true
            };
            var servicePath = "https://example.com/api/service#section";
            var cmd = new TestRemoteCommand(environmentSettings, servicePath);

            // Act
            var result = cmd.GetServiceUri();

            // Assert
            result.Should().Be("https://example.com/api/service#section", "absolute URLs with fragments should be returned as-is");
        }
    }
}
