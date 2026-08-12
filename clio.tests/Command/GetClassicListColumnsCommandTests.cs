namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
internal class GetClassicListColumnsCommandTests : BaseCommandTests<GetClassicListColumnsOptions> {

	private const string SelectUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string SchemaUId = "11111111-1111-1111-1111-111111111111";
	private const string PackageUId = "22222222-2222-2222-2222-222222222222";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private GetClassicListColumnsCommand _command;
	private IClassicListColumnParser _parser;

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
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
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
		IReadOnlyList<string> result = _parser.ParseColumns(bodies);

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
		IReadOnlyList<string> result = _parser.ParseColumns([body]);

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
		IReadOnlyList<string> result = _parser.ParseColumns(bodies);

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
		IReadOnlyList<string> result = _parser.ParseColumns(bodies);

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
		IReadOnlyList<string> result = _parser.ParseColumns(bodies);

		// Assert
		result.Should().Equal(["CreatedOn"],
			because: "a nested function has its own execution scope and cannot compose or contribute to the outer method result");
	}

	[Test]
	[Description("ParseColumns accepts quoted Classic method and path property keys.")]
	public void ParseColumns_ShouldReadColumns_WhenMethodAndPathKeysAreQuoted() {
		// Arrange
		string body = """
			"getGridDataColumns": function() { return { Name: { "path": "Name" } }; }
			""";

		// Act
		IReadOnlyList<string> result = _parser.ParseColumns([body]);

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
		IReadOnlyList<string> columns = _parser.ParseColumns([body]);

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
		IReadOnlyList<string> columns = _parser.ParseColumns([body]);

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
	[Description("TryResolve returns a failure envelope without querying Creatio when schema-name is blank.")]
	public void TryResolve_ShouldFailBeforeRemoteCalls_WhenSchemaNameIsBlank() {
		// Arrange
		var options = new GetClassicListColumnsOptions { SchemaName = " " };

		// Act
		bool result = _command.TryResolve(options, out GetClassicListColumnsResponse response);

		// Assert
		result.Should().BeFalse(because: "a section schema name is required to resolve any metadata");
		response.Success.Should().BeFalse(because: "validation errors use the failure envelope");
		response.Error.Should().Contain("schema-name is required", because: "the caller needs an actionable option error");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	private static PageDesignerHierarchySchema Schema(string name, string body) => new() {
		UId = SchemaUId,
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
