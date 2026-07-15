using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListSchemaHierarchyToolTests {

	[Test]
	[Category("Unit")]
	public void ListHierarchy_Should_Resolve_Command_And_Default_ManagerName() {
		ConsoleLogger.Instance.ClearMessages();
		FakeListSchemaHierarchyCommand defaultCommand = new();
		FakeListSchemaHierarchyCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaHierarchyCommand>(Arg.Any<ListSchemaHierarchyOptions>())
			.Returns(resolvedCommand);
		ListSchemaHierarchyTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		ListSchemaHierarchyResponse response = tool.ListHierarchy(new ListSchemaHierarchyArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContractPageV2");
		resolvedCommand.CapturedOptions.ManagerName.Should().Be("ClientUnitSchemaManager");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void ListHierarchy_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeListSchemaHierarchyCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaHierarchyCommand>(Arg.Any<ListSchemaHierarchyOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		ListSchemaHierarchyTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		ListSchemaHierarchyResponse response = tool.ListHierarchy(new ListSchemaHierarchyArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("boom");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	// Lesson #2: editable ⇔ non-product maintainer AND InstallType==0.
	[TestCase("Customer", 0, true)]
	[TestCase("Customer", 1, false)]   // installed customer package → read-only
	[TestCase("Partner", 0, true)]
	[TestCase("Creatio", 0, false)]    // product maintainer
	[TestCase("Terrasoft", 0, false)]  // product maintainer
	[TestCase(null, 0, false)]
	[TestCase("", 0, false)]
	public void IsClientEditable_Should_Classify_By_Maintainer_And_InstallType(
		string maintainer, int installType, bool expected) {
		ListSchemaHierarchyCommand.IsClientEditable(maintainer, installType).Should().Be(expected);
	}

	[Test]
	[Category("Unit")]
	// F1: strictly distinct HierarchyLevels (the real case, e.g. Contract 299<320<...<607) → order
	// is fully dependency-determined, no ambiguity warning.
	public void DetectOrderingAmbiguity_Should_Return_Empty_When_Levels_Are_Distinct() {
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "CoreContracts", IsBase = true, HierarchyLevel = 299 },
			new() { Package = "SalesContracts", IsBase = false, HierarchyLevel = 320 },
			new() { Package = "WorkContractsProcess", IsBase = false, HierarchyLevel = 607 }
		};
		ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas).Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	// F1: two independent replacing schemas at the same HierarchyLevel → dependency depth does not
	// order them; warn (and name both packages) so the caller knows the tiebreak is arbitrary.
	public void DetectOrderingAmbiguity_Should_Warn_When_Two_Replacing_Schemas_Share_A_Level() {
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "CoreContracts", IsBase = true, HierarchyLevel = 299 },
			new() { Package = "PkgA", IsBase = false, HierarchyLevel = 400 },
			new() { Package = "PkgB", IsBase = false, HierarchyLevel = 400 }
		};
		var warnings = ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas);
		warnings.Should().HaveCount(1);
		warnings[0].Should().Contain("PkgA").And.Contain("PkgB").And.Contain("400");
	}

	[Test]
	[Category("Unit")]
	// Base sharing a level with a replacing schema is not ambiguous (base is always first); a SINGLE
	// unknown-level replacing schema is also fine (nothing to be ambiguous against) — neither warns.
	public void DetectOrderingAmbiguity_Should_Ignore_Base_Share_And_Single_Null() {
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "Base", IsBase = true, HierarchyLevel = 400 },
			new() { Package = "Repl", IsBase = false, HierarchyLevel = 400 },
			new() { Package = "OneNull", IsBase = false, HierarchyLevel = null }
		};
		ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas).Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	// #2 (review): ≥2 replacing schemas with NO reported HierarchyLevel share the "unknown" bucket and
	// get an arbitrary tiebreak — undetermined order, so it must warn (naming both) rather than emit
	// empty Warnings (which the contract defines as "order fully determined").
	public void DetectOrderingAmbiguity_Should_Warn_When_Multiple_Schemas_Lack_A_Level() {
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "Base", IsBase = true, HierarchyLevel = 299 },
			new() { Package = "NoLevelA", IsBase = false, HierarchyLevel = null },
			new() { Package = "NoLevelB", IsBase = false, HierarchyLevel = null }
		};
		var warnings = ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas);
		warnings.Should().HaveCount(1);
		warnings[0].Should().Contain("NoLevelA").And.Contain("NoLevelB");
	}

	[Test]
	[Category("Unit")]
	// F1 (Major, review): pin the ACTUAL sort with a fixture where each key is INDEPENDENTLY load-bearing,
	// so dropping any of them changes the result. base 'MBase' has a NON-minimal level (500) → the base-first
	// pin is the only thing putting it first (level-only would sort it 2nd, after Zeta@320). Names disagree
	// with levels (level-order Zeta<Alpha; name-order Alpha<Zeta) → dropping ThenBy(HierarchyLevel) reorders
	// them. Null level sorts last via int.MaxValue. Also pins MapHierarchyEntry's HierarchyLevel extraction.
	public void TryListHierarchy_Should_Order_Base_First_Then_By_HierarchyLevel_And_Map_Levels() {
		const string rows = @"{""rows"":[
			{""Name"":""P"",""UId"":""u-alpha"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Alpha"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":607},
			{""Name"":""P"",""UId"":""u-mid"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Mid"",""Maintainer"":""Custom"",""InstallType"":0,""HierarchyLevel"":null},
			{""Name"":""P"",""UId"":""u-base"",""ExtendParent"":false,""ParentName"":""B"",""PackageName"":""MBase"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":500},
			{""Name"":""P"",""UId"":""u-zeta"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Zeta"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":320}
		]}";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(rows);
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(Arg.Any<string>()).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		bool ok = command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "P" }, out ListSchemaHierarchyResponse response);

		ok.Should().BeTrue();
		// base first (despite level 500), then ascending level (Zeta 320 < Alpha 607), unknown level last.
		response.Schemas.Select(l => l.Package)
			.Should().Equal("MBase", "Zeta", "Alpha", "Mid");
		response.Schemas[0].IsBase.Should().BeTrue();
		response.Schemas.Single(l => l.Package == "MBase").HierarchyLevel.Should().Be(500);
		response.Schemas.Single(l => l.Package == "Mid").HierarchyLevel.Should().BeNull();
		// distinct concrete levels + only one null → no ambiguity, and empty warnings normalize to null.
		response.Warnings.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	// Pins the null-bucket partition: a base schema with an unknown (null) level must NOT be named in the
	// unknown-bucket warning (base is always first). A regression to schemas.Where(...) would name it.
	public void DetectOrderingAmbiguity_Should_Exclude_Base_From_The_Null_Bucket() {
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "NoLevelBase", IsBase = true, HierarchyLevel = null },
			new() { Package = "NoLevelA", IsBase = false, HierarchyLevel = null },
			new() { Package = "NoLevelB", IsBase = false, HierarchyLevel = null }
		};
		var w = ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas);
		w.Should().HaveCount(1);
		w[0].Should().Contain("NoLevelA").And.Contain("NoLevelB").And.NotContain("NoLevelBase");
	}

	[Test]
	[Category("Unit")]
	// Pins the name tiebreak (line 128): two non-base schemas at the SAME HierarchyLevel order by package
	// name. Input order is reversed (Zeta before Alpha) so removing the ThenBy(Package) breaks this test.
	public void TryListHierarchy_Should_Break_Level_Ties_By_Package_Name() {
		const string rows = @"{""rows"":[
			{""Name"":""P"",""UId"":""u-b"",""ExtendParent"":false,""ParentName"":""B"",""PackageName"":""Base"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":100},
			{""Name"":""P"",""UId"":""u-z"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Zeta"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":300},
			{""Name"":""P"",""UId"":""u-a"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Alpha"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":300}
		]}";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(rows);
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(Arg.Any<string>()).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "P" }, out ListSchemaHierarchyResponse response);

		// Zeta and Alpha share level 300; input has Zeta first, but the name tiebreak puts Alpha first.
		response.Schemas.Select(l => l.Package).Should().Equal("Base", "Alpha", "Zeta");
		response.Warnings.Should().NotBeNullOrEmpty(); // shared level → ambiguity warned
	}

	private sealed class FakeListSchemaHierarchyCommand : ListSchemaHierarchyCommand {
		public ListSchemaHierarchyOptions CapturedOptions { get; private set; }

		public FakeListSchemaHierarchyCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryListHierarchy(ListSchemaHierarchyOptions options, out ListSchemaHierarchyResponse response) {
			CapturedOptions = options;
			response = new ListSchemaHierarchyResponse { Success = true, SchemaName = options.SchemaName, Count = 0 };
			return true;
		}
	}
}
