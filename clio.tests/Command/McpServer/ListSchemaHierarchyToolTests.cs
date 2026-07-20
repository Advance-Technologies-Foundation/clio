using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class ListSchemaHierarchyToolTests {

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("ListHierarchy resolves the command for the requested environment and applies the default manager-name.")]
	public void ListHierarchy_Should_Resolve_Command_And_Default_ManagerName() {
		// Arrange
		FakeListSchemaHierarchyCommand defaultCommand = new();
		FakeListSchemaHierarchyCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaHierarchyCommand>(Arg.Any<ListSchemaHierarchyOptions>())
			.Returns(resolvedCommand);
		ListSchemaHierarchyTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListSchemaHierarchyResponse response = tool.ListHierarchy(new ListSchemaHierarchyArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved fake command returns a successful response");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the environment-scoped command must execute");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContractPageV2", because: "schema-name is the required lookup key");
		resolvedCommand.CapturedOptions.ManagerName.Should().Be("ClientUnitSchemaManager", because: "the tool supplies the documented default");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev", because: "environment-name drives command resolution");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the startup command must not run for environment-scoped calls");
	}

	[Test]
	[Category("Unit")]
	[Description("ListHierarchy returns a redacted error response when environment-scoped command resolution fails.")]
	public void ListHierarchy_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		FakeListSchemaHierarchyCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaHierarchyCommand>(Arg.Any<ListSchemaHierarchyOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		ListSchemaHierarchyTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListSchemaHierarchyResponse response = tool.ListHierarchy(new ListSchemaHierarchyArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "resolver failures must be returned as typed tool failures");
		response.Error.Should().Contain("boom", because: "the caller needs the resolver failure reason");
	}

	[Test]
	[Category("Unit")]
	[Description("ListHierarchy returns a typed failure when the MCP request explicitly passes null args.")]
	public void ListHierarchy_Should_Return_Error_When_Args_Are_Null() {
		// Arrange
		FakeListSchemaHierarchyCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListSchemaHierarchyTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListSchemaHierarchyResponse response = tool.ListHierarchy(null);

		// Assert
		response.Success.Should().BeFalse(because: "args:null is invalid but should not escape as an NRE");
		response.Error.Should().Contain("args", because: "the failure should name the missing argument object");
	}

	[Test]
	[Category("Unit")]
	[Description("ListHierarchy redacts a sensitive URI/host in the command's inner error before returning it to the MCP caller.")]
	public void ListHierarchy_Should_Redact_Sensitive_Inner_Error() {
		// Arrange
		FakeListSchemaHierarchyCommand defaultCommand = new();
		FakeListSchemaHierarchyCommand resolvedCommand = new() {
			ResponseToReturn = new ListSchemaHierarchyResponse {
				Success = false, Error = "POST https://secret-host.example.com/0/DataService failed"
			}
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListSchemaHierarchyCommand>(Arg.Any<ListSchemaHierarchyOptions>())
			.Returns(resolvedCommand);
		ListSchemaHierarchyTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListSchemaHierarchyResponse response = tool.ListHierarchy(new ListSchemaHierarchyArgs("ContractPageV2") {
			EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the resolved command reported a failure");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "a URI/host in the inner error must be redacted before reaching the MCP transcript");
		response.Error.Should().Contain("[redacted-uri]",
			because: "the sensitive URI is replaced with the stable redaction placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("IsClientEditable requires a non-product maintainer and a confirmed developed-here InstallType value.")]
	// Lesson #2: editable ⇔ non-product maintainer AND InstallType==0.
	[TestCase("Customer", 0, true)]
	[TestCase("Customer", 1, false)]   // installed customer package → read-only
	[TestCase("Partner", 0, true)]
	[TestCase("Creatio", 0, false)]    // product maintainer
	[TestCase(" Creatio ", 0, false)]  // product maintainer with padding
	[TestCase("Terrasoft", 0, false)]  // product maintainer
	[TestCase(null, 0, false)]
	[TestCase("", 0, false)]
	[TestCase("Partner", null, false)]
	public void IsClientEditable_Should_Classify_By_Maintainer_And_InstallType(
		string maintainer, int? installType, bool expected) {
		// Arrange / Act
		bool actual = ListSchemaHierarchyCommand.IsClientEditable(maintainer, installType);

		// Assert
		actual.Should().Be(expected, because: "unknown or product-owned packages must not be selected as editable");
	}

	[Test]
	[Category("Unit")]
	[Description("DetectOrderingAmbiguity returns no warnings when replacing schemas have distinct hierarchy levels.")]
	// F1: strictly distinct HierarchyLevels (the real case, e.g. Contract 299<320<...<607) → order
	// is fully dependency-determined, no ambiguity warning.
	public void DetectOrderingAmbiguity_Should_Return_Empty_When_Levels_Are_Distinct() {
		// Arrange
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "CoreContracts", IsBase = true, ExtendParent = false, HierarchyLevel = 299 },
			new() { Package = "SalesContracts", IsBase = false, ExtendParent = true, HierarchyLevel = 320 },
			new() { Package = "WorkContractsProcess", IsBase = false, ExtendParent = true, HierarchyLevel = 607 }
		};

		// Act & Assert
		ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas).Should().BeEmpty(
			because: "distinct concrete levels provide a dependency-determined order");
	}

	[Test]
	[Category("Unit")]
	[Description("DetectOrderingAmbiguity warns when two replacing schemas share the same hierarchy level.")]
	// F1: two independent replacing schemas at the same HierarchyLevel → dependency depth does not
	// order them; warn (and name both packages) so the caller knows the tiebreak is arbitrary.
	public void DetectOrderingAmbiguity_Should_Warn_When_Two_Replacing_Schemas_Share_A_Level() {
		// Arrange
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "CoreContracts", IsBase = true, ExtendParent = false, HierarchyLevel = 299 },
			new() { Package = "PkgA", IsBase = false, ExtendParent = true, HierarchyLevel = 400 },
			new() { Package = "PkgB", IsBase = false, ExtendParent = true, HierarchyLevel = 400 }
		};

		// Act
		var warnings = ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas);

		// Assert
		warnings.Should().HaveCount(1, because: "equal levels require a tiebreak outside dependency depth");
		warnings[0].Should().Contain("PkgA", because: "the warning names the first ambiguous package")
			.And.Contain("PkgB", because: "the warning names the second ambiguous package")
			.And.Contain("400", because: "the warning reports the shared level");
	}

	[Test]
	[Category("Unit")]
	[Description("DetectOrderingAmbiguity warns even for a single replacing schema whose hierarchy level is unknown.")]
	public void DetectOrderingAmbiguity_Should_Warn_When_Single_Replacing_Schema_Lacks_A_Level() {
		// Arrange
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "Base", IsBase = true, ExtendParent = false, HierarchyLevel = 400 },
			new() { Package = "Repl", IsBase = false, ExtendParent = true, HierarchyLevel = 400 },
			new() { Package = "OneNull", IsBase = false, ExtendParent = true, HierarchyLevel = null }
		};

		// Act & Assert
		ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas).Should().ContainSingle()
			.Which.Should().Contain("OneNull", because: "an unknown level means this layer's position is not fully determined");
	}

	[Test]
	[Category("Unit")]
	[Description("DetectOrderingAmbiguity warns when multiple replacing schemas have no reported hierarchy level.")]
	// #2 (review): ≥2 replacing schemas with NO reported HierarchyLevel share the "unknown" bucket and
	// get an arbitrary tiebreak — undetermined order, so it must warn (naming both) rather than emit
	// empty Warnings (which the contract defines as "order fully determined").
	public void DetectOrderingAmbiguity_Should_Warn_When_Multiple_Schemas_Lack_A_Level() {
		// Arrange
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "Base", IsBase = true, ExtendParent = false, HierarchyLevel = 299 },
			new() { Package = "NoLevelA", IsBase = false, ExtendParent = true, HierarchyLevel = null },
			new() { Package = "NoLevelB", IsBase = false, ExtendParent = true, HierarchyLevel = null }
		};

		// Act
		var warnings = ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas);

		// Assert
		warnings.Should().HaveCount(1, because: "all unknown-level replacing schemas share an undetermined order bucket");
		warnings[0].Should().Contain("NoLevelA", because: "the warning names the first unknown-level package")
			.And.Contain("NoLevelB", because: "the warning names the second unknown-level package");
	}

	[Test]
	[Category("Unit")]
	[Description("TryListHierarchy orders base first, then hierarchy level, and maps hierarchy level values from DataService.")]
	// F1 (Major, review): pin the ACTUAL sort with a fixture where each key is INDEPENDENTLY load-bearing,
	// so dropping any of them changes the result. base 'MBase' has a NON-minimal level (500) → the base-first
	// pin is the only thing putting it first (level-only would sort it 2nd, after Zeta@320). Names disagree
	// with levels (level-order Zeta<Alpha; name-order Alpha<Zeta) → dropping ThenBy(HierarchyLevel) reorders
	// them. Null level sorts last via int.MaxValue. Also pins MapHierarchyEntry's HierarchyLevel extraction.
	public void TryListHierarchy_Should_Order_Base_First_Then_By_HierarchyLevel_And_Map_Levels() {
		// Arrange
		const string rows = @"{""rows"":[
			{""Name"":""P"",""UId"":""u-alpha"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Alpha"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":607},
			{""Name"":""P"",""UId"":""u-mid"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Mid"",""Maintainer"":""Custom"",""InstallType"":0,""HierarchyLevel"":null},
			{""Name"":""P"",""UId"":""u-base"",""ExtendParent"":false,""ParentName"":""B"",""PackageName"":""MBase"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":500},
			{""Name"":""P"",""UId"":""u-zeta"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Zeta"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":320}
		]}";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(rows);
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "P" }, out ListSchemaHierarchyResponse response);

		// Assert
		ok.Should().BeTrue(because: "valid DataService rows should produce a hierarchy response");
		// base first (despite level 500), then ascending level (Zeta 320 < Alpha 607), unknown level last.
		response.Schemas.Select(l => l.Package)
			.Should().Equal(["MBase", "Zeta", "Alpha", "Mid"],
				because: "base is pinned first, concrete levels sort ascending, and null levels sort last");
		response.Schemas[0].IsBase.Should().BeTrue(because: "ExtendParent=false marks the base schema");
		response.Schemas.Single(l => l.Package == "MBase").HierarchyLevel.Should().Be(500,
			because: "HierarchyLevel must be surfaced as provenance");
		response.Schemas.Single(l => l.Package == "Mid").HierarchyLevel.Should().BeNull(
			because: "missing HierarchyLevel must stay unknown, not fabricated");
		response.Warnings.Should().NotBeNullOrEmpty(because: "a single unknown hierarchy level still makes order partly undetermined");
	}

	[Test]
	[Category("Unit")]
	[Description("DetectOrderingAmbiguity excludes base schemas from the unknown-level bucket warning.")]
	// Pins the null-bucket partition: a base schema with an unknown (null) level must NOT be named in the
	// unknown-bucket warning (base is always first). A regression to schemas.Where(...) would name it.
	public void DetectOrderingAmbiguity_Should_Exclude_Base_From_The_Null_Bucket() {
		// Arrange
		var schemas = new System.Collections.Generic.List<SchemaHierarchyEntry> {
			new() { Package = "NoLevelBase", IsBase = true, ExtendParent = false, HierarchyLevel = null },
			new() { Package = "NoLevelA", IsBase = false, ExtendParent = true, HierarchyLevel = null },
			new() { Package = "NoLevelB", IsBase = false, ExtendParent = true, HierarchyLevel = null }
		};

		// Act
		var w = ListSchemaHierarchyCommand.DetectOrderingAmbiguity(schemas);

		// Assert
		w.Should().HaveCount(1, because: "only non-base unknown levels affect replacing-schema ordering");
		w[0].Should().Contain("NoLevelA", because: "the warning names the first replacing schema")
			.And.Contain("NoLevelB", because: "the warning names the second replacing schema")
			.And.NotContain("NoLevelBase", because: "base is pinned first independently of hierarchy level");
	}

	[Test]
	[Category("Unit")]
	[Description("TryListHierarchy uses package name as a stable tiebreaker when hierarchy levels match.")]
	// Pins the name tiebreak (line 128): two non-base schemas at the SAME HierarchyLevel order by package
	// name. Input order is reversed (Zeta before Alpha) so removing the ThenBy(Package) breaks this test.
	public void TryListHierarchy_Should_Break_Level_Ties_By_Package_Name() {
		// Arrange
		const string rows = @"{""rows"":[
			{""Name"":""P"",""UId"":""u-b"",""ExtendParent"":false,""ParentName"":""B"",""PackageName"":""Base"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":100},
			{""Name"":""P"",""UId"":""u-z"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Zeta"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":300},
			{""Name"":""P"",""UId"":""u-a"",""ExtendParent"":true,""ParentName"":""B"",""PackageName"":""Alpha"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":300}
		]}";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(rows);
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		// Act
		command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "P" }, out ListSchemaHierarchyResponse response);

		// Assert
		// Zeta and Alpha share level 300; input has Zeta first, but the name tiebreak puts Alpha first.
		response.Schemas.Select(l => l.Package).Should().Equal(["Base", "Alpha", "Zeta"],
			because: "same-level replacing schemas are sorted deterministically by package name");
		response.Warnings.Should().NotBeNullOrEmpty(because: "shared level means the dependency order is ambiguous");
	}

	[Test]
	[Category("Unit")]
	[Description("TryListHierarchy fails when no schema rows match the requested name and manager.")]
	public void TryListHierarchy_Should_Fail_When_Schema_Not_Found() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns("""{"success":true,"rows":[]}""");
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "Missing" },
			out ListSchemaHierarchyResponse response);

		// Assert
		ok.Should().BeFalse(because: "zero rows means a lookup miss, not a valid empty schema hierarchy");
		response.Success.Should().BeFalse(because: "callers need a failure signal for typoed schema names");
		response.Error.Should().Contain("Missing", because: "the message should identify the missing schema");
	}

	[Test]
	[Category("Unit")]
	[Description("TryListHierarchy treats missing ExtendParent as unknown, not as a confirmed base schema.")]
	public void TryListHierarchy_Should_Warn_When_ExtendParent_Is_Missing() {
		// Arrange
		const string rows = @"{""rows"":[
			{""Name"":""P"",""UId"":""u-unknown"",""ParentName"":""B"",""PackageName"":""Unknown"",""Maintainer"":""Customer"",""InstallType"":0,""HierarchyLevel"":300},
			{""Name"":""P"",""UId"":""u-base"",""ExtendParent"":false,""ParentName"":""B"",""PackageName"":""Base"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":100}
		]}";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(rows);
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "P" },
			out ListSchemaHierarchyResponse response);

		// Assert
		ok.Should().BeTrue(because: "unknown ExtendParent is a warning, not a transport failure");
		response.Schemas.Single(l => l.Package == "Unknown").IsBase.Should().BeFalse(
			because: "unknown metadata must not be elevated to a confirmed base layer");
		response.Warnings.Should().Contain(warning => warning.Contains("ExtendParent"),
			because: "callers need to know the base/replacing classification is uncertain");
	}

	[Test]
	[Category("Unit")]
	[Description("TryListHierarchy warns when the DataService result reaches the configured row cap.")]
	public void TryListHierarchy_Should_Warn_When_Result_Reaches_RowCount_Cap() {
		// Arrange
		string row = @"{""Name"":""P"",""UId"":""u-base"",""ExtendParent"":false,""ParentName"":""B"",""PackageName"":""Base"",""Maintainer"":""Creatio"",""InstallType"":1,""HierarchyLevel"":100}";
		string rows = @"{""rows"":[" + string.Join(",", Enumerable.Repeat(row, 200)) + "]}";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(rows);
		IServiceUrlBuilder url = Substitute.For<IServiceUrlBuilder>();
		url.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://x/svc");
		ListSchemaHierarchyCommand command = new(client, url, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryListHierarchy(new ListSchemaHierarchyOptions { SchemaName = "P" },
			out ListSchemaHierarchyResponse response);

		// Assert
		ok.Should().BeTrue(because: "hitting the cap does not prove the query failed");
		response.Warnings.Should().Contain(warning => warning.Contains("rowCount cap"),
			because: "callers must not treat a capped result as complete");
	}

	private sealed class FakeListSchemaHierarchyCommand : ListSchemaHierarchyCommand {
		public ListSchemaHierarchyOptions CapturedOptions { get; private set; }
		public ListSchemaHierarchyResponse ResponseToReturn { get; init; }

		public FakeListSchemaHierarchyCommand()
			: base(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), ConsoleLogger.Instance) {
		}

		public override bool TryListHierarchy(ListSchemaHierarchyOptions options, out ListSchemaHierarchyResponse response) {
			CapturedOptions = options;
			response = ResponseToReturn ?? new ListSchemaHierarchyResponse { Success = true, SchemaName = options.SchemaName, Count = 0 };
			return response.Success;
		}
	}
}
