using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Unit tests for <see cref="LegacyMobileSettingsReader"/>: the effective classic Mobile-wizard settings are the
/// ordered application (ROOT -> HEAD) of EVERY package layer's diff array — a schema such as
/// <c>MobileCaseGridPageSettingsDefaultWorkplace</c> may live in several packages, and missing one layer silently
/// changes the converted page. Raw bodies never leave the reader.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class LegacyMobileSettingsReaderTests {

	private const string SchemaName = "MobileCaseGridPageSettingsDefaultWorkplace";
	private const string RootUId = "11111111-1111-1111-1111-111111111111";
	private const string MiddleUId = "22222222-2222-2222-2222-222222222222";
	private const string HeadUId = "33333333-3333-3333-3333-333333333333";
	private const string DesignPackageUId = "99999999-9999-9999-9999-999999999999";

	private const string RootBody = """
		[
		  { "operation": "insert", "name": "settings", "values": { "entitySchemaName": "Case", "items": [], "subtitleItems": [], "groupItems": [], "settingsType": "GridPage", "operation": "insert", "localizableStrings": {} } },
		  { "operation": "insert", "name": "title", "values": { "row": 0, "content": "Number", "columnName": "Number", "dataValueType": 1, "operation": "insert" }, "parentName": "settings", "propertyName": "items", "index": 0 },
		  { "operation": "insert", "name": "sub-account", "values": { "row": 0, "content": "Account", "columnName": "Account", "dataValueType": 10, "operation": "insert" }, "parentName": "settings", "propertyName": "subtitleItems", "index": 0 }
		]
		""";

	private const string MiddleBody = """
		[
		  { "operation": "remove", "name": "sub-account" },
		  { "operation": "insert", "name": "grp-status", "values": { "row": 0, "content": "Status", "columnName": "Status", "dataValueType": 10, "operation": "insert" }, "parentName": "settings", "propertyName": "groupItems", "index": 0 }
		]
		""";

	private const string HeadBody = """
		[
		  { "operation": "merge", "name": "title", "values": { "content": "Case number" } },
		  { "operation": "insert", "name": "grp-owner", "values": { "row": 1, "content": "Owner", "columnName": "Owner", "dataValueType": 10, "operation": "insert" }, "parentName": "settings", "propertyName": "groupItems", "index": 1 }
		]
		""";

	private IApplicationClient _client;
	private IServiceUrlBuilder _urlBuilder;
	private IPageDesignerHierarchyClient _hierarchy;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_urlBuilder.Build(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
		_hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		_hierarchy.GetDesignPackageUId(Arg.Any<string>()).Returns(DesignPackageUId);
	}

	private LegacyMobileSettingsReader Reader() =>
		new(_client, _urlBuilder, _hierarchy, () => new JsonDiffApplier());

	/// <summary>Answers the SysSchema SelectQuery: the single-row lookup and the all-packages cross-check.</summary>
	private void ArrangeSysSchema(string uid, string packageUId, params string[] allPackages) {
		_client.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")), Arg.Any<string>())
			.Returns(ci => {
				string body = ci.ArgAt<string>(1);
				JObject query = JObject.Parse(body);
				int rowCount = query["rowCount"]?.Value<int>() ?? 1;
				var rows = new JArray();
				if (rowCount == 1) {
					rows.Add(new JObject { ["UId"] = uid, ["PackageUId"] = packageUId, ["PackageName"] = allPackages.Length > 0 ? allPackages[^1] : "Custom" });
				} else {
					foreach (string package in allPackages) {
						rows.Add(new JObject { ["PackageName"] = package });
					}
				}
				return new JObject { ["success"] = true, ["rows"] = rows }.ToString();
			});
	}

	private static PageDesignerHierarchySchema Layer(string uid, string package, string body, int schemaType = 0) =>
		new() { UId = uid, Name = SchemaName, PackageUId = package + "-uid", PackageName = package, SchemaVersion = 1, Body = body, SchemaType = schemaType };

	[Test]
	[Description("Three package layers are applied ROOT -> HEAD: the middle layer removes a subtitle column and inserts a group column, the head layer merges the title caption and appends a group column — and the merged settings reflect all of it, with every layer reported without its body.")]
	public void Read_ShouldMergeAllPackageLayersInHierarchyOrder_WhenSchemaSpansThreePackages() {
		// Arrange — the hierarchy service returns HEAD -> ROOT.
		ArrangeSysSchema(HeadUId, "Custom-uid", "CrtBase", "Product", "Custom");
		_hierarchy.GetParentSchemas(HeadUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			Layer(HeadUId, "Custom", HeadBody), Layer(MiddleUId, "Product", MiddleBody), Layer(RootUId, "CrtBase", RootBody)
		});

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeTrue(because: $"three well-formed layers merge cleanly: {result.Error}");
		result.Layers.Select(l => l.PackageName).Should().Equal(new[] { "CrtBase", "Product", "Custom" }, because: "layers are reported ROOT -> HEAD in package-hierarchy order");
		result.Layers.Select(l => l.OperationCount).Should().Equal(new[] { 3, 2, 2 }, because: "each layer reports how many operations it contributed");
		JObject settings = result.EffectiveSettings;
		settings["subtitleItems"]!.Should().BeEmpty(because: "the middle layer removed the only subtitle column");
		settings["groupItems"]!.Select(g => g["columnName"]!.ToString()).Should().Equal(new[] { "Status", "Owner" }, because: "middle inserted Status at 0, head appended Owner at 1");
		settings["items"]![0]!["content"]!.ToString().Should().Be("Case number", because: "the head layer's merge changed the title caption");
		result.Notes.Should().BeEmpty(because: "every SysSchema package is present in the hierarchy");
		result.HeadSchemaType.Should().Be(0, because: "the head layer's raw ClientUnitSchemaType is surfaced for the caller's detection report");
	}

	[Test]
	[Description("When the requested schema row resolves to a LOWER package, the reader re-queries the hierarchy from the ROOT schema so the upper replacing layers appear — otherwise a package layer would be silently dropped.")]
	public void Read_ShouldRequeryFromRoot_WhenRequestedRowIsNotTheHead() {
		// Arrange — the first read (from the requested uid) returns only root+itself; the root re-query returns all.
		ArrangeSysSchema(MiddleUId, "Product-uid", "CrtBase", "Product", "Custom");
		_hierarchy.GetParentSchemas(MiddleUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			Layer(MiddleUId, "Product", MiddleBody), Layer(RootUId, "CrtBase", RootBody)
		});
		_hierarchy.GetParentSchemas(RootUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			Layer(HeadUId, "Custom", HeadBody), Layer(MiddleUId, "Product", MiddleBody), Layer(RootUId, "CrtBase", RootBody)
		});

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeTrue(because: result.Error);
		result.Layers.Should().HaveCount(3, because: "the root re-query surfaced the Custom layer the first read did not return");
		result.EffectiveSettings["items"]![0]!["content"]!.ToString().Should().Be("Case number", because: "the head layer's merge was applied");
	}

	[Test]
	[Description("A package that stores the schema but is absent from the resolved hierarchy is reported in Notes (never silent), while the layers that were found still merge.")]
	public void Read_ShouldNoteMissingPackage_WhenSysSchemaHasALayerTheHierarchyDidNotReturn() {
		// Arrange
		ArrangeSysSchema(HeadUId, "Custom-uid", "CrtBase", "Product", "Custom", "UsrSecondCustom");
		_hierarchy.GetParentSchemas(HeadUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			Layer(HeadUId, "Custom", HeadBody), Layer(MiddleUId, "Product", MiddleBody), Layer(RootUId, "CrtBase", RootBody)
		});

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeTrue(because: "the found layers still merge");
		result.Notes.Should().ContainSingle(n => n.Contains("UsrSecondCustom") && n.Contains("NOT part of the resolved hierarchy"),
			because: "an unresolved package layer would silently change the page, so it is surfaced");
	}

	[Test]
	[Description("Escaped \\$ sequences in a stored body are resolved before JSON parsing, and a trailing semicolon is tolerated.")]
	public void Read_ShouldUnescapeDollar_BeforeParsing() {
		// Arrange
		string body = RootBody.Replace("\"content\": \"Number\"", "\"content\": \"\\$Number\"") + ";";
		ArrangeSysSchema(RootUId, "CrtBase-uid", "CrtBase");
		_hierarchy.GetParentSchemas(RootUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> { Layer(RootUId, "CrtBase", body) });

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeTrue(because: $"the escape is normalized before parsing: {result.Error}");
		result.EffectiveSettings["items"]![0]!["content"]!.ToString().Should().Be("$Number", because: "\\$ becomes $");
	}

	[Test]
	[Description("A Freedom UI JSON object body stored under a legacy settings name is detected as the ENG-95733 override case and reported as a clean failure naming the package, not a crash.")]
	public void Read_ShouldFailWithFreedomUiShape_WhenBodyIsAJsonObject() {
		// Arrange
		ArrangeSysSchema(HeadUId, "Custom-uid", "CrtBase", "Custom");
		_hierarchy.GetParentSchemas(HeadUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			Layer(HeadUId, "Custom", "{ \"viewConfigDiff\": [] }"), Layer(RootUId, "CrtBase", RootBody)
		});

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeFalse(because: "a Freedom UI body is not a wizard settings array");
		result.BodyShape.Should().Be(LegacyBodyShape.FreedomUiJsonObject, because: "the shape is classified for the caller");
		result.Error.Should().Contain("Custom").And.Contain("ENG-95733", because: "the failure names the package and the owning story");
	}

	[Test]
	[Description("A body that is neither an operation array nor a JSON object fails cleanly with the package name and the parse error.")]
	public void Read_ShouldFailUnparseable_WhenBodyIsNotJson() {
		// Arrange
		ArrangeSysSchema(RootUId, "CrtBase-uid", "CrtBase");
		_hierarchy.GetParentSchemas(RootUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> { Layer(RootUId, "CrtBase", "define(\"x\", [], function() {})") });

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeFalse(because: "an AMD module is not a settings array");
		result.BodyShape.Should().Be(LegacyBodyShape.Unparseable, because: "the shape is classified for the caller");
		result.Error.Should().Contain("not a JSON operation array", because: "the failure explains what was expected");
	}

	[Test]
	[Description("When no layer contains a 'settings' root the reader fails with a message naming the missing node.")]
	public void Read_ShouldFail_WhenNoSettingsRootExists() {
		// Arrange
		ArrangeSysSchema(RootUId, "CrtBase-uid", "CrtBase");
		_hierarchy.GetParentSchemas(RootUId, DesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			Layer(RootUId, "CrtBase", """[ { "operation": "insert", "name": "other", "values": { "a": 1 } } ]""")
		});

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeFalse(because: "there is nothing to convert without the settings root");
		result.Error.Should().Contain("'settings' root node", because: "the failure names the missing node");
	}

	[Test]
	[Description("A hierarchy service failure surfaces as a clean failure result (with the recovery hint), never as an exception.")]
	public void Read_ShouldFail_WhenHierarchyServiceThrows() {
		// Arrange
		ArrangeSysSchema(RootUId, "CrtBase-uid", "CrtBase");
		_hierarchy.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("designer down"));

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read(SchemaName);

		// Assert
		result.Success.Should().BeFalse(because: "the hierarchy could not be loaded");
		result.Error.Should().Contain("designer down", because: "the underlying error is preserved for the operator");
	}

	[Test]
	[Description("An unknown schema name fails with the lookup error instead of reading a hierarchy.")]
	public void Read_ShouldFail_WhenSchemaRowIsMissing() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new JObject { ["success"] = true, ["rows"] = new JArray() }.ToString());

		// Act
		LegacyMobileSettingsReadResult result = Reader().Read("MobileNopeGridPageSettings");

		// Assert
		result.Success.Should().BeFalse(because: "no SysSchema row exists");
		result.Error.Should().Contain("not found", because: "the lookup error is surfaced");
		_hierarchy.DidNotReceive().GetParentSchemas(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Unescape resolves \\$ to $, trims whitespace and drops a single trailing semicolon; an empty body yields an empty string.")]
	public void Unescape_ShouldNormalizeBodyText() {
		LegacyMobileSettingsReader.Unescape("  [\"\\$a\"] ;  ").Should().Be("[\"$a\"]", because: "escape, whitespace and terminator are normalized");
		LegacyMobileSettingsReader.Unescape(null).Should().BeEmpty(because: "a null body has nothing to normalize");
	}
}
