using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class EntitySchemaDependencyResolverTests
{

	private const string SelectQueryUrl = "http://local/DataService/json/SyncReply/SelectQuery";

	/// <summary>
	/// Identity of the package being edited, as the caller already holds it. The dependency read must use
	/// it rather than resolve the name again through the whole installed-package list (issue #1461).
	/// </summary>
	private static readonly Guid TargetPackageUId = new("6f1b6b4a-7f2e-4a4c-9a1e-2f3d4c5b6a70");

	private FindEntitySchemaCommand _findCommand;
	private IPackageDependencyManager _dependencyManager;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private EntitySchemaDependencyResolver _resolver;

	[SetUp]
	public void Setup() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectQueryUrl);
		_logger = Substitute.For<ILogger>();
		_findCommand = Substitute.For<FindEntitySchemaCommand>(_applicationClient, _serviceUrlBuilder, _logger);
		_dependencyManager = Substitute.For<IPackageDependencyManager>();
		// Stubbed in Setup rather than per test: an unstubbed IReadOnlyList<string> member answers with an
		// empty collection, which happens to be the "no existing dependencies" case, so a test that meant to
		// exercise the filter would silently pass without it.
		_dependencyManager.GetDependencies(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>()).Returns([]);
		SetInstalledApplications();
		_resolver = new EntitySchemaDependencyResolver(_findCommand, _dependencyManager, _applicationClient,
			_serviceUrlBuilder, _logger);
	}

	[TearDown]
	public void TearDown() {
		_findCommand.ClearReceivedCalls();
		_dependencyManager.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	/// <summary>
	/// Answers the installed-application SelectQuery with the supplied application root package codes.
	/// </summary>
	/// <param name="applicationCodes">Root package names to report as installed applications.</param>
	private void SetInstalledApplications(params string[] applicationCodes) {
		string rows = string.Join(",", applicationCodes.Select(code => $"{{\"Code\":\"{code}\",\"Name\":\"{code}\"}}"));
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns($"{{\"success\":true,\"rows\":[{rows}]}}");
	}

	/// <summary>Stubs the schema search used to find contributing packages.</summary>
	/// <param name="packageNames">Packages to report as contributing the schema.</param>
	private void SetContributingPackages(params string[] packageNames) {
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Returns(packageNames.Select(Result).ToList());
	}

	private static EntitySchemaSearchResult Result(string packageName) =>
		new("Opportunity", packageName, "Creatio", "Opportunity");

	/// <summary>Every warning the resolver wrote, in order, so a test can assert on the set as a whole.</summary>
	/// <returns>The warning texts.</returns>
	private List<string> Warnings() =>
		_logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => (string)call.GetArguments()[0]!)
			.ToList();

	[Test]
	[Description("Reports the single candidate without touching the package, because the failing designer response carries no evidence that a missing dependency is the cause (issue #722).")]
	public void Resolve_ShouldReportTheCandidateWithoutWriting_WhenExactlyOneCandidateExists() {
		// Arrange
		SetContributingPackages("CrtLeadOppMgmtApp", "Custom");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "the caller needs the concrete package name to act on");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
	}

	[Test]
	[Description("Never writes a package dependency on any path: the designer answers a genuine SchemaIsNotAvailableException with a generic WCF error page that is indistinguishable from a WAF block or a transient fault, so no evidence for such a write exists (issue #722).")]
	public void Resolve_ShouldNeverAddADependency_WhateverTheCandidateCount() {
		// Arrange
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		_resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
		_dependencyManager.DidNotReceive()
			.RemoveDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
	}

	[Test]
	[Description("Reports every candidate ranked with installed applications first - the case a standard schema always lands in, and the one the previous blanket refusal reported to nobody (issue #722).")]
	public void Resolve_ShouldReportRankedCandidates_WhenMultipleCandidatesExist() {
		// Arrange
		SetInstalledApplications("CrtLeadOppMgmtApp", "SalesEnterprise");
		SetContributingPackages("CoreLeadOpportunity", "CrtLeadOppMgmtApp", "SalesEnterprise", "Custom");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp", "SalesEnterprise", "CoreLeadOpportunity"],
			because: "installed applications must be ranked first so the caller reads the likely answer first, and each group must be ordered so the reported list is stable");
		resolution.ApplicationCandidateCount.Should().Be(2,
			because: "the caller needs to know how many of the leading entries carry the application ranking signal");
	}

	[Test]
	[Description("Excludes the target package from the dependency candidates so it never proposes a self-dependency (ENG-91314).")]
	public void Resolve_ShouldExcludeTargetPackage_WhenSchemaExistsInTargetToo() {
		// Arrange
		SetContributingPackages("Custom", "CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "only CrtLeadOppMgmtApp should remain after excluding the target package");
	}

	[Test]
	[Description("Drops packages the target already depends on, so a candidate list never proposes a dependency that is already declared (issue #722).")]
	public void Resolve_ShouldExcludeExistingDependencies_WhenTargetAlreadyDependsOnACandidate() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>()).Returns(["crtcore", "CoreLeadOpportunity"]);
		SetContributingPackages("CoreLeadOpportunity", "CrtLeadOppMgmtApp", "CrtCore");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "an already-declared dependency is not a fix, and the match must ignore case as package names do");
		resolution.DependenciesKnown.Should().BeTrue(
			because: "the dependency read succeeded, so the caller may state that the list is filtered");
	}

	[Test]
	[Description("Reports nothing at all when every contributing package is already a dependency, because a missing dependency is then not what the caller is looking at (issue #722).")]
	public void Resolve_ShouldReportNoCandidates_WhenEveryContributorIsAlreadyADependency() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>()).Returns(["CrtLeadOppMgmtApp"]);
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().BeEmpty(
			because: "with no addable candidate there is no evidence for a missing dependency and none must be claimed");
		resolution.LookupSucceeded.Should().BeTrue(
			because: "the search ran to completion, so its empty answer is a finding of fact rather than a missing answer");
	}

	[Test]
	[Description("Still reports the candidates, unranked, when the installed-application lookup used to rank them fails (issue #722).")]
	public void Resolve_ShouldStillReportCandidates_WhenApplicationRankingLookupFails() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery unavailable"));
		SetContributingPackages("CrtLeadOppMgmtApp", "CoreLeadOpportunity");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CoreLeadOpportunity", "CrtLeadOppMgmtApp"],
			because: "ranking is an ordering hint, so losing it must degrade to an ordered list, never suppress the diagnosis");
		resolution.ApplicationCandidateCount.Should().Be(0,
			because: "with no ranking signal the caller must not be told the order means anything");
	}

	[Test]
	[Description("Marks the candidate list as unfiltered when the current-dependencies read failed, so the caller can carry that caveat into the message instead of asserting the list excludes declared dependencies (issue #722).")]
	public void Resolve_ShouldReportDependenciesUnknown_WhenTheExistingDependencyReadFailed() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery failed"));
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.DependenciesKnown.Should().BeFalse(
			because: "the subtraction that removes already-declared packages was a no-op, and the caller must say so");
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "a degraded read must withhold the claim, not the diagnosis");
	}

	[Test]
	[Description("Tells the caller the candidate search itself failed, rather than returning an empty list that reads as 'no package contributes this schema' (issue #722).")]
	public void Resolve_ShouldReportLookupFailure_WhenTheSchemaSearchThrows() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery unreachable"));

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.LookupSucceeded.Should().BeFalse(
			because: "a search that never completed is the absence of an answer, not the answer 'nothing contributes this schema'");
		resolution.LookupFailureReason.Should().Contain("SelectQuery unreachable",
			because: "the reason must travel in the result, since the log warning never reaches an MCP client");
		resolution.Candidates.Should().BeEmpty(because: "nothing was found");
	}

	[Test]
	[Description("Bounds every remote read it adds to an already-failing path, so an environment that accepts the connection and then stops answering costs a bounded wait rather than wedging the caller (issue #722).")]
	public void Resolve_ShouldBoundEveryDiagnosticRead_WhenEnrichingAFailure() {
		// Arrange
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		_resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		_findCommand.Received(1).FindSchemas(Arg.Any<FindEntitySchemaOptions>(),
			Arg.Is<int>(timeout => timeout > 0 && timeout != Timeout.Infinite));
		_dependencyManager.Received(1).GetDependencies(TargetPackageUId, "Custom",
			Arg.Is<int>(timeout => timeout > 0 && timeout != Timeout.Infinite));
		_applicationClient.Received().ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(),
			Arg.Is<int>(timeout => timeout > 0 && timeout != Timeout.Infinite), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Redacts and bounds the failure text it reports, because SelectQuery falls back to the raw response body when the server answers success:false with no errorInfo (issue #722).")]
	public void Resolve_ShouldRedactAndBoundFailures_WhenTheLookupFailsWithASecretBearingMessage() {
		// Arrange
		string secretBearingMessage =
			"SelectQuery failed: {\"Message\":\"Authentication failed.\",\"token\":\"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln\"} "
			+ new string('x', 600);
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Throws(new InvalidOperationException(secretBearingMessage));

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		List<string> warnings = Warnings();
		warnings.Should().ContainSingle(because: "the failed lookup must be reported exactly once");
		warnings[0].Should().NotContain("eyJzdWIiOiIxIn0",
			because: "an un-redacted server body reaching a warning is the same leak this change removes from the error messages");
		resolution.LookupFailureReason.Should().NotContain("eyJzdWIiOiIxIn0",
			because: "the reason is surfaced to the caller, so it must be redacted before it is carried there");
		resolution.LookupFailureReason!.Length.Should().BeLessThan(secretBearingMessage.Length,
			because: "the failure text must be bounded rather than copied whole into an agent transcript");
	}

	[Test]
	[Description("Returns no candidates when no other package contains the schema (ENG-91314). The dependency read is no longer skipped in this case: it runs alongside the schema search, so it costs a round-trip but no wall-clock time (issue #1461).")]
	public void Resolve_ShouldReportNoCandidates_WhenSchemaNotFoundInOtherPackages() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>()).Returns([]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("UsrNonExistent", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().BeEmpty(
			because: "there are no candidate packages to report");
		resolution.LookupSucceeded.Should().BeTrue(
			because: "the search ran to completion, so its empty answer is a finding of fact");
		_dependencyManager.Received(1).GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>());
		_applicationClient.DidNotReceive().ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Starts the schema search and the dependency read before either has finished, so the diagnostic costs the slower of the two reads rather than their sum on an environment that has stopped answering (issue #1461).")]
	public void Resolve_ShouldRunTheSchemaSearchAndTheDependencyReadConcurrently_WhenEnrichingAFailure() {
		// Arrange — a two-sided rendezvous, so NO sequential order can pass: whichever read runs first blocks
		// in SignalAndWait until the other arrives, and a sequential implementation never sends it. The
		// timeout makes that a failed assertion within ten seconds rather than a hung test run.
		using Barrier bothReadsInFlight = new(2);
		bool schemaSearchMetTheDependencyRead = false;
		bool dependencyReadMetTheSchemaSearch = false;
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>())
			.Returns(_ => {
				dependencyReadMetTheSchemaSearch = bothReadsInFlight.SignalAndWait(TimeSpan.FromSeconds(10));
				return new List<string>();
			});
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Returns(_ => {
				schemaSearchMetTheDependencyRead = bothReadsInFlight.SignalAndWait(TimeSpan.FromSeconds(10));
				return new List<EntitySchemaSearchResult> { Result("CrtLeadOppMgmtApp") };
			});

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		schemaSearchMetTheDependencyRead.Should().BeTrue(
			because: "the schema search must still be running when the dependency read starts");
		dependencyReadMetTheSchemaSearch.Should().BeTrue(
			because: "the dependency read must still be running when the schema search starts - asserting only one direction would let a sequential implementation that simply reordered the reads pass");
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "running the reads concurrently must not change what they report");
	}

	[Test]
	[Description("Gives up on the offloaded schema search after a bounded wait, so a thread pool that never schedules it cannot make the diagnosis wait without end (issue #1461).")]
	public void Resolve_ShouldReportLookupFailure_WhenTheOffloadedSchemaSearchDoesNotFinishInTime() {
		// Arrange
		using ManualResetEventSlim releaseTheSchemaSearch = new();
		_resolver.ContributorsWaitTimeoutMs = 200;
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Returns(_ => {
				// Stands in for a read that never answers. Released in the assert phase so the test leaves no
				// thread blocked behind it.
				releaseTheSchemaSearch.Wait(TimeSpan.FromSeconds(30));
				return new List<EntitySchemaSearchResult>();
			});

		try {
			// Act
			EntitySchemaDependencyResolution resolution =
				_resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

			// Assert
			resolution.LookupSucceeded.Should().BeFalse(
				because: "a search that was never scheduled is the absence of an answer, not the answer 'nothing contributes this schema'");
			resolution.LookupFailureReason.Should().Contain("did not finish within",
				because: "the caller must be told the lookup was abandoned rather than completed");
		} finally {
			releaseTheSchemaSearch.Set();
		}
	}

	[Test]
	[Description("Says nothing about an unfiltered candidate list when there is no candidate list, because the dependency-read warning now travels into the MCP tool result and would describe a list that was never built (issue #1461).")]
	public void Resolve_ShouldNotWarnAboutTheCandidateList_WhenTheDependencyReadFailedAndNothingContributes() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>())
			.Throws(new InvalidOperationException("GetPackageProperties unreachable"));
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>()).Returns([]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("UsrNonExistent", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().BeEmpty(because: "no package contributes the schema");
		Warnings().Should().BeEmpty(
			because: "a caller whose lookup produced no candidate list must not be told that list may be unfiltered");
	}

	[Test]
	[Description("Says nothing about an unfiltered candidate list when the schema search itself failed, so the only failure reported is the one that actually stopped the lookup (issue #1461).")]
	public void Resolve_ShouldReportOnlyTheLookupFailure_WhenBothReadsFailed() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>())
			.Throws(new InvalidOperationException("GetPackageProperties unreachable"));
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery unreachable"));

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.LookupSucceeded.Should().BeFalse(because: "the search never completed");
		Warnings().Should().ContainSingle(
				because: "only the failure that stopped the lookup may be reported; the dependency read's failure describes a list that was never built")
			.Which.Should().Contain("SelectQuery unreachable",
				because: "the reported failure must be the one that stopped the lookup");
	}

	[Test]
	[Description("Still warns that the candidate list is unfiltered when the dependency read failed but candidates were found, because that list really may contain packages the target already depends on (issue #722).")]
	public void Resolve_ShouldWarnAboutTheCandidateList_WhenTheDependencyReadFailedAndCandidatesExist() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>())
			.Throws(new InvalidOperationException("GetPackageProperties unreachable"));
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		_resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		Warnings().Should().ContainSingle(
				because: "the caveat belongs to the list, so it is stated exactly once, where the list exists")
			.Which.Should().Contain("already dependencies",
				because: "the caller must learn that the list was never filtered");
	}

	[Test]
	[Description("Reports the schema search's own failure rather than the wrapper a blocked task would raise, so the caller reads the reason the environment gave (issue #1461).")]
	public void Resolve_ShouldReportTheOriginalFailure_WhenTheConcurrentSchemaSearchThrows() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery unreachable"));

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.LookupFailureReason.Should().Be("SelectQuery unreachable",
			because: "awaiting the offloaded read must rethrow the original exception, not an AggregateException whose text buries the reason behind 'One or more errors occurred'");
	}

	[Test]
	[Description("Still reports the candidates, marked unfiltered, when the dependency read fails while the schema search succeeds - one read failing must never discard the other's result (issue #1461).")]
	public void Resolve_ShouldKeepTheSchemaSearchResult_WhenTheConcurrentDependencyReadFails() {
		// Arrange
		_dependencyManager.GetDependencies(TargetPackageUId, "Custom", Arg.Any<int>())
			.Throws(new InvalidOperationException("GetPackageProperties unreachable"));
		SetContributingPackages("CrtLeadOppMgmtApp", "CoreLeadOpportunity");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CoreLeadOpportunity", "CrtLeadOppMgmtApp"],
			because: "the read that did answer must still be reported when the other one failed");
		resolution.DependenciesKnown.Should().BeFalse(
			because: "the caller must carry the caveat that the list was never filtered");
	}

	[Test]
	[Description("Deduplicates package names when the same schema appears multiple times in the same package (ENG-91314).")]
	public void Resolve_ShouldDeduplicateCandidates_WhenPackageAppearsMultipleTimes() {
		// Arrange
		SetContributingPackages("CrtLeadOppMgmtApp", "CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", TargetPackageUId, "Custom");

		// Assert
		resolution.Candidates.Should().ContainSingle(
			because: "duplicate package names must be collapsed into one candidate");
	}

}
