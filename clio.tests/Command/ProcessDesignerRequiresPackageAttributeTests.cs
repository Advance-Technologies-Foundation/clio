using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

// Lives in its own file rather than appended to RemoteCommandCliogateTests.cs, where it started:
// these are the lock-in tests for the process-builder gate, and someone changing
// BundledPackages will grep under Command/ and Common/, not a file named for cliogate
// remote-command behaviour.
namespace Clio.Tests
{
    [TestFixture]
    [Category("Unit")]
    [Property("Module", "Command")]
    [Description("Reflection lock-in tests asserting the four process-designer command options classes are gated on the bundled CrtProcessBuilder package at the bundled version, that the hint names the install command, and that the MCP args record carries the same requirement.")]
    public class ProcessDesignerRequiresPackageAttributeTests
    {
        // The hint is the user-visible remediation channel, so it is pinned verbatim: it must name a verb
        // that actually exists. It is deliberately NOT feature-gated, because a gated verb is filtered out
        // of the parse array and the hint would then point at an unknown command.
        // Compared against the shared constant, which catches a site that spells its own hint instead of
        // reusing it. It does NOT prove the hint names a verb that exists - nothing here parses it - so the
        // verb name below is also spelled out literally, and a rename that misses this file fails here.
        private const string ExpectedProcessBuilderHint =
            "Run 'clio install-process-builder -e <environment>' (or call the install-process-builder MCP tool) "
            + "to install or update " + BundledPackages.ProcessBuilderPackageName + ".";

        private static RequiresPackageAttribute GetProcessBuilderRequirement(Type type)
            => (RequiresPackageAttribute[])type.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: false)
                is { Length: > 0 } attrs
                ? System.Array.Find(attrs, a => string.Equals(a.Name, BundledPackages.ProcessBuilderPackageName, System.StringComparison.OrdinalIgnoreCase))
                : null;

        [TestCase(typeof(CreateBusinessProcessOptions))]
        [TestCase(typeof(ModifyBusinessProcessOptions))]
        [TestCase(typeof(DescribeProcessOptions))]
        [TestCase(typeof(ListUserTasksOptions))]
        [Test]
        [Description("Each process-designer command options class that actually calls ProcessDesignService must be gated on the bundled package NAME only - presence-only, because the recorded package version is inert on a re-install and a floor could never be satisfied by an upgraded environment. (get-process-signature is excluded — it uses the built-in DataService; see the negative test below.)")]
        public void OptionsType_ShouldDeclarePresenceOnlyProcessBuilderRequirement_WhenProcessDesignerCommand(
            Type optionsType)
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(optionsType);

            // Assert
            requirement.Should().NotBeNull(
                because: $"{optionsType.Name} must carry the declarative {BundledPackages.ProcessBuilderPackageName} requirement so the MCP gate fires");
            requirement!.Version.Should().BeNullOrEmpty(
                because: "the requirement is PRESENCE-ONLY on purpose. A version floor is unsatisfiable here: "
                    + "Creatio does not rewrite a package's SysPackage row when it re-installs a package it "
                    + "already has, so the recorded version stays whatever the FIRST install wrote and a raised "
                    + "floor would refuse this command forever on an environment that was upgraded correctly. "
                    + "Verified on both runtimes 2026-08-05, with the mechanism in "
                    + "PackageDBStorage.SavePackageDescriptor's GetIsPackageDescriptorModified guard");
            requirement.Hint.Should().Be(ExpectedProcessBuilderHint,
                because: "the install hint must be consistent across all process-designer gates");
        }

        [Test]
        [Description("get-process-signature must NOT be gated on the process-builder package: it reads the built-in DataService (ProcessSchemaRequest / VwProcessLib), not ProcessDesignService, so gating its public CLI verb on the experimental package was a shipped-capability regression (PR #715).")]
        public void GetProcessSignatureOptions_ShouldNotDeclareProcessBuilderRequirement_BecauseItUsesTheBuiltInDataService()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(typeof(GetProcessSignatureOptions));

            // Assert
            requirement.Should().BeNull(
                because: "get-process-signature works against the built-in DataService on every Creatio; requiring the process-builder package broke the public 'gps' verb on environments without it");
        }

        [Test]
        [Description("The validate-process-graph args record must carry the same versioned requirement, because the standalone tool manually calls EnsureRequirements(args) which reads the attribute off the args type rather than an options class.")]
        public void ValidateProcessGraphArgs_ShouldDeclarePresenceOnlyProcessBuilderRequirement_WhenStandaloneTool()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(
                typeof(Clio.Command.McpServer.Tools.ProcessDesigner.ValidateProcessGraphArgs));

            // Assert
            requirement.Should().NotBeNull(
                because: "the standalone validator reads [RequiresPackage] off the args record, so the gate "
                    + "would silently not fire if the attribute moved to an options class");
            requirement!.Version.Should().BeNullOrEmpty(
                because: "the requirement is PRESENCE-ONLY on purpose. A version floor is unsatisfiable here: "
                    + "Creatio does not rewrite a package's SysPackage row when it re-installs a package it "
                    + "already has, so the recorded version stays whatever the FIRST install wrote and a raised "
                    + "floor would refuse this command forever on an environment that was upgraded correctly. "
                    + "Verified on both runtimes 2026-08-05, with the mechanism in "
                    + "PackageDBStorage.SavePackageDescriptor's GetIsPackageDescriptorModified guard");
            requirement.Hint.Should().Be(ExpectedProcessBuilderHint,
                because: "the validator hint must match the other process-designer gates");
        }

        [Test]
        [Description("The describe-business-process MCP args record must NOT carry [RequiresPackage]: the requirement belongs on the command OPTIONS type (DescribeProcessOptions), which the centralized BaseTool gate reads, not on the MCP args record.")]
        public void DescribeProcessArgs_ShouldNotDeclareAnyPackageRequirement_BecauseGateReadsOptionsType()
        {
            // Arrange & Act
            bool hasRequirement = typeof(Clio.Command.McpServer.Tools.ProcessDesigner.DescribeProcessArgs)
                .IsDefined(typeof(RequiresPackageAttribute), inherit: false);

            // Assert
            hasRequirement.Should().BeFalse(
                because: "the gate reads [RequiresPackage] off the command options type (T in BaseTool<T>), so the stray attribute on the args record was incorrect and must be removed");
        }
    }

    [TestFixture]
    [Category("Unit")]
    [Property("Module", "Core")]
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
