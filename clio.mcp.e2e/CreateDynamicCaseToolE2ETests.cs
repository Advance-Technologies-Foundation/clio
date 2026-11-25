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
using Clio.Common;
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
/// Then <c>modify-dynamic-case-as-new-version</c> and <c>set-active-dynamic-case-version</c>: a version planned and
/// created inactive, the family described, activated, re-activated as unchanged and rolled back to the root, and an
/// inactive version described and changed in place by name with <c>enabled</c> refused on it.
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

	/// <summary>The first CrtProcessBuilder that carries DescribeCase, ModifyCase and the case version operations.</summary>
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
	[Description("A dry run plans the case in the lookup's position order and writes nothing: no case schema and no settings row for the object appear, and the log holds exactly the verdict and the result document.")]
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
		ShouldHoldOnlyTheVerdictAndThePlan(planned);
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
		// Two items: an explicit list of one stage is refused by its count (a case keeps 2 to 20) before its values.
		CallToolResult unknownValue = await CallAsync(context, new Dictionary<string, object?> {
			["stages"] = new[] { "No such stage", "Nor this one" }
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
	[Description("The filter arguments are bound over stdio and refused before any write: deactivate-unfiltered without a filter and filter with remove-filter as invalid-request by clio, a filter on a text column as filter-column-invalid by the server.")]
	[AllureName("create-dynamic-case and modify-dynamic-case refuse wrong filter arguments")]
	public async Task DynamicCase_ShouldRefuseWrongFilterArguments_AndWriteNothing() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		int casesBefore = (await CaseSchemaRowsAsync(context)).GetArrayLength();
		int settingsBefore = (await SettingsRowsAsync(context)).GetArrayLength();

		// Act
		CallToolResult deactivateAlone = await CallAsync(context, new Dictionary<string, object?> {
			["dry-run"] = true, ["deactivate-unfiltered"] = true
		});
		CallToolResult textFilter = await CallAsync(context, new Dictionary<string, object?> {
			["dry-run"] = true,
			["filter"] = new Dictionary<string, object?> { ["column"] = TextColumn, ["values"] = new[] { "x" } }
		});
		CallToolResult filterAndRemove = await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyDynamicCaseTool.ModifyDynamicCaseToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["entity-schema-name"] = Entity,
				["dry-run"] = true,
				["filter"] = new Dictionary<string, object?> { ["column"] = TextColumn, ["values"] = new[] { "x" } },
				["remove-filter"] = true
			});

		// Assert
		IgnoreWhenProcessBuilderIsBehind(ToolText(textFilter), MinimumPackageVersion);
		ShouldBeRefused(deactivateAlone, "deactivate-unfiltered disables a case only to let a filtered one start");
		LogText(deactivateAlone).Should().Contain("[invalid-request]", because: "clio names the stable code");
		ShouldBeRefused(textFilter, "a filter column must be a lookup of the object");
		Text(ResultOf(textFilter), "errorCode").Should().Be("filter-column-invalid", because: "the code names the reason");
		ShouldBeRefused(filterAndRemove, "a filter and its removal contradict each other");
		LogText(filterAndRemove).Should().Contain("[invalid-request]", because: "clio names the stable code");
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
	[Description("describe-dynamic-case answers records on every stage: the records that hold its value, finished cases included, a count from 0 up.")]
	[AllureName("describe-dynamic-case counts the records per stage")]
	public async Task DescribeDynamicCase_ShouldCountTheRecordsPerStage() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");

			// Act
			JsonObject described = await DescribeCaseAsync(context, created);

			// Assert
			described["stages"]!.AsArray().Should().OnlyContain(
				stage => stage!["records"] != null && stage["records"]!.GetValue<int>() >= 0,
				because: "every stage reports the records that hold its value, so a final stage records reached is not 0");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("A stage caption renames the stage's lookup value for every record, so without confirm-value-rename it is refused value-rename-confirmation-required; a caption that is another value's name is refused stage-value-ambiguous even with the flag. Nothing is written: the stage and its value keep their names.")]
	[AllureName("modify-dynamic-case refuses a stage rename without the user's yes and to a taken name")]
	public async Task ModifyDynamicCase_ShouldRefuseAStageRename_WithoutTheUsersYesOrToATakenName() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			JsonObject caseBlock = created["case"]!.AsObject();
			List<string> stored = StageValues(caseBlock);
			string first = stored[0];
			string firstCaption = Text(StageOf(caseBlock, first), "caption")!;
			string secondCaption = Text(StageOf(caseBlock, stored[1]), "caption")!;

			// Act
			CallToolResult unconfirmedCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["stages"] = WithCaption(stored, first, firstCaption + " E2E")
			});
			CallToolResult takenCall = await ModifyAsync(context, created, new Dictionary<string, object?> {
				["stages"] = WithCaption(stored, first, secondCaption),
				["confirm-value-rename"] = true
			});

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(unconfirmedCall), CaseChangePackageVersion);
			ShouldBeRefused(unconfirmedCall, "a value rename reaches every record, so the user agrees first");
			Text(ResultOf(unconfirmedCall), "errorCode").Should().Be("value-rename-confirmation-required",
				because: "the code names the yes that is missing");
			ShouldBeRefused(takenCall, "two values would read the same on the bar");
			Text(ResultOf(takenCall), "errorCode").Should().Be("stage-value-ambiguous",
				because: "the name is another value's");
			JsonObject after = await DescribeCaseAsync(context, created);
			Text(StageOf(after, first), "caption").Should().Be(firstCaption, because: "a refusal writes nothing");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("A modify-dynamic-case dry run that reorders the stages answers planned with the moves in the diff, logs only the verdict and the result document, and writes nothing: the stored order is unchanged.")]
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
			ShouldHoldOnlyTheVerdictAndThePlan(planCall);
			plan["diff"]!["moved"]!.AsArray().Should().NotBeEmpty(because: "two stages swap places");
			StageValues(await DescribeCaseAsync(context, created)).Should().Equal(stored,
				because: "a dry run writes nothing");
		} finally {
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("A reorder is written in place: modified, the case schema UId stays, the stored order follows the list, every stage keeps its UId and every stage still connects to every other; the same call again answers unchanged.")]
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
			Text(after, "schemaUId").Should().Be(Text(created["case"]!.AsObject(), "schemaUId"),
				because: "the save is in place: the same case schema, not a new version");
			after["connections"]!["count"]!.GetValue<int>().Should().Be(reordered.Length * (reordered.Length - 1),
				because: "every stage still connects to every other and no connection is left dangling");
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
	[Description("create-dynamic-case with group and successful on a stage builds the final group: describe-dynamic-case reads the member's group and successful=false, and both stages final.")]
	[AllureName("create-dynamic-case builds a final group")]
	public async Task CreateDynamicCase_ShouldBuildTheFinalGroup_WhenAStageNamesItsGroup() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		try {
			CallToolResult plannedCall = await CallAsync(context, new Dictionary<string, object?> {
				["dry-run"] = true, ["add-progress-bar"] = false
			});
			IgnoreWhenProcessBuilderIsBehind(ToolText(plannedCall), MinimumPackageVersion);
			ShouldHaveLanded(plannedCall, "the seeded object takes a case");
			List<string> values = StageValues(ResultOf(plannedCall)["case"]!.AsObject());
			string parent = values[^2];
			string member = values[^1];

			// Act
			CallToolResult createCall = await CallAsync(context, new Dictionary<string, object?> {
				["add-progress-bar"] = false,
				["stages"] = values.Select<string, object>(value => value == member
					? new Dictionary<string, object?> { ["value"] = value, ["group"] = parent, ["successful"] = false }
					: value).ToArray()
			});

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), CaseChangePackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the last two stages can form the final group on create");
			JsonObject after = await DescribeCaseAsync(context, created);
			JsonObject grouped = StageOf(after, member);
			Text(grouped, "group").Should().Be(parent, because: "the member's stored ParentStageUId points at the parent");
			grouped["successful"]!.GetValue<bool>().Should().BeFalse(because: "IsSuccessful was stored as false");
			grouped["isFinal"]!.GetValue<bool>().Should().BeTrue(because: "a member of the final group is final");
			StageOf(after, parent)["isFinal"]!.GetValue<bool>().Should().BeTrue(because: "the parent heads the final group");
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

	[Test]
	[Description("A change saved as a new version: the dry run plans an inactive version and writes nothing; the write creates it inactive with a designer link while the case stays active; describe lists both members; the activation dry run writes nothing; each dry run logs only the verdict and the result document; the activation reads the version back as active; the same activation again, with an unneeded confirm-package-position, answers unchanged; activating the root goes back.")]
	[AllureName("modify-dynamic-case-as-new-version and set-active-dynamic-case-version: create, check, activate, go back")]
	public async Task DynamicCaseVersion_ShouldBeCreatedInactive_ThenActivated_ThenRolledBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		JsonObject? version = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			string rootName = CaseNameOf(created);
			string[] reordered = SwapFirstTwo(StageValues(created["case"]!.AsObject()));
			Dictionary<string, object?> change = new() { ["stages"] = reordered };

			// Act
			CallToolResult planCall = await NewVersionAsync(context, created, change, dryRun: true);
			JsonObject afterPlan = await DescribeCaseAsync(context, created);
			CallToolResult versionCall = await NewVersionAsync(context, created, change, dryRun: false);
			IgnoreWhenProcessBuilderIsBehind(ToolText(versionCall), CaseChangePackageVersion);
			// Assigned before the assertion, so a failed-leftover or unknown write still reaches the version cleanup.
			version = ResultOf(versionCall);
			ShouldHaveLanded(versionCall, "the active case can be changed as a new version");
			string versionName = Text(version["version"]!.AsObject(), "name")!;
			JsonObject afterVersion = await DescribeCaseAsync(context, created);
			CallToolResult activatePlanCall = await SetActiveAsync(context, versionName, dryRun: true);
			JsonObject afterActivatePlan = await DescribeCaseAsync(context, created);
			CallToolResult activateCall = await SetActiveAsync(context, versionName, dryRun: false);
			JsonObject afterActivate = await DescribeCaseAsync(context, created);
			CallToolResult againCall = await SetActiveAsync(context, versionName, dryRun: false,
				confirmPackagePosition: true);
			CallToolResult backCall = await SetActiveAsync(context, rootName, dryRun: false);
			JsonObject afterBack = await DescribeCaseAsync(context, created);

			// Assert
			IgnoreWhenProcessBuilderIsBehind(ToolText(planCall), CaseChangePackageVersion);
			ShouldHaveLanded(planCall, "a dry run of a valid version plans it");
			Text(ResultOf(planCall), "outcome").Should().Be("planned", because: "a dry run writes nothing");
			ShouldHoldOnlyTheVerdictAndThePlan(planCall);
			ResultOf(planCall)["version"]!["active"]!.GetValue<bool>().Should().BeFalse(
				because: "a version is always planned inactive");
			afterPlan["versions"].Should().BeNull(because: "the dry run wrote no version, so the case has no other member");
			Text(version, "outcome").Should().Be("version-created", because: "the version was saved");
			version["version"]!["active"]!.GetValue<bool>().Should().BeFalse(because: "creating never activates");
			Text(version["version"]!.AsObject(), "designerUrl").Should().NotBeNullOrWhiteSpace(
				because: "a saved version opens in the case designer, which is where the user checks it");
			Text(version, "sourceName").Should().Be(rootName, because: "the version is copied from the active case");
			Text(afterVersion, "schemaName").Should().Be(rootName, because: "describe by the root reads the root");
			afterVersion["activeVersion"]!.GetValue<bool>().Should().BeTrue(
				because: "the case stays active until the separate activation");
			StageValues(afterVersion).Should().NotEqual(reordered, because: "the change went to the version only");
			MembersOf(afterVersion).Should().BeEquivalentTo([rootName, versionName],
				because: "describe lists the whole family once it has a second member");
			ActiveMemberOf(afterVersion).Should().Be(rootName, because: "the root is still the platform's pick");
			ShouldHaveLanded(activatePlanCall, "the version can be activated");
			Text(ResultOf(activatePlanCall), "outcome").Should().Be("planned", because: "a dry run writes nothing");
			ShouldHoldOnlyTheVerdictAndThePlan(activatePlanCall);
			ActiveMemberOf(afterActivatePlan).Should().Be(rootName, because: "the activation dry run wrote nothing");
			ShouldHaveLanded(activateCall, "the version can be activated");
			Text(ResultOf(activateCall), "outcome").Should().Be("activated", because: "the switch read back");
			Text(ResultOf(activateCall)["activeVersion"]!.AsObject(), "name").Should().Be(versionName,
				because: "the result names the member really active, as read back");
			ActiveMemberOf(afterActivate).Should().Be(versionName, because: "the switch was stored");
			ShouldHaveLanded(againCall, "activating the active member again is a success");
			Text(ResultOf(againCall), "outcome").Should().Be("unchanged",
				because: "the call is idempotent, and a package-position yes the switch does not need binds and changes nothing");
			ShouldHaveLanded(backCall, "the root is a member like any other, so activating it goes back");
			ActiveMemberOf(afterBack).Should().Be(rootName, because: "the rollback was stored");
		} finally {
			await CleanUpVersionAsync(context, created, version);
			await CleanUpAsync(context, created, null, null, string.Empty);
		}
	}

	[Test]
	[Description("An inactive version is read by its own name with a note naming the active case, changed in place by name (it stays inactive), and refused invalid-request for enabled, writing nothing.")]
	[AllureName("describe and modify a dynamic case's inactive version by name")]
	public async Task DynamicCaseVersion_ShouldBeDescribedAndChangedInPlace_AndRefuseEnabled() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(CaseChangePackageVersion);
		JsonObject? created = null;
		JsonObject? version = null;
		try {
			CallToolResult createCall = await CallAsync(context, WithoutBar());
			IgnoreWhenProcessBuilderIsBehind(ToolText(createCall), MinimumPackageVersion);
			created = ResultOf(createCall);
			ShouldHaveLanded(createCall, "the seeded object takes a case");
			string rootName = CaseNameOf(created);
			CallToolResult versionCall = await NewVersionAsync(context, created, [], dryRun: false);
			IgnoreWhenProcessBuilderIsBehind(ToolText(versionCall), CaseChangePackageVersion);
			version = ResultOf(versionCall);
			ShouldHaveLanded(versionCall, "an unchanged copy of the active case is a valid version");
			string versionName = Text(version["version"]!.AsObject(), "name")!;
			string captionBefore = Text(version["case"]!.AsObject(), "caption")!;
			Dictionary<string, object?> draft = new() {
				["environment-name"] = context.EnvironmentName,
				["case-name"] = versionName
			};

			// Act
			CallToolResult describeCall = await DescribeAsync(context, new Dictionary<string, object?> {
				["case-name"] = versionName
			});
			CallToolResult captionCall = await ProcessDesignerE2EArrange.CallToolAsync(context,
				ModifyDynamicCaseTool.ModifyDynamicCaseToolName,
				new Dictionary<string, object?>(draft) { ["caption"] = captionBefore + " draft" });
			CallToolResult enabledCall = await ProcessDesignerE2EArrange.CallToolAsync(context,
				ModifyDynamicCaseTool.ModifyDynamicCaseToolName,
				new Dictionary<string, object?>(draft) { ["enabled"] = false });
			JsonObject root = await DescribeCaseAsync(context, created);

			// Assert
			ShouldHaveLanded(describeCall, "any member of the family can be read by name");
			JsonObject described = ResultOf(describeCall, DescribeMarker)[DescribeMarker]!.AsArray()[0]!.AsObject();
			Text(described, "schemaName").Should().Be(versionName, because: "the version itself is read, not the root");
			described["activeVersion"]!.GetValue<bool>().Should().BeFalse(because: "the version was never activated");
			LogText(describeCall).Should().Contain($"is not the active version of its case; '{rootName}' (",
				because: "the note names the active member");
			ShouldHaveLanded(captionCall, "an inactive version is changed in place");
			Text(ResultOf(captionCall), "outcome").Should().Be("modified", because: "the caption changed");
			ResultOf(captionCall)["case"]!["activeVersion"]!.GetValue<bool>().Should().BeFalse(
				because: "an in-place change does not activate the version");
			ShouldBeRefused(enabledCall, "enabled lives on the active case");
			Text(ResultOf(enabledCall), "errorCode").Should().Be("invalid-request", because: "the code names the reason");
			Text(root, "caption").Should().NotEndWith(" draft", because: "the draft's change never reaches the root");
			root["enabled"]!.GetValue<bool>().Should().BeTrue(because: "the refused enabled wrote nothing");
		} finally {
			await CleanUpVersionAsync(context, created, version);
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

	private static async Task<CallToolResult> NewVersionAsync(ProcessDesignerArrangeContext context,
		JsonObject created, Dictionary<string, object?> change, bool dryRun) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName,
			["case-name"] = CaseNameOf(created),
			["dry-run"] = dryRun
		};
		foreach (KeyValuePair<string, object?> pair in change) {
			args[pair.Key] = pair.Value;
		}
		return await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyDynamicCaseAsNewVersionTool.ModifyDynamicCaseAsNewVersionToolName, args);
	}

	private static Task<CallToolResult> SetActiveAsync(ProcessDesignerArrangeContext context, string versionName,
		bool dryRun, bool confirmPackagePosition = false) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName,
			["version-name"] = versionName,
			["dry-run"] = dryRun
		};
		if (confirmPackagePosition) {
			args["confirm-package-position"] = true;
		}
		return ProcessDesignerE2EArrange.CallToolAsync(context,
			SetActiveDynamicCaseVersionTool.SetActiveDynamicCaseVersionToolName, args);
	}

	/// <summary>The names of the case's version family members, as describe lists them.</summary>
	private static List<string> MembersOf(JsonObject caseBlock) =>
		[.. (caseBlock["versions"]?.AsArray() ?? []).Select(member => Text(member!.AsObject(), "name")!)];

	/// <summary>The member describe names as the platform's pick.</summary>
	private static string? ActiveMemberOf(JsonObject caseBlock) =>
		(caseBlock["versions"]?.AsArray() ?? []).Select(member => member!.AsObject())
			.Where(member => member["active"]!.GetValue<bool>())
			.Select(member => Text(member, "name"))
			.SingleOrDefault();

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

	/// <summary>The stored stages in order, the one named carrying a caption.</summary>
	private static object[] WithCaption(List<string> values, string value, string caption) =>
		[.. values.Select<string, object>(stored => stored == value
			? new Dictionary<string, object?> { ["value"] = stored, ["caption"] = caption }
			: stored)];

	/// <summary>
	/// A dry run's log holds exactly two lines: the Info verdict, then the result document. Notes and warnings travel
	/// once, inside the document; asserting the count holds whether or not this stand's dry run returned any. When the
	/// document carries warnings, the verdict counts them, since no separate line does.
	/// </summary>
	private static void ShouldHoldOnlyTheVerdictAndThePlan(CallToolResult result) {
		IReadOnlyList<CommandLogMessageEnvelope> log = McpCommandExecutionParser.Extract(result).Output ?? [];
		log.Should().HaveCount(2, because: "the log holds the verdict and the result document only, with no second "
			+ "copy of a note or a warning. The server said: {0}", LogText(result));
		log[0].MessageType.Should().Be(LogDecoratorType.Info, because: "a planned dry run is a success");
		log[0].Value.Should().StartWith("Dry run:", because: "the verdict comes first");
		JsonObject document = JsonNode.Parse(log[1].Value!)!.AsObject();
		Text(document, "outcome").Should().Be("planned",
			because: "the result document follows the verdict and is the last line");
		int warnings = (document["warnings"] as JsonArray ?? new JsonArray())
			.Count(warning => !string.IsNullOrWhiteSpace(warning?.GetValue<string>()));
		if (warnings > 0) {
			log[0].Value.Should().Contain(warnings == 1 ? "1 warning in the result" : $"{warnings} warnings in the result",
				because: "the verdict is the one top-level line that says warnings wait in the document");
		} else {
			log[0].Value.Should().NotContain("in the result: show", because: "a dry run without warnings counts none");
		}
	}

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

	// A version goes before its root: the root is made active again first (a version may be the active one when an
	// assertion failed half-way), then the version's own link-row binding, then the version schema. Deleting a case
	// schema removes its link row but leaves that row's package binding behind, so the binding goes first.
	private static async Task CleanUpVersionAsync(ProcessDesignerArrangeContext context, JsonObject? created,
		JsonObject? version) {
		// unknown counts too when the answer still names the version: the server answers it after a save it could not
		// read back, and the version is then on the shared seeded object.
		if (created is null || version is null
			|| Text(version, "outcome") is not ("version-created" or "failed-leftover" or "unknown")
			|| version["version"] is not JsonObject) {
			return;
		}
		try {
			CallToolResult back = await SetActiveAsync(context, CaseNameOf(created), dryRun: false);
			if (McpCommandExecutionParser.Extract(back).ExitCode != 0) {
				await TestContext.Error.WriteLineAsync($"Could not activate the root again: {LogText(back)}");
			}
		} catch (Exception exception) {
			PayloadDumpReader.DeleteIfPresent(exception.Message);
			await TestContext.Error.WriteLineAsync($"Activating the root again failed: {exception.Message}");
		}
		JsonObject? settings = version["settings"]?.AsObject();
		List<string> bindings = settings?["bindings"]?.AsArray().Select(binding => binding!.GetValue<string>()).ToList()
			?? [];
		await RemoveBindingRowAsync(context, bindings, "SysDcmSchemaInSettings_",
			settings is null ? null : Text(settings, "sysDcmSchemaInSettingsId"));
		string? versionName = version["version"] is JsonObject block ? Text(block, "name") : null;
		if (!string.IsNullOrWhiteSpace(versionName)) {
			await DeleteProcessAsync(context.EnvironmentName, versionName);
		}
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
