using System;
using System.Collections.Generic;
using System.Globalization;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
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
    // Also Common: the D8 tests at the end of this fixture exercise clio/Common/RequiredPackageChecker.cs and
    // clio/Common/BundledPackageConvergence.cs, so the targeted filter a developer runs after editing the
    // convergence rule (Category=Unit&Module=Common) has to reach them.
    [Property("Module", "Common")]
    [Description("Reflection lock-in tests asserting the four process-designer command options classes are gated on the bundled CrtProcessBuilder package, that the requirement is presence-only, that the hint names the install command, and that the MCP args record carries the same requirement.")]
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

        [TestCase(typeof(DescribeProcessOptions))]
        [TestCase(typeof(ListUserTasksOptions))]
        [Test]
        [Description("The READ-side process-designer options classes are gated on the bundled package NAME presence-only: no operation they call was introduced in a particular version, and an older server merely degrades their read-back. Create/Modify are deliberately NOT here any more — their email operation shipped in the 1.2.0.1 archive, so this test's original premise expired for them; see the versioned-requirement test below. (get-process-signature is excluded — it uses the built-in DataService; see the negative test below.)")]
        public void OptionsType_ShouldDeclarePresenceOnlyProcessBuilderRequirement_WhenProcessDesignerCommand(
            Type optionsType)
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(optionsType);

            // Assert
            requirement.Should().NotBeNull(
                because: $"{optionsType.Name} must carry the declarative {BundledPackages.ProcessBuilderPackageName} requirement so the MCP gate fires");
            requirement!.Version.Should().BeNullOrEmpty(
                because: "the attribute states what THIS command needs in order to work, and each of these "
                    + "fails only when the package is absent entirely — no operation any of them calls was "
                    + "introduced in a particular version. Keeping an environment current is the separate "
                    + "convergence rule's job (IBundledPackageConvergence), which compares against the "
                    + "archive; a literal here would restate that policy in a place that cannot track it");
            requirement.Hint.Should().Be(ExpectedProcessBuilderHint,
                because: "the install hint must be consistent across all process-designer gates");
        }

        [TestCase(typeof(CreateBusinessProcessOptions))]
        [TestCase(typeof(ModifyBusinessProcessOptions))]
        [Test]
        [Description("Create and Modify declare a VERSIONED requirement naming the newest operation they send that an older server does not have: `setFlowCondition`, which an older dispatch registry rejects by token. The floor's NUMBER is now set by the message contract rather than by a tightened validator. Until 1.4.0.41 the package validated formulas itself, and the floor tracked when each of its refusals arrived, measured one archive at a time (.32, .35, .37). .41 DELETED that validator, and .42 corrected the rewrite that replaced it (every serialised error in one message, not just the first; an element-scoped reference named as such), because the platform's own pre-save gate already refuses every class of bad formula — a flow condition included, measured with the package's guards built out and installed. So the floor no longer says 'below this a bad formula is not refused'; it says 'below this a refusal reads differently', which is what the shipped tool descriptions promise. Do not lower it below .37 either: the refusals that survive the collapse (the activity-result guard, the element-retarget scan) were measured there. The bundled-archive guard asserts the shipped archive satisfies the literal, so it can never demand a version clio does not carry.")]
        public void OptionsType_ShouldDeclareVersionedProcessBuilderRequirement_WhenTheCommandShipsVersionedOperations(
            Type optionsType)
        {
            // Arrange & Act
            RequiresPackageAttribute requirement = GetProcessBuilderRequirement(optionsType);

            // Assert
            requirement.Should().NotBeNull(
                because: $"{optionsType.Name} must carry the declarative {BundledPackages.ProcessBuilderPackageName} requirement so the MCP gate fires");
            requirement!.Version.Should().Be("1.4.0.58",
                because: "TWO reasons stand behind this floor. ENG-96325's lookup-constant contract shipped in the 1.4.0.40 archive - a mappings[] value on a Lookup target may carry an already-composed macro that an older server rejects as 'not a bare Guid' - and setFlowCondition is an operation an older server does not carry AT ALL — its dispatch "
                    + "registry rejects the token, which reads to a caller as a clio bug rather than a stale "
                    + "environment — and that alone justifies a versioned floor. What sets the NUMBER changed with "
                    + "the formula collapse. The formula half used to be a TIGHTENED VALIDATOR, measured one "
                    + "archive at a time up to .37; .41 is the version that REMOVED that validator, on the "
                    + "measurement that the platform's own pre-save gate already refuses every class of bad "
                    + "formula, a flow condition included (spec/eng-95891-formula-expressions/"
                    + "eng-95891-formula-expressions-save-gate-probe.md). So .41 checks strictly LESS than .37 "
                    + "did, an environment between them refuses at least as much, and the floor now buys the "
                    + "MESSAGE contract the tool descriptions promise rather than the existence of a refusal: "
                    + "below .41 a bad formula is refused in the package's own wording, and an unresolvable "
                    + "parameter reference comes back as the platform's serialised {ErrorType:2,ErrorData:{…}} "
                    + "rather than as a sentence, because PlatformValidationMessage is not there. Do NOT lower it "
                    + "to .37 on the grounds that .37 also refuses — it does, with different text — and do not "
                    + "lower it below .37 on any grounds, because the refusals that SURVIVE the collapse were "
                    + "measured there: the activity-result guard in .32, and the element-retarget refusal's two "
                    + "fail-open holes (a dependency scan that failed OPEN on a sub-process reference, and an "
                    + "element UId matched in D-form only) closed in .37. The platform-grammar element segment "
                    + ".35 added is deliberately NOT in that list any more: the strict pattern it mirrored is used "
                    + "only for data-source filter map paths, so a parameter value's element scoping survived the "
                    + "looser form and the refusal protected nothing. "
                    + "ENG-91853 raised it to .58, and THAT one is a plain capability floor rather than a message one: below it the package refuses flows[].kind, flows[].condition and the two gateway element tokens outright, so an agent following the descriptions is refused by the environment rather than by clio, one round-trip later and with no hint that the package is what is behind. This subsumes the earlier 1.3.1.1 performer floor and the 1.2.0.1 email floor. When the "
                    + "next versioned operation ships, move this pin WITH the rebundle in the same commit");
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
        [Description("The validate-process-graph args record must carry the same presence-only requirement, because the standalone tool manually calls EnsureRequirements(args) which reads the attribute off the args type rather than an options class.")]
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
                because: "it must state the same requirement as the four options classes above — the validator "
                    + "calls no operation the others do not");
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

        // ---------------------------------------------------------------------------------------------
        // Decision D8: the stale-package detector. Measured on a live stand, a request carrying a
        // future-shaped 'connections' array is answered NORMALLY by an older package, with the member
        // silently ignored - no contract implements IExtensibleDataObject, so the serializer drops unknown
        // members at every nesting level. A green log and a wrong process is the worst outcome in this
        // domain, so something has to notice. The decision is to rely on the CONVERGENCE rule rather than
        // build a second mechanism or reintroduce a version literal the pin above forbids: the rule already
        // refuses when the environment carries an older version than this clio ships, on every gated call,
        // and every rebundle is required to raise the shipped version. This test is what makes that
        // reliance real rather than assumed - it drives the SHIPPED attribute through the REAL checker and
        // the REAL convergence rule.
        [Test]
        [Description("D8 stale-package detector: an environment whose CrtProcessBuilder predates this clio's bundled archive is REFUSED on a gated process-designer call, with a message naming both versions and the install hint. This is the mechanism that stops an older package from silently ignoring a 'connections' array - it cannot be a version literal on the attribute, because that would restate a delivery policy in a place that cannot track the archive.")]
        public void ProcessDesignerGate_ShouldRefuse_WhenEnvironmentPackagePredatesTheBundledArchive()
        {
            // Arrange - the environment has the package (so the presence-only requirement is SATISFIED) but
            // at a version older than the one this distribution carries.
            IApplicationPackageListProvider packages = Substitute.For<IApplicationPackageListProvider>();
            packages.GetPackages().Returns([
                CreatePackageInfo(BundledPackages.ProcessBuilderPackageName, "1.0.0.0")
            ]);
            IBundledPackageCatalog catalog = Substitute.For<IBundledPackageCatalog>();
            catalog.IsBundled(BundledPackages.ProcessBuilderPackageName).Returns(true);
            catalog.TryGetVersion(BundledPackages.ProcessBuilderPackageName,
                    out Arg.Any<PackageVersion>(), out Arg.Any<string>())
                .Returns(call => {
                    call[1] = PackageVersion.ParseVersion("1.1.0.0");
                    call[2] = null;
                    return true;
                });
            IRequiredPackageChecker checker = new RequiredPackageChecker(packages,
                new BundledPackageConvergence(catalog, Substitute.For<ILogger>()));

            // Act - a SHIPPED options type that is presence-only BY DESIGN (see the split above): this test pins
            // the CONVERGENCE mechanism, and its own description says the refusal cannot be a version literal —
            // driving it through Modify, which now carries the versioned performer literal, would test the literal instead.
            Action act = () => checker.EnsureRequirements(new DescribeProcessOptions());

            // Assert
            PackageRequirementException refusal = act.Should().Throw<PackageRequirementException>(
                    because: "an older package answers a connections request normally and drops the member, so the "
                        + "only protection is refusing before the request is sent")
                .Which;
            refusal.Message.Should().Contain("1.0.0.0",
                because: "the reader has to see what the environment actually has, or they cannot tell a convergence "
                    + "refusal from a missing-package one");
            refusal.Message.Should().Contain("1.1.0.0",
                because: "and what this clio ships, which is the version the environment must be brought to");
            refusal.Message.Should().Contain("install-process-builder",
                because: "the refusal is only actionable if it names the verb that fixes it");
        }

        [Test]
        [Description("The same gate does NOT refuse when the environment is already at the bundled version: the detector must catch a stale package without blocking a current one, or every gated call on a correctly-installed environment would fail.")]
        public void ProcessDesignerGate_ShouldAllow_WhenEnvironmentPackageMatchesTheBundledArchive()
        {
            // Arrange
            IApplicationPackageListProvider packages = Substitute.For<IApplicationPackageListProvider>();
            packages.GetPackages().Returns([
                CreatePackageInfo(BundledPackages.ProcessBuilderPackageName, "1.1.0.0")
            ]);
            IBundledPackageCatalog catalog = Substitute.For<IBundledPackageCatalog>();
            catalog.IsBundled(BundledPackages.ProcessBuilderPackageName).Returns(true);
            catalog.TryGetVersion(BundledPackages.ProcessBuilderPackageName,
                    out Arg.Any<PackageVersion>(), out Arg.Any<string>())
                .Returns(call => {
                    call[1] = PackageVersion.ParseVersion("1.1.0.0");
                    call[2] = null;
                    return true;
                });
            IRequiredPackageChecker checker = new RequiredPackageChecker(packages,
                new BundledPackageConvergence(catalog, Substitute.For<ILogger>()));

            // Act - presence-only shipped type, same reason as the refuse test above.
            Action act = () => checker.EnsureRequirements(new DescribeProcessOptions());

            // Assert
            act.Should().NotThrow(
                because: "a converged environment must pass; a detector that refused here would take the whole "
                    + "process-designer surface down on a correct install");
            // Without this the test is vacuous: "did not throw" is equally satisfied by the gate having been
            // removed from the options type, or by the convergence call having been deleted outright. Asserting
            // that the rule was CONSULTED is what makes this a negative control rather than an absence of evidence.
            catalog.Received(1).TryGetVersion(BundledPackages.ProcessBuilderPackageName,
                out Arg.Any<PackageVersion>(), out Arg.Any<string>());
        }

        private static PackageInfo CreatePackageInfo(string name, string version)
        {
            PackageDescriptor descriptor = new() {
                DependsOn = new List<PackageDependency>(),
                UId = Guid.NewGuid(),
                Maintainer = "Fake_Maintainer",
                ModifiedOnUtc = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture),
                Name = name,
                PackageVersion = version,
                ProjectPath = string.Empty
            };
            return new PackageInfo(descriptor, string.Empty, new List<string>());
        }
    }
}
