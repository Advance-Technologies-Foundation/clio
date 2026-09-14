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
		_dependencyManager.GetDependencies(Arg.Any<string>(), Arg.Any<int>()).Returns([]);
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

	[Test]
	[Description("Reports the single candidate without touching the package, because the failing designer response carries no evidence that a missing dependency is the cause (issue #722).")]
	public void Resolve_ShouldReportTheCandidateWithoutWriting_WhenExactlyOneCandidateExists() {
		// Arrange
		SetContributingPackages("CrtLeadOppMgmtApp", "Custom");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		_resolver.Resolve("Opportunity", "Custom");

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
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "only CrtLeadOppMgmtApp should remain after excluding the target package");
	}

	[Test]
	[Description("Drops packages the target already depends on, so a candidate list never proposes a dependency that is already declared (issue #722).")]
	public void Resolve_ShouldExcludeExistingDependencies_WhenTargetAlreadyDependsOnACandidate() {
		// Arrange
		_dependencyManager.GetDependencies("Custom", Arg.Any<int>()).Returns(["crtcore", "CoreLeadOpportunity"]);
		SetContributingPackages("CoreLeadOpportunity", "CrtLeadOppMgmtApp", "CrtCore");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		_dependencyManager.GetDependencies("Custom", Arg.Any<int>()).Returns(["CrtLeadOppMgmtApp"]);
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		_dependencyManager.GetDependencies("Custom", Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery failed"));
		SetContributingPackages("CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

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
		_resolver.Resolve("Opportunity", "Custom");

		// Assert
		_findCommand.Received(1).FindSchemas(Arg.Any<FindEntitySchemaOptions>(),
			Arg.Is<int>(timeout => timeout > 0 && timeout != Timeout.Infinite));
		_dependencyManager.Received(1).GetDependencies("Custom",
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
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

		// Assert
		List<string> warnings = _logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => (string)call.GetArguments()[0]!)
			.ToList();
		warnings.Should().ContainSingle(because: "the failed lookup must be reported exactly once");
		warnings[0].Should().NotContain("eyJzdWIiOiIxIn0",
			because: "an un-redacted server body reaching a warning is the same leak this change removes from the error messages");
		resolution.LookupFailureReason.Should().NotContain("eyJzdWIiOiIxIn0",
			because: "the reason is surfaced to the caller, so it must be redacted before it is carried there");
		resolution.LookupFailureReason!.Length.Should().BeLessThan(secretBearingMessage.Length,
			because: "the failure text must be bounded rather than copied whole into an agent transcript");
	}

	[Test]
	[Description("Returns no candidates without reading the package dependencies when no other package contains the schema (ENG-91314).")]
	public void Resolve_ShouldReportNoCandidates_WhenSchemaNotFoundInOtherPackages() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>(), Arg.Any<int>()).Returns([]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("UsrNonExistent", "Custom");

		// Assert
		resolution.Candidates.Should().BeEmpty(
			because: "there are no candidate packages to report");
		_dependencyManager.DidNotReceive().GetDependencies(Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Description("Deduplicates package names when the same schema appears multiple times in the same package (ENG-91314).")]
	public void Resolve_ShouldDeduplicateCandidates_WhenPackageAppearsMultipleTimes() {
		// Arrange
		SetContributingPackages("CrtLeadOppMgmtApp", "CrtLeadOppMgmtApp");

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom");

		// Assert
		resolution.Candidates.Should().ContainSingle(
			because: "duplicate package names must be collapsed into one candidate");
	}

}
