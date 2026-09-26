using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-100154: the short form of a named <c>get-tool-contract</c> lookup and the default rule that fits a
/// named lookup's reply inline.
/// </summary>
/// <remarks>
/// The rules are driven directly over synthetic contracts so each one is pinned on its own, and then once
/// over the real catalog for the two claims that matter to a caller: a destructive tool's safety lead
/// survives shortening, and an explicit <c>detail=full</c> still returns everything.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ToolContractShortFormTests {

	private static readonly Func<string, bool> NothingDestructive = _ => false;
	private static readonly Func<string, bool> EverythingDestructive = _ => true;

	[Test]
	[Category("Unit")]
	[Description("A description within the bound is kept whole; a longer one is cut at the last sentence boundary inside the bound, never mid-sentence.")]
	public void BuildLead_Should_CutAtTheLastSentenceBoundaryInsideTheBound() {
		// Arrange
		string description = "First sentence. Second sentence. Third sentence is longer than the rest.";

		// Act
		string whole = ToolContractShortForm.BuildLead(description, description.Length);
		string cut = ToolContractShortForm.BuildLead(description, 40);

		// Assert
		whole.Should().Be(description, because: "a description that already fits is not touched");
		cut.Should().Be("First sentence. Second sentence.",
			because: "the lead ends on the last sentence that fits, so it never reads as a truncated thought");
	}

	[Test]
	[Category("Unit")]
	[Description("An abbreviation such as e.g. is not taken for a sentence end, the same rule the compact index uses.")]
	public void BuildLead_Should_NotBreakOnAnAbbreviation() {
		// Arrange
		string description = "Reads a value, e.g. a code. Then more text follows here.";

		// Act
		string lead = ToolContractShortForm.BuildLead(description, 30);

		// Assert
		lead.Should().Be("Reads a value, e.g. a code.",
			because: "cutting after 'e.g.' would end the lead mid-example");
	}

	[Test]
	[Category("Unit")]
	[Description("When even the first sentence is longer than the bound, the lead is cut at a word boundary and marked with an ellipsis.")]
	public void BuildLead_Should_CutAtAWordBoundary_WhenTheFirstSentenceIsTooLong() {
		// Arrange
		string description = "One very long first sentence without any break at all until here.";

		// Act
		string lead = ToolContractShortForm.BuildLead(description, 20);

		// Assert
		lead.Should().Be("One very long first…",
			because: "a cut inside a word would be unreadable, and the ellipsis says the sentence goes on");
	}

	[Test]
	[Category("Unit")]
	[Description("The short form keeps what a caller decides at call time with - required list, validators, error codes, preconditions, flows - and leaves out the description tail and the examples, saying so.")]
	public void Shorten_Should_KeepTheCallTimeContract_AndSayWhatItLeftOut() {
		// Arrange
		ToolContractDefinition full = BuildContract("probe-tool", descriptionSentences: 200, examples: 3);

		// Act
		ToolContractDefinition shortForm = ToolContractShortForm.Shorten(full, destructive: true);

		// Assert
		shortForm.Detail.Should().Be(ToolContractShortForm.ShortDetail,
			because: "a short contract must say that it is short, or a caller reads an omission as an absence");
		shortForm.FullContractBytes.Should().Be(ToolContractShortForm.MeasureBytes(full),
			because: "the caller is told what the full form would cost before deciding to ask for it");
		shortForm.InputSchema.Required.Should().Equal(full.InputSchema.Required,
			because: "the required list is what the call has to satisfy");
		shortForm.InputSchema.Validators.Should().BeEquivalentTo(full.InputSchema.Validators,
			because: "validators carry the refusal codes a caller plans around");
		shortForm.ErrorContract.Should().BeEquivalentTo(full.ErrorContract,
			because: "the refusal codes are part of the short form by definition");
		shortForm.Preconditions.Should().Equal(full.Preconditions,
			because: "a precondition is exactly what must not be dropped to save space");
		shortForm.PreferredFlow.Should().BeEquivalentTo(full.PreferredFlow,
			because: "the flow says how to dispatch the tool at all");
		shortForm.Examples.Should().BeEmpty(because: "examples are the bulk the short form trades away");
		shortForm.Description.Should().Contain("3 examples",
			because: "the description names what was left out");
		shortForm.Description.Should().Contain("detail=\"full\"",
			because: "the description says how to get the rest in one call");
		shortForm.Description.Length.Should().BeLessThan(full.Description.Length,
			because: "the description tail is left out");
	}

	[Test]
	[Category("Unit")]
	[Description("A destructive tool keeps the longer lead that holds its safety warning; any other tool keeps the shorter one.")]
	public void Shorten_Should_KeepALongerLead_ForADestructiveTool() {
		// Arrange
		ToolContractDefinition full = BuildContract("probe-tool", descriptionSentences: 200, examples: 0);

		// Act
		string destructiveLead = ToolContractShortForm.Shorten(full, destructive: true).Description;
		string otherLead = ToolContractShortForm.Shorten(full, destructive: false).Description;

		// Assert
		destructiveLead.Length.Should().BeGreaterThan(otherLead.Length,
			because: "the room a destructive tool keeps is where its confirmation duty is written");
		otherLead.Length.Should().BeLessThan(ToolContractShortForm.MaxNonDestructiveLeadChars + 200,
			because: "a non-destructive tool gives up that room so a multi-tool reply can fit");
	}

	[Test]
	[Category("Unit")]
	[Description("Shortening is idempotent, so a contract already short is never shortened again or re-measured as full.")]
	public void Shorten_Should_BeIdempotent() {
		// Arrange
		ToolContractDefinition once = ToolContractShortForm.Shorten(
			BuildContract("probe-tool", descriptionSentences: 200, examples: 2), destructive: true);

		// Act
		ToolContractDefinition twice = ToolContractShortForm.Shorten(once, destructive: true);

		// Assert
		twice.Should().BeSameAs(once, because: "the short form of a short form is itself");
	}

	[Test]
	[Category("Unit")]
	[Description("The default leaves every contract in full while the reply fits the budget, so a small contract is never touched.")]
	public void Apply_Should_LeaveAReplyThatFits_Untouched() {
		// Arrange
		ToolContractDefinition[] contracts = [BuildContract("small-a", 5, 1), BuildContract("small-b", 5, 1)];

		// Act
		IReadOnlyList<ToolContractDefinition> result =
			ToolContractShortForm.Apply(contracts, detail: null, MeasureReply, NothingDestructive);

		// Assert
		result.Should().OnlyContain(contract => contract.Detail == null,
			because: "a reply that fits is returned exactly as before ENG-100154");
		result.Select(contract => contract.Examples.Count).Should().OnlyContain(count => count == 1,
			because: "small contracts keep their examples, which is where examples earn their place");
	}

	[Test]
	[Category("Unit")]
	[Description("When the reply does not fit, the LARGEST contract is shortened first and the smaller one is left in full once the reply fits.")]
	public void Apply_Should_ShortenTheLargestContractFirst_UntilTheReplyFits() {
		// Arrange
		ToolContractDefinition large = BuildContract("large", descriptionSentences: 900, examples: 5);
		ToolContractDefinition small = BuildContract("small", descriptionSentences: 5, examples: 1);

		// Act
		IReadOnlyList<ToolContractDefinition> result =
			ToolContractShortForm.Apply([small, large], detail: null, MeasureReply, NothingDestructive);

		// Assert
		result.Select(contract => contract.Name).Should().Equal(["small", "large"],
			because: "fitting never reorders or drops a requested contract");
		result.Single(contract => contract.Name == "large").Detail.Should().Be(ToolContractShortForm.ShortDetail,
			because: "the largest contract is the one that costs the reply its inline delivery");
		result.Single(contract => contract.Name == "small").Detail.Should().BeNull(
			because: "once the reply fits, nothing else is shortened");
		MeasureReply(result).Should().BeLessThanOrEqualTo(ToolContractShortForm.InlineReplyBudgetBytes,
			because: "the reply fits after fitting");
	}

	[Test]
	[Category("Unit")]
	[Description("detail=full returns every named contract complete however large, and detail=short returns every one short however small.")]
	public void Apply_Should_HonourAnExplicitDetail() {
		// Arrange
		ToolContractDefinition large = BuildContract("large", descriptionSentences: 900, examples: 5);
		ToolContractDefinition small = BuildContract("small", descriptionSentences: 5, examples: 1);

		// Act
		IReadOnlyList<ToolContractDefinition> full =
			ToolContractShortForm.Apply([large, small], "FULL", MeasureReply, EverythingDestructive);
		IReadOnlyList<ToolContractDefinition> allShort =
			ToolContractShortForm.Apply([large, small], "short", MeasureReply, EverythingDestructive);

		// Assert
		full.Should().OnlyContain(contract => contract.Detail == null,
			because: "an explicit full request is the escape hatch and must never be shortened");
		allShort.Should().OnlyContain(contract => contract.Detail == ToolContractShortForm.ShortDetail,
			because: "an explicit short request shortens every contract, fitting or not");
	}

	[Test]
	[Category("Unit")]
	[Description("A destructive tool's short form keeps a confirmation duty stated deep in its description, not only the warning it opens with; a non-destructive tool's short form carries no such clause.")]
	public void Shorten_Should_KeepAConfirmationClauseBeyondTheLead_ForADestructiveTool() {
		// Arrange
		ToolContractDefinition full = BuildContract("probe-tool", descriptionSentences: 200, examples: 0) with {
			Description = BuildContract("probe-tool", 200, 0).Description
				+ " Before deleting a record you MUST count the matches, name the object and get an explicit yes."
				+ " Trailing reference text follows."
		};

		// Act
		string destructive = ToolContractShortForm.Shorten(full, destructive: true).Description;
		string other = ToolContractShortForm.Shorten(full, destructive: false).Description;

		// Assert
		destructive.Should().Contain(
			"Before deleting a record you MUST count the matches, name the object and get an explicit yes.",
			because: "a confirmation duty is kept wherever the description states it, or the short form hands the "
				+ "caller a destructive capability without the duty that goes with it");
		destructive.Should().NotContain("Trailing reference text",
			because: "only the clause is kept, not the reference text after it");
		other.Should().NotContain("explicit yes",
			because: "the clause is kept for destructive tools; this one has nothing to confirm");
	}

	[Test]
	[Category("Unit")]
	[Description("Over the real catalog, every destructive tool's default short form states each confirmation duty its full description states, counted by the 'explicit yes' phrase the catalog words every duty with.")]
	public void DefaultLookup_Should_KeepEveryConfirmationDuty_OfEveryDestructiveTool() {
		// Arrange
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		McpToolInvokerRegistry registry = new(Substitute.For<IServiceProvider>(), typeof(SchemaSyncTool).Assembly,
			featureToggle, JsonSerializerOptions.Default);
		ToolContractGetTool tool = new(registry);
		string[] names = (tool.GetToolContracts().Index ?? []).Select(entry => entry.Name).ToArray();

		// Act
		List<string> lost = [];
		int shortened = 0;
		foreach (string name in names.Where(registry.IsDestructive)) {
			ToolContractDefinition shortForm = tool.GetToolContracts(new ToolContractGetArgs([name])).Tools!.Single();
			if (shortForm.Detail is null) {
				continue;
			}
			shortened++;
			ToolContractDefinition full = tool.GetToolContracts(
				new ToolContractGetArgs([name], ToolContractShortForm.FullDetail)).Tools!.Single();
			int inFull = Occurrences(full.Description, ToolContractShortForm.ConfirmationMarker);
			int inShort = Occurrences(shortForm.Description, ToolContractShortForm.ConfirmationMarker);
			if (inShort < inFull) {
				lost.Add($"{name}: {inShort} of {inFull}");
			}
		}

		// Assert
		shortened.Should().BeGreaterThan(0,
			because: "anti-vacuity: the destructive process tools are far over the budget, so some must be short");
		lost.Should().BeEmpty(because: "a short form must not drop a confirmation duty its full form states");
	}

	[TestCase("create-business-process")]
	[TestCase("modify-business-process")]
	[Category("Unit")]
	[Description("Over the real catalog, the default single lookup of a destructive process tool is short AND still carries its access-rights confirmation duty, the warning its description leads with.")]
	public void DefaultLookup_Should_KeepTheSafetyLead_OfADestructiveProcessTool(string toolName) {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractDefinition contract = tool.GetToolContracts(new ToolContractGetArgs([toolName])).Tools!.Single();

		// Assert
		contract.Detail.Should().Be(ToolContractShortForm.ShortDetail,
			because: $"{toolName}'s full contract is several times the inline budget, so the default read is short");
		contract.Description.Should().Contain("BEFORE CALLING with an accessRights block",
			because: "the confirmation duty is the one part of the description a short form must not lose");
		contract.Description.Should().Contain("get an explicit yes",
			because: "the warning must survive to its instruction, not just its heading");
	}

	[Test]
	[Category("Unit")]
	[Description("Over the real catalog, detail=full still returns the complete create-business-process contract, marked as neither short nor sized.")]
	public void FullLookup_Should_ReturnTheCompleteContract() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractDefinition shortForm = tool.GetToolContracts(
			new ToolContractGetArgs(["create-business-process"])).Tools!.Single();
		ToolContractDefinition full = tool.GetToolContracts(
			new ToolContractGetArgs(["create-business-process"], ToolContractShortForm.FullDetail)).Tools!.Single();

		// Assert
		full.Detail.Should().BeNull(because: "a full contract carries no short-form marker, as before ENG-100154");
		full.FullContractBytes.Should().BeNull(because: "a full contract carries no size note either");
		full.Description.Length.Should().BeGreaterThan(shortForm.Description.Length * 5,
			because: "the full description is the one the short form stands in for");
		ToolContractShortForm.MeasureBytes(full).Should().Be(shortForm.FullContractBytes,
			because: "the size the short form advertises is the size detail=full actually returns");
	}

	private static int Occurrences(string text, string value) {
		int count = 0;
		for (int index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase); index >= 0;
			index = text.IndexOf(value, index + value.Length, StringComparison.OrdinalIgnoreCase)) {
			count++;
		}
		return count;
	}

	private static int MeasureReply(IReadOnlyList<ToolContractDefinition> contracts) =>
		ToolContractShortForm.MeasureBytes(new ToolContractGetResponse(true, Tools: contracts));

	private static ToolContractDefinition BuildContract(string name, int descriptionSentences, int examples) {
		string description = string.Join(" ", Enumerable.Range(1, descriptionSentences)
			.Select(index => $"Sentence number {index} explains one more rule of this probe tool."));
		return new ToolContractDefinition(
			name,
			description,
			new ToolInputSchemaContract(
				["environment-name"],
				[new ToolContractField("environment-name", "string", "Registered environment. Second sentence here.")],
				Validators: [new ToolContractValidator("probe-rule", "probe-refused", Field: "environment-name")]),
			new ToolOutputContract("envelope", "success", ["success == false"],
				[new ToolContractField("success", "boolean", "Whether it worked. More words follow.")]),
			new ToolErrorContract([new ToolErrorCodeContract("probe-refused", "The probe refused the call.")]),
			[],
			[],
			Enumerable.Range(1, examples)
				.Select(index => new ToolContractExample(
					$"Example {index} " + new string('x', 400),
					new Dictionary<string, object?> { ["environment-name"] = "dev" }))
				.ToArray(),
			new ToolFlowHint([name], "Call it directly."),
			[],
			[],
			Preconditions: ["The environment must be registered."]);
	}

	private static ToolContractGetTool BuildToolWithRegistry() {
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		McpToolInvokerRegistry registry = new(
			Substitute.For<IServiceProvider>(),
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			JsonSerializerOptions.Default);
		return new ToolContractGetTool(registry);
	}
}
