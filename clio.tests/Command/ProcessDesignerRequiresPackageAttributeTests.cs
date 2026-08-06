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
        [Description("Each process-designer command options class that actually calls ProcessDesignService must be gated on the bundled package NAME and VERSION, so the gate refuses both a missing and a stale package. (get-process-signature is excluded — it uses the built-in DataService; see the negative test below.)")]
        public void OptionsType_ShouldDeclareVersionedProcessBuilderRequirement_WhenProcessDesignerCommand(
            Type optionsType)
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(optionsType);

            // Assert
            requirement.Should().NotBeNull(
                because: $"{optionsType.Name} must carry the declarative {BundledPackages.ProcessBuilderPackageName} requirement so the MCP gate fires");
            requirement!.Version.Should().Be(BundledPackages.ProcessBuilderVersion,
                because: "the floor must equal the version clio bundles: a lower floor accepts a stale package "
                    + "whose server rejects operations this clio sends, and the refusal then comes from the "
                    + "server as an unexplained error instead of from the gate with an install hint");
            System.Version.TryParse(requirement.Version, out System.Version floor).Should().BeTrue(
                because: $"RequiredPackageChecker parses the floor through System.Version, so "
                    + $"'{requirement.Version}' must be parseable or every gated command throws instead of gating");
            floor!.Revision.Should().BeGreaterThanOrEqualTo(0,
                because: $"'{requirement.Version}' must carry all four parts, matching the archive descriptor: "
                    + "a part-count mismatch compares as installed < required and makes the gate unsatisfiable "
                    + "by any successful install");
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
        public void ValidateProcessGraphArgs_ShouldDeclareVersionedProcessBuilderRequirement_WhenStandaloneTool()
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(
                typeof(Clio.Command.McpServer.Tools.ProcessDesigner.ValidateProcessGraphArgs));

            // Assert
            requirement.Should().NotBeNull(
                because: "the standalone validator reads [RequiresPackage] off the args record, so the gate "
                    + "would silently not fire if the attribute moved to an options class");
            requirement!.Version.Should().Be(BundledPackages.ProcessBuilderVersion,
                because: "the floor must equal the version clio bundles: a lower floor accepts a stale package "
                    + "whose server rejects operations this clio sends, and the refusal then comes from the "
                    + "server as an unexplained error instead of from the gate with an install hint");
            System.Version.TryParse(requirement.Version, out System.Version floor).Should().BeTrue(
                because: $"RequiredPackageChecker parses the floor through System.Version, so "
                    + $"'{requirement.Version}' must be parseable or every gated command throws instead of gating");
            floor!.Revision.Should().BeGreaterThanOrEqualTo(0,
                because: $"'{requirement.Version}' must carry all four parts, matching the archive descriptor: "
                    + "a part-count mismatch compares as installed < required and makes the gate unsatisfiable "
                    + "by any successful install");
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
}
