namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
internal class GetClassicListColumnsCommandTests : BaseCommandTests<GetClassicListColumnsOptions> {

	private const string SelectUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string SchemaUId = "11111111-1111-1111-1111-111111111111";
	private const string PackageUId = "22222222-2222-2222-2222-222222222222";
	private const string BaseSchemaUId = "33333333-3333-3333-3333-333333333333";
	private const string ProfileUrl = "http://test/DataService/json/SyncReply/QueryProfile";
	private const string UserInfoUrl = "http://test/ServiceModel/UserInfoService.svc/GetCurrentUserInfo";
	private const string ContactId = "44444444-4444-4444-4444-444444444444";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private GetClassicListColumnsCommand _command;
	private IClassicListColumnParser _parser;
	// Execute writes the serialized response through ILogger, so the Execute tests need a substitute to read it
	// back — TryResolve tests never touch it, which is exactly why Execute's own behaviour went unasserted.
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectUrl);
		_applicationClient.ExecutePostRequest(SelectUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{SchemaUId}}}", "PackageUId": "{{{PackageUId}}}" }] }""");
		_hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(PackageUId);
		_command = Container.GetRequiredService<GetClassicListColumnsCommand>();
		_parser = Container.GetRequiredService<IClassicListColumnParser>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_hierarchyClient.ClearReceivedCalls();
		_columnManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient<ILogger>(_ => _logger);
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_hierarchyClient);
		containerBuilder.AddSingleton(_columnManager);
	}

	[Test]
	[Description("ParseColumns preserves static path and bindTo declarations from both supported Classic section methods while ignoring lookalikes outside them.")]
	public void ParseColumns_ShouldReturnOrderedDistinctStaticPaths_WhenSupportedMethodsDeclareColumns() {
		// Arrange
		string[] bodies = [
			"""
			define([], function() { return {
			  outside: { path: "Ignored" },
			  getGridDataColumns: function() {
			    return { Id: { path: "Id" }, Name: { bindTo: "Name" } };
			  }
			}; });
			""",
			"""
			define([], function() { return {
			  initColumnsConfig: function() {
			    return [{ "path": "Name" }, { path: "Account.PrimaryContact.Name" }];
			  }
			}; });
			"""
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["Id", "Name", "Account.PrimaryContact.Name"],
			because: "only supported method bodies should contribute static paths in base-to-top declaration order");
	}

	[Test]
	[Description("ParseColumns ignores braces and path-shaped text inside strings and comments when locating a supported method boundary.")]
	public void ParseColumns_ShouldRespectFunctionBoundary_WhenStringsAndCommentsContainBraces() {
		// Arrange
		string body = """
			getGridDataColumns: function() {
			  const text = "} path: 'IgnoredString'";
			  // } path: "IgnoredComment"
			  return { Name: { path: "Name" } };
			}, after: { path: "IgnoredAfter" }
			""";

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns([body]).Columns;

		// Assert
		result.Should().Equal(["Name"],
			because: "string/comment braces must not terminate the function or admit paths outside it");
	}

	[Test]
	[Description("ParseColumns treats a derived declaration as an override of the same Classic list-column method.")]
	public void ParseColumns_ShouldReplaceBaseMethodColumns_WhenDerivedSchemaOverridesMethod() {
		// Arrange
		string[] bodies = [
			"getGridDataColumns: function() { return { Name: { path: 'Name' } }; }",
			"getGridDataColumns: function() { return { CreatedOn: { path: 'CreatedOn' } }; }"
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["CreatedOn"],
			because: "a derived method that does not call its parent replaces the inherited method result");
	}

	[Test]
	[Description("ParseColumns composes inherited static columns when a derived Classic method explicitly calls its parent.")]
	public void ParseColumns_ShouldIncludeBaseColumns_WhenDerivedMethodCallsParent() {
		// Arrange
		string[] bodies = [
			"getGridDataColumns: function() { return { Name: { path: 'Name' } }; }",
			"getGridDataColumns: function() { const columns = this.callParent(arguments); columns.CreatedOn = { path: 'CreatedOn' }; return columns; }"
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["Name", "CreatedOn"],
			because: "an explicit callParent composes the derived literals with the inherited method result");
	}

	[Test]
	[Description("ParseColumns ignores callParent and path literals inside nested functions when deciding the outer method override.")]
	public void ParseColumns_ShouldIgnoreNestedFunctionEvidence_WhenDerivedMethodOverridesParent() {
		// Arrange
		string[] bodies = [
			"getGridDataColumns: function() { return { Name: { path: 'Name' } }; }",
			"getGridDataColumns: function() { const helper = function() { this.callParent(arguments); return { Injected: { path: 'Injected' } }; }; return { CreatedOn: { path: 'CreatedOn' } }; }"
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["CreatedOn"],
			because: "a nested function has its own execution scope and cannot compose or contribute to the outer method result");
	}

	[Test]
	[Description("ParseColumns stops the callParent walk-down at the first middle layer that does not call its parent.")]
	public void ParseColumns_ShouldTruncateAtMiddleLayer_WhenThatLayerDoesNotCallParent() {
		// Arrange — base -> middle -> top, mirroring a real BaseSectionV2 -> EntitySectionV2 -> Usr* chain.
		// The middle layer overrides outright, so the base literals must not reach the result.
		string[] bodies = [
			"getGridDataColumns: function() { return { A: { path: 'A' } }; }",
			"getGridDataColumns: function() { return { B: { path: 'B' } }; }",
			"getGridDataColumns: function() { const columns = this.callParent(arguments); columns.C = { path: 'C' }; return columns; }"
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["B", "C"],
			because: "the walk-down stops at the most-derived layer that does not call its parent, so the base layer is excluded");
	}

	[Test]
	[Description("ParseColumns composes every layer when each derived Classic method calls its parent across three layers.")]
	public void ParseColumns_ShouldComposeAllLayers_WhenEveryDerivedMethodCallsParent() {
		// Arrange — the mirror of the truncation case: the walk-down must decrement twice and reach the base.
		string[] bodies = [
			"getGridDataColumns: function() { return { A: { path: 'A' } }; }",
			"getGridDataColumns: function() { const columns = this.callParent(arguments); columns.B = { path: 'B' }; return columns; }",
			"getGridDataColumns: function() { const columns = this.callParent(arguments); columns.C = { path: 'C' }; return columns; }"
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["A", "B", "C"],
			because: "an unbroken callParent chain composes every layer base-to-derived");
	}

	[Test]
	[Description("ParseColumns harvests a column method declared inside the Classic 'methods' object rather than at the top level.")]
	public void ParseColumns_ShouldHarvestColumns_WhenMethodIsDeclaredInsideMethodsObject() {
		// Arrange
		string body = """
			define([], function() { return {
			  entitySchemaName: "Contact",
			  methods: {
			    getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			  }
			}; });
			""";

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns([body]).Columns;

		// Assert
		result.Should().Equal(["Name"],
			because: "Classic schemas commonly nest the column methods under 'methods', and missing that lookup "
				+ "would silently degrade the answer to entity-default instead of failing");
	}

	[Test]
	[Description("ParseColumns accepts quoted Classic method and path property keys.")]
	public void ParseColumns_ShouldReadColumns_WhenMethodAndPathKeysAreQuoted() {
		// Arrange
		string body = """
			"getGridDataColumns": function() { return { Name: { "path": "Name" } }; }
			""";

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns([body]).Columns;

		// Assert
		result.Should().Equal(["Name"], because: "quoted JavaScript property keys are valid static declarations");
	}

	[Test]
	[Description("The parser ignores fake entity and list-column method declarations embedded in comments and strings.")]
	public void Parser_ShouldIgnoreDeclarations_WhenTheyAreNotExecutableCode() {
		// Arrange
		string body = """
			define([], function() {
				// entitySchemaName: "InjectedEntity"
				const sample = "getGridDataColumns: function() { return { Injected: { path: 'Injected' } }; }";
				return {
					"entitySchemaName": "Contact",
					getGridDataColumns: function() {
						const expression = 1 + /callParent\(.*path:'Injected'/;
						return { Name: { path: "Name" } };
					},
					probe: function() { return 1 + /entitySchemaName:'SysAdminUnit'/; }
				};
			});
			""";

		// Act
		string entity = _parser.ParseEntityName([body]);
		IReadOnlyList<string> columns = _parser.ParseColumns([body]).Columns;

		// Assert
		entity.Should().Be("Contact", because: "comments, strings, and regex literals must not redirect metadata reads to another entity");
		columns.Should().Equal(["Name"], because: "string contents must not masquerade as an executable method");
	}

	[Test]
	[Description("The parser binds entity and list-column metadata to the object returned by the AMD define factory.")]
	public void Parser_ShouldIgnoreSchemaLikeObjects_OutsideDefineFactoryReturn() {
		// Arrange
		string body = """
			const decoy = { entitySchemaName: "SysAdminUnit", getGridDataColumns: function() { return { Injected: { path: "Injected" } }; } };
			define([], function() {
				return { entitySchemaName: "Contact", getGridDataColumns: function() { return { Name: { path: "Name" } }; }, diff: [] };
			});
			""";

		// Act
		string entity = _parser.ParseEntityName([body]);
		IReadOnlyList<string> columns = _parser.ParseColumns([body]).Columns;

		// Assert
		entity.Should().Be("Contact", because: "only the AMD factory return defines the Classic section schema");
		columns.Should().Equal(["Name"], because: "schema-like objects outside the factory return are unrelated code");
	}

	[Test]
	[Description("TryResolve returns schema-default with ordered captions when the Classic section hierarchy declares static list columns.")]
	public void TryResolve_ShouldReturnSchemaDefault_WhenSectionDeclaresStaticColumns() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", """
				entitySchemaName: "Contact",
				getGridDataColumns: function() { return { Name: { path: "Name" }, CreatedOn: { path: "CreatedOn" } }; }
				""")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name"), Column("CreatedOn", "Created on")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "the section hierarchy and entity metadata are available; error: {0}", response.Error);
		response.Success.Should().BeTrue(because: "static schema columns form a successful resolution");
		response.Source.Should().Be("schema-default", because: "schema declarations take precedence over entity fallback");
		response.Entity.Should().Be("Contact", because: "the response must expose the section entity");
		response.Columns.Select(column => column.Name).Should().Equal(["Name", "CreatedOn"],
			because: "the resolver must preserve the schema declaration order");
		response.Columns.Select(column => column.Caption).Should().Equal(["Full name", "Created on"],
			because: "direct entity column captions should enrich the stable column paths");
	}

	[Test]
	[Description("TryResolve takes entitySchemaName from the most-derived layer that declares one, not the base layer.")]
	public void TryResolve_ShouldTakeEntityName_FromTheMostDerivedLayer() {
		// Arrange — the layers declare DIFFERENT entities on purpose. Every other fixture repeats one entity on
		// every layer, so reversing the loop or taking the first non-null would keep the suite green.
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", """
				entitySchemaName: "Contact",
				getGridDataColumns: function() { return { Name: { path: "Name" } }; }
				"""),
			Schema("Base", "entitySchemaName: 'BaseEntity', diff: []", BaseSchemaUId)
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "the hierarchy resolves; error: {0}", response.Error);
		response.Entity.Should().Be("Contact",
			because: "the most-derived declaration is the section's effective entity; taking the base layer's "
				+ "would query the wrong entity's metadata and enrich captions from it");
	}

	[Test]
	[Description("ParseColumns treats a body that parses cleanly but exposes no Classic schema object as skipped, and says so without claiming a syntax error.")]
	public void ParseColumns_ShouldReportUnanchoredLayer_WhenABodyParsesButExposesNoSchemaObject() {
		// Arrange — valid JavaScript, no schema markers anywhere: the "factory returns a local" shape.
		string[] bodies = [
			"getGridDataColumns: function() { return { Name: { path: 'Name' } }; }",
			"define([], function() { var config = 1 + 2; return config; });"
		];

		// Act
		ClassicListColumnParseResult result = _parser.ParseColumns(bodies);

		// Assert
		result.UnparsedLayerCount.Should().Be(1, because: "the layer contributed nothing and was skipped");
		result.UnanchoredLayerCount.Should().Be(1,
			because: "the body is valid JavaScript, so reporting it as a syntax error would send the reader "
				+ "looking for a broken body that is not broken");
		result.Columns.Should().Equal(["Name"], because: "the readable layer still contributes");
	}

	[Test]
	[Description("ParseColumns detects callParent in an arrow-function override, so the composition rule does not depend on the override's function form.")]
	public void ParseColumns_ShouldComposeBaseColumns_WhenDerivedMethodIsAnArrowFunction() {
		// Arrange
		string[] bodies = [
			"getGridDataColumns: function() { return { Name: { path: 'Name' } }; }",
			"getGridDataColumns: () => { const columns = this.callParent(arguments); columns.CreatedOn = { path: 'CreatedOn' }; return columns; }"
		];

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns(bodies).Columns;

		// Assert
		result.Should().Equal(["Name", "CreatedOn"],
			because: "CallsParent looks for the callParent CALL, not for a particular function form, so an "
				+ "arrow-function override composes exactly like a classic one");
	}

	[Test]
	[Description("TryResolve returns schema-default when the section declares its column method inside the Classic 'methods' object.")]
	public void TryResolve_ShouldReturnSchemaDefault_WhenColumnMethodIsNestedUnderMethods() {
		// Arrange — the nested shape must reach schema-default through the resolver, not only through the
		// parser API; a missed lookup would degrade silently to entity-default with a plausible answer.
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", """
				entitySchemaName: "Contact",
				methods: {
				  getGridDataColumns: function() { return { Name: { path: "Name" } }; }
				}
				""")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "the nested declaration is a valid Classic shape; error: {0}", response.Error);
		response.Source.Should().Be("schema-default",
			because: "a column method nested under 'methods' is still a static schema declaration");
		response.Columns.Select(column => column.Name).Should().Equal(["Name"],
			because: "the nested method's literals must be harvested like a top-level declaration");
	}

	[Test]
	[Description("TryResolve returns entity-default when a Classic section has no static list-column declaration but its entity has a primary display column.")]
	public void TryResolve_ShouldReturnEntityDefault_WhenSchemaColumnsAreAbsent() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'UsrMncrdSct', diff: []")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("UsrMncrdSct", "UsrName", Column("UsrName", "Name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrMncrdSct26e53fc1Section" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "an entity primary display column is a valid Classic default; error: {0}", response.Error);
		response.Source.Should().Be("entity-default", because: "the schema contains no hardcoded columns");
		response.Columns.Should().ContainSingle(because: "BaseDataView defaults to exactly the primary display column");
		response.Columns.Single().Name.Should().Be("UsrName", because: "the live-verified primary display column is the fallback");
		response.Notes.Should().ContainSingle(because: "the response should explain why the entity fallback was selected");
	}

	[Test]
	[Description("TryResolve returns a successful none source when neither the section schema nor entity metadata supplies a default list column.")]
	public void TryResolve_ShouldReturnNone_WhenNoDefaultColumnExists() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'UsrNoDisplay', diff: []")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("UsrNoDisplay", null));
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrNoDisplaySection" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "fallback exhaustion is a supported result rather than an operational failure; error: {0}", response.Error);
		response.Success.Should().BeTrue(because: "none must remain machine-distinguishable from an exception");
		response.Source.Should().Be("none", because: "no supported default source was found");
		response.Columns.Should().BeEmpty(because: "the resolver must not fabricate a list column");
		response.Notes.Should().ContainSingle(because: "the empty successful result needs a supportable explanation");
	}

	[Test]
	[Description("TryResolve anchors on the schema's own package when the designer design-package call fails for any reason, including a non-JSON response from an expired session.")]
	public void TryResolve_ShouldAnchorOnSchemaPackage_WhenDesignPackageLookupThrows() {
		// Arrange
		_hierarchyClient.GetDesignPackageUId(SchemaUId)
			.Returns(_ => throw new Newtonsoft.Json.JsonReaderException("Unexpected character encountered: <"));
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'UsrMncrdSct', diff: []")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("UsrMncrdSct", "UsrName", Column("UsrName", "Name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrMncrdSct26e53fc1Section" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(
			because: "a failed design-package lookup must degrade to the schema's own package, not fail the command; error: {0}",
			response.Error);
		response.Source.Should().Be("entity-default", because: "the hierarchy still resolves through the fallback anchor");
		_hierarchyClient.Received(1).GetParentSchemas(SchemaUId, PackageUId);
		response.Notes.Should().Contain(note => note.Contains("anchoring on the schema's own package"),
			because: "a silent fallback is undiagnosable; the resolver has no logger, so notes carry the degradation");
	}

	[Test]
	[Description("TryResolve re-anchors on the most-base variant of the requested name and resolves columns from the full hierarchy when the name resolves to a replacing layer.")]
	public void TryResolve_ShouldReAnchorOnBaseVariant_WhenNameResolvesToReplacingLayer() {
		// Arrange — the requested name exists twice: the queried replacing layer and its most-base variant.
		// Only the re-fetched hierarchy carries the column declaration, so the assertions fail if the
		// resolver keeps the partial hierarchy anchored on the replacing layer.
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("UsrMncrdSct26e53fc1Section", "entitySchemaName: 'UsrMncrdSct', diff: []"),
			Schema("UsrMncrdSct26e53fc1Section", "entitySchemaName: 'UsrMncrdSct', diff: []", BaseSchemaUId)
		]);
		_hierarchyClient.GetParentSchemas(BaseSchemaUId, PackageUId).Returns([
			Schema("UsrMncrdSct26e53fc1Section", """
				entitySchemaName: "UsrMncrdSct",
				getGridDataColumns: function() { return { UsrName: { path: "UsrName" } }; }
				"""),
			Schema("BaseDataView", "entitySchemaName: 'UsrMncrdSct', diff: []", BaseSchemaUId)
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("UsrMncrdSct", "UsrName", Column("UsrName", "Name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrMncrdSct26e53fc1Section" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "a replaced Classic section must still resolve; error: {0}", response.Error);
		_hierarchyClient.Received(1).GetParentSchemas(BaseSchemaUId, PackageUId);
		response.Source.Should().Be("schema-default",
			because: "the re-anchored hierarchy is the one that declares the static columns");
		response.Columns.Select(column => column.Name).Should().Equal(["UsrName"],
			because: "cross-package replaced sections must report the replacing layer's own columns, "
				+ "not silently degrade to the entity default");
	}

	[Test]
	[Description("TryResolve keeps the initial hierarchy when the re-anchored designer call returns nothing, instead of failing on an empty result.")]
	public void TryResolve_ShouldKeepInitialHierarchy_WhenReAnchoredLookupReturnsEmpty() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("UsrMncrdSct26e53fc1Section", """
				entitySchemaName: "UsrMncrdSct",
				getGridDataColumns: function() { return { UsrName: { path: "UsrName" } }; }
				"""),
			Schema("UsrMncrdSct26e53fc1Section", "entitySchemaName: 'UsrMncrdSct', diff: []", BaseSchemaUId)
		]);
		_hierarchyClient.GetParentSchemas(BaseSchemaUId, PackageUId).Returns([]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("UsrMncrdSct", "UsrName", Column("UsrName", "Name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrMncrdSct26e53fc1Section" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(
			because: "an empty re-anchored hierarchy must degrade to the hierarchy already in hand; error: {0}",
			response.Error);
		response.Source.Should().Be("schema-default",
			because: "the initial hierarchy still declares the static columns");
		response.Columns.Select(column => column.Name).Should().Equal(["UsrName"],
			because: "the fallback must preserve the columns the first designer call already returned");
	}

	[Test]
	[Description("TryResolve returns a failure envelope without querying Creatio when schema-name is blank.")]
	public void TryResolve_ShouldFailBeforeRemoteCalls_WhenSchemaNameIsBlank() {
		// Arrange
		var options = new GetClassicListColumnsOptions { SchemaName = " " };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "a section schema name is required to resolve any metadata");
		response.Success.Should().BeFalse(because: "validation errors use the failure envelope");
		response.Error.Should().Be("schema-name is required",
			because: "the error travels into the machine-consumed `error` field, so it must not carry the C# "
				+ "parameter name ArgumentException appends when given a nameof argument");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("ParseColumns merges a single schema declaring both supported methods in the fixed getGridDataColumns-then-initColumnsConfig order.")]
	public void ParseColumns_ShouldMergeGridDataColumnsFirst_WhenOneSchemaDeclaresBothMethods() {
		// Arrange — one body declaring both methods; initColumnsConfig is written first on purpose so the
		// assertion pins the merge rule rather than the source order inside the body.
		string body = """
			define([], function() { return {
			  entitySchemaName: "Contact",
			  initColumnsConfig: function() { return [{ path: "Name" }, { path: "CreatedOn" }]; },
			  getGridDataColumns: function() { return { Id: { path: "Id" }, Name: { path: "Name" } }; }
			}; });
			""";

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns([body]).Columns;

		// Assert
		result.Should().Equal(["Id", "Name", "CreatedOn"],
			because: "ColumnMethodNames fixes the merge order as getGridDataColumns then initColumnsConfig, "
				+ "with the shared seen set keeping the first occurrence of a repeated path");
	}

	[Test]
	[Description("ParseColumns reports how many schema layers could not be parsed instead of dropping them silently.")]
	public void ParseColumns_ShouldReportUnparsedLayers_WhenABodyIsNotValidJavaScript() {
		// Arrange
		string[] bodies = [
			"getGridDataColumns: function() { return { Name: { path: 'Name' } }; }",
			"getGridDataColumns: function() { return {"
		];

		// Act
		ClassicListColumnParseResult result = _parser.ParseColumns(bodies);

		// Assert
		result.UnparsedLayerCount.Should().Be(1,
			because: "a body that survives neither the direct parse nor the object re-wrap must be counted, "
				+ "otherwise the caller cannot tell a complete answer from a partial one");
		result.Columns.Should().Equal(["Name"], because: "the layers that did parse still contribute their columns");
	}

	[Test]
	[Description("TryResolve notes the skipped layers when a section body cannot be parsed, so a degraded answer is not mistaken for a complete one.")]
	public void TryResolve_ShouldNoteSkippedLayers_WhenASectionBodyCannotBeParsed() {
		// Arrange — the most-derived layer is unparseable, so the answer silently comes from its ancestor
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'Contact', getGridDataColumns: function() { return {"),
			Schema("Base", """
				entitySchemaName: "Contact",
				getGridDataColumns: function() { return { Name: { path: "Name" } }; }
				""", BaseSchemaUId)
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "an unparseable layer degrades the answer but does not fail the command; error: {0}",
			response.Error);
		response.Source.Should().Be("schema-default", because: "the layers that did parse still declare static columns");
		response.Notes.Should().Contain(note => note.Contains("could not be parsed"),
			because: "the caller must be able to see that the resolved set may be incomplete");
	}

	[Test]
	[Description("TryResolve returns a failure envelope when the requested schema name is syntactically invalid.")]
	public void TryResolve_ShouldFail_WhenSchemaNameFormatIsInvalid() {
		// Arrange
		var options = new GetClassicListColumnsOptions { SchemaName = "1BadName-" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "an invalid schema name cannot address any Creatio metadata");
		response.Error.Should().Contain("must start with a letter",
			because: "the caller needs the canonical schema-name format error");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("TryResolve returns a failure envelope carrying the lookup error when SysSchema has no row for the section.")]
	public void TryResolve_ShouldFail_WhenSectionSchemaIsNotFound() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectUrl, Arg.Any<string>())
			.Returns("""{ "success": true, "rows": [] }""");
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrMissingSection" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "there is no section to resolve columns for");
		response.Success.Should().BeFalse(because: "a missing section is a failure envelope, not an empty success");
		response.Error.Should().Contain("not found", because: "the caller needs to know the schema does not exist");
		response.Error.Should().Contain("UsrMissingSection",
			because: "the failure must identify the schema the caller requested");
	}

	[Test]
	[Description("TryResolve returns a failure envelope when the SysSchema row is missing its UId or package UId.")]
	public void TryResolve_ShouldFail_WhenSchemaMetadataIsIncomplete() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectUrl, Arg.Any<string>()).Returns(
			$$$"""{ "success": true, "rows": [{ "UId": "{{{SchemaUId}}}", "PackageUId": "" }] }""");
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "the hierarchy call needs both identifiers");
		response.Error.Should().Contain("metadata is incomplete",
			because: "a blank UId or package UId is a distinct, diagnosable condition");
		_hierarchyClient.DidNotReceiveWithAnyArgs().GetParentSchemas(default, default);
	}

	[Test]
	[Description("TryResolve returns a failure envelope when the designer returns no hierarchy for the section.")]
	public void TryResolve_ShouldFail_WhenHierarchyIsEmpty() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([]);
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "without a hierarchy there is no schema body to parse");
		response.Error.Should().Contain("hierarchy is empty",
			because: "an empty designer result must be distinguishable from a section with no columns");
	}

	[Test]
	[Description("TryResolve returns a failure envelope when no layer of the hierarchy declares entitySchemaName.")]
	public void TryResolve_ShouldFail_WhenNoLayerDeclaresEntitySchemaName() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "diff: []")
		]);
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "column captions and the entity fallback both need the bound entity");
		response.Error.Should().Contain("does not declare entitySchemaName",
			because: "the caller needs to know the section is not bound to an entity");
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
	}

	[Test]
	[Description("TryResolve returns a failure envelope carrying the inner message when the entity metadata lookup throws.")]
	public void TryResolve_ShouldFail_WhenEntityMetadataLookupThrows() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'Contact', diff: []")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException("entity metadata unavailable"));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "the entity fallback and the captions both depend on the entity metadata");
		response.Error.Should().Contain("entity metadata unavailable",
			because: "the envelope is the only channel the caller has for the underlying reason");
	}

	[Test]
	[Description("ParseColumns records which Classic method declared each path so a consumer can tell rendered columns from loaded ones.")]
	public void ParseColumns_ShouldRecordColumnOrigins_WhenBothMethodsDeclareColumns() {
		// Arrange — Id is load-only, CreatedOn is render-only, Name is declared by both. The flattened list
		// alone cannot express that difference, which is the whole reason origins exist.
		string body = """
			define([], function() { return {
			  entitySchemaName: "Contact",
			  initColumnsConfig: function() { return [{ path: "Name" }, { path: "CreatedOn" }]; },
			  getGridDataColumns: function() { return { Id: { path: "Id" }, Name: { path: "Name" } }; }
			}; });
			""";

		// Act
		ClassicListColumnParseResult result = _parser.ParseColumns([body]);

		// Assert
		result.Columns.Should().Equal(["Id", "Name", "CreatedOn"],
			because: "adding origins must not change the merge order or the membership of the reported set");
		result.ColumnOrigins["Id"].Should().Be("getGridDataColumns",
			because: "Id is declared only by the method describing what the section LOADS");
		result.ColumnOrigins["CreatedOn"].Should().Be("initColumnsConfig",
			because: "CreatedOn is declared only by the method describing what the grid RENDERS");
		result.ColumnOrigins["Name"].Should().Be("both",
			because: "a path both methods declare must not be recorded as belonging to whichever one ran first — "
				+ "the duplicate is dropped from Columns, but the second declaration is still a fact about the body");
		result.DeclaresBothColumnMethods.Should().BeTrue(
			because: "the resolver turns this into the runtime note that the flattened set is an approximation");
	}

	[Test]
	[Description("ParseColumns reports a single-method section without the both-methods approximation flag.")]
	public void ParseColumns_ShouldNotFlagBothMethods_WhenOnlyOneMethodIsDeclared() {
		// Arrange
		string body = """
			define([], function() { return {
			  entitySchemaName: "Contact",
			  getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			}; });
			""";

		// Act
		ClassicListColumnParseResult result = _parser.ParseColumns([body]);

		// Assert
		result.DeclaresBothColumnMethods.Should().BeFalse(
			because: "the merge approximation only exists when both methods contribute; flagging it otherwise "
				+ "would attach a caveat to a set that has none");
		result.ColumnOrigins["Name"].Should().Be("getGridDataColumns",
			because: "the origin is a fact about the body regardless of how many methods it declares");
	}

	[Test]
	[Description("ParseColumns counts a layer that composes its parent and deletes a column, since subtraction is not applied.")]
	public void ParseColumns_ShouldCountSubtractiveLayers_WhenAnOverrideDeletesAComposedColumn() {
		// Arrange — the shape attested in real Classic bodies: compose the parent, then remove a key. The
		// override declares no literal of its own, so StartDate survives the additive walk.
		string[] bodies = [
			"""
			define([], function() { return {
			  entitySchemaName: "Activity",
			  getGridDataColumns: function() { return { Title: { path: "Title" }, StartDate: { path: "StartDate" } }; }
			}; });
			""",
			"""
			define([], function() { return {
			  getGridDataColumns: function() { var c = this.callParent(arguments); delete c.StartDate; return c; }
			}; });
			"""
		];

		// Act
		ClassicListColumnParseResult result = _parser.ParseColumns(bodies);

		// Assert
		result.Columns.Should().Equal(["Title", "StartDate"],
			because: "the composition is additive only, so the deleted column survives — this test pins the "
				+ "current behaviour rather than claiming it is right");
		result.SubtractiveLayerCount.Should().Be(1,
			because: "the resolver needs to say so at runtime instead of presenting a confidently wrong set");
	}

	[Test]
	[Description("Resolve notes the merge approximation and exposes per-column origins when a section declares both methods.")]
	public void TryResolve_ShouldNoteTheMergeApproximation_WhenSectionDeclaresBothMethods() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", """
				entitySchemaName: "Contact",
				initColumnsConfig: function() { return [{ path: "Name" }]; },
				getGridDataColumns: function() { return { Id: { path: "Id" }, Name: { path: "Name" } }; }
				""")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name"), Column("Id", "Id")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "the hierarchy resolves; error: {0}", response.Error);
		response.Notes.Should().ContainSingle(note => note.Contains("both getGridDataColumns and initColumnsConfig"),
			because: "the approximation must be visible at runtime, not only in the doc");
		response.Columns.Single(column => column.Name == "Id").Origin.Should().Be("getGridDataColumns",
			because: "a consumer selecting the rendered set has to be able to drop the load-only column");
		response.Columns.Single(column => column.Name == "Name").Origin.Should().Be("both",
			because: "Name is both loaded and rendered, and either selection keeps it");
	}

	[Test]
	[Description("Resolve omits origin on the entity-default fallback, where no Classic method declared the column.")]
	public void TryResolve_ShouldOmitOrigin_WhenColumnsComeFromTheEntityFallback() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'Contact', diff: []")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("entity-default", because: "the section declares no static columns");
		response.Columns.Single().Origin.Should().BeNull(
			because: "naming a declaring method for a column the entity fallback invented would be a claim "
				+ "about the section body that is not true");
	}

	[Test]
	[Description("Execute redacts the error field, not only notes, before writing the response to stdout.")]
	public void Execute_ShouldRedactError_WhenTheFailureMessageCarriesAUri() {
		// Arrange — the entity metadata read is one of the pipeline steps whose exception reaches Error verbatim,
		// and a transport message there routinely carries the host and full request URI.
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'Contact', diff: []")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException(
				"Request to http://secret-host:8080/0/DataService/json/SyncReply/SelectQuery failed"));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a failed resolution exits non-zero");
		string written = LastWrittenInfo();
		written.Should().NotContain("secret-host",
			because: "the CLI writes the serialized response straight to stdout, so Error has to be redacted "
				+ "here exactly as the MCP tool redacts it before returning");
		written.Should().Contain("\"success\":false",
			because: "redaction must not disturb the failure envelope the caller parses");
	}

	[Test]
	[Description("Execute writes the serialized response and exits zero on a successful resolution.")]
	public void Execute_ShouldWriteSerializedResponseAndExitZero_WhenResolutionSucceeds() {
		// Arrange
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", """
				entitySchemaName: "Contact",
				getGridDataColumns: function() { return { Name: { path: "Name" } }; }
				""")
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Contact", "Name", Column("Name", "Full name")));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the 0/1 exit code is part of the CLI contract and nothing else asserts it");
		string written = LastWrittenInfo();
		written.Should().Contain("\"source\":\"schema-default\"",
			because: "the serialized response is the command's only output channel");
		written.Should().Contain("\"origin\":\"getGridDataColumns\"",
			because: "the provenance field has to survive serialization to reach the consumer");
	}

	[Test]
	[Description("TryResolve reports the saved grid profile as the column source and prefers it over the section's static declaration.")]
	public void TryResolve_ShouldReturnProfile_WhenSavedGridProfileDeclaresColumns() {
		// Arrange — the body declares a NARROWER set on purpose: a product section's getGridDataColumns is a
		// load-adding layer, not the view definition, so the profile answer has to win rather than merge.
		ArrangeSection("""
			entitySchemaName: "Account",
			getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			""", "Account", "Name", Column("Name", "Entity title"));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(false,
			listed: [("Name", "Name"), ("PrimaryContact", "Primary contact"), ("Phone", "Primary phone")],
			tiled: [("Name", "Name"), ("Web", "Web")]));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeTrue(because: "a saved profile is a supported source; error: {0}", response.Error);
		response.Source.Should().Be("profile",
			because: "the saved profile holds what the list renders, so it outranks the static declaration");
		response.Columns.Select(column => column.Name).Should().Equal(["Name", "PrimaryContact", "Phone"],
			because: "the reported set and its order must be the stored configuration's, not the body's");
		response.Columns[1].Caption.Should().Be("Primary contact",
			because: "the caption the profile stores is the header the user actually sees");
		response.View.Should().Be("GridDataView", because: "the consumer has to know which view answered");
		response.ViewType.Should().Be("listed",
			because: "a grid stores a listed and a tiled configuration with different sets, so the answer is "
				+ "incomplete without naming the one used");
		response.ProfileScope.Should().Be("shared",
			because: "no personal row exists for the caller, so this is the section's shared default");
	}

	[Test]
	[Description("TryResolve reports the tiled configuration when the saved profile marks the grid as tiled.")]
	public void TryResolve_ShouldReportTheTiledConfiguration_WhenTheProfileMarksTheGridTiled() {
		// Arrange
		ArrangeSection("entitySchemaName: 'Contact', diff: []", "Contact", "Name", Column("Name", "Full name"));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(true,
			listed: [("Name", "Name")],
			tiled: [("Name", "Name"), ("Job", "Job title")]));
		var options = new GetClassicListColumnsOptions { SchemaName = "ContactSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.ViewType.Should().Be("tiled", because: "the stored isTiled flag selects which configuration renders");
		response.Columns.Select(column => column.Name).Should().Equal(["Name", "Job"],
			because: "the tiled configuration carries its own set and order");
	}

	[Test]
	[Description("TryResolve falls back to the other stored configuration and notes it when the active one is empty.")]
	public void TryResolve_ShouldFallBackToTheOtherConfiguration_WhenTheActiveOneIsEmpty() {
		// Arrange — a stand can store an empty active configuration; reporting nothing would then hand the
		// answer to the static branch even though the profile plainly holds a usable set.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(true, listed: [("Name", "Name")], tiled: []));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "a usable stored configuration is still a profile answer");
		response.ViewType.Should().Be("listed",
			because: "viewType must name the configuration actually reported, not the active flag");
		response.Notes.Should().Contain(note => note.Contains("is empty"),
			because: "substituting the inactive configuration changes the answer and must not be silent");
	}

	[Test]
	[Description("TryResolve reads the legacy profile shape where the configurations are top-level JSON arrays.")]
	public void TryResolve_ShouldReadTheLegacyProfileShape_WhenConfigurationsAreTopLevelArrays() {
		// Arrange — older profile keys keep the two configurations at the top level as arrays of cells, with the
		// binding nested under key[].name.bindTo instead of a flat bindTo.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Entity title"));
		const string legacy = """
			{
			  "isTiled": false,
			  "listedColumnsConfig": "[{\"metaPath\":\"Name\"},{\"key\":[{\"name\":{\"bindTo\":\"Type\"},\"caption\":\"Type\"}]}]",
			  "tiledColumnsConfig": "[[{\"metaPath\":\"Web\"}]]"
			}
			""";
		ArrangeProfile(ActiveView("mainView"), legacy);
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "a legacy stored configuration is still the rendered set");
		response.Columns.Select(column => column.Name).Should().Equal(["Name", "Type"],
			because: "both the flat metaPath cell and the nested key[].name.bindTo cell must be harvested");
		response.Columns[0].Caption.Should().Be("Entity title",
			because: "a legacy cell without a stored caption falls back to the entity column title");
		response.View.Should().Be("mainView", because: "the active view name comes from the profile, not a constant");
	}

	[Test]
	[Description("TryResolve flattens a legacy tiled profile configuration, which nests one array per rendered row.")]
	public void TryResolve_ShouldFlattenLegacyTiledRows_WhenTheLegacyProfileIsTiled() {
		// Arrange — the legacy tiled shape is an array of ROWS, each an array of cells, while the legacy listed
		// shape is flat. Without flattening, a tiled legacy profile yields no columns at all and the answer
		// silently falls through to the static declaration.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		const string legacyTiled = """
			{
			  "isTiled": true,
			  "listedColumnsConfig": "[{\"metaPath\":\"Ignored\"}]",
			  "tiledColumnsConfig": "[[{\"metaPath\":\"Name\",\"caption\":\"Name\"}],[{\"metaPath\":\"Web\",\"caption\":\"Web\"},{\"metaPath\":\"City\",\"caption\":\"City\"}]]"
			}
			""";
		ArrangeProfile(ActiveView("mainView"), legacyTiled);
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "a legacy tiled configuration is still the rendered set");
		response.ViewType.Should().Be("tiled", because: "the stored isTiled flag selects the tiled configuration");
		response.Columns.Select(column => column.Name).Should().Equal(["Name", "Web", "City"],
			because: "cells of every rendered row must be flattened into one ordered list, and the listed "
				+ "configuration must not contribute");
	}

	[Test]
	[Description("TryResolve marks a profile answer as user-scoped and warns when the calling user has a personal row for the list.")]
	public void TryResolve_ShouldMarkTheProfileUserScoped_WhenTheCallerHasAPersonalRow() {
		// Arrange
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []),
			personalRowExists: true);
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.ProfileScope.Should().Be("user",
			because: "a personal row means the layout can be this user's own rather than the section's default");
		response.Notes.Should().Contain(note => note.Contains("personal profile"),
			because: "presenting one user's saved layout as the canonical set is exactly what must not happen "
				+ "silently");
	}

	[Test]
	[Description("TryResolve reports an unknown profile scope and notes it when the calling user's contact cannot be read.")]
	public void TryResolve_ShouldReportUnknownScope_WhenTheCallersContactCannotBeRead() {
		// Arrange — the scope check is two extra reads; losing them must cost the classification, not the answer.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		_applicationClient.ExecutePostRequest(UserInfoUrl, Arg.Any<string>()).Returns("{}");
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "an unclassifiable profile is still the rendered set");
		response.ProfileScope.Should().Be("unknown",
			because: "claiming 'shared' without evidence would understate the risk of reporting a personal layout");
		response.Notes.Should().Contain(note => note.Contains("personal or shared"),
			because: "the lost distinction has to be visible to the consumer");
	}

	[Test]
	[Description("TryResolve notes a malformed stored configuration and still answers from the static declaration.")]
	public void TryResolve_ShouldNoteMalformedProfileJson_AndFallThroughToTheStaticDeclaration() {
		// Arrange
		ArrangeSection("""
			entitySchemaName: "Account",
			getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			""", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("GridDataView"),
			"""{ "isTiled": false, "listedColumnsConfig": "not json", "tiledColumnsConfig": "" }""");
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("schema-default",
			because: "an unreadable profile must not block the answer the section itself declares");
		response.Notes.Should().Contain(note => note.Contains("not valid JSON"),
			because: "an unreadable stored configuration and an absent one lead to the same fallback, so only "
				+ "the note tells them apart");
		response.ViewType.Should().BeNull(because: "a non-profile answer must not advertise a stored view type");
	}

	[Test]
	[Description("TryResolve resolves the static declaration when the stand holds no profile for the section.")]
	public void TryResolve_ShouldReturnSchemaDefault_WhenTheStandHoldsNoProfile() {
		// Arrange — the ordinary case for a section nobody has opened: QueryProfile answers with an empty object.
		ArrangeSection("""
			entitySchemaName: "UsrThing",
			getGridDataColumns: function() { return { UsrName: { path: "UsrName" } }; }
			""", "UsrThing", "UsrName", Column("UsrName", "Name"));
		ArrangeProfile("{}", "{}");
		var options = new GetClassicListColumnsOptions { SchemaName = "UsrThingSection" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("schema-default",
			because: "an absent profile is not a failure, it just hands the answer to the next source");
		response.Notes.Should().BeEmpty(
			because: "an absent profile is the normal case and must not add noise to every custom section");
		response.ProfileScope.Should().BeNull(because: "there is no profile to classify");
	}

	[Test]
	[Description("TryResolve skips the profile entirely and reports only statically declared columns when ignore-profile is set.")]
	public void TryResolve_ShouldSkipTheProfile_WhenIgnoreProfileIsSet() {
		// Arrange — the profile is deliberately stubbed with a WIDER set, so passing the flag has to be what
		// changes the answer rather than the stand happening to hold nothing.
		ArrangeSection("""
			entitySchemaName: "Account",
			getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			""", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("GridDataView"),
			ModernProfile(false, listed: [("Name", "Name"), ("Phone", "Primary phone")], tiled: []));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2", IgnoreProfile = true };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("schema-default",
			because: "ignore-profile answers the narrower question of what the section declares in code");
		response.Columns.Select(column => column.Name).Should().Equal(["Name"],
			because: "the stored configuration must not leak into the static answer");
		_applicationClient.DidNotReceive().ExecutePostRequest(ProfileUrl, Arg.Any<string>());
		response.View.Should().BeNull(because: "a static answer has no stored view to report");
	}

	[Test]
	[Description("TryResolve keeps the skipped-layer degradation note off a profile answer, where it would be a false claim.")]
	public void TryResolve_ShouldNotReportSkippedLayers_WhenTheAnswerComesFromAProfile() {
		// Arrange — one unparseable layer plus a readable one. The note it produces is about the BODIES, so it
		// must not travel with columns that came out of a stored configuration.
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([
			Schema("Top", "entitySchemaName: 'Account', diff: []"),
			Schema("Broken", "function( {", BaseSchemaUId)
		]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties("Account", "Name", Column("Name", "Name")));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "the stored configuration still answers");
		response.Notes.Should().NotContain(note => note.Contains("may be incomplete"),
			because: "a layer that could not be parsed says nothing about a profile-sourced set, so claiming the "
				+ "columns may be incomplete would be affirmatively false");
	}

	[Test]
	[Description("TryResolve composes the exact profile keys it reads: <Section>ActiveViewSettingsProfile and <Section>GridSettings<ActiveView>.")]
	public void TryResolve_ShouldPinTheComposedProfileKeys_WhenReadingASavedProfile() {
		// Arrange — the fixture matches request bodies loosely on purpose (a stub cannot know the key before the
		// active view is read), so the KEY CONTRACT is pinned here instead: without this, dropping the section
		// prefix or the resolved view suffix leaves every profile test green while the live read returns nothing
		// and the command silently reverts to schema-default — the exact defect ENG-95229 was filed for.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("mainView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "the arranged profile answers");
		_applicationClient.Received(1).ExecutePostRequest(ProfileUrl,
			Arg.Is<string>(body => JObject.Parse(body).Value<string>("key") == "AccountSectionV2ActiveViewSettingsProfile"));
		_applicationClient.Received(1).ExecutePostRequest(ProfileUrl,
			Arg.Is<string>(body => JObject.Parse(body).Value<string>("key") == "AccountSectionV2GridSettingsmainView"));
	}

	[Test]
	[Description("TryResolve filters the personal-row check on both the composed grid key and the calling user's contact.")]
	public void TryResolve_ShouldPinTheScopeFilters_WhenClassifyingTheProfile() {
		// Arrange — the contact filter is the mechanism the ticket's privacy constraint rests on: a scope query
		// that dropped it would report `user` for a row belonging to somebody else, so the filters are asserted
		// rather than left to a substring stub that matches either way.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("mainView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []),
			personalRowExists: true);
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.ProfileScope.Should().Be("user", because: "the arranged personal row exists for the caller");
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl, Arg.Is<string>(body =>
			body.Contains("SysProfileData")
			&& body.Contains("AccountSectionV2GridSettingsmainView")
			&& body.Contains(ContactId)));
	}

	[Test]
	[Description("TryResolve reports a FAILED profile read as a note instead of letting it read as 'this stand holds no profile'.")]
	public void TryResolve_ShouldNoteAFailedProfileRead_WhenQueryProfileThrows() {
		// Arrange — an expired session, a permission-gated route or a transport error must not be indistinguishable
		// from a pristine stand: both answer schema-default, and only the note tells the caller which one it got.
		ArrangeSection("""
			entitySchemaName: "Account",
			getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			""", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("mainView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		_applicationClient
			.ExecutePostRequest(ProfileUrl, Arg.Is<string>(body => body.Contains("GridSettings")))
			.Returns(_ => throw new InvalidOperationException("403 Forbidden"));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Success.Should().BeTrue(because: "the static declaration is still a usable answer");
		response.Source.Should().Be("schema-default", because: "no profile could be read");
		response.Notes.Should().Contain(note => note.Contains("could not be read") && note.Contains("403 Forbidden"),
			because: "a failed read that reports nothing is the one degradation the caller cannot detect, and "
				+ "without the reason a permission-gated route is indistinguishable from a pristine stand");
		response.View.Should().BeNull(because: "no view was read, so naming one would be a false claim");
	}

	[Test]
	[Description("TryResolve says the active view was assumed when its own read failed, so `view` is never presented as an answer that was read.")]
	public void TryResolve_ShouldNoteAnAssumedActiveView_WhenTheActiveViewReadFails() {
		// Arrange — the active-view read and the grid read are separate round-trips; only the first one fails here.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("mainView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		_applicationClient
			.ExecutePostRequest(ProfileUrl, Arg.Is<string>(body => body.Contains("ActiveViewSettingsProfile")))
			.Returns(_ => throw new InvalidOperationException("session expired"));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "the grid read still answered under the default view name");
		response.View.Should().Be("GridDataView", because: "the platform default is the assumed fallback");
		response.Notes.Should().Contain(
			note => note.Contains("active view could not be read") && note.Contains("session expired"),
			because: "reporting a view nobody read as the section's active view is exactly the silent claim to "
				+ "avoid, and the failure's own reason is what tells a broken session from an unconfigured one");
	}

	[Test]
	[Description("TryResolve explains a profile that EXISTS but yields no columns, the case the reader's own invariant singles out.")]
	public void TryResolve_ShouldNoteAnUnreadableProfileShape_WhenAnExistingProfileYieldsNoColumns() {
		// Arrange — a present payload whose configuration keys are none the parser knows. Without a note this is
		// byte-identical on the wire to a stand that holds no profile at all.
		ArrangeSection("""
			entitySchemaName: "Account",
			getGridDataColumns: function() { return { Name: { path: "Name" } }; }
			""", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("mainView"), """{ "someUnknownShape": { "columns": [] } }""");
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("schema-default", because: "no columns came out of the stored payload");
		response.Notes.Should().Contain(note => note.Contains("no column configuration could be read"),
			because: "a payload shape the parser does not understand looks like a parser failure and must say so");
	}

	[Test]
	[Description("TryResolve keeps answering when a modern stored configuration holds literals instead of item objects.")]
	public void TryResolve_ShouldSurviveNonObjectItems_WhenTheStoredConfigurationHoldsLiterals() {
		// Arrange — indexing a bare JToken with a string throws for a JValue, so one string or null element used
		// to take down the whole read-only command and lose the static answer it could still have given.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Phone", "Phone"));
		const string literals = """
			{
			  "isTiled": true,
			  "DataGrid": {
			    "isTiled": false,
			    "listedConfig": "{\"items\":[\"Name\",null,17,{\"bindTo\":\"Phone\",\"caption\":\"Phone\"}]}",
			    "tiledConfig": "{}"
			  }
			}
			""";
		ArrangeProfile(ActiveView("mainView"), literals);
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Success.Should().BeTrue(because: "one unexpected element type must not fail a read-only command");
		response.Source.Should().Be("profile", because: "the readable items still answer");
		response.Columns.Select(column => column.Name).Should().Equal(["Phone"],
			because: "the literal elements carry no binding and are skipped, not fatal");
	}

	[Test]
	[Description("TryResolve keeps answering when a legacy configuration nests deeper than the one level the flatten removes.")]
	public void TryResolve_ShouldSurviveDeeplyNestedLegacyRows_WhenOneFlattenLevelIsNotEnough() {
		// Arrange — the flatten removes exactly one level, so a doubly nested row survives as a JArray cell, and
		// indexing that with a string key throws ArgumentException.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		const string nested = """
			{
			  "isTiled": false,
			  "listedColumnsConfig": "[[{\"metaPath\":\"Name\"}],[[{\"metaPath\":\"Web\"}]]]",
			  "tiledColumnsConfig": "[]"
			}
			""";
		ArrangeProfile(ActiveView("mainView"), nested);
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Success.Should().BeTrue(because: "an unexpected nesting depth must not fail the command");
		response.Columns.Select(column => column.Name).Should().Equal(["Name"],
			because: "the cells one level down are still harvested and the deeper array is skipped");
	}

	[Test]
	[Description("TryResolve reports an unknown scope with a note when the personal-row check itself fails.")]
	public void TryResolve_ShouldReportUnknownScope_WhenTheScopeQueryFails() {
		// Arrange — SelectQueryHelper throws on a success:false envelope, and that branch decides between the
		// honest `unknown` and an unsupported `shared` claim, so it is pinned rather than left to inspection.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("mainView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysProfileData")))
			.Returns("""{ "success": false, "errorInfo": { "message": "profile lookup refused" } }""");
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "the columns were read; only their classification failed");
		response.ProfileScope.Should().Be("unknown",
			because: "claiming `shared` on a failed check would assert something the command did not establish");
		response.Notes.Should().Contain(note => note.Contains("could not be classified as personal or shared"),
			because: "the reason the scope is unknown belongs in the answer");
	}

	[Test]
	[Description("TryResolve carries the contact read's own failure reason into the scope note when that read throws.")]
	public void TryResolve_ShouldNoteTheContactReadFailureReason_WhenTheContactReadThrows() {
		// Arrange — an empty user-info body and a throwing one both cost the classification, but only the second
		// one carries a diagnosis; dropping it leaves a broken session looking like a stand that answers blankly.
		ArrangeSection("entitySchemaName: 'Account', diff: []", "Account", "Name", Column("Name", "Name"));
		ArrangeProfile(ActiveView("GridDataView"), ModernProfile(false, listed: [("Name", "Name")], tiled: []));
		_applicationClient
			.ExecutePostRequest(UserInfoUrl, Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException("401 Unauthorized"));
		var options = new GetClassicListColumnsOptions { SchemaName = "AccountSectionV2" };

		// Act
		_command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		response.Source.Should().Be("profile", because: "the columns were read; only their classification failed");
		response.ProfileScope.Should().Be("unknown", because: "the caller's contact never came back");
		response.Notes.Should().Contain(
			note => note.Contains("personal or shared") && note.Contains("401 Unauthorized"),
			because: "the operator needs to see an authorization failure as such, not as a silent 'unknown'");
	}

	// The profile reader talks to three different endpoints, so the substitute is arranged PER URL: a single
	// catch-all response would let these tests pass without ever exercising the routing.
	private void ArrangeProfile(string activeViewResponse, string gridResponse, bool personalRowExists = false) {
		_serviceUrlBuilder.Build(ClassicListProfileReader.QueryProfileUrl).Returns(ProfileUrl);
		_serviceUrlBuilder.Build(CreatioServicePaths.GetCurrentUserInfo).Returns(UserInfoUrl);
		// The personal-row check goes through SelectQueryHelper, which asks for the route by ENUM while the
		// hierarchy walk asks for the same endpoint by path — both have to be stubbed or the check silently
		// degrades to an unknown scope and the assertions below stop testing anything.
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		_applicationClient
			.ExecutePostRequest(ProfileUrl, Arg.Is<string>(body => body.Contains("ActiveViewSettingsProfile")))
			.Returns(activeViewResponse);
		_applicationClient
			.ExecutePostRequest(ProfileUrl, Arg.Is<string>(body => body.Contains("GridSettings")))
			.Returns(gridResponse);
		_applicationClient.ExecutePostRequest(UserInfoUrl, Arg.Any<string>())
			.Returns($$"""{ "success": true, "userInfo": { "contactId": "{{ContactId}}" } }""");
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysProfileData")))
			.Returns(personalRowExists
				? $$"""{ "success": true, "rows": [{ "Id": "{{ContactId}}" }] }"""
				: """{ "success": true, "rows": [] }""");
	}

	private void ArrangeSection(
		string body,
		string entity,
		string primaryDisplayColumn,
		params EntitySchemaPropertyColumnInfo[] columns) {
		_hierarchyClient.GetParentSchemas(SchemaUId, PackageUId).Returns([Schema("Top", body)]);
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			Properties(entity, primaryDisplayColumn, columns));
	}

	private static string ActiveView(string viewName) => $$"""{ "activeViewName": "{{viewName}}" }""";

	// Builds the modern profile payload: the two configurations are JSON *strings* nested under DataGrid, which
	// is where the authoritative isTiled flag lives too. The outer isTiled is deliberately left true in every
	// payload — a stock section really does ship it disagreeing with the inner one, and reading the outer flag
	// silently returns the wrong view, so the fixture has to keep that trap in place.
	private static string ModernProfile(
		bool isTiled,
		(string path, string caption)[] listed,
		(string path, string caption)[] tiled) {
		static string Items((string path, string caption)[] columns) => columns.Length == 0
			? "{}"
			: new JObject {
				["items"] = new JArray(columns.Select(column =>
					new JObject { ["bindTo"] = column.path, ["caption"] = column.caption }))
			}.ToString(Formatting.None);
		return new JObject {
			["isTiled"] = true,
			["DataGrid"] = new JObject {
				["isTiled"] = isTiled,
				["listedConfig"] = Items(listed),
				["tiledConfig"] = Items(tiled)
			}
		}.ToString(Formatting.None);
	}

	// Execute's only output channel is the container-registered ILogger, so the assertions read what it received.
	private string LastWrittenInfo() {
		var written = new List<string>();
		_logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteInfo))
			.ToList()
			.ForEach(call => written.Add(call.GetArguments()[0] as string));
		written.Should().NotBeEmpty(because: "Execute writes the serialized response through the logger");
		return written[^1];
	}

	private static PageDesignerHierarchySchema Schema(string name, string body) => Schema(name, body, SchemaUId);

	private static PageDesignerHierarchySchema Schema(string name, string body, string uid) => new() {
		UId = uid,
		Name = name,
		PackageUId = PackageUId,
		PackageName = "TestPackage",
		Body = body
	};

	private static EntitySchemaPropertyColumnInfo Column(string name, string title) => new(
		name, Guid.NewGuid(), "own", title, null, "Text", false, false, null);

	private static EntitySchemaPropertiesInfo Properties(
		string name,
		string primaryDisplayColumn,
		params EntitySchemaPropertyColumnInfo[] columns) => new(
			name, name, null, "(merged: all packages)", null, false, "Id", primaryDisplayColumn,
			columns.Length, 0, null, false, false, null, false, null, false, false, false, false,
			null, null, columns);
}
