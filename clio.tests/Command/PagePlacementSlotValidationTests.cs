using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// GH-1752: a viewConfigDiff insert/move that names a parentName but no propertyName. The body-only rule, the
/// applier's fidelity to the client differ, and get-page's recovery read.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PagePlacementSlotValidationTests {

	internal static string WebBody(string diff) =>
		"define('UsrProof',/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {return {" +
		"viewConfigDiff:/**SCHEMA_VIEW_CONFIG_DIFF*/" + diff + "/**SCHEMA_VIEW_CONFIG_DIFF*/," +
		"viewModelConfigDiff:/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/," +
		"modelConfigDiff:/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/," +
		"handlers:/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,converters:/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/," +
		"validators:/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/};});";

	internal static string MobileBody(string diff) =>
		"{\"viewConfigDiff\":" + diff + ",\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";

	[TestCase("move", false)]
	[TestCase("move", true)]
	[TestCase("insert", false)]
	[TestCase("insert", true)]
	[Description("An insert or move that names a parentName but no propertyName is rejected on web and mobile bodies, and the error names the operation and the fix.")]
	public void Validate_ShouldReject_WhenPlacementNamesParentWithoutSlot(string operation, bool mobile) {
		// Arrange
		string diff = "[{\"operation\":\"merge\",\"name\":\"Other\",\"values\":{}},{\"operation\":\"" + operation
			+ "\",\"name\":\"Profile\",\"parentName\":\"FeedContainer\",\"index\":0,\"values\":{}}]";
		string body = mobile ? MobileBody(diff) : WebBody(diff);

		// Act
		SchemaValidationResult result = PagePlacementSlotValidation.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(because: "the differ indexes parent[undefined] and rejects the page");
		result.Errors.Should().ContainSingle(because: "exactly one operation lacks its slot");
		result.Errors[0].Should().Contain("viewConfigDiff[1]").And.Contain($"{operation} 'Profile'")
			.And.Contain("'FeedContainer'").And.Contain("\"propertyName\": \"items\"",
				because: "the caller needs the position, the element, the parent and the remedy");
		result.Errors[0].Should().Contain("Item \"FeedContainer\" is not a container for other items",
			because: "the message quotes the platform's own rejection");
	}

	[TestCase("{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"propertyName\":\"items\"}",
		TestName = "Validate_ShouldAccept_MoveWithSlot")]
	[TestCase("{\"operation\":\"move\",\"name\":\"Profile\",\"index\":0}",
		TestName = "Validate_ShouldAccept_MoveToRootWithoutParent")]
	[TestCase("{\"operation\":\"insert\",\"name\":\"Profile\",\"values\":{}}",
		TestName = "Validate_ShouldAccept_InsertToRootWithoutParent")]
	[TestCase("{\"operation\":\"set\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"values\":{}}",
		TestName = "Validate_ShouldAccept_SetWithoutSlot")]
	[TestCase("{\"operation\":\"Move\",\"name\":\"Profile\",\"parentName\":\"Feed\"}",
		TestName = "Validate_ShouldAccept_MisCasedVerbTheDifferDiscards")]
	[Description("Shapes the differ handles without a slot (root placement, set reusing the replaced element's slot, a verb the differ drops) are not rejected.")]
	public void Validate_ShouldAccept_WhenNoSlotIsRead(string operation) {
		// Arrange
		string body = MobileBody("[" + operation + "]");

		// Act
		SchemaValidationResult result = PagePlacementSlotValidation.Validate(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "the differ never reads propertyName for this shape");
		result.Errors.Should().BeEmpty(because: "a valid body carries no error");
	}

	[TestCase("\"\"")]
	[TestCase("null")]
	[TestCase("0")]
	[Description("An empty, null or non-string propertyName is treated as missing.")]
	public void Validate_ShouldReject_WhenSlotIsEmptyOrNotAString(string propertyName) {
		// Arrange
		string body = WebBody("[{operation:'move',name:'Profile',parentName:'Feed',propertyName:" + propertyName + "}]");

		// Act
		SchemaValidationResult result = PagePlacementSlotValidation.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(because: "the differ cannot place the element into such a slot");
	}

	[TestCase("{\"viewConfigDiff\": [")]
	[TestCase("define('UsrProof', function() { return {}; });")]
	[TestCase("")]
	[Description("A body whose diff cannot be read is left to the syntax validators instead of being rejected here.")]
	public void Validate_ShouldReportValid_WhenDiffCannotBeRead(string body) {
		// Arrange

		// Act
		SchemaValidationResult result = PagePlacementSlotValidation.Validate(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "this rule must never block a save on a parse failure of its own");
	}

	[TestCase("move")]
	[TestCase("insert")]
	[Description("The applier raises the client differ's not-a-container error, not ArgumentNullException, for a placement into an existing parent without a slot.")]
	public void JsonDiffApplier_ShouldThrowNotContainer_WhenPlacementIntoExistingParentHasNoSlot(string operation) {
		// Arrange
		var source = JArray.Parse("""
			[
				{ "name": "Main", "items": [ { "name": "Profile", "items": [] } ] },
				{ "name": "Feed", "items": [] }
			]
			""");
		var operations = JArray.Parse("[{\"operation\":\"" + operation + "\",\"name\":\""
			+ (operation == "move" ? "Profile" : "NewChild") + "\",\"parentName\":\"Feed\",\"index\":0,\"values\":{}}]");
		var applier = new JsonDiffApplier();

		// Act
		Action act = () => applier.Apply(source, operations);

		// Assert
		act.Should().Throw<JsonDiffApplierException>(
				because: "the client differ reads parent[undefined] and throws its not-a-container error")
			.WithMessage("Item \"Feed\" is not a container for other items");
	}
}

