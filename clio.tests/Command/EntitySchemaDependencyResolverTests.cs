using System;
using System.Collections.Generic;
using System.Linq;
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
	private List<PackageDependencySpec> _capturedSpecs;

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
		_dependencyManager.GetDependencies(Arg.Any<string>()).Returns([]);
		SetInstalledApplications();
		_resolver = new EntitySchemaDependencyResolver(_findCommand, _dependencyManager, _applicationClient,
			_serviceUrlBuilder, _logger);
		_capturedSpecs = null;
		_dependencyManager.AddDependencies(Arg.Any<string>(),
				Arg.Do<IEnumerable<PackageDependencySpec>>(specs => _capturedSpecs = specs.ToList()))
			.Returns(callInfo => _capturedSpecs.Select(s => s.Name).ToList());
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

	private static EntitySchemaSearchResult Result(string packageName) =>
		new("Opportunity", packageName, "Creatio", "Opportunity");

	[Test]
	[Description("Adds the single candidate dependency and reports the change when exactly one other package contributes the schema (ENG-91314).")]
	public void Resolve_ShouldAddDependencyAndReportChange_WhenExactlyOneCandidateExists() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp"), Result("Custom")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.DependencyAdded.Should().BeTrue(
			because: "exactly one candidate package was found and added as a dependency");
		_capturedSpecs.Should().ContainSingle(because: "only the single candidate should be added");
		_capturedSpecs[0].Name.Should().Be("CrtLeadOppMgmtApp",
			because: "the non-target package should be added as a dependency");
	}

	[Test]
	[Description("Reports every candidate without writing anything when more than one package contributes the schema - the case a standard schema always lands in, and the one the previous blanket refusal reported to nobody (issue #722).")]
	public void Resolve_ShouldReportRankedCandidatesWithoutWriting_WhenMultipleCandidatesExist() {
		// Arrange
		SetInstalledApplications("CrtLeadOppMgmtApp", "SalesEnterprise");
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([
				Result("CoreLeadOpportunity"),
				Result("CrtLeadOppMgmtApp"),
				Result("SalesEnterprise"),
				Result("Custom")
			]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.DependencyAdded.Should().BeFalse(
			because: "more than one candidate can be correct, so clio must not choose one on the caller's behalf");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp", "SalesEnterprise", "CoreLeadOpportunity"],
			because: "installed applications must be ranked first so the caller reads the likely answer first, and each group must be ordered so the reported list is stable");
		resolution.ApplicationCandidateCount.Should().Be(2,
			because: "the caller needs to know how many of the leading entries carry the application ranking signal");
	}

	[Test]
	[Description("Returns the candidates but writes nothing when the caller is a read path that must not mutate the package (issue #722).")]
	public void Resolve_ShouldNeverWrite_WhenAutoAddIsNotAllowed() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp"), Result("Custom")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: false);

		// Assert
		resolution.DependencyAdded.Should().BeFalse(
			because: "a read path must never change the target package's dependency list");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "the read path still needs the candidate so its error message can name a concrete fix");
	}

	[Test]
	[Description("Excludes the target package from the dependency candidates so it does not add a self-dependency (ENG-91314).")]
	public void Resolve_ShouldExcludeTargetPackage_WhenSchemaExistsInTargetToo() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("Custom"), Result("CrtLeadOppMgmtApp")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "only CrtLeadOppMgmtApp should remain after excluding the target package");
		_capturedSpecs.Should().ContainSingle(because: "only CrtLeadOppMgmtApp should remain after excluding Custom");
		_capturedSpecs[0].Name.Should().Be("CrtLeadOppMgmtApp",
			because: "only the non-target package should remain as a dependency candidate");
	}

	[Test]
	[Description("Drops packages the target already depends on, so a candidate list never proposes a dependency that is already declared (issue #722).")]
	public void Resolve_ShouldExcludeExistingDependencies_WhenTargetAlreadyDependsOnACandidate() {
		// Arrange
		_dependencyManager.GetDependencies("Custom").Returns(["crtcore", "CoreLeadOpportunity"]);
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CoreLeadOpportunity"), Result("CrtLeadOppMgmtApp"), Result("CrtCore")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: false);

		// Assert
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "an already-declared dependency is not a fix, and the match must ignore case as package names do");
	}

	[Test]
	[Description("Reports nothing at all when every contributing package is already a dependency, because a missing dependency is then not what the caller is looking at (issue #722).")]
	public void Resolve_ShouldReportNoCandidates_WhenEveryContributorIsAlreadyADependency() {
		// Arrange
		_dependencyManager.GetDependencies("Custom").Returns(["CrtLeadOppMgmtApp"]);
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.Candidates.Should().BeEmpty(
			because: "with no addable candidate there is no evidence for a missing dependency and none must be claimed");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
	}

	[Test]
	[Description("Still reports the candidates, unranked, when the installed-application lookup used to rank them fails (issue #722).")]
	public void Resolve_ShouldStillReportCandidates_WhenApplicationRankingLookupFails() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Throws(new InvalidOperationException("SelectQuery unavailable"));
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp"), Result("CoreLeadOpportunity")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: false);

		// Assert
		resolution.Candidates.Should().Equal(["CoreLeadOpportunity", "CrtLeadOppMgmtApp"],
			because: "ranking is an ordering hint, so losing it must degrade to an ordered list, never suppress the diagnosis");
		resolution.ApplicationCandidateCount.Should().Be(0,
			because: "with no ranking signal the caller must not be told the order means anything");
	}

	[Test]
	[Description("Reports the candidate but refuses to write when the current-dependencies read failed, because the 'exactly one remains' safety condition was then never actually evaluated (issue #722).")]
	public void Resolve_ShouldNotWrite_WhenTheExistingDependencyReadFailed() {
		// Arrange
		_dependencyManager.GetDependencies("Custom").Throws(new InvalidOperationException("SelectQuery failed"));
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.DependencyAdded.Should().BeFalse(
			because: "with the existing dependencies unknown the filter was a no-op, so a single remaining candidate proves nothing");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "a degraded read must withhold the write, not the diagnosis");
	}

	[Test]
	[Description("Still reports the candidate it tried to add when the dependency write is refused, so the caller is not told that nothing was found (issue #722).")]
	public void Resolve_ShouldStillReportTheCandidate_WhenTheDependencyWriteIsRefused() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp")]);
		_dependencyManager.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Package is not editable"));

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.DependencyAdded.Should().BeFalse(
			because: "the write was refused, so the caller must not be told to retry the load");
		resolution.Candidates.Should().Equal(["CrtLeadOppMgmtApp"],
			because: "a refused write must not make the caller report 'clio found no package', which is the opposite of what happened");
		_logger.Received().WriteWarning(Arg.Is<string>(msg => msg.Contains("Package is not editable")));
	}

	[Test]
	[Description("Redacts and bounds the failure text it logs, because SelectQuery falls back to the raw response body when the server answers success:false with no errorInfo (issue #722).")]
	public void Resolve_ShouldRedactAndBoundLoggedFailures_WhenTheLookupFailsWithASecretBearingMessage() {
		// Arrange
		string secretBearingMessage =
			"SelectQuery failed: {\"Message\":\"Authentication failed.\",\"token\":\"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln\"} "
			+ new string('x', 600);
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Throws(new InvalidOperationException(secretBearingMessage));

		// Act
		_resolver.Resolve("Opportunity", "Custom", allowAutoAdd: false);

		// Assert
		List<string> warnings = _logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => (string)call.GetArguments()[0]!)
			.ToList();
		warnings.Should().ContainSingle(because: "the failed lookup must be reported exactly once");
		warnings[0].Should().NotContain("eyJzdWIiOiIxIn0",
			because: "an un-redacted server body reaching a warning is the same leak this change removes from the error messages");
		warnings[0].Length.Should().BeLessThan(secretBearingMessage.Length,
			because: "the failure text must be bounded rather than copied whole into an agent transcript");
	}

	[Test]
	[Description("Returns no candidates without calling the dependency manager when no other package contains the schema (ENG-91314).")]
	public void Resolve_ShouldReportNoCandidates_WhenSchemaNotFoundInOtherPackages() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("UsrNonExistent", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.Candidates.Should().BeEmpty(
			because: "there are no candidate packages to add as dependencies");
		resolution.DependencyAdded.Should().BeFalse(
			because: "nothing was added");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
	}

	[Test]
	[Description("Catches exceptions from the dependency manager and reports nothing so the caller falls through to its own error message (ENG-91314).")]
	public void Resolve_ShouldReportNothing_WhenDependencyManagerThrows() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp")]);
		_dependencyManager.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Package not found"));

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.DependencyAdded.Should().BeFalse(
			because: "a failing resolution must not crash the caller; the enriched error message takes over");
		_logger.Received().WriteWarning(Arg.Is<string>(msg => msg.Contains("Package not found")));
	}

	[Test]
	[Description("Deduplicates package names when the same schema appears multiple times in the same package (ENG-91314).")]
	public void Resolve_ShouldDeduplicateCandidates_WhenPackageAppearsMultipleTimes() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([Result("CrtLeadOppMgmtApp"), Result("CrtLeadOppMgmtApp")]);

		// Act
		EntitySchemaDependencyResolution resolution = _resolver.Resolve("Opportunity", "Custom",
			allowAutoAdd: true);

		// Assert
		resolution.Candidates.Should().ContainSingle(
			because: "duplicate package names must be collapsed into one candidate");
		_capturedSpecs.Should().ContainSingle(because: "duplicate package names must be collapsed into one dependency");
	}

}
