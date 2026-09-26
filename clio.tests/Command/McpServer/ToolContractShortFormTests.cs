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
/// The rules are driven directly over synthetic contracts so each one is pinned on its own, and then over
/// the real catalog for the claims that matter to a caller: no safety sentence of any tool - in its
/// description or in a field description - is lost to shortening, and an explicit <c>detail=full</c> still
/// returns everything.
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
	[Description("A destructive tool whose description opens with a safety warning keeps the longer lead that holds the whole warning block; the same description on a non-destructive tool keeps the ordinary one.")]
	public void Shorten_Should_KeepTheWarningBlock_ForADestructiveToolThatOpensWithIt() {
		// Arrange
		ToolContractDefinition baseline = BuildContract("probe-tool", descriptionSentences: 200, examples: 0);
		ToolContractDefinition full = baseline with {
			Description = "Deletes records. This is irreversible. An absent filter reaches every record and nothing "
				+ "warns you. " + baseline.Description
		};

		// Act
		string destructiveLead = ToolContractShortForm.Shorten(full, destructive: true).Description;
		string otherLead = ToolContractShortForm.Shorten(full, destructive: false).Description;

		// Assert
		destructiveLead.Length.Should().BeGreaterThan(ToolContractShortForm.MaxOrdinaryLeadChars + 200,
			because: "the warning block runs past the ordinary lead, and a destructive tool keeps all of it");
		otherLead.Length.Should().BeLessThan(ToolContractShortForm.MaxOrdinaryLeadChars + 200,
			because: "a non-destructive tool gives up that room so a multi-tool reply can fit");
	}

	[Test]
	[Category("Unit")]
	[Description("A destructive tool whose warning comes LATE keeps only the ordinary lead, with the warning appended as a safety sentence, so the long lead is not spent on reference text.")]
	public void Shorten_Should_KeepTheOrdinaryLead_ForADestructiveToolWhoseWarningComesLate() {
		// Arrange
		ToolContractDefinition baseline = BuildContract("probe-tool", descriptionSentences: 200, examples: 0);
		ToolContractDefinition full = baseline with {
			Description = baseline.Description + " ASK FIRST: never on your own initiative."
		};

		// Act
		string shortForm = ToolContractShortForm.Shorten(full, destructive: true).Description;

		// Assert
		shortForm.Should().Contain("ASK FIRST: never on your own initiative.",
			because: "the late warning is still kept, as a safety sentence");
		shortForm.Length.Should().BeLessThan(ToolContractShortForm.MaxOrdinaryLeadChars + 400,
			because: "the lead before the warning is reference text, and the long lead would spend the reply on it");
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
	[Description("A safety sentence stated deep in the description survives shortening for ANY tool, destructive or not, while the reference text around it is left out.")]
	public void Shorten_Should_KeepASafetySentenceBeyondTheLead_ForAnyTool() {
		// Arrange
		ToolContractDefinition full = BuildContract("probe-tool", descriptionSentences: 200, examples: 0) with {
			Description = BuildContract("probe-tool", 200, 0).Description
				+ " Before deleting a record you MUST count the matches, name the object and get an explicit yes."
				+ " Trailing reference text follows."
				+ " ASK FIRST: do not call this on your own initiative after a build."
				+ " More trailing reference text."
		};

		// Act
		string destructive = ToolContractShortForm.Shorten(full, destructive: true).Description;
		string other = ToolContractShortForm.Shorten(full, destructive: false).Description;

		// Assert
		foreach (string description in new[] { destructive, other }) {
			description.Should().Contain(
				"Before deleting a record you MUST count the matches, name the object and get an explicit yes.",
				because: "a confirmation duty is kept wherever the description states it, or the short form hands "
					+ "the caller a capability without the duty that goes with it");
			description.Should().Contain("ASK FIRST: do not call this on your own initiative after a build.",
				because: "an ask-first rule is a safety duty too, and a non-destructive tool can carry one "
					+ "(set-active-business-process-version is not destructive and is ask-first)");
			description.Should().NotContain("Trailing reference text",
				because: "only the safety sentences are kept, not the reference text between them");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("A safety sentence longer than the per-sentence cap is cut to a window around its marker, so a marker inside a run-on reference paragraph cannot pull the whole paragraph into the short form.")]
	public void SafetySentences_Should_CutARunOnSentence_AroundItsMarker() {
		// Arrange
		string filler = string.Join(" ", Enumerable.Repeat("word", 400));
		string text = filler + " this step is irreversible " + filler + ".";

		// Act
		IReadOnlyList<string> sentences = ToolContractShortForm.SafetySentences(text, 0);

		// Assert
		sentences.Should().ContainSingle(because: "the text is one sentence with one marker");
		sentences[0].Should().Contain("this step is irreversible",
			because: "the window is centred on the marker");
		sentences[0].Length.Should().BeLessThanOrEqualTo(ToolContractShortForm.MaxSafetySentenceChars + 2,
			because: "the cap plus the two ellipses bounds what one run-on sentence can cost");
	}

	[Test]
	[Category("Unit")]
	[Description("A field description that states a safety duty is kept whole; any other is cut to its first sentence, and a leading 'Optional.' label does not count as that sentence.")]
	public void Shorten_Should_KeepASafetyFieldWhole_AndSkipALabelWhenCuttingAnOrdinaryOne() {
		// Arrange
		ToolContractDefinition baseline = BuildContract("probe-tool", descriptionSentences: 200, examples: 0);
		ToolContractDefinition full = baseline with {
			InputSchema = baseline.InputSchema with {
				Properties = [
					new ToolContractField("force", "boolean",
						"Optional. Overwrites remote changes. Set true ONLY after the user explicitly confirms overwriting changes made outside this session."),
					new ToolContractField("page-name", "string",
						"Optional. The page to read. Everything after this sentence is reference text.")
				]
			}
		};

		// Act
		ToolContractDefinition shortForm = ToolContractShortForm.Shorten(full, destructive: false);

		// Assert
		shortForm.InputSchema.Properties.Single(field => field.Name == "force").Description.Should().Be(
			full.InputSchema.Properties.Single(field => field.Name == "force").Description,
			because: "the confirmation duty of a flag lives in the field description, and cutting it to "
				+ "'Optional.' would hand the caller the flag without the duty");
		shortForm.InputSchema.Properties.Single(field => field.Name == "page-name").Description.Should().Be(
			"Optional. The page to read.",
			because: "a label is not a description; the first real sentence is kept after it");
	}

	[Test]
	[Category("Unit")]
	[Description("Over the real catalog at detail=short, every sentence the safety pattern matches in a tool's full description is in its short description, and every field description carrying one is kept whole.")]
	public void ShortLookup_Should_KeepEverySafetySentence_OfEveryTool() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();
		string[] names = (tool.GetToolContracts().Index ?? []).Select(entry => entry.Name).ToArray();

		// Act
		List<string> lost = [];
		int checkedSentences = 0;
		int checkedFields = 0;
		foreach (string name in names) {
			ToolContractDefinition full = tool.GetToolContracts(
				new ToolContractGetArgs([name], ToolContractShortForm.FullDetail)).Tools!.Single();
			ToolContractDefinition shortForm = tool.GetToolContracts(
				new ToolContractGetArgs([name], ToolContractShortForm.ShortDetail)).Tools!.Single();
			foreach (string sentence in ToolContractShortForm.SafetySentences(full.Description ?? string.Empty, 0)) {
				checkedSentences++;
				if (!shortForm.Description.Contains(sentence.Trim('…'), StringComparison.Ordinal)) {
					lost.Add($"{name}: \"{sentence[..Math.Min(80, sentence.Length)]}\"");
				}
			}
			IEnumerable<(ToolContractField Full, ToolContractField Short)> fields =
				(full.InputSchema?.Properties ?? []).Zip(shortForm.InputSchema?.Properties ?? [])
				.Concat((full.OutputContract?.Fields ?? []).Zip(shortForm.OutputContract?.Fields ?? []));
			foreach ((ToolContractField fullField, ToolContractField shortField) in fields
				         .Where(pair => ToolContractShortForm.SafetyDuty.IsMatch(pair.Full.Description ?? string.Empty))) {
				checkedFields++;
				if (shortField.Description != fullField.Description) {
					lost.Add($"{name}.{fullField.Name}: field description cut");
				}
			}
		}

		// Assert
		checkedSentences.Should().BeGreaterThan(20,
			because: "anti-vacuity: the catalog words many safety duties, and a pattern that matched none "
				+ "would pass this test while protecting nothing");
		checkedFields.Should().BeGreaterThan(0,
			because: "anti-vacuity: flags such as update-page's force carry their duty in the field description");
		lost.Should().BeEmpty(because: "a short form must not drop a safety sentence its full form states");
	}

	[TestCase("set-active-business-process-version", "ASK FIRST")]
	[TestCase("modify-business-process-as-new-version", "Never chain the two")]
	[TestCase("modify-business-process-as-new-version", "permanent")]
	[Category("Unit")]
	[Description("Over the real catalog, the duties the first short form was reviewed for losing - ask-first, never-chain, permanence - are in the short form of the tool that states them.")]
	public void ShortLookup_Should_KeepTheReviewedDuty(string toolName, string duty) {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractDefinition full = tool.GetToolContracts(
			new ToolContractGetArgs([toolName], ToolContractShortForm.FullDetail)).Tools!.Single();
		ToolContractDefinition shortForm = tool.GetToolContracts(
			new ToolContractGetArgs([toolName], ToolContractShortForm.ShortDetail)).Tools!.Single();

		// Assert
		full.Description.Should().Contain(duty, because: "anti-vacuity: the full contract states the duty");
		shortForm.Description.Should().Contain(duty,
			because: "a short form keyed on one confirmation phrase dropped this duty, which is why the pattern exists");
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
		int also = contract.Description.IndexOf(" Also: ", StringComparison.Ordinal);
		string lead = also < 0 ? contract.Description : contract.Description[..also];
		lead.Should().Contain("BEFORE CALLING with an accessRights block",
			because: "the confirmation duty is the warning the description LEADS with, so it must be in the lead "
				+ "and not merely recovered later as a matched sentence");
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
