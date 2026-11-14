using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerStoredReads;

namespace Clio.Mcp.E2E;

/// <summary>
/// <c>create-dynamic-case</c> over the real MCP server: a dry run that writes nothing, a create that writes the case,
/// its settings rows and bindings and the record-page bar, an equivalent re-run that answers already-present, a
/// different order on the same object that is refused as case-exists, and refusals that write nothing. Then
/// <c>describe-dynamic-case</c> and <c>modify-dynamic-case</c> on a case each test creates without the bar: a read by
/// object, a reorder planned and written, a stage removed and added back, a final stage group, and disable / enable.
/// </summary>
/// <remarks>
/// <para>Runs on the ProcessDesigner lane. Its seed ships in the AutoTestClioMcp 7.8.0 test package, which the
/// lane's stand installs: the object <see cref="Entity"/> with the lookup column <see cref="StageColumn"/> over
/// <see cref="StageLookup"/> (four values ordered by an integer position column that differs from both the display
/// and the creation order), a non-lookup column <see cref="TextColumn"/>, a tabbed Freedom UI record page
/// registered in its RelatedPage add-on, and no case on the object. The case itself is written to
/// <see cref="Package"/>. Without the object the tests ignore themselves on a developer stand and fail on the lane
/// (<c>McpE2E:RequireProcessBuilder</c>), where a missing seed means the stand content is broken, not that the
/// tests should be skipped. Locally it needs <c>McpE2E__Sandbox__EnvironmentName</c>,
/// <c>McpE2E__AllowDestructiveMcpTests=true</c>, a CrtProcessBuilder that carries BuildCase and the seed.</para>
/// <para>Cleanup follows the undo order the result enables, strictly in sequence: the link-row binding, the
/// settings-row binding only when this run CREATED the settings row (a reused row may hold other cases' link rows,
/// which deleting it would cascade away), the case schema, then the record page's own body restored. A replacing
/// page schema this run created is reported rather than deleted, because a delete by name can reach the base page.</para>
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category(McpE2ECategories.ProcessDesigner)]
[AllureFeature(CreateDynamicCaseTool.CreateDynamicCaseToolName)]
[NonParallelizable]
public sealed class CreateDynamicCaseToolE2ETests {

	/// <summary>The first CrtProcessBuilder that carries BuildCase.</summary>
	private const string MinimumPackageVersion = CreateDynamicCaseOptions.MinimumProcessBuilderVersion;

	/// <summary>The first CrtProcessBuilder that carries DescribeCase and ModifyCase.</summary>
	private const string CaseChangePackageVersion = DynamicCaseChangeRequirement.MinimumProcessBuilderVersion;

	/// <summary>The member only a describe result document carries; it has no outcome.</summary>
	private const string DescribeMarker = "cases";

	private const string Subject = "create-dynamic-case";
	private const string Package = "Custom";
	private const string Entity = "UsrDcmTOrder";
	private const string StageColumn = "UsrStatus";
	private const string StageLookup = "UsrDcmTStatus";

	/// <summary>The lookup's integer position column, passed explicitly: only unprefixed names (Position,
	/// StageOrder, Number, StageNumber) are detected without it, and a custom column carries the Usr prefix.</summary>
	private const string OrderColumn = "UsrPosition";
	private const string TextColumn = "UsrName";
	private const int StageCount = 4;

	[Test]
	[Description("A dry run plans the case in the lookup's position order and writes nothing: no case schema and no settings row for the object appear.")]
	[AllureName("A dry run plans the case and writes nothing")]
	public async Task CreateDynamicCase_ShouldPlanAndWriteNothing_OnADryRun() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		int casesBefore = (await CaseSchemaRowsAsync(context)).GetArrayLength();
		int settingsBefore = (await SettingsRowsAsync(context)).GetArrayLength();

		// Act
		CallToolResult planned = await CallAsync(context, new Dictionary<string, object?> { ["dry-run"] = true });

