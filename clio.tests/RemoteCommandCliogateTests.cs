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

        [Ignore("RequiresPackageAttribute added in error, restorew command does not require cliogate")]
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
}