/// <summary>
/// GH-1752: the save path (update-page, sync-pages, the CLI) refuses a placement without a slot on web and
/// mobile pages, in every mode, even with validate=false.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PagePlacementSlotSaveGuardTests {
	private const string Uid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private ServiceProvider _provider;
	private IApplicationClient _client;
	private IPageDesignerHierarchyClient _hierarchy;
	private PageUpdateCommand _command;

	private void Arrange(bool mobile) {
		_client = Substitute.For<IApplicationClient>();
		_hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		var urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>()).Returns(x => x.Arg<string>());
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns(ci => urls.Build(ServiceUrlBuilder.KnownRoutes[ci.Arg<ServiceUrlBuilder.KnownRoute>()]));
		string empty = mobile ? PagePlacementSlotValidationTests.MobileBody("[]") : PagePlacementSlotValidationTests.WebBody("[]");
		string parentDiff = "[{\"operation\":\"insert\",\"name\":\"Main\",\"values\":{\"items\":[]}},"
			+ "{\"operation\":\"insert\",\"name\":\"Feed\",\"values\":{\"items\":[]}},"
			+ "{\"operation\":\"insert\",\"name\":\"Profile\",\"parentName\":\"Main\",\"propertyName\":\"items\",\"values\":{}}]";
		string parent = mobile ? PagePlacementSlotValidationTests.MobileBody(parentDiff) : PagePlacementSlotValidationTests.WebBody(parentDiff);
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SelectQuery")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[{\"UId\":\"" + Uid + "\"}]}");
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("GetSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonConvert.SerializeObject(new { success = true, schema = new { name = "UsrProof", body = empty } }));
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
		_hierarchy.GetDesignPackageUId(Arg.Any<string>()).Returns("pkg");
		_hierarchy.GetParentSchemas(Arg.Any<string>(), "pkg").Returns([
			new PageDesignerHierarchySchema { UId = Uid, Name = "UsrProof", PackageUId = "pkg", SchemaType = mobile ? 10 : 9 },
			new PageDesignerHierarchySchema { UId = "base", Name = "Base", SchemaType = mobile ? 10 : 9, Body = parent }
		]);
		var services = new ServiceCollection();
		services.AddSingleton(_client).AddSingleton(urls).AddSingleton(_hierarchy)
			.AddSingleton(Substitute.For<ILogger>()).AddSingleton(Substitute.For<IPageBaselineGuard>())
			.AddSingleton(Substitute.For<IPersistedResourceKeyReader>()).AddTransient<IJsonDiffApplier, JsonDiffApplier>()
			.AddTransient<Func<IJsonDiffApplier>>(sp => () => sp.GetRequiredService<IJsonDiffApplier>())
			.AddTransient<PageUpdateCommand>();
		_provider = services.BuildServiceProvider();
		_command = _provider.GetRequiredService<PageUpdateCommand>();
	}

	[TearDown]
	public void TearDown() => _provider?.Dispose();

	[TestCase(true, "replace", false)]
	[TestCase(true, "replace", true)]
	[TestCase(true, "append", false)]
	[TestCase(true, "append", true)]
	[TestCase(false, "replace", false)]
	[TestCase(false, "append", true)]
	[Description("No write path saves a move with a parentName but no propertyName, mobile or web, even with validate=false.")]
	public void TryUpdatePage_ShouldRejectPlacementWithoutSlot_WhenValidationIsDisabled(bool mobile, string mode, bool dryRun) {
		// Arrange
		Arrange(mobile);
		string diff = "[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"index\":0}]";
		var options = new PageUpdateOptions {
			SchemaName = "UsrProof", Validate = false, Mode = mode, DryRun = dryRun,
			Body = mobile ? PagePlacementSlotValidationTests.MobileBody(diff) : PagePlacementSlotValidationTests.WebBody(diff)
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a page saved with this operation can neither render nor be read back by get-page");
		response.Error.Should().StartWith(PageUpdateCommand.PlacementSlotFailurePrefix)
			.And.Contain("move 'Profile'").And.Contain("propertyName",
				because: "the refusal names the operation and the missing slot instead of 'Value cannot be null'");
		_client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("The same move with its propertyName slot is saved on mobile and web pages.")]
	public void TryUpdatePage_ShouldSave_WhenPlacementNamesItsSlot(bool mobile) {
		// Arrange
		Arrange(mobile);
		string diff = "[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"propertyName\":\"items\",\"index\":0}]";
		var options = new PageUpdateOptions {
			SchemaName = "UsrProof", Validate = false,
			Body = mobile ? PagePlacementSlotValidationTests.MobileBody(diff) : PagePlacementSlotValidationTests.WebBody(diff)
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a placement with its slot is valid: " + response.Error);
		_client.Received().ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}

/// <summary>
/// GH-1752: get-page reads a page whose chain carries a placement without a slot, reporting it, so the page can
/// be repaired through clio; every other strict-resolution failure still fails the read.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageGetCommandSlotlessPlacementTests {
	private const string SchemaName = "UsrProof_MobileFormPage";
	private const string ParentDiff =
		"[{\"operation\":\"insert\",\"name\":\"Main\",\"values\":{\"type\":\"crt.GridContainer\",\"items\":[]}},"
		+ "{\"operation\":\"insert\",\"name\":\"Feed\",\"values\":{\"type\":\"crt.GridContainer\",\"items\":[]}},"
		+ "{\"operation\":\"insert\",\"name\":\"Profile\",\"parentName\":\"Main\",\"propertyName\":\"items\",\"values\":{\"type\":\"crt.GridContainer\",\"items\":[]}}]";

	private static PageGetCommand CreateCommand(string headDiff, string parentDiff = ParentDiff) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/SelectQuery");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"Template"}]}""");
		IPageDesignerHierarchyClient hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchy.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchy.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1,
				SchemaType = 10, Body = PagePlacementSlotValidationTests.MobileBody(headDiff)
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "Template", PackageUId = "pkg-2", PackageName = "CrtPkg", SchemaVersion = 1,
				SchemaType = 10, Body = PagePlacementSlotValidationTests.MobileBody(parentDiff)
			}
		]);
		return new PageGetCommand(client, urls, Substitute.For<ILogger>(), hierarchy, new PageSchemaBodyParser(),
			new PageBundleBuilder(() => new JsonDiffApplier(), () => new JsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
	}

	private static JObject FindElement(JContainer tree, string name) =>
		tree.DescendantsAndSelf().OfType<JObject>().FirstOrDefault(x => x.Value<string>("name") == name);

	[Test]
	[Description("A page whose own body moves an element without a slot is read: the bundle is resolved without that move, a warning names it, and the raw body still carries it for repair.")]
	public void TryGetPage_ShouldReadPageWithWarning_WhenOwnBodyMovesWithoutSlot() {
		// Arrange
		PageGetCommand command = CreateCommand("[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"index\":0}]");
		var options = new PageGetOptions { SchemaName = SchemaName, Environment = "dev", ExcludeOwnBody = true };

		// Act
		bool ok = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		ok.Should().BeTrue(because: "the page must stay readable so it can be repaired through clio: " + response.Error);
		response.Warnings.Should().ContainSingle(because: "exactly one operation was skipped");
		response.Warnings[0].Should().Contain($"Schema '{SchemaName}' viewConfigDiff[0] (move 'Profile')")
			.And.Contain("update-page", because: "the warning names the schema, the operation and how to repair it");
		JArray view = JArray.Parse(response.Bundle.ViewConfig.ToJsonString());
		FindElement(view, "Main")["items"]!.Should().Contain(x => x.Value<string>("name") == "Profile",
			because: "the skipped move leaves the element where the template placed it");
		response.Raw.Body.Should().Contain("\"move\"", because: "the editable body is returned unchanged so the caller can fix it");
		response.BaseViewModelConfig.Should().NotBeNull(because: "the replace-mode base is still resolved for validation");
	}

	[Test]
	[Description("A chain that fails strict resolution for another reason still fails get-page, even when it also carries a placement without a slot.")]
	public void TryGetPage_ShouldFailStrictly_WhenAnotherOperationStillFails() {
		// Arrange
		string headDiff = "[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"index\":0},"
			+ "{\"operation\":\"insert\",\"name\":\"Orphan\",\"parentName\":\"Main\",\"propertyName\":\"tools\",\"values\":{}}]";
		PageGetCommand command = CreateCommand(headDiff);
		var options = new PageGetOptions { SchemaName = SchemaName, Environment = "dev" };

		// Act
		bool ok = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		ok.Should().BeFalse(because: "only the placement-without-slot shape is recovered; any other platform rejection stays strict");
		response.Error.Should().Contain("Failed to resolve page bundle").And.Contain("Item \"Main\" is not a container",
			because: "the remaining failure is reported in the strict wording");
		response.Warnings.Should().BeNull(because: "a failed read carries no recovery warnings");
	}

	[Test]
	[Description("A strict failure the slotless placement did not cause (here a cyclic parentName on that same operation) is not recovered, so get-page does not misreport it as a missing slot.")]
	public void TryGetPage_ShouldFailStrictly_WhenTheRejectionIsNotTheMissingSlot() {
		// Arrange
		PageGetCommand command = CreateCommand("[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Profile\",\"index\":0}]");
		var options = new PageGetOptions { SchemaName = SchemaName, Environment = "dev" };

		// Act
		bool ok = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		ok.Should().BeFalse(because: "the differ rejects the chain for a cycle, not for the missing slot");
		response.Error.Should().Contain("Cyclic dependency exists for object \"Profile\"",
			because: "the platform's real reason is reported, not a slot warning");
	}

	[Test]
	[Description("A page with no placement without a slot reads exactly as before, with no warnings.")]
	public void TryGetPage_ShouldNotWarn_WhenEveryPlacementNamesItsSlot() {
		// Arrange
		PageGetCommand command = CreateCommand("[{\"operation\":\"move\",\"name\":\"Profile\",\"parentName\":\"Feed\",\"propertyName\":\"items\",\"index\":0}]");
		var options = new PageGetOptions { SchemaName = SchemaName, Environment = "dev" };

		// Act
		bool ok = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		ok.Should().BeTrue(because: "a valid chain resolves strictly: " + response.Error);
		response.Warnings.Should().BeNull(because: "nothing was skipped");
		JArray view = JArray.Parse(response.Bundle.ViewConfig.ToJsonString());
		FindElement(view, "Feed")["items"]!.Should().Contain(x => x.Value<string>("name") == "Profile",
			because: "the valid move is applied");
	}
}