		// Assert
		IgnoreWhenProcessBuilderIsBehind(ToolText(planned), MinimumPackageVersion);
		ShouldHaveLanded(planned, "a dry run that passes every check is a success");
		JsonObject result = ResultOf(planned);
		Text(result, "outcome").Should().Be("planned", because: "a dry run plans");
		Text(result["case"]!.AsObject(), "orderSource").Should().StartWith("orderColumn:",
			because: "the seeded lookup orders by an integer position column");
		result["case"]!["stages"]!.AsArray().Should().HaveCount(StageCount, because: "one stage per lookup value");
		Text(result["page"]!.AsObject(), "status").Should().Be("planned",
			because: "the page checks and the append dry run ran too");
		(await CaseSchemaRowsAsync(context)).GetArrayLength().Should().Be(casesBefore, because: "a dry run writes no case");
		(await SettingsRowsAsync(context)).GetArrayLength().Should().Be(settingsBefore,
			because: "a dry run writes no settings row");
	}

	[Test]
	[Description("The lifecycle on one object: create writes the case in the package with four ordered stages, full-mesh connections and the designer colour, both settings rows and their bindings, and the bar on the record page; an equivalent re-run answers already-present for case and page and writes nothing; a different explicit order on the same object is refused as case-exists.")]
	[AllureName("create-dynamic-case creates, re-runs as already-present and refuses a different case")]
	public async Task CreateDynamicCase_ShouldCreate_ThenAnswerAlreadyPresent_ThenRefuseADifferentCase() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		string pageDirectory = ScratchDirectory.CreateUnderPhysicalTemp("clio-dynamic-case-e2e");
		JsonObject? created = null;
		PageGetResponse? pageBefore = null;
		string? bodyBefore = null;
		try {
			CallToolResult planCall = await CallAsync(context, new Dictionary<string, object?> { ["dry-run"] = true });
			IgnoreWhenProcessBuilderIsBehind(ToolText(planCall), MinimumPackageVersion);
			JsonObject plan = ResultOf(planCall);
			string? pageName = Text(plan["page"]!.AsObject(), "pageSchemaName");
			pageName.Should().NotBeNullOrWhiteSpace(because: "the seeded object has a registered record page");
			pageBefore = await GetPageAsync(context, pageName!, pageDirectory);
			// The MCP get-page answers with files, and a later read of the same page overwrites them.
			bodyBefore = await File.ReadAllTextAsync(pageBefore.Files.BodyFile);

			// Act
			CallToolResult createCall = await CallAsync(context, []);
			created = ResultOf(createCall);
			CallToolResult rerunCall = await CallAsync(context, []);
			JsonArray stages = created["case"]!["stages"]!.AsArray();
			List<string> reversed = stages.Select(stage => Text(stage!.AsObject(), "value")!).Reverse().ToList();
			CallToolResult differentCall = await CallAsync(context, new Dictionary<string, object?> {
				["stages"] = reversed.Select(value => new Dictionary<string, object?> { ["value"] = value }).ToArray()
			});

			// Assert
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			Text(created, "outcome").Should().Be("created", because: "nothing existed on the object");
			JsonObject caseBlock = created["case"]!.AsObject();
			Text(caseBlock, "packageName").Should().Be(Package, because: "the case lands in the requested package");
			caseBlock["enabled"]!.GetValue<bool>().Should().BeTrue(because: "the bar shows only an enabled case");
			caseBlock["activeVersion"]!.GetValue<bool>().Should().BeTrue(because: "the bar shows only the active version");
			stages.Should().HaveCount(StageCount, because: "one stage per lookup value");
			stages.Select(stage => Text(stage!.AsObject(), "color")).Should().OnlyContain(color => color == "#0C8701",
				because: "a null colour crashes the Freedom UI bar");
			caseBlock["connections"]!["count"]!.GetValue<int>().Should().Be(StageCount * (StageCount - 1),
				because: "every stage is reachable from every other");
			Text(caseBlock, "designerUrl").Should().Contain("vm=DcmDesigner#case/", because: "the case opens by UId");
			created["obligations"]!["compileRequired"]!.GetValue<bool>().Should().BeFalse(because: "a case is interpreted");
			Text(created["page"]!.AsObject(), "status").Should().Be("inserted", because: "the bar was added");
			await ShouldStoreTheCaseAsync(context, caseBlock, stages);
			await ShouldStoreTheSettingsAsync(context, created["settings"]!.AsObject());
			await ShouldCarryTheBarAsync(context, pageName!, pageDirectory);

			ShouldHaveLanded(rerunCall, "an equivalent re-run is a success");
			JsonObject rerun = ResultOf(rerunCall);
			Text(rerun, "outcome").Should().Be("already-present", because: "the equivalent case exists");
			Text(rerun["page"]!.AsObject(), "status").Should().Be("already-present", because: "the bar exists");

			ShouldBeRefused(differentCall, "another order is another case");
			Text(ResultOf(differentCall), "errorCode").Should().Be("case-exists",
				because: "create does not change an existing case; modify-dynamic-case does");
			(await CaseSchemaRowsAsync(context)).GetArrayLength().Should().Be(1,
				because: "neither the re-run nor the refusal wrote a second case");
		} finally {
			await CleanUpAsync(context, created, pageBefore, bodyBefore, pageDirectory);
			ScratchDirectory.TryDelete(pageDirectory);
		}
	}

	[Test]
	[Description("A column that is not a lookup is refused as column-not-lookup and an unknown stage value as stage-value-unknown; neither writes a case.")]
	[AllureName("create-dynamic-case refuses a non-lookup column and an unknown stage value")]
	public async Task CreateDynamicCase_ShouldRefuseAndWriteNothing_WhenTheColumnOrAStageIsWrong() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		int casesBefore = (await CaseSchemaRowsAsync(context)).GetArrayLength();
		int settingsBefore = (await SettingsRowsAsync(context)).GetArrayLength();

		// Act
		CallToolResult notLookup = await CallAsync(context, new Dictionary<string, object?> {
			["stage-column-name"] = TextColumn
		});
		CallToolResult unknownValue = await CallAsync(context, new Dictionary<string, object?> {
			["stages"] = new[] { "No such stage" }
		});

		// Assert
		IgnoreWhenProcessBuilderIsBehind(ToolText(notLookup), MinimumPackageVersion);
		ShouldBeRefused(notLookup, "a text column has no values to become stages");
		Text(ResultOf(notLookup), "errorCode").Should().Be("column-not-lookup", because: "the code names the reason");
		ShouldBeRefused(unknownValue, "a stage must name a lookup value");
		Text(ResultOf(unknownValue), "errorCode").Should().Be("stage-value-unknown", because: "the code names the reason");
		(await CaseSchemaRowsAsync(context)).GetArrayLength().Should().Be(casesBefore, because: "a refusal writes nothing");
		(await SettingsRowsAsync(context)).GetArrayLength().Should().Be(settingsBefore,
			because: "a refusal writes no settings row");
	}

	[Test]
	[Description("describe-dynamic-case by object reads the case create wrote: one case, enabled, the stages in the UsrPosition order, no running case and no stage group.")]
	[AllureName("describe-dynamic-case reads a case by its object")]
	public async Task DescribeDynamicCase_ShouldReadTheCase_ByItsObject() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");

			// Act
			CallToolResult describeCall = await DescribeAsync(context, new Dictionary<string, object?> {
				["entity-schema-name"] = Entity
			});

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(describeCall), CaseChangePackageVersion);
			ShouldHaveLanded(describeCall, "the object has a case to read");
			JsonArray cases = ResultOf(describeCall, DescribeMarker)["cases"]!.AsArray();
			cases.Should().ContainSingle(because: "create wrote one case on the object");
			JsonObject described = cases[0]!.AsObject();
			Text(described, "schemaName").Should().Be(CaseNameOf(created), because: "it is the case create wrote");
			described["enabled"]!.GetValue<bool>().Should().BeTrue(because: "create enables the case");
			StageValues(described).Should().Equal(StageValues(created["case"]!.AsObject()),
				because: "the stages are read in the stored order, the lookup's UsrPosition order create wrote");
			(described["runningInstances"]?.GetValue<int>() ?? -1).Should().Be(0,
				because: "a case nothing has started yet has no running case");
			described["stages"]!.AsArray().Should().OnlyContain(stage => stage!["group"] == null,
				because: "create wrote no stage group");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("A modify-dynamic-case dry run that reorders the stages answers planned with the moves in the diff and writes nothing: the stored order is unchanged.")]
	[AllureName("modify-dynamic-case plans a reorder and writes nothing")]
	public async Task ModifyDynamicCase_ShouldPlanAReorder_AndWriteNothing_OnADryRun() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			List<string> stored = StageValues(created["case"]!.AsObject());

			// Act
			CallToolResult planCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["stages"] = SwapFirstTwo(stored),
				["dry-run"] = true
			});

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(planCall), CaseChangePackageVersion);
			ShouldHaveLanded(planCall, "a dry run that passes every check is a success");
			JsonObject plan = ResultOf(planCall);
			Text(plan, "outcome").Should().Be("planned", because: "a dry run plans");
			plan["diff"]!["moved"]!.AsArray().Should().NotBeEmpty(because: "two stages swap places");
			StageValues(await DescribeCaseAsync(context, created)).Should().Equal(stored,
				because: "a dry run writes nothing");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("A reorder is written in place: modified, the stored order follows the list and every stage keeps its UId; the same call again answers unchanged.")]
	[AllureName("modify-dynamic-case reorders in place and a re-run is unchanged")]
	public async Task ModifyDynamicCase_ShouldReorderInPlace_ThenAnswerUnchanged() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			Dictionary<string, string> uidsBefore = StageUIdsByValue(await DescribeCaseAsync(context, created));
			string[] reordered = SwapFirstTwo(StageValues(created["case"]!.AsObject()));

			// Act
			CallToolResult firstCall = await ModifyAsync(context, created,
				new Dictionary<string, object?> { ["stages"] = reordered });
			CallToolResult secondCall = await ModifyAsync(context, created,
				new Dictionary<string, object?> { ["stages"] = reordered });

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(firstCall), CaseChangePackageVersion);
			ShouldHaveLanded(firstCall, "a reorder of the stored stages is a valid change");
			Text(ResultOf(firstCall), "outcome").Should().Be("modified", because: "the order changed");
			JsonObject after = await DescribeCaseAsync(context, created);
			StageValues(after).Should().Equal(reordered, because: "the stored order follows the list");
			StageUIdsByValue(after).Should().Equal(uidsBefore,
				because: "a stage that stays keeps its UId, so a running case keeps its place");
			ShouldHaveLanded(secondCall, "the same call again is a success");
			Text(ResultOf(secondCall), "outcome").Should().Be("unchanged", because: "nothing is left to change");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("A stage left out of the list is removed and added back by naming it again: the re-added stage gets a new stage UId and the designer colour, keeps its lookup value, and the other stages keep their UIds.")]
	[AllureName("modify-dynamic-case removes a stage and adds it back")]
	public async Task ModifyDynamicCase_ShouldRemoveAStage_ThenAddItBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			List<string> stored = StageValues(created["case"]!.AsObject());
			Dictionary<string, string> uidsBefore = StageUIdsByValue(await DescribeCaseAsync(context, created));
			string removed = stored[1];

			// Act
			CallToolResult removeCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["stages"] = stored.Where(value => value != removed).ToArray()
			});
			JsonObject afterRemove = await DescribeCaseAsync(context, created);
			CallToolResult addCall = await ModifyAsync(context, created,
				new Dictionary<string, object?> { ["stages"] = stored.ToArray() });

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(removeCall), CaseChangePackageVersion);
			ShouldHaveLanded(removeCall, "a stage nothing sits in can be removed");
			ResultOf(removeCall)["diff"]!["removed"]!.AsArray()
				.Select(change => Text(change!.AsObject(), "value")).Should().Equal([removed],
					because: "the stage left out is the one removed");
			StageValues(afterRemove).Should().NotContain(removed, because: "the removal was stored");
			ShouldHaveLanded(addCall, "a lookup value of the stage column can be added");
			ResultOf(addCall)["diff"]!["added"]!.AsArray()
				.Select(change => Text(change!.AsObject(), "value")).Should().Equal([removed],
					because: "the value named again is the one added");
			JsonObject after = await DescribeCaseAsync(context, created);
			StageValues(after).Should().Equal(stored, because: "the stage is back in its place");
			JsonObject readded = StageOf(after, removed);
			Text(readded, "uId").Should().NotBe(uidsBefore[removed], because: "a re-added stage is a new stage");
			Text(readded, "color").Should().Be("#0C8701", because: "a new stage takes the designer colour");
			StageUIdsByValue(after).Where(pair => pair.Key != removed).Should()
				.BeEquivalentTo(uidsBefore.Where(pair => pair.Key != removed), because: "the other stages keep their UIds");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("successful=false on a stage outside the final group is refused as stage-group-invalid with nothing written; grouping the last two stages, the second unsuccessful, stores the group and the flag, read back by describe-dynamic-case from the saved case.")]
	[AllureName("modify-dynamic-case groups the final stages and refuses an unsuccessful non-final stage")]
	public async Task ModifyDynamicCase_ShouldGroupTheFinalStages_AndRefuseAnUnsuccessfulNonFinalStage() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			List<string> stored = StageValues(created["case"]!.AsObject());
			string parent = stored[^2];
			string member = stored[^1];

			// Act
			CallToolResult refusedCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["stages"] = stored.Select<string, object>((value, index) => index == 0
					? new Dictionary<string, object?> { ["value"] = value, ["successful"] = false }
					: value).ToArray()
			});
			JsonObject afterRefusal = await DescribeCaseAsync(context, created);
			CallToolResult groupCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["stages"] = stored.Select<string, object>(value => value == member
					? new Dictionary<string, object?> { ["value"] = value, ["group"] = parent, ["successful"] = false }
					: value).ToArray()
			});

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(refusedCall), CaseChangePackageVersion);
			ShouldBeRefused(refusedCall, "only a stage of the final group can end the case unsuccessfully");
			Text(ResultOf(refusedCall), "errorCode").Should().Be("stage-group-invalid",
				because: "the code names the reason");
			afterRefusal["stages"]!.AsArray().Should().OnlyContain(
				stage => stage!["successful"] == null || stage["successful"]!.GetValue<bool>(),
				because: "a refusal writes nothing");
			ShouldHaveLanded(groupCall, "the last two stages form the final group");
			Text(ResultOf(groupCall), "outcome").Should().Be("modified", because: "the group is new");
			JsonObject after = await DescribeCaseAsync(context, created);
			JsonObject grouped = StageOf(after, member);
			Text(grouped, "group").Should().Be(parent,
				because: "the member's stored ParentStageUId points at the parent stage, which describe names by value");
			grouped["successful"]!.GetValue<bool>().Should().BeFalse(because: "IsSuccessful was stored as false");
			StageOf(after, parent)["isFinal"]!.GetValue<bool>().Should().BeTrue(because: "the parent heads the final group");
			grouped["isFinal"]!.GetValue<bool>().Should().BeTrue(because: "a member of the final group is final");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("enabled=false disables the case and describe reads it disabled; enabled mixed with a caption is refused as invalid-request with nothing written; enabled=true enables it again.")]
	[AllureName("modify-dynamic-case disables and enables a case")]
	public async Task ModifyDynamicCase_ShouldDisableAndEnable_AndRefuseEnabledWithAnotherChange() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			string captionBefore = Text(created["case"]!.AsObject(), "caption")!;

			// Act
			CallToolResult disableCall = await ModifyAsync(context, created,
				new Dictionary<string, object?> { ["enabled"] = false });
			JsonObject afterDisable = await DescribeCaseAsync(context, created);
			CallToolResult mixedCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["enabled"] = true,
				["caption"] = captionBefore + " renamed"
			});
			JsonObject afterMixed = await DescribeCaseAsync(context, created);
			CallToolResult enableCall = await ModifyAsync(context, created,
				new Dictionary<string, object?> { ["enabled"] = true });
			JsonObject afterEnable = await DescribeCaseAsync(context, created);

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(disableCall), CaseChangePackageVersion);
			ShouldHaveLanded(disableCall, "an enabled case can be disabled");
			Text(ResultOf(disableCall), "outcome").Should().Be("modified", because: "the case was enabled");
			afterDisable["enabled"]!.GetValue<bool>().Should().BeFalse(because: "the disable was stored");
			ShouldBeRefused(mixedCall, "enabled is a save of its own");
			Text(ResultOf(mixedCall), "errorCode").Should().Be("invalid-request", because: "the code names the reason");
			afterMixed["enabled"]!.GetValue<bool>().Should().BeFalse(because: "a refusal writes nothing");
			Text(afterMixed, "caption").Should().Be(captionBefore, because: "a refusal writes nothing");
			ShouldHaveLanded(enableCall, "a disabled case with no other case on the object can be enabled");
			afterEnable["enabled"]!.GetValue<bool>().Should().BeTrue(because: "the enable was stored");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	#region Methods: Arrange and calls

	private static async Task<ProcessDesignerArrangeContext> StartAsync(
		string minimumPackageVersion = MinimumPackageVersion) {
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Opt in with McpE2E__AllowDestructiveMcpTests: this fixture writes a case, settings rows, "
				+ "package bindings and a record page.");
		}
		// The lifecycle test makes about ten calls, a build among them, and its cleanup restores the page through the
		// same session: the three-minute default ran out before the restore ("A task was canceled").
		ProcessDesignerArrangeContext context = await ProcessDesignerE2EArrange.StartAsync(Subject,
			minimumPackageVersion, sessionTimeout: TimeSpan.FromMinutes(10));
		JsonElement entity = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", Entity, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "EntitySchemaManager",
					SelectQueryHelper.TextDataValueType)
			], rowCount: 1), "the seeded object's SysSchema row");
		if (entity.GetArrayLength() == 0) {
			await context.DisposeAsync();
			string reason = $"The seeded object {Entity} is not on '{context.EnvironmentName}'. It ships in the "
				+ "AutoTestClioMcp 7.8.0 test package; install it, or create the object as the fixture remarks describe.";
			if (settings.RequireProcessBuilder) {
				Assert.Fail(reason + " This lane installs AutoTestClioMcp, so a missing seed fails the run instead of "
					+ "skipping it.");
			}
			Assert.Ignore(reason);
		}
		return context;
	}

	private static async Task<CallToolResult> CallAsync(ProcessDesignerArrangeContext context,
		Dictionary<string, object?> extra) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName,
			["package-name"] = Package,
			["entity-schema-name"] = Entity,
			["stage-column-name"] = StageColumn,
			["order-column"] = OrderColumn
		};
		foreach (KeyValuePair<string, object?> pair in extra) {
			args[pair.Key] = pair.Value;
		}
		return await ProcessDesignerE2EArrange.CallToolAsync(context, CreateDynamicCaseTool.CreateDynamicCaseToolName,
			args);
	}

	// A case-change test needs no record page, and leaving the page alone keeps its cleanup to the case and its rows.
	private static Dictionary<string, object?> WithoutBar() => new() { ["add-progress-bar"] = false };

	private static async Task<CallToolResult> DescribeAsync(ProcessDesignerArrangeContext context,
		Dictionary<string, object?> identity) {
		Dictionary<string, object?> args = new() { ["environment-name"] = context.EnvironmentName };
		foreach (KeyValuePair<string, object?> pair in identity) {
			args[pair.Key] = pair.Value;
		}
		return await ProcessDesignerE2EArrange.CallToolAsync(context,
			DescribeDynamicCaseTool.DescribeDynamicCaseToolName, args);
	}

	/// <summary>The stored case as describe-dynamic-case reads it by name, after asserting the read landed.</summary>
	private static async Task<JsonObject> DescribeCaseAsync(ProcessDesignerArrangeContext context, JsonObject created) {
		CallToolResult described = await DescribeAsync(context, new Dictionary<string, object?> {
			["case-name"] = CaseNameOf(created)
		});
		ShouldHaveLanded(described, "the case this test created can be read by name");
		JsonArray cases = ResultOf(described, DescribeMarker)[DescribeMarker]!.AsArray();
		cases.Should().ContainSingle(because: "a case name names one case");
		return cases[0]!.AsObject();
	}

	private static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, JsonObject created,
		Dictionary<string, object?> change) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName,
			["case-name"] = CaseNameOf(created)
		};
		foreach (KeyValuePair<string, object?> pair in change) {
			args[pair.Key] = pair.Value;
		}
		return await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyDynamicCaseTool.ModifyDynamicCaseToolName,
			args);
	}

	private static string CaseNameOf(JsonObject created) => Text(created["case"]!.AsObject(), "schemaName")!;

	/// <summary>The stage lookup values of a case block, in the stored order.</summary>
	private static List<string> StageValues(JsonObject caseBlock) =>
		[.. caseBlock["stages"]!.AsArray().Select(stage => Text(stage!.AsObject(), "value")!)];

	/// <summary>Each stage's own UId by its lookup value, in the stored order.</summary>
	private static Dictionary<string, string> StageUIdsByValue(JsonObject caseBlock) =>
		caseBlock["stages"]!.AsArray().Select(stage => stage!.AsObject())
			.ToDictionary(stage => Text(stage, "value")!, stage => Text(stage, "uId")!);

	private static JsonObject StageOf(JsonObject caseBlock, string value) =>
		caseBlock["stages"]!.AsArray().Select(stage => stage!.AsObject())
			.Single(stage => Text(stage, "value") == value);

	private static string[] SwapFirstTwo(List<string> values) => [values[1], values[0], .. values.Skip(2)];

	/// <summary>The result document: the one execution-log message that is a JSON object carrying an outcome.</summary>
	private static JsonObject ResultOf(CallToolResult result) => ResultOf(result, "outcome");

	/// <summary>The result document: the one execution-log message that is a JSON object carrying the marker.</summary>
	private static JsonObject ResultOf(CallToolResult result, string marker) {
		foreach (string line in LogText(result).Split('\n')) {
			if (!line.TrimStart().StartsWith('{')) {
				continue;
			}
			try {
				if (JsonNode.Parse(line) is JsonObject document && document.ContainsKey(marker)) {
					return document;
				}
			} catch (JsonException) {
				// Not the result document; keep looking.
			}
		}
		Assert.Fail($"The tool printed no result document. It said: {LogText(result)}");
		return null!;
	}

	private static async Task<PageGetResponse> GetPageAsync(ProcessDesignerArrangeContext context, string pageName,
		string directory) {
		CallToolResult result = await context.Session.CallToolAsync(PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = pageName,
					["environment-name"] = context.EnvironmentName,
					["output-directory"] = directory
				}
			}, context.CancellationTokenSource.Token);
		PageGetResponse page = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(result);
		page.Success.Should().BeTrue(because: $"the record page must be readable: {page.Error}");
		return page;
	}

	#endregion

	#region Methods: Stored reads

	// By the default case name, which the tool derives from the object when no name is passed.
	private static Task<JsonElement> CaseSchemaRowsAsync(ProcessDesignerArrangeContext context) =>
		SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", Entity + "Case",
					SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "DcmSchemaManager",
					SelectQueryHelper.TextDataValueType)
			], rowCount: 10), "the object's case schemas");

	private static Task<JsonElement> SettingsRowsAsync(ProcessDesignerArrangeContext context) =>
		SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysDcmSettings",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[], rowCount: 1000), "the settings rows");

	private static async Task ShouldStoreTheCaseAsync(ProcessDesignerArrangeContext context, JsonObject caseBlock,
		JsonArray stages) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("MetaData", "MetaData"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", "PackageName")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("UId", Text(caseBlock, "schemaUId")!,
					SelectQueryHelper.GuidDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "DcmSchemaManager",
					SelectQueryHelper.TextDataValueType)
			], rowCount: 1), "the case's SysSchema row");
		rows.GetArrayLength().Should().Be(1, because: "the case is a DcmSchemaManager schema");
		TextOf(rows[0], "PackageName").Should().Be(Package, because: "the case is saved into the requested package");
		string metaData = DecodeMetaData(rows[0].GetProperty("MetaData"));
		foreach (JsonNode? stage in stages) {
			metaData.Should().Contain(Text(stage!.AsObject(), "value")!,
				because: "every stage stores its lookup value as StageRecordId");
		}
	}

	private static async Task ShouldStoreTheSettingsAsync(ProcessDesignerArrangeContext context, JsonObject settings) {
		JsonArray bindings = settings["bindings"]!.AsArray();
		string? linkId = Text(settings, "sysDcmSchemaInSettingsId");
		linkId.Should().NotBeNullOrWhiteSpace(because: "the designer opens a case only through its link row");
		JsonElement link = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysDcmSchemaInSettings",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryHelper.SelectQueryFilterDefinition("Id", linkId!, SelectQueryHelper.GuidDataValueType)],
			rowCount: 1), "the link row");
		link.GetArrayLength().Should().Be(1, because: "the link row was written");
		bindings.Select(binding => binding!.GetValue<string>()).Should()
			.Contain(binding => binding.StartsWith("SysDcmSchemaInSettings_", StringComparison.Ordinal),
				because: "the link row is bound to the package");
		string? settingsId = Text(settings, "sysDcmSettingsId");
		settingsId.Should().NotBeNullOrWhiteSpace(because: "the designer needs the settings row the link points at");
		JsonElement settingsRow = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysDcmSettings",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryHelper.SelectQueryFilterDefinition("Id", settingsId!, SelectQueryHelper.GuidDataValueType)],
			rowCount: 1), "the settings row");
		settingsRow.GetArrayLength().Should().Be(1, because: "the settings row exists");
		if (Text(settings, "status") == "created") {
			bindings.Select(binding => binding!.GetValue<string>()).Should()
				.Contain(binding => binding.StartsWith("SysDcmSettings_", StringComparison.Ordinal),
					because: "a settings row this call created is bound to the package");
		}
	}

	private static async Task ShouldCarryTheBarAsync(ProcessDesignerArrangeContext context, string pageName,
		string directory) {
		PageGetResponse page = await GetPageAsync(context, pageName, directory);
		string viewConfig = Regex.Replace(await File.ReadAllTextAsync(page.Files.BundleFile), @"\s+", string.Empty);
		viewConfig.Should().Contain("crt.EntityStageProgressBar", because: "the bar was appended to the record page");
		viewConfig.Should().Contain($"\"entityName\":\"{Entity}\"", because: "the bar names the case object");
		viewConfig.Should().Contain("\"saveOnChange\":false",
			because: "without save-on-change the bar keeps its default: a stage click only sets the field");
	}

	#endregion

	#region Methods: Cleanup

	// Strictly sequential and best-effort: each step reports its own failure instead of throwing, so one failed undo
	// neither hides the assertion that failed nor skips the steps after it.
	private static async Task CleanUpAsync(ProcessDesignerArrangeContext context, JsonObject? created,
		PageGetResponse? pageBefore, string? bodyBefore, string directory) {
		if (created is null
			|| Text(created, "outcome") is not ("created" or "created-page-failed" or "created-page-skipped")) {
			return;
		}
		JsonObject settings = created["settings"]!.AsObject();
		List<string> bindings = settings["bindings"]?.AsArray().Select(binding => binding!.GetValue<string>()).ToList()
			?? [];
		await RemoveBindingRowAsync(context, bindings, "SysDcmSchemaInSettings_",
			Text(settings, "sysDcmSchemaInSettingsId"));
		if (Text(settings, "status") == "created") {
			await RemoveBindingRowAsync(context, bindings, "SysDcmSettings_", Text(settings, "sysDcmSettingsId"));
		}
		string? caseName = Text(created["case"]!.AsObject(), "schemaName");
		if (!string.IsNullOrWhiteSpace(caseName)) {
			await DeleteProcessAsync(context.EnvironmentName, caseName);
		}
		await RestorePageAsync(context, pageBefore, bodyBefore, directory);
	}

	private static async Task RemoveBindingRowAsync(ProcessDesignerArrangeContext context, List<string> bindings,
		string prefix, string? keyValue) {
		string? binding = bindings.FirstOrDefault(name => name.StartsWith(prefix, StringComparison.Ordinal));
		if (binding is null || string.IsNullOrWhiteSpace(keyValue)) {
			return;
		}
		try {
			CallToolResult removed = await ProcessDesignerE2EArrange.CallToolAsync(context,
				RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName, new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["package-name"] = Package,
					["binding-name"] = binding,
					["key-value"] = keyValue
				});
			if (McpCommandExecutionParser.Extract(removed).ExitCode != 0) {
				await TestContext.Error.WriteLineAsync($"Could not remove binding row {binding}: {LogText(removed)}");
			}
		} catch (Exception exception) {
			PayloadDumpReader.DeleteIfPresent(exception.Message);
			await TestContext.Error.WriteLineAsync($"Removing binding row {binding} failed: {exception.Message}");
		}
	}

	private static async Task RestorePageAsync(ProcessDesignerArrangeContext context, PageGetResponse? pageBefore,
		string? bodyBefore, string directory) {
		if (pageBefore?.Page is null || bodyBefore is null) {
			return;
		}
		string pageName = pageBefore.Page.SchemaName;
		if (pageBefore.Editable is null) {
			await TestContext.Error.WriteLineAsync($"The progress bar was written into a replacing schema of "
				+ $"'{pageName}' in '{pageBefore.Page.DesignPackageName}' that this run created; delete it by hand.");
			return;
		}
		try {
			await GetPageAsync(context, pageName, directory);
			CallToolResult restored = await context.Session.CallToolAsync(PageUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = pageName,
						["body"] = bodyBefore,
						["environment-name"] = context.EnvironmentName,
						["output-directory"] = directory,
						// The body written back is the page's own; its pre-existing defects are not this run's to fix.
						["validate"] = false
					}
				}, context.CancellationTokenSource.Token);
			PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(restored);
			if (!response.Success) {
				await TestContext.Error.WriteLineAsync($"Could not restore '{pageName}': {response.Error}");
			}
		} catch (Exception exception) {
			// A failed restore is reported, not thrown, so the parser's payload dump would otherwise ship with a
			// green run.
			PayloadDumpReader.DeleteIfPresent(exception.Message);
			await TestContext.Error.WriteLineAsync($"Restoring '{pageName}' failed: {exception.Message}");
		}
	}

	#endregion
}
