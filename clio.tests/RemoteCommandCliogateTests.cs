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
    [Category("Unit")]
    [Property("Module", "Core")]
    [Description("Unit tests confirming RemoteCommand no longer self-gates on cliogate; package requirements are now declarative ([RequiresPackage]) and enforced at the dispatch chokepoints, not inside RemoteCommand.Execute.")]
    public class RemoteCommandCliogateTests
    {
        private class TestOptions : RemoteCommandOptions { }

        private class TestRemoteCommand : RemoteCommand<TestOptions>
        {
            public bool RemoteCommandExecuted { get; private set; }

            public TestRemoteCommand(string servicePath, IClioGateway clioGateway)
            {
                ServicePath = servicePath;
                ClioGateWay = clioGateway;
                Logger = Substitute.For<ILogger>();
            }

            protected override void ExecuteRemoteCommand(TestOptions options) => RemoteCommandExecuted = true;
            protected override void ProceedResponse(string response, TestOptions options) { }
            protected override string ServicePath { get; set; }
        }

        [Test]
        [Description("RemoteCommand.Execute must run the remote command even for a cliogate ServicePath when no gateway is present, because the cliogate requirement is now enforced declaratively by [RequiresPackage] at the dispatch chokepoint, not inside RemoteCommand.")]
        public void Execute_ShouldRunRemoteCommand_WhenCliogateServicePathAndNoGateway()
        {
            // Arrange
            var cmd = new TestRemoteCommand("/rest/CreatioApiGateway/GetSysInfo", clioGateway: null);

            // Act
            var result = cmd.Execute(new TestOptions());

            // Assert
            result.Should().Be(0,
                because: "RemoteCommand no longer self-gates on cliogate; it must proceed regardless of gateway presence");
            cmd.RemoteCommandExecuted.Should().BeTrue(
                because: "the remote command must be dispatched without an inline cliogate pre-check");
        }

        [Test]
        [Description("RemoteCommand.Execute must never call IClioGateway.CheckCompatibleVersion, because the legacy inline version gate has been removed in favour of [RequiresPackage].")]
        public void Execute_ShouldNotCallCheckCompatibleVersion_WhenGatewayPresent()
        {
            // Arrange
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("/rest/CreatioApiGateway/GetSysInfo", gateway);

            // Act
            cmd.Execute(new TestOptions());

            // Assert
            gateway.DidNotReceive().CheckCompatibleVersion(Arg.Any<string>());
        }

        [Test]
        [Description("RemoteCommand.Execute must run a non-cliogate ServicePath command as before, confirming the removed gate did not change the happy path.")]
        public void Execute_ShouldRunRemoteCommand_WhenServicePathDoesNotRequireCliogate()
        {
            // Arrange
            var cmd = new TestRemoteCommand("/api/SomeOtherService", clioGateway: null);

            // Act
            var result = cmd.Execute(new TestOptions());

            // Assert
            result.Should().Be(0,
                because: "a non-cliogate ServicePath was never gated and must still execute");
            cmd.RemoteCommandExecuted.Should().BeTrue(
                because: "the remote command must be dispatched normally");
        }
    }

    [TestFixture]
    [Category("Unit")]
    [Property("Module", "Command")]
    [Description("Reflection tests asserting the cliogate package requirement migrated from the legacy RemoteCommand gate onto the command options classes via [RequiresPackage].")]
    public class CliogateRequiresPackageAttributeTests
    {
        private const string ExpectedCliogateHint =
            "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.";

        private static RequiresPackageAttribute GetCliogateRequirement(Type optionsType)
            => (RequiresPackageAttribute[])optionsType.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: false)
                is { Length: > 0 } attrs
                ? System.Array.Find(attrs, a => string.Equals(a.Name, "cliogate", System.StringComparison.OrdinalIgnoreCase))
                : null;

        [TestCase(typeof(LockPackageOptions), "2.0.0.42")]
        [TestCase(typeof(UnlockPackageOptions), "2.0.0.42")]
        [TestCase(typeof(RestoreWorkspaceOptions), "2.0.0.0")]
        [TestCase(typeof(Clio.Command.SqlScriptCommand.ExecuteSqlScriptOptions), "2.0.0.41")]
        [Test]
        [Description("Each migrated versioned command options class must declare [RequiresPackage(\"cliogate\", <version>)] so the requirement is enforced at the dispatch chokepoint.")]
        public void OptionsType_ShouldDeclareVersionedCliogateRequirement_WhenCommandWasMigrated(
            Type optionsType, string expectedVersion)
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetCliogateRequirement(optionsType);

            // Assert
            requirement.Should().NotBeNull(
                because: $"{optionsType.Name} must carry the declarative cliogate requirement after migration");
            requirement!.Version.Should().Be(expectedVersion,
                because: "the migrated version must match the legacy ClioGateMinVersion value");
            requirement.Hint.Should().Be(ExpectedCliogateHint,
                because: "the cliogate install hint must be restored so the unmet-requirement error tells the user to run install-gate");
        }

        [Test]
        [Description("show-package-file-content relied on the implicit ServicePath trigger that never enforced a version, so its migrated requirement must be presence-only (no version).")]
        public void ShowPackageFileContentOptions_ShouldDeclarePresenceOnlyCliogateRequirement_WhenMigrated()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetCliogateRequirement(typeof(ShowPackageFileContentOptions));

            // Assert
            requirement.Should().NotBeNull(
                because: "show-package-file-content requires cliogate to be installed");
            requirement!.Version.Should().BeNullOrEmpty(
                because: "the legacy implicit ServicePath trigger never enforced a version, so the requirement is presence-only");
            requirement.Hint.Should().Be(ExpectedCliogateHint,
                because: "the cliogate install hint must be restored even for the presence-only requirement");
        }

        [Test]
        [Description("get-info must NOT carry [RequiresPackage] because it degrades gracefully to ApplicationInfoService when cliogate is absent or old, instead of hard-failing.")]
        public void GetCreatioInfoCommandOptions_ShouldNotDeclareCliogateRequirement_BecauseItDegradesGracefully()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetCliogateRequirement(typeof(GetCreatioInfoCommandOptions));

            // Assert
            requirement.Should().BeNull(
                because: "get-info must stay reachable without cliogate and fall back to ApplicationInfoService");
        }
    }

    [TestFixture]
    [Category("Unit")]
    [Property("Module", "Command")]
    [Description("Reflection lock-in tests asserting the four process-designer command options classes are gated on the clioprocessbuilder package (presence-only), and that the MCP args record no longer carries a stray requirement.")]
    public class ProcessDesignerRequiresPackageAttributeTests
    {
        private const string ExpectedProcessBuilderHint =
            "This experimental feature requires the clioprocessbuilder package on the target environment.";

        private static RequiresPackageAttribute GetProcessBuilderRequirement(Type type)
            => (RequiresPackageAttribute[])type.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: false)
                is { Length: > 0 } attrs
                ? System.Array.Find(attrs, a => string.Equals(a.Name, "clioprocessbuilder", System.StringComparison.OrdinalIgnoreCase))
                : null;

        [TestCase(typeof(CreateBusinessProcessOptions))]
        [TestCase(typeof(ModifyBusinessProcessOptions))]
        [TestCase(typeof(DescribeProcessOptions))]
        [Test]
        [Description("Each process-designer command options class that actually calls ProcessDesignService must declare [RequiresPackage(\"clioprocessbuilder\")] with no version (presence-only) so the centralized BaseTool.ResolveCommand gate enforces the requirement uniformly. (get-process-signature is excluded — it uses the built-in DataService; see the negative test below.)")]
        public void OptionsType_ShouldDeclarePresenceOnlyProcessBuilderRequirement_WhenProcessDesignerCommand(
            Type optionsType)
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(optionsType);

            // Assert
            requirement.Should().NotBeNull(
                because: $"{optionsType.Name} must carry the declarative clioprocessbuilder requirement so the MCP gate fires");
            requirement!.Version.Should().BeNullOrEmpty(
                because: "the process-designer requirement is presence-only (any installed version satisfies it)");
            requirement.Hint.Should().Be(ExpectedProcessBuilderHint,
                because: "the install hint must be consistent across all process-designer gates");
        }

        [Test]
        [Description("get-process-signature must NOT carry [RequiresPackage(\"clioprocessbuilder\")]: it reads the built-in DataService (ProcessSchemaRequest / VwProcessLib), not ProcessDesignService, so gating its public CLI verb on the experimental package was a shipped-capability regression (PR #715).")]
        public void GetProcessSignatureOptions_ShouldNotDeclareProcessBuilderRequirement_BecauseItUsesTheBuiltInDataService()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(typeof(GetProcessSignatureOptions));

            // Assert
            requirement.Should().BeNull(
                because: "get-process-signature works against the built-in DataService on every Creatio; requiring clioprocessbuilder broke the public 'gps' verb on environments without the experimental package");
        }

        [Test]
        [Description("The validate-process-graph args record must carry the presence-only clioprocessbuilder requirement, because the standalone tool manually calls EnsureRequirements(args) which reads the attribute off the args type.")]
        public void ValidateProcessGraphArgs_ShouldDeclarePresenceOnlyProcessBuilderRequirement_WhenStandaloneTool()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(
                typeof(Clio.Command.McpServer.Tools.ProcessDesigner.ValidateProcessGraphArgs));

            // Assert
            requirement.Should().NotBeNull(
                because: "the standalone validator reads [RequiresPackage] off the args record to gate on clioprocessbuilder");
            requirement!.Version.Should().BeNullOrEmpty(
                because: "the validator requirement is presence-only, consistent with the BaseTool process tools");
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
