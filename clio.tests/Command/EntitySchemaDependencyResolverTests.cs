using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class EntitySchemaDependencyResolverTests
{

	private FindEntitySchemaCommand _findCommand;
	private IPackageDependencyManager _dependencyManager;
	private ILogger _logger;
	private EntitySchemaDependencyResolver _resolver;
	private List<PackageDependencySpec> _capturedSpecs;

	[SetUp]
	public void Setup() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_findCommand = Substitute.For<FindEntitySchemaCommand>(client, urlBuilder, _logger);
		_dependencyManager = Substitute.For<IPackageDependencyManager>();
		_resolver = new EntitySchemaDependencyResolver(_findCommand, _dependencyManager, _logger);
		_capturedSpecs = null;
		_dependencyManager.AddDependencies(Arg.Any<string>(),
				Arg.Do<IEnumerable<PackageDependencySpec>>(specs => _capturedSpecs = specs.ToList()))
			.Returns(callInfo => _capturedSpecs.Select(s => s.Name).ToList());
	}

	[TearDown]
	public void TearDown() {
		_findCommand.ClearReceivedCalls();
		_dependencyManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Adds the single candidate dependency and returns true when exactly one other package contains the schema (ENG-91314).")]
	public void TryAutoResolve_ShouldAddDependencyAndReturnTrue_WhenExactlyOneCandidateExists() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([
				new EntitySchemaSearchResult("Opportunity", "CrtLeadOppMgmtApp", "Creatio", "Opportunity"),
				new EntitySchemaSearchResult("Opportunity", "Custom", "Customer", "Opportunity")
			]);

		// Act
		bool resolved = _resolver.TryAutoResolve("Opportunity", "Custom");

		// Assert
		resolved.Should().BeTrue(
			because: "exactly one candidate package was found and added as a dependency");
		_capturedSpecs.Should().ContainSingle(because: "only the single candidate should be added");
		_capturedSpecs[0].Name.Should().Be("CrtLeadOppMgmtApp",
			because: "the non-target package should be added as a dependency");
	}

	[Test]
	[Description("Refuses auto-resolution and returns false when the schema exists in multiple other packages to avoid unscoped dependency additions (ENG-91314).")]
	public void TryAutoResolve_ShouldReturnFalse_WhenMultipleCandidatePackagesExist() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([
				new EntitySchemaSearchResult("Opportunity", "CoreLeadOpportunity", "Creatio", "Opportunity"),
				new EntitySchemaSearchResult("Opportunity", "CrtLeadOppMgmtApp", "Creatio", "Opportunity"),
				new EntitySchemaSearchResult("Opportunity", "Custom", "Customer", "Opportunity")
			]);

		// Act
		bool resolved = _resolver.TryAutoResolve("Opportunity", "Custom");

		// Assert
		resolved.Should().BeFalse(
			because: "ambiguous resolution with multiple candidates must refuse to add dependencies automatically");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
		_logger.Received().WriteWarning(Arg.Is<string>(msg =>
			msg.Contains("multiple packages") && msg.Contains("manually")));
	}

	[Test]
	[Description("Excludes the target package from the dependency candidates so it does not add a self-dependency (ENG-91314).")]
	public void TryAutoResolve_ShouldExcludeTargetPackage_WhenSchemaExistsInTargetToo() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([
				new EntitySchemaSearchResult("Opportunity", "Custom", "Customer", null),
				new EntitySchemaSearchResult("Opportunity", "CrtLeadOppMgmtApp", "Creatio", "Opportunity")
			]);

		// Act
		_resolver.TryAutoResolve("Opportunity", "Custom");

		// Assert
		_capturedSpecs.Should().ContainSingle(because: "only CrtLeadOppMgmtApp should remain after excluding Custom");
		_capturedSpecs[0].Name.Should().Be("CrtLeadOppMgmtApp",
			because: "only the non-target package should remain as a dependency candidate");
	}

	[Test]
	[Description("Returns false without calling the dependency manager when no other package contains the schema (ENG-91314).")]
	public void TryAutoResolve_ShouldReturnFalse_WhenSchemaNotFoundInOtherPackages() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([]);

		// Act
		bool resolved = _resolver.TryAutoResolve("UsrNonExistent", "Custom");

		// Assert
		resolved.Should().BeFalse(
			because: "there are no candidate packages to add as dependencies");
		_dependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
	}

	[Test]
	[Description("Catches exceptions from the dependency manager and returns false so the caller falls through to the enriched error message (ENG-91314).")]
	public void TryAutoResolve_ShouldReturnFalse_WhenDependencyManagerThrows() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([
				new EntitySchemaSearchResult("Opportunity", "CrtLeadOppMgmtApp", "Creatio", "Opportunity")
			]);
		_dependencyManager.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Package not found"));

		// Act
		bool resolved = _resolver.TryAutoResolve("Opportunity", "Custom");

		// Assert
		resolved.Should().BeFalse(
			because: "a failing auto-resolve must not crash the caller; the enriched error message takes over");
		_logger.Received().WriteWarning(Arg.Is<string>(msg => msg.Contains("Package not found")));
	}

	[Test]
	[Description("Deduplicates package names when the same schema appears multiple times in the same package (ENG-91314).")]
	public void TryAutoResolve_ShouldDeduplicateCandidates_WhenPackageAppearsMultipleTimes() {
		// Arrange
		_findCommand.FindSchemas(Arg.Any<FindEntitySchemaOptions>())
			.Returns([
				new EntitySchemaSearchResult("Opportunity", "CrtLeadOppMgmtApp", "Creatio", "Opportunity"),
				new EntitySchemaSearchResult("Opportunity", "CrtLeadOppMgmtApp", "Creatio", "Opportunity")
			]);

		// Act
		_resolver.TryAutoResolve("Opportunity", "Custom");

		// Assert
		_capturedSpecs.Should().ContainSingle(because: "duplicate package names must be collapsed into one dependency");
	}

}
