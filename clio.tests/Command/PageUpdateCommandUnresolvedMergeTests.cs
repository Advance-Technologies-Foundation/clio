using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// GH-1753: <c>update-page</c> saves a viewModelConfigDiff / modelConfigDiff merge whose path does not resolve,
/// which the platform differ then skips. The save must stay successful (the warning is advisory) but must no
/// longer be silent about it, on every write path that shares <see cref="PageUpdateCommand.TryUpdatePage"/>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandUnresolvedMergeTests {

	private const string Uid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string UnresolvedMerge =
		"""[{ "operation": "merge", "path": ["dataSources", "UsrNewDS"], "values": { "type": "crt.EntityDataSource" } }]""";
	private const string ResolvedMerge =
		"""[{ "operation": "merge", "path": ["dataSources", "PDS", "config"], "values": { "entitySchemaName": "Account" } }]""";

	private static readonly string SkippedCheckPrefix =
		PageUpdateCommand.UnresolvedMergeCheckSkippedWarning.Split("{0}")[0];

	private ServiceProvider _provider;
	private IApplicationClient _client;
	private IPageDesignerHierarchyClient _hierarchy;
	private PageUpdateCommand _command;

	private static string Body(string modelConfigDiff) =>
		"define('UsrPage',/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {return {"
		+ "viewConfigDiff:/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,"
		+ "viewModelConfigDiff:/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,"
		+ "modelConfigDiff:/**SCHEMA_MODEL_CONFIG_DIFF*/" + modelConfigDiff + "/**SCHEMA_MODEL_CONFIG_DIFF*/,"
		+ "handlers:/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,converters:/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,"
		+ "validators:/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/};});";

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>()).Returns(x => x.Arg<string>());
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns(ci => urls.Build(ServiceUrlBuilder.KnownRoutes[ci.Arg<ServiceUrlBuilder.KnownRoute>()]));
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SelectQuery")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[{\"UId\":\"" + Uid + "\"}]}");
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("GetSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonConvert.SerializeObject(new { success = true, schema = new { name = "UsrPage", body = Body("[]") } }));
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
		_hierarchy.GetDesignPackageUId(Arg.Any<string>()).Returns("pkg");
		_hierarchy.GetParentSchemas(Arg.Any<string>(), "pkg").Returns([
			new PageDesignerHierarchySchema { UId = Uid, Name = "UsrPage", PackageUId = "pkg", SchemaType = 9 },
			new PageDesignerHierarchySchema {
				UId = "base", Name = "Base",
				Body = Body("""[{ "operation": "merge", "path": [], "values": { "dataSources": { "PDS": { "config": {} } } } }]""")
			}
		]);
		ServiceCollection services = new();
		services.AddSingleton(_client).AddSingleton(urls).AddSingleton(_hierarchy)
			.AddSingleton(Substitute.For<ILogger>()).AddSingleton(Substitute.For<IPageBaselineGuard>())
			.AddSingleton(Substitute.For<IPersistedResourceKeyReader>())
			.AddTransient<Func<IJsonDiffApplier>>(_ => () => new JsonDiffApplier())
			.AddTransient<Func<IJsonPathDiffApplier>>(_ => () => new JsonPathDiffApplier())
			.AddTransient<IPageSchemaBodyParser, PageSchemaBodyParser>()
			.AddTransient<IPageUnresolvedMergeDetector, PageUnresolvedMergeDetector>()
			.AddTransient<PageUpdateCommand>();
		_provider = services.BuildServiceProvider();
		_command = _provider.GetRequiredService<PageUpdateCommand>();
	}

	[TearDown]
	public void TearDown() => _provider.Dispose();

	[TestCase("replace", false)]
	[TestCase("replace", true)]
	[TestCase("append", false)]
	[TestCase("append", true)]
	[Description("A merge into a data source key the parent schemas never define is reported as a warning on a save and on a dry run, in both write modes, and the save itself still succeeds.")]
	public void TryUpdatePage_ShouldWarnAboutUnresolvedConfigMerge_WhenPathDoesNotExist(string mode, bool dryRun) {
		// Arrange
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = mode, DryRun = dryRun, Body = Body(UnresolvedMerge) };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the warning is advisory and must not block the write: " + response.Error);
		response.Warnings.Should().ContainSingle(w => w.StartsWith("modelConfigDiff merge at path [\"dataSources\",\"UsrNewDS\"]"),
			because: "the caller must learn that this merge is saved but has no effect");
	}

	[TestCase("replace")]
	[TestCase("append")]
	[Description("A merge whose path exists in the parent schema's data source produces no unresolved-merge warning.")]
	public void TryUpdatePage_ShouldNotWarn_WhenConfigMergeResolvesInParent(string mode) {
		// Arrange
		PageUpdateOptions options = new() { SchemaName = "UsrPage", Mode = mode, Body = Body(ResolvedMerge) };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the body is valid: " + response.Error);
		(response.Warnings ?? []).Should().NotContain(w => w.Contains("modelConfigDiff merge"),
			because: "dataSources.PDS.config exists in the parent schema");
	}

	[Test]
	[Description("When the parent schemas cannot be read for the check, the save still succeeds and the response says the check did not run instead of staying silent.")]
	public void TryUpdatePage_ShouldSaveAndSayCheckDidNotRun_WhenHierarchyCannotBeRead() {
		// Arrange
		_hierarchy.GetParentSchemas(Uid, "pkg").Throws(new InvalidOperationException("hierarchy down"));
		PageUpdateOptions options = new() { SchemaName = "UsrPage", TargetSchemaUId = Uid, Body = Body(UnresolvedMerge) };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "an advisory check must never fail a save: " + response.Error);
		response.Warnings.Should().ContainSingle(w => w.StartsWith(SkippedCheckPrefix) && w.Contains("hierarchy down"),
			because: "a skipped check must be visible and carry its reason");
		_client.Received(1).ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A body whose config merges all target the root needs no hierarchy read for the check.")]
	public void TryUpdatePage_ShouldNotReadHierarchyForCheck_WhenBodyHasOnlyRootMerges() {
		// Arrange
		PageUpdateOptions options = new() {
			SchemaName = "UsrPage", TargetSchemaUId = Uid,
			Body = Body("""[{ "operation": "merge", "path": [], "values": { "dataSources": {} } }]""")
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the body is valid: " + response.Error);
		_hierarchy.DidNotReceive().GetParentSchemas(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("An HTTP timeout of the hierarchy read surfaces as TaskCanceledException; it must not fail the save, because no caller cancellation can reach this synchronous path.")]
	public void TryUpdatePage_ShouldSave_WhenHierarchyReadTimesOut() {
		// Arrange
		_hierarchy.GetParentSchemas(Uid, "pkg").Throws(new System.Threading.Tasks.TaskCanceledException("request timed out"));
		PageUpdateOptions options = new() { SchemaName = "UsrPage", TargetSchemaUId = Uid, Body = Body(UnresolvedMerge) };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a timed-out advisory read is a check that did not run, not a failed save: " + response.Error);
		response.Warnings.Should().ContainSingle(w => w.StartsWith(SkippedCheckPrefix),
			because: "the caller must learn the merge check did not run");
		_client.Received(1).ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The reason in the skipped-check warning is server-authored text: credentials are scrubbed, line breaks flattened and the length capped before it reaches the caller.")]
	public void TryUpdatePage_ShouldNeutralizeReason_WhenCheckCannotRun() {
		// Arrange
		string serverError = "boom at https://admin:S3cr3tPw@stand.local/0/rest\n" + new string('x', 5000);
		_hierarchy.GetParentSchemas(Uid, "pkg").Throws(new InvalidOperationException(serverError));
		PageUpdateOptions options = new() { SchemaName = "UsrPage", TargetSchemaUId = Uid, Body = Body(UnresolvedMerge) };

		// Act
		_command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		string warning = response.Warnings.Single(w => w.StartsWith(SkippedCheckPrefix));
		warning.Should().NotContain("S3cr3tPw", because: "a credential in a server error must never be echoed to the caller");
		warning.Should().NotContain("\n", because: "server text must not forge extra lines in the warning");
		warning.Length.Should().BeLessThan(1000, because: "a server stack trace must not turn one warning into a page");
	}

	[Test]
	[Description("The edited schema's own stored body is not part of the base: a replace body that patches a data source only that stored body creates is reported, because the write overwrites it.")]
	public void TryUpdatePage_ShouldWarn_WhenTargetExistsOnlyInTheOwnBodyBeingReplaced() {
		// Arrange
		_hierarchy.GetParentSchemas(Arg.Any<string>(), "pkg").Returns([
			new PageDesignerHierarchySchema {
				UId = Uid, Name = "UsrPage", PackageUId = "pkg", SchemaType = 9,
				Body = Body("""[{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": { "config": {} } } }]""")
			},
			new PageDesignerHierarchySchema {
				UId = "base", Name = "Base",
				Body = Body("""[{ "operation": "merge", "path": [], "values": { "dataSources": { "PDS": { "config": {} } } } }]""")
			}
		]);
		PageUpdateOptions options = new() {
			SchemaName = "UsrPage", Mode = "replace",
			Body = Body("""[{ "operation": "merge", "path": ["dataSources", "UsrNewDS", "config"], "values": { "entitySchemaName": "Account" } }]""")
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the warning is advisory: " + response.Error);
		response.Warnings.Should().ContainSingle(w => w.StartsWith("modelConfigDiff merge at path [\"dataSources\",\"UsrNewDS\",\"config\"]"),
			because: "replace overwrites the own body, so UsrNewDS no longer exists in what the merge layers over");
	}

	[Test]
	[Description("Append is checked on the MERGED body: a fragment that patches a data source the page's current body creates is not reported.")]
	public void TryUpdatePage_ShouldNotWarn_WhenAppendPatchesWhatTheCurrentBodyCreates() {
		// Arrange
		string currentBody = Body("""[{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": { "config": {} } } }]""");
		_client.ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("GetSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonConvert.SerializeObject(new { success = true, schema = new { name = "UsrPage", body = currentBody } }));
		PageUpdateOptions options = new() {
			SchemaName = "UsrPage", Mode = "append",
			Body = Body("""[{ "operation": "merge", "path": ["dataSources", "UsrNewDS", "config"], "values": { "entitySchemaName": "Account" } }]""")
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the merged body is valid: " + response.Error);
		(response.Warnings ?? []).Should().NotContain(w => w.Contains("modelConfigDiff merge"),
			because: "the merged body creates UsrNewDS before the fragment's merge patches it");
	}

	[Test]
	[Description("The parent check and the merge check share one hierarchy read on the target-schema-uid path.")]
	public void TryUpdatePage_ShouldReadHierarchyOnce_WhenBothChecksNeedIt() {
		// Arrange
		_hierarchy.GetParentSchemas(Uid, "pkg").Returns([
			new PageDesignerHierarchySchema { UId = Uid, Name = "UsrPage", PackageUId = "pkg", SchemaType = 9 },
			new PageDesignerHierarchySchema {
				UId = "base", Name = "Base",
				Body = "define('Base',/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {return {"
					+ "viewConfigDiff:/**SCHEMA_VIEW_CONFIG_DIFF*/[{operation:'insert',name:'MainContainer',values:{items:[]}}]/**SCHEMA_VIEW_CONFIG_DIFF*/,"
					+ "viewModelConfigDiff:/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,"
					+ "modelConfigDiff:/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,"
					+ "handlers:/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,converters:/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,"
					+ "validators:/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/};});"
			}
		]);
		string body = Body(UnresolvedMerge).Replace(
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{operation:'insert',name:'Child',parentName:'MainContainer',propertyName:'items',values:{}}]/**SCHEMA_VIEW_CONFIG_DIFF*/");
		PageUpdateOptions options = new() { SchemaName = "UsrPage", TargetSchemaUId = Uid, Validate = false, Body = body };

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the parent resolves and the merge warning is advisory: " + response.Error);
		_hierarchy.Received(1).GetParentSchemas(Uid, "pkg");
		response.Warnings.Should().Contain(w => w.StartsWith("modelConfigDiff merge at path"),
			because: "the merge check still ran on the shared read");
	}
}
