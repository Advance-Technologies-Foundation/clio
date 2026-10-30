using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Covers the silent-drop detection: a server that predates the sendEmail element discards an email block and
/// still answers success, so the only way to know the configuration landed is to read it back. These tests pin
/// the pure halves (what the payload asked for, what the description is missing, what the caller is told).
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public class EmailBlockExpectationTests {

	#region Methods: FromDescriptor

	[Test]
	[Description("A build descriptor with a DUPLICATE property name - accepted by JsonNode.Parse, thrown as ArgumentException only when the JsonObject materialises - must not escape the detectors: the parse helper materialises the tree under its own catch, so every detector returns empty instead of taking the whole read-back verification down. Pinned at both nesting levels, because the throw happens on whichever object carries the duplicate.")]
	public void Detectors_ShouldReturnEmptyWithoutThrowing_WhenTheDescriptorCarriesADuplicateKey() {
		// Arrange - a duplicate at the top level and one nested inside an elements[] entry
		const string duplicateTopLevel = """
			{"name":"UsrProc","name":"UsrProc2","elements":[
				{"name":"SendMail1","type":"sendEmail","email":{"template":"Case closure notification"}}]}
			""";
		const string duplicateNested = """
			{"name":"UsrProc","elements":[
				{"name":"SendMail1","type":"sendEmail","email":{"template":"T","template":"T2"}}]}
			""";

		// Act
		Func<IReadOnlyList<string>> topLevelEmail = () => EmailBlockExpectation.FromDescriptor(duplicateTopLevel);
		Func<IReadOnlyList<string>> topLevelTemplate = () => EmailBlockExpectation.TemplateElements(duplicateTopLevel);
		Func<IReadOnlyList<string>> nestedEmail = () => EmailBlockExpectation.FromDescriptor(duplicateNested);
		Func<IReadOnlyList<string>> nestedTemplate = () => EmailBlockExpectation.TemplateElements(duplicateNested);

		// Assert
		topLevelEmail.Should().NotThrow(because: "a duplicate key is the caller's payload problem, reported by the command's own error path, not by the read-back guard")
			.Which.Should().BeEmpty(because: "an unparseable payload skips the verification rather than accusing the server");
		topLevelTemplate.Should().NotThrow().Which.Should().BeEmpty();
		nestedEmail.Should().NotThrow(because: "the duplicate sits on a nested object, which is where a materialisation that stopped at the root would still throw in the caller")
			.Which.Should().BeEmpty();
		nestedTemplate.Should().NotThrow().Which.Should().BeEmpty();
	}

	[Test]
	[Description("A later operation whose email block carries an EMPTY body, or only a bodyFormat, does NOT count as switching the element back to a custom message: the server refuses both payloads (a non-empty body is required, and a bodyFormat alone configures nothing), so neither can leave a custom element behind - and dropping the element from the template check would stop verifying a template on exactly the batch that failed.")]
	public void TemplateElementsFromOperations_ShouldKeepAnElement_WhenALaterOperationCarriesAnEmptyBodyOrOnlyABodyFormat() {
		// Arrange
		const string emptyBody = """
			[{"op":"setElement","elementName":"Tpl1","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"Tpl1","elementUpdate":{"email":{"body":"   "}}}]
			""";
		const string formatOnly = """
			[{"op":"setElement","elementName":"Tpl1","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"Tpl1","elementUpdate":{"email":{"bodyFormat":"html"}}}]
			""";

		// Act
		IReadOnlyList<string> afterEmptyBody = EmailBlockExpectation.TemplateElementsFromOperations(emptyBody);
		IReadOnlyList<string> afterFormatOnly = EmailBlockExpectation.TemplateElementsFromOperations(formatOnly);

		// Assert
		afterEmptyBody.Should().BeEquivalentTo(["Tpl1"],
			because: "an explicitly empty body is refused at build, so it never produces the custom-message element that would legitimately have no template");
		afterFormatOnly.Should().BeEquivalentTo(["Tpl1"],
			because: "a bodyFormat with no body is refused as configuring nothing, so it cannot clear the template either");
	}

	[Test]
	[Description("The same duplicate-key payload on the modify path: the operations detectors return empty without throwing, for a duplicate on the operation object and for one nested in elementUpdate.email.")]
	public void OperationsDetectors_ShouldReturnEmptyWithoutThrowing_WhenAnOperationCarriesADuplicateKey() {
		// Arrange
		const string duplicateOnOperation = """
			[{"op":"setElement","op":"setElement","elementName":"SendMail1",
			  "elementUpdate":{"email":{"template":"Case closure notification"}}}]
			""";
		const string duplicateInEmail = """
			[{"op":"setElement","elementName":"SendMail1",
			  "elementUpdate":{"email":{"template":"T","template":"T2"}}}]
			""";

		// Act
		Func<IReadOnlyList<string>> onOperationEmail = () => EmailBlockExpectation.FromOperations(duplicateOnOperation);
		Func<IReadOnlyList<string>> onOperationTemplate = () => EmailBlockExpectation.TemplateElementsFromOperations(duplicateOnOperation);
		Func<IReadOnlyList<string>> inEmailEmail = () => EmailBlockExpectation.FromOperations(duplicateInEmail);
		Func<IReadOnlyList<string>> inEmailTemplate = () => EmailBlockExpectation.TemplateElementsFromOperations(duplicateInEmail);

		// Assert
		onOperationEmail.Should().NotThrow().Which.Should().BeEmpty(because: "the verification is skipped, not the command");
		onOperationTemplate.Should().NotThrow().Which.Should().BeEmpty();
		inEmailEmail.Should().NotThrow(because: "the duplicate is three objects deep; the walk must reach it").Which.Should().BeEmpty();
		inEmailTemplate.Should().NotThrow().Which.Should().BeEmpty();
	}

	[Test]
	[Description("A build descriptor's elements carrying an email block are the ones whose configuration must be verified; elements without one are irrelevant to the check.")]
	public void FromDescriptor_ShouldReturnOnlyElementsCarryingAnEmailBlock() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","elements":[
				{"name":"StartEvent1","type":"startEvent"},
				{"name":"SendMail1","type":"sendEmail","email":{"mode":"auto"}},
				{"name":"Task1","type":"userTask"},
				{"name":"SendMail2","type":"sendEmail","email":{"subject":"x"}}]}
			""";

		// Act
		IReadOnlyList<string> expected = EmailBlockExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().BeEquivalentTo(["SendMail1", "SendMail2"],
			because: "only the elements that actually asked for an email configuration can have one dropped");
	}

	[Test]
	[Description("A descriptor with no email block anywhere produces no expectation, so the ordinary build path never pays for the extra read-back.")]
	public void FromDescriptor_ShouldReturnEmpty_WhenNoElementCarriesAnEmailBlock() {
		// Arrange
		const string descriptor = """{"name":"UsrProc","elements":[{"name":"Task1","type":"userTask"}]}""";

		// Act, Assert
		EmailBlockExpectation.FromDescriptor(descriptor).Should().BeEmpty(
			because: "with nothing to verify the command must skip the describe round trip entirely");
	}

	[Test]
	[Description("An unparseable or empty descriptor yields no expectation: the command reports the real JSON error through its normal path, and this check must not add a second, misleading message.")]
	public void FromDescriptor_ShouldReturnEmpty_WhenPayloadIsNotUsableJson() {
		// Arrange, Act, Assert
		EmailBlockExpectation.FromDescriptor("not json at all").Should().BeEmpty(
			because: "unparseable text is the command's JSON error to report, not a second message from this check");
		EmailBlockExpectation.FromDescriptor("").Should().BeEmpty(
			because: "an empty payload carries no element that could have asked for an email block");
		EmailBlockExpectation.FromDescriptor("[1,2,3]").Should().BeEmpty(
			because: "a JSON array is not a descriptor object, so there is nothing to read elements from");
	}

	#endregion

	#region Methods: FromOperations

	[Test]
	[Description("Both modify routes that carry an email block are detected: addElement nests it under 'element', while setElement nests it under 'elementUpdate' and names the element on the operation itself.")]
	public void FromOperations_ShouldDetectBothAddElementAndSetElementRoutes() {
		// Arrange
		const string operations = """
			[{"op":"addElement","element":{"name":"Added1","type":"sendEmail","email":{"mode":"manual"}}},
			 {"op":"setElement","elementName":"Existing1","elementUpdate":{"email":{"to":[{"value":"a@b.c"}]}}},
			 {"op":"setElement","elementName":"Existing2","elementUpdate":{"useBackgroundMode":true}},
			 {"op":"removeElement","elementName":"Gone1"}]
			""";

		// Act
		IReadOnlyList<string> expected = EmailBlockExpectation.FromOperations(operations);

		// Assert
		expected.Should().BeEquivalentTo(["Added1", "Existing1"],
			because: "a setElement that changes only useBackgroundMode carries no email block to lose, and a "
				+ "removeElement carries none either");
	}

	[Test]
	[Description("A non-array operations payload yields no expectation rather than throwing, because the command already rejects that shape with a precise error.")]
	public void FromOperations_ShouldReturnEmpty_WhenPayloadIsNotAnArray() {
		// Arrange, Act, Assert
		EmailBlockExpectation.FromOperations("""{"op":"setElement"}""").Should().BeEmpty(
			because: "an operations payload is an ARRAY; an object is not one and must not be walked as if it were");
		EmailBlockExpectation.FromOperations("broken").Should().BeEmpty(
			because: "unparseable text is the command's JSON error to report, not a second message from this check");
	}

	#endregion

	#region Methods: Missing

	[Test]
	[Description("An element the caller configured that comes back with NO email block is reported as dropped — this is the exact signature of a server that ignored the block while answering success.")]
	public void Missing_ShouldReportAnElementDescribedWithoutItsEmailBlock() {
		// Arrange
		DescribeProcessResult described = new() {
			Elements = [
				new DescribedElement { Name = "SendMail1", Email = null },
				new DescribedElement { Name = "SendMail2", Email = new DescribedEmail { Mode = "auto" } }
			]
		};

		// Act
		IReadOnlyList<string> missing = EmailBlockExpectation.Missing(described, ["SendMail1", "SendMail2"]);

		// Assert
		missing.Should().BeEquivalentTo(["SendMail1"],
			because: "SendMail2 came back configured, so only SendMail1's configuration was discarded");
	}

	[Test]
	[Description("An element the comparison cannot find in the read-back is NOT reported: the read-back is the only evidence here, so an unresolvable identifier is a reason to stay quiet rather than to accuse the server of discarding a configuration.")]
	public void Missing_ShouldStayQuiet_WhenTheElementIsNotInTheDescriptionAtAll() {
		// Arrange
		DescribeProcessResult described = new() {
			Elements = [new DescribedElement { Name = "SomethingElse", Email = null }]
		};

		// Act, Assert
		EmailBlockExpectation.Missing(described, ["SendMail1"]).Should().BeEmpty(
			because: "a false 'your email config was discarded' on an edit that actually applied is worse than "
				+ "silence — the caller would undo or re-apply work that was already correct");
	}

	[Test]
	[Description("setElement identifies an element by local name OR UId, so an expectation recorded as a UId must match the described element by its uid — otherwise a perfectly good edit is reported as a discarded email block.")]
	public void Missing_ShouldMatchByUid_WhenTheCallerIdentifiedTheElementThatWay() {
		// Arrange — the caller used the element's UId, which the server resolves just as it resolves a name.
		const string uid = "6762ca3f-4324-490a-9797-fbf311a80113";
		DescribeProcessResult configured = new() {
			Elements = [new DescribedElement { Name = "SendMail1", Uid = uid, Email = new DescribedEmail() }]
		};
		DescribeProcessResult discarded = new() {
			Elements = [new DescribedElement { Name = "SendMail1", Uid = uid, Email = null }]
		};

		// Act, Assert
		EmailBlockExpectation.Missing(configured, [uid]).Should().BeEmpty(
			because: "the element was found by UId and it carries its email block, so nothing was discarded");
		EmailBlockExpectation.Missing(discarded, [uid]).Should().BeEquivalentTo([uid],
			because: "matching by UId must still detect a genuinely discarded block, not just suppress the warning");
	}

	[Test]
	[Description("A description that carries no elements at all reports nothing missing: an unreadable read-back is not evidence that the server dropped anything, and accusing it would be a false alarm.")]
	public void Missing_ShouldReportNothing_WhenTheDescriptionCarriesNoElements() {
		// Arrange
		DescribeProcessResult described = new() { Elements = null };

		// Act, Assert
		EmailBlockExpectation.Missing(described, ["SendMail1"]).Should().BeEmpty(
			because: "absent evidence must not be reported as a confirmed drop");
	}

	#endregion

	#region Methods: BuildWarning

	[Test]
	[Description("The warning names the affected element, states the element is unconfigured, tells the agent not to report success, and names the fix — the four things a caller needs to act correctly.")]
	public void BuildWarning_ShouldStateTheConsequenceAndTheFix() {
		// Arrange, Act
		string? warning = EmailBlockExpectation.BuildWarning(["SendMail1"]);

		// Assert
		warning.Should().NotBeNull(because: "a dropped block must produce a warning; silence would read as success");
		warning.Should().Contain("SendMail1", because: "the caller has to know WHICH element is unconfigured");
		warning.Should().Contain("UNCONFIGURED",
			because: "the consequence is the point: the process has an email step that will not send");
		warning.Should().Contain("usual cause",
			because: "the check observed an absent block, it did not measure the package version — the explanation "
				+ "must be offered as the likely one rather than asserted as a finding");
		warning.Should().Contain("read-back shows no email block",
			because: "the caller needs the OBSERVATION stated plainly, since that is the part this check can stand behind");
		warning.Should().Contain("install-process-builder",
			because: "a warning without the remedy leaves the caller stuck");
		warning.Should().Contain(Clio.Common.BundledPackages.ProcessBuilderInstallRemedy,
			because: "ENG-102116: the warning rides on a SUCCESSFUL build, so the refusal hint never reaches it, and a "
				+ "bare verb read as permission to run an install that rebuilds the configuration and restarts the "
				+ "site for every user");
		Clio.Common.BundledPackages.ProcessBuilderInstallRemedy.Should().Contain("after user approval",
			because: "the remedy clause exists to carry the approval; without it the shared constant is a bare verb again");
	}

	[Test]
	[Description("No dropped elements produces no warning, so a healthy environment stays silent instead of emitting a reassuring non-message on every build.")]
	public void BuildWarning_ShouldReturnNull_WhenNothingWasDropped() {
		// Arrange, Act, Assert
		EmailBlockExpectation.BuildWarning([]).Should().BeNull(
			because: "nothing dropped means nothing to say; a non-null result would emit an empty warning");
	}

	#endregion

	#region Methods: MacroBodyElements / UnresolvedBodyMacros / BuildMacroWarning

	[Test]
	[Description("Only elements whose email.body carries a [[param:…]] / [[element:…]] placeholder are returned: a plain-HTML body has no macros to fail to resolve, so it need not be verified.")]
	public void MacroBodyElements_ShouldReturnOnlyElementsWhoseBodyCarriesAMacro() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","elements":[
				{"name":"PlainMail","type":"sendEmail","email":{"body":"<p>Hello</p>"}},
				{"name":"ParamMail","type":"sendEmail","email":{"body":"<p>[[param:ContactName]]</p>"}},
				{"name":"ElementMail","type":"sendEmail","email":{"body":"<p>[[element:ReadOrder.ResultEntity.Number]]</p>"}},
				{"name":"NoBodyMail","type":"sendEmail","email":{"subject":"x"}}]}
			""";

		// Act
		IReadOnlyList<string> macroElements = EmailBlockExpectation.MacroBodyElements(descriptor);

		// Assert
		macroElements.Should().BeEquivalentTo(["ParamMail", "ElementMail"],
			because: "only a body that actually embeds a macro placeholder can fail to resolve on an old package");
	}

	[Test]
	[Description("An element whose sent body carried a macro but comes back with hasBody:true and an empty body is the signature of a package that stored the placeholders verbatim without resolving them — reported so the caller is warned before a literal [[…]] is emailed.")]
	public void UnresolvedBodyMacros_ShouldReportAMacroBodyThatCameBackEmptyDespiteHasBody() {
		// Arrange — the old-package degradation: block present, hasBody true, but no decoded body.
		DescribeProcessResult described = new() {
			Elements = [new DescribedElement {
				Name = "ParamMail", Email = new DescribedEmail { HasBody = true, Body = null }
			}]
		};

		// Act
		IReadOnlyList<string> unresolved = EmailBlockExpectation.UnresolvedBodyMacros(described, ["ParamMail"]);

		// Assert
		unresolved.Should().BeEquivalentTo(["ParamMail"],
			because: "hasBody:true with a null body is the read-back signature of macros stored but not resolved");
	}

	[Test]
	[Description("A healthy build decodes the tokens back into a non-null [[…]] author-form body, so an element whose body came back populated is NOT reported — the presence of [[ in the read-back is exactly what a resolved body looks like, so it must never be treated as the failure signal.")]
	public void UnresolvedBodyMacros_ShouldStayQuiet_WhenTheBodyDecodedBackToAuthorForm() {
		// Arrange — the healthy case: describe returns the decoded [[param:…]] body.
		DescribeProcessResult described = new() {
			Elements = [new DescribedElement {
				Name = "ParamMail",
				Email = new DescribedEmail { HasBody = true, Body = "<p>[[param:ContactName]]</p>" }
			}]
		};

		// Act, Assert
		EmailBlockExpectation.UnresolvedBodyMacros(described, ["ParamMail"]).Should().BeEmpty(
			because: "a decoded body is present, so the macros resolved — decode reproducing [[…]] must not read as a failure");
	}

	[Test]
	[Description("The macro warning names the affected element, states the literal would be emailed, and names the fix; nothing unresolved produces no warning.")]
	public void BuildMacroWarning_ShouldStateConsequenceAndFix_AndBeSilentWhenClean() {
		// Arrange, Act
		string? warning = EmailBlockExpectation.BuildMacroWarning(["ParamMail"]);

		// Assert
		warning.Should().NotBeNull(because: "an unresolved body must produce a warning; silence would read as success");
		warning.Should().Contain("ParamMail", because: "the caller has to know which element's body did not resolve");
		warning.Should().Contain("did NOT resolve",
			because: "the observation is the point: the placeholders were stored, not resolved");
		warning.Should().Contain("install-process-builder",
			because: "a warning without the remedy leaves the caller stuck");
		EmailBlockExpectation.BuildMacroWarning([]).Should().BeNull(
			because: "a healthy build must stay silent rather than emit a reassuring non-message");
	}

	#endregion


	#region Methods: Templates (ENG-95986)

	[Test]
	[Description("A build descriptor's elements naming an email template are the ones whose template must be verified after the build; an email block without a template, or with a blank one, is not a template expectation.")]
	public void TemplateElements_ShouldReturnOnlyElementsNamingATemplate() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","elements":[
				{"name":"Custom1","type":"sendEmail","email":{"body":"<p>x</p>"}},
				{"name":"Tpl1","type":"sendEmail","email":{"template":"Case closure notification"}},
				{"name":"Blank1","type":"sendEmail","email":{"template":"  "}},
				{"name":"Tpl2","type":"sendEmail","email":{"messageSource":"template","template":"80cdb129-de99-433f-8783-9d71c205607b"}}]}
			""";

		// Act
		IReadOnlyList<string> expected = EmailBlockExpectation.TemplateElements(descriptor);

		// Assert
		expected.Should().BeEquivalentTo(["Tpl1", "Tpl2"],
			because: "only an element that sent a template can have its template discarded by an older server");
	}

	[Test]
	[Description("Both modify routes that can carry a template are detected: addElement under 'element.email' and setElement under 'elementUpdate.email' with the name on the operation.")]
	public void TemplateElementsFromOperations_ShouldDetectBothRoutes() {
		// Arrange
		const string operations = """
			[{"op":"addElement","element":{"name":"Added1","type":"sendEmail","email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"Existing1","elementUpdate":{"email":{"template":"Welcome","subject":"Hi"}}},
			 {"op":"setElement","elementName":"Existing2","elementUpdate":{"email":{"subject":"only a subject"}}}]
			""";

		// Act
		IReadOnlyList<string> expected = EmailBlockExpectation.TemplateElementsFromOperations(operations);

		// Assert
		expected.Should().BeEquivalentTo(["Added1", "Existing1"],
			because: "a subject-only update names no template and must not be accused of losing one");
	}

	[Test]
	[Description("A sent template that the read-back does not show is reported; an element whose read-back carries the template is not, and one absent from the read-back or without an email block is left to the block-landed check.")]
	public void UnlandedTemplates_ShouldReportOnlyElementsWhoseReadBackLacksTheTemplate() {
		// Arrange
		DescribeProcessResult described = new() {
			Elements = [
				new DescribedElement { Name = "Landed", Email = new DescribedEmail { MessageSource = "template", Template = "80cdb129-de99-433f-8783-9d71c205607b" } },
				new DescribedElement { Name = "Dropped", Email = new DescribedEmail { Subject = "Hi" } },
				new DescribedElement { Name = "NoBlock", Email = null }
			]
		};

		// Act
		IReadOnlyList<string> unlanded = EmailBlockExpectation.UnlandedTemplates(described,
			["Landed", "Dropped", "NoBlock", "Unknown"]);

		// Assert
		unlanded.Should().BeEquivalentTo(["Dropped"],
			because: "only an element that reports an email block WITHOUT the template it was sent proves the member was discarded; a missing block is the other check's finding and an unknown element is no evidence");
	}

	[Test]
	[Description("The template warning names the elements, the wrong-mode consequence and the one action that fixes it, and is null when every template landed.")]
	public void BuildTemplateWarning_ShouldNameElementsAndRemedy_OrBeNullWhenNothingUnlanded() {
		// Arrange, Act
		string? warning = EmailBlockExpectation.BuildTemplateWarning(["Tpl1"]);
		string? none = EmailBlockExpectation.BuildTemplateWarning([]);

		// Assert
		warning.Should().Contain("'Tpl1'", because: "the caller must know which element lost its template");
		warning.Should().Contain("Localizable template not found",
			because: "the run-time failure of an element left without a mode is the reason the warning exists");
		warning.Should().Contain("install-process-builder", because: "the remedy is a package that supports template mode");
		warning.Should().Contain(EmailBlockExpectation.TemplateWarningMarker,
			because: "the e2e negative assertion references this marker; the warning must carry it verbatim");
		none.Should().BeNull(because: "no warning is emitted when every sent template reads back");
	}

	[Test]
	[Description("A batch that sets a template and then, for the SAME element, sends a body or messageSource 'custom' is not a template expectation: the server clears the template on that switch by contract, so 'no template' in the read-back is the requested state, not a discarded member.")]
	public void TemplateElementsFromOperations_ShouldDropAnElementSwitchedBackToCustomLaterInTheBatch() {
		// Arrange
		const string operations = """
			[{"op":"setElement","elementName":"A","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"A","elementUpdate":{"email":{"body":"<p>custom</p>"}}},
			 {"op":"setElement","elementName":"B","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"B","elementUpdate":{"email":{"messageSource":"custom"}}},
			 {"op":"setElement","elementName":"C","elementUpdate":{"email":{"body":"<p>x</p>"}}},
			 {"op":"setElement","elementName":"C","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"C","elementUpdate":{"email":{"template":"Welcome","subject":"again"}}}]
			""";

		// Act
		IReadOnlyList<string> expected = EmailBlockExpectation.TemplateElementsFromOperations(operations);

		// Assert
		expected.Should().BeEquivalentTo(["C"],
			because: "A and B end the batch as custom-message elements (a body, then an explicit custom mode), so a "
				+ "missing template is what was asked for; C ends as a template element and is listed ONCE even though "
				+ "two operations named its template");
	}

	#endregion

	#region Methods: Attachments

	private static DescribedEmailAttachment Row(string slot, string? sourceElement = null,
			string? sourceElementParameter = null, string? processParameter = null) =>
		new() {
			Slot = slot, SourceElement = sourceElement, SourceElementParameter = sourceElementParameter,
			ProcessParameter = processParameter
		};

	private static DescribeProcessResult WithAttachments(string element, params DescribedEmailAttachment[] rows) =>
		new() {
			Elements = [
				new DescribedElement {
					Name = element,
					Email = new DescribedEmail { Subject = "s", Attachments = rows.Length == 0 ? null : [.. rows] }
				}
			]
		};

	private static EmailBlockExpectation.SentAttachments SentParameters(string element, int count) =>
		new(element, [.. Enumerable.Range(1, count).Select(index =>
			new EmailBlockExpectation.AttachmentSource($"P{index}", null, null, null))]);

	private static DescribedEmailAttachment[] ParameterRows(int count) =>
		[.. Enumerable.Range(1, count).Select(index => Row($"Attachments{index}", processParameter: $"P{index}"))];

	[Test]
	[Description("The attachments lists a payload sends are detected on every route the block travels - a create element, an addElement element and a setElement update - with their entries in the write shape; an email block without the member is not tracked (an absent list leaves the attachments untouched), and an empty list is tracked as zero entries (it removes every attachment).")]
	public void AttachmentElements_ShouldBeDetected_OnEveryRoute() {
		// Arrange
		const string descriptor = """
			{"name":"UsrP","elements":[
			  {"name":"SE1","type":"sendEmail","email":{"subject":"s","attachments":[
			    {"sourceElement":"OF1","sourceElementParameter":"ObjectFiles"},{"processParameter":"Doc"}]}},
			  {"name":"SE2","type":"sendEmail","email":{"subject":"no list"}},
			  {"name":"SE3","type":"sendEmail","email":{"subject":"s","attachments":[]}}]}
			""";
		const string operations = """
			[{"op":"addElement","element":{"name":"SE4","type":"sendEmail","email":{"attachments":[{"expression":"[#Docs#]"}]}}},
			 {"op":"setElement","elementName":"SE1","elementUpdate":{"email":{"attachments":[{"processParameter":"Docs"}]}}},
			 {"op":"setElement","elementName":"SE2","elementUpdate":{"email":{"subject":"untouched list"}}},
			 {"op":"setElement","elementName":"SE3","elementUpdate":{"email":{"attachments":[]}}}]
			""";

		// Act
		IReadOnlyList<EmailBlockExpectation.SentAttachments> built = EmailBlockExpectation.AttachmentElements(descriptor);
		IReadOnlyList<EmailBlockExpectation.SentAttachments> edited =
			EmailBlockExpectation.AttachmentElementsFromOperations(operations);

		// Assert
		built.Should().HaveCount(2, because: "SE2 sent no list, so there is nothing about its attachments to verify");
		built[0].Element.Should().Be("SE1", because: "the build list is keyed by the element that sent it");
		built[0].Entries.Should().Equal(
			[new EmailBlockExpectation.AttachmentSource(null, "OF1", "ObjectFiles", null),
				new EmailBlockExpectation.AttachmentSource("Doc", null, null, null)],
			because: "each entry keeps its source in the write shape, the key a described row is compared on");
		built[1].Entries.Should().BeEmpty(because: "an empty list is tracked: it asks for every attachment to go");
		edited.Select(list => list.Element).Should().Equal(["SE4", "SE1", "SE3"],
			because: "addElement and setElement both carry the block, and a setElement without the member is not tracked");
		edited[0].Entries.Should().ContainSingle(because: "the addElement element's list is read from element.email")
			.Which.Expression.Should().Be("[#Docs#]", because: "an expression entry keeps its formula as the source");
		edited[2].Entries.Should().BeEmpty(
			because: "an empty setElement list removes every attachment and is tracked as zero");
	}

	[Test]
	[Description("A later setElement in the same batch replaces the attachments expectation for its element, as the server's replace rule does: only the last list sent decides what the element ends with.")]
	public void AttachmentElementsFromOperations_ShouldKeepTheLastListSentForAnElement() {
		// Arrange
		const string operations = """
			[{"op":"setElement","elementName":"SE1","elementUpdate":{"email":{"attachments":[{"processParameter":"A"},{"processParameter":"B"}]}}},
			 {"op":"setElement","elementName":"se1","elementUpdate":{"email":{"attachments":[{"processParameter":"C"}]}}}]
			""";

		// Act
		IReadOnlyList<EmailBlockExpectation.SentAttachments> sent =
			EmailBlockExpectation.AttachmentElementsFromOperations(operations);

		// Assert
		sent.Should().ContainSingle(because: "one element has one final list, whatever case the caller named it in")
			.Which.Entries.Should().ContainSingle(because: "the second list replaced the first")
			.Which.ProcessParameter.Should().Be("C", because: "the expectation is the list sent LAST");
	}

	[TestCase(2, 1)]
	[TestCase(2, 0)]
	[Description("An element that was sent N attachments and reads back fewer - or none at all, which is what a server without the member answers while reporting success - is reported with both counts, so the caller learns the email goes out without those files.")]
	public void UnlandedAttachments_ShouldReportAnElementDescribedWithFewerEntries(int sentCount, int describedCount) {
		// Arrange
		EmailBlockExpectation.SentAttachments sent = SentParameters("SE1", sentCount);
		DescribeProcessResult described = WithAttachments("SE1", ParameterRows(describedCount));

		// Act
		IReadOnlyList<EmailBlockExpectation.UnlandedAttachments> unlanded =
			EmailBlockExpectation.Unlanded(described, [sent]);

		// Assert
		unlanded.Should().ContainSingle(because: "the read-back shows fewer attachment rows than the list sent")
			.Which.Should().Be(new EmailBlockExpectation.UnlandedAttachments("SE1", sentCount, describedCount),
				because: "the caller needs the element and both counts to see how much did not land");
	}

	[TestCase(2, 2)]
	[TestCase(0, 0)]
	[TestCase(0, 2)]
	[Description("No false warning: every sent entry landed, an empty list read back as no attachments, or an element absent from the read-back or without an email block (the block-landed check's finding) leave this check quiet.")]
	public void UnlandedAttachments_ShouldStayQuiet_WhenEveryEntryLanded(int sentCount, int describedCount) {
		// Arrange
		EmailBlockExpectation.SentAttachments sent = SentParameters("SE1", sentCount);
		DescribeProcessResult described = WithAttachments("SE1", ParameterRows(describedCount));
		described.Elements.Add(new DescribedElement { Name = "NoBlock", Email = null });

		// Act
		IReadOnlyList<EmailBlockExpectation.UnlandedAttachments> unlanded = EmailBlockExpectation.Unlanded(described,
			[sent, SentParameters("NoBlock", 1), SentParameters("Unknown", 1)]);

		// Assert
		unlanded.Should().BeEmpty(
			because: "a list that landed in full, an empty list read back as null, a missing block and an unknown element are no evidence of a dropped list");
	}

	[Test]
	[Description("The not-landed warning names the element and both counts, states the consequence (the email goes out without the files) and the fix (a package that supports attachments, then send the list again), carries no package version - a version in a caller-facing message is advice the reader has already satisfied - and is null when every list landed.")]
	public void BuildAttachmentWarning_ShouldStateTheConsequenceAndTheFix_WithoutAVersion() {
		// Arrange
		EmailBlockExpectation.UnlandedAttachments unlanded = new("SE1", 3, 0);

		// Act
		string? warning = EmailBlockExpectation.BuildAttachmentWarning([unlanded]);
		string? none = EmailBlockExpectation.BuildAttachmentWarning([]);

		// Assert
		warning.Should().Contain("'SE1' was sent 3 and reports 0",
			because: "the caller must know which element lost attachments and how many");
		warning.Should().Contain("WITHOUT those files", because: "the consequence is an email missing its files");
		warning.Should().Contain("install-process-builder",
			because: "the remedy is a package that supports attachments");
		warning.Should().Contain("send the list again", because: "installing alone does not restore the list");
		warning.Should().NotMatchRegex(@"\d+\.\d+\.\d+",
			because: "no package version belongs in a caller-facing remedy");
		none.Should().BeNull(because: "no warning is emitted when every list landed");
	}

	[TestCase(1)]
	[TestCase(3)]
	[Description("The replace rule's removals are counted from the outcome: rows the element carried before the edit whose source it no longer carries afterwards. One removed and three removed are each reported with their count in qualitative wording without a version; a list that removed nothing reports no line.")]
	public void RemovedAttachments_ShouldSayHowManyTheReplaceRuleRemoved(int removedCount) {
		// Arrange
		DescribedEmailAttachment kept = Row("Attachments1", "OF1", "ObjectFiles");
		DescribeProcessResult before = WithAttachments("SE1",
			[kept, .. Enumerable.Range(2, removedCount).Select(index => Row($"Attachments{index}", processParameter: $"P{index}"))]);
		DescribeProcessResult after = WithAttachments("SE1", kept);
		EmailBlockExpectation.SentAttachments sent = new("SE1",
			[new EmailBlockExpectation.AttachmentSource(null, "OF1", "ObjectFiles", null)]);
		string expectedCount = removedCount == 1
			? "1 attachment was removed from 'SE1'"
			: $"{removedCount} attachments were removed from 'SE1'";

		// Act
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed =
			EmailBlockExpectation.Removed(before, after, [sent]);
		string? line = EmailBlockExpectation.BuildRemovedAttachmentsWarning(removed);
		string? none = EmailBlockExpectation.BuildRemovedAttachmentsWarning(
			EmailBlockExpectation.Removed(before, before, [sent]));

		// Assert
		removed.Should().ContainSingle(because: "one element lost rows to the replace rule")
			.Which.Count.Should().Be(removedCount,
				because: "every row whose source the list no longer names is counted");
		line.Should().Contain(expectedCount, because: "the line says how many rows went and from which element");
		line.Should().Contain("by the replace rule", because: "the line names the rule that removed them");
		line.Should().NotMatchRegex(@"\d+\.\d+\.\d+", because: "no package version belongs in a caller-facing line");
		none.Should().BeNull(because: "an edit that removed no row reports no removal line");
	}

	[Test]
	[Description("The removal count stays quiet where it has no evidence: no before-read (create, or a failed read), an element the before-read does not show (added by this batch), and a row the server rebuilt in its own shape for a source the list still names, which is replaced rather than removed.")]
	public void RemovedAttachments_ShouldStayQuiet_WithoutEvidenceOfARemoval() {
		// Arrange
		DescribeProcessResult before = WithAttachments("SE1", Row("Attachments1", "OF1", "ObjectFiles"));
		DescribeProcessResult after = WithAttachments("SE1", Row("Attachments2", "OF1", "ObjectFiles"));
		EmailBlockExpectation.SentAttachments sent = new("SE1",
			[new EmailBlockExpectation.AttachmentSource(null, "OF1", "ObjectFiles", null)]);
		EmailBlockExpectation.SentAttachments added = new("SE9", []);

		// Act
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> noBefore =
			EmailBlockExpectation.Removed(null, after, [sent]);
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> rebuilt =
			EmailBlockExpectation.Removed(before, after, [sent, added]);

		// Assert
		noBefore.Should().BeEmpty(because: "without a before-read nothing can be said about what the edit removed");
		rebuilt.Should().BeEmpty(
			because: "a row rebuilt under a new slot for the same source is not a removal, and an element absent before the edit had nothing to lose");
	}

	[Test]
	[Description("Two rows of one source listed out of slot order, of which the edit kept one: the removed slot is the one the read-back no longer shows, not the first row describe listed, because a row whose slot and source both survive is matched before the rest is matched by source.")]
	public void RemovedAttachments_ShouldNameTheRemovedSlot_WhenTwoRowsShareASourceOutOfSlotOrder() {
		// Arrange
		DescribeProcessResult before = WithAttachments("SE1", Row("Attachments2", processParameter: "Doc"),
			Row("Attachments1", processParameter: "Doc"));
		DescribeProcessResult after = WithAttachments("SE1", Row("Attachments1", processParameter: "Doc"));
		EmailBlockExpectation.SentAttachments sent = new("SE1",
			[new EmailBlockExpectation.AttachmentSource("Doc", null, null, null)]);

		// Act
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed =
			EmailBlockExpectation.Removed(before, after, [sent]);
		string? warning = EmailBlockExpectation.BuildRemovedAttachmentsWarning(removed);

		// Assert
		removed.Should().ContainSingle(because: "the element lost one of its two rows")
			.Which.Rows.Should().ContainSingle(because: "exactly one row went")
			.Which.Slot.Should().Be("Attachments2",
				because: "Attachments1 is still in the read-back, so the row that went is Attachments2");
		warning.Should().Contain("Attachments2 {\"processParameter\":\"Doc\"}",
			because: "the warning names the slot that was actually removed");
		warning.Should().NotContain("Attachments1 {",
			because: "the slot that survived must not be reported as removed");
	}

	[Test]
	[Description("The replace rule's warning prints each removed row's slot and source in the write shape, because describe no longer shows a removed row, and points at that printed entry to restore it; a row describe could not name a source for, before the edit as after it, is sent to the designer to rebuild instead. It does not claim the server's notices always name the entry that restores it, which is false for a row describe cannot decode.")]
	public void BuildRemovedAttachmentsWarning_ShouldPrintTheRemovedEntries_InTheWriteShape() {
		// Arrange
		DescribedEmailAttachment kept = Row("Attachments1", "OF1", "ObjectFiles");
		DescribeProcessResult before = WithAttachments("SE1", kept, Row("Attachments2", processParameter: "Docs"),
			Row("Attachments3"));
		DescribeProcessResult after = WithAttachments("SE1", kept);
		EmailBlockExpectation.SentAttachments sent = new("SE1",
			[new EmailBlockExpectation.AttachmentSource(null, "OF1", "ObjectFiles", null)]);

		// Act
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed =
			EmailBlockExpectation.Removed(before, after, [sent]);
		string? warning = EmailBlockExpectation.BuildRemovedAttachmentsWarning(removed);
		string? note = EmailBlockExpectation.BuildClearedAttachmentsNote(removed);

		// Assert
		warning.Should().Contain("2 attachments were removed from 'SE1'",
			because: "the warning says how many rows the replace rule took and from which element");
		warning.Should().Contain("Attachments2 {\"processParameter\":\"Docs\"}",
			because: "the removed entry is printed in the write shape, so the caller can send it back");
		warning.Should().Contain("Attachments3 with no source describe could name",
			because: "a row describe could not decode is named by its slot instead of as an empty entry");
		warning.Should().Contain("send the list again with the removed entry added as printed here",
			because: "after the edit describe no longer shows the removed row, so the printed entry is what restores it");
		warning.Should().Contain("rebuild it in the designer",
			because: "a row with no source was not named by the describe taken before the edit either, so only the designer can restore it");
		warning.Should().NotContain("describe taken before this edit",
			because: "that describe showed no source for the row, so pointing at it would restore nothing");
		warning.Should().NotContain("notices name",
			because: "the server's notices do not name a restoring entry for every row, so the warning must not promise it");
		note.Should().BeNull(because: "a non-empty list did not ask for every attachment to go");
	}

	[Test]
	[Description("An empty attachments list removes every row because the caller asked for exactly that, so its outcome is an informative note naming what went - not the replace rule's warning, which would tell the caller it lost something.")]
	public void BuildClearedAttachmentsNote_ShouldReportAnAskedForRemoval_WithoutAWarning() {
		// Arrange
		DescribeProcessResult before = WithAttachments("SE1", Row("Attachments1", "OF1", "ObjectFiles"),
			Row("Attachments2", processParameter: "Doc"));
		DescribeProcessResult after = WithAttachments("SE1");
		EmailBlockExpectation.SentAttachments cleared = new("SE1", []);

		// Act
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed =
			EmailBlockExpectation.Removed(before, after, [cleared]);
		string? warning = EmailBlockExpectation.BuildRemovedAttachmentsWarning(removed);
		string? note = EmailBlockExpectation.BuildClearedAttachmentsNote(removed);

		// Assert
		removed.Should().ContainSingle(because: "the element lost both rows")
			.Which.Requested.Should().BeTrue(because: "an empty list asks for every attachment to go");
		warning.Should().BeNull(because: "a removal the caller asked for is not a surprise to warn about");
		note.Should().Contain("removed every attachment", because: "the note says what the empty list did");
		note.Should().Contain("'SE1' (2: Attachments1 {\"sourceElement\":\"OF1\",\"sourceElementParameter\":\"ObjectFiles\"}",
			because: "the note names what went, so a list emptied by mistake can still be restored");
		note.Should().Contain("as asked", because: "the note confirms the outcome rather than reporting a loss");
		note.Should().NotContain("not intended", because: "the note does not suggest the removal was a mistake");
	}

	[Test]
	[Description("A removed expression row is printed readably - an apostrophe, angle brackets and a plus sign appear as themselves rather than as HTML-safe escapes - and the printed entry still parses back to the same expression, so the caller can send it as it reads.")]
	public void BuildRemovedAttachmentsWarning_ShouldPrintAnExpressionReadably_AndParseBackToTheSameValue() {
		// Arrange
		const string expression = "[#Docs#] + '<draft>'";
		DescribedEmailAttachment kept = Row("Attachments1", "OF1", "ObjectFiles");
		DescribeProcessResult before = WithAttachments("SE1", kept,
			new DescribedEmailAttachment { Slot = "Attachments2", Expression = expression });
		DescribeProcessResult after = WithAttachments("SE1", kept);
		EmailBlockExpectation.SentAttachments sent = new("SE1",
			[new EmailBlockExpectation.AttachmentSource(null, "OF1", "ObjectFiles", null)]);

		// Act
		string? warning = EmailBlockExpectation.BuildRemovedAttachmentsWarning(
			EmailBlockExpectation.Removed(before, after, [sent]));

		// Assert
		warning.Should().Contain("Attachments2 {\"expression\":\"[#Docs#] + '<draft>'\"}",
			because: "the printed entry is meant to be read and copied back, so its characters must appear as themselves");
		int start = warning!.IndexOf("{\"expression\"", StringComparison.Ordinal);
		string printed = warning[start..(warning.IndexOf('}', start) + 1)];
		JsonNode.Parse(printed)!["expression"]!.GetValue<string>().Should().Be(expression,
			because: "the readable form is still JSON, so the entry parses back to the expression that was removed");
	}

	[Test]
	[Description("An empty attachments list sent to each of two Send email elements is reported in one note whose wording is plural, naming both elements and what each lost.")]
	public void BuildClearedAttachmentsNote_ShouldSpeakOfLists_WhenSeveralElementsWereCleared() {
		// Arrange
		DescribeProcessResult before = new() {
			Elements = [
				.. WithAttachments("SE1", Row("Attachments1", processParameter: "Docs")).Elements,
				.. WithAttachments("SE2", Row("Attachments1", processParameter: "Doc")).Elements
			]
		};
		DescribeProcessResult after = new() {
			Elements = [.. WithAttachments("SE1").Elements, .. WithAttachments("SE2").Elements]
		};

		// Act
		string? note = EmailBlockExpectation.BuildClearedAttachmentsNote(EmailBlockExpectation.Removed(before, after,
			[new EmailBlockExpectation.SentAttachments("SE1", []), new EmailBlockExpectation.SentAttachments("SE2", [])]));

		// Assert
		note.Should().StartWith("The empty attachments lists removed every attachment of the Send email elements",
			because: "two empty lists were sent, one to each element, so the note speaks of lists and elements");
		note.Should().Contain("'SE1' (1: Attachments1 {\"processParameter\":\"Docs\"})",
			because: "the note names what the first element lost");
		note.Should().Contain("'SE2' (1: Attachments1 {\"processParameter\":\"Doc\"})",
			because: "the note names what the second element lost");
	}

	[Test]
	[Description("One batch that names a Send email element by its name and then by its UId is tracked as two lists, because only the read-back can tell they are one element; the read-back checks collapse them on the element it resolves and judge only the list sent last, so the earlier list neither raises a not-landed warning nor counts the removal twice.")]
	public void AttachmentChecks_ShouldJudgeOneElementOnce_WhenTheBatchNamesItByNameAndByUId() {
		// Arrange
		const string uid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55";
		const string operations = """
			[{"op":"setElement","elementName":"SE1","elementUpdate":{"email":{"attachments":[{"processParameter":"A"},{"processParameter":"B"}]}}},
			 {"op":"setElement","elementName":"5c58c4c4-134b-4744-9c67-96d9c69c9d55","elementUpdate":{"email":{"attachments":[{"processParameter":"C"}]}}}]
			""";
		DescribeProcessResult before = WithAttachments("SE1", Row("Attachments1", processParameter: "Old"));
		DescribeProcessResult after = WithAttachments("SE1", Row("Attachments1", processParameter: "C"));
		before.Elements[0].Uid = uid;
		after.Elements[0].Uid = uid;

		// Act
		IReadOnlyList<EmailBlockExpectation.SentAttachments> sent =
			EmailBlockExpectation.AttachmentElementsFromOperations(operations);
		IReadOnlyList<EmailBlockExpectation.UnlandedAttachments> unlanded = EmailBlockExpectation.Unlanded(after, sent);
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed =
			EmailBlockExpectation.Removed(before, after, sent);

		// Assert
		sent.Select(list => list.Element).Should().Equal(["SE1", uid],
			because: "the payload alone cannot tell that the name and the UId are one element");
		unlanded.Should().BeEmpty(
			because: "the element ends with the one entry of the list sent last, which landed; the earlier two-entry list is not judged");
		removed.Should().ContainSingle(because: "one element lost one row, however many ways the batch named it")
			.Which.Count.Should().Be(1, because: "the removed row is counted once");
	}

	[Test]
	[Description("A list sent to an element that the same batch then removes and adds again is forgotten: the element the batch ends with is a new one carrying only what its own addElement sends, so an addElement without a list leaves nothing to verify and the earlier two-entry list raises no fewer-landed warning against a fresh element with no rows.")]
	public void AttachmentElementsFromOperations_ShouldForgetTheList_WhenTheElementIsRemovedAndAddedAgain() {
		// Arrange
		const string operations = """
			[{"op":"setElement","elementName":"SE1","elementUpdate":{"email":{"attachments":[{"processParameter":"A"},{"processParameter":"B"}]}}},
			 {"op":"removeElement","elementName":"SE1"},
			 {"op":"addElement","element":{"name":"SE1","type":"sendEmail","email":{"subject":"fresh"}}}]
			""";
		DescribeProcessResult after = WithAttachments("SE1");

		// Act
		IReadOnlyList<EmailBlockExpectation.SentAttachments> sent =
			EmailBlockExpectation.AttachmentElementsFromOperations(operations);
		string? warning = EmailBlockExpectation.BuildAttachmentWarning(EmailBlockExpectation.Unlanded(after, sent));

		// Assert
		sent.Should().BeEmpty(because: "the list was sent to the element the batch removed, not to the one it added");
		warning.Should().BeNull(because: "a fresh element that was sent no list has no attachments to miss");
	}

	[Test]
	[Description("A batch that removes a Send email element and adds a new one under the same name WITH an attachments list reports no replace-rule removal: the before-read and the read-back resolve the name to two different UIds, so the old element's rows went with the element and are not compared with the new element's. The same lists on one surviving element still report the removal.")]
	public void Removed_ShouldReportNoRemoval_WhenTheBatchRemovesAndAddsTheElementAgainWithAList() {
		// Arrange
		const string operations = """
			[{"op":"removeElement","elementName":"SE1"},
			 {"op":"addElement","element":{"name":"SE1","type":"sendEmail","email":{"attachments":[{"processParameter":"C"}]}}}]
			""";
		DescribeProcessResult before = WithAttachments("SE1", Row("Attachments1", processParameter: "Old"),
			Row("Attachments2", processParameter: "Older"));
		DescribeProcessResult after = WithAttachments("SE1", Row("Attachments1", processParameter: "C"));
		DescribeProcessResult sameElementAfter = WithAttachments("SE1", Row("Attachments1", processParameter: "C"));
		before.Elements[0].Uid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55";
		after.Elements[0].Uid = "0f1d2c3b-4a59-4687-9e0d-1c2b3a495867";
		sameElementAfter.Elements[0].Uid = "5C58C4C4-134B-4744-9C67-96D9C69C9D55";

		// Act
		IReadOnlyList<EmailBlockExpectation.SentAttachments> sent =
			EmailBlockExpectation.AttachmentElementsFromOperations(operations);
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> replaced =
			EmailBlockExpectation.Removed(before, after, sent);
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> surviving =
			EmailBlockExpectation.Removed(before, sameElementAfter, sent);

		// Assert
		sent.Should().ContainSingle(because: "the addElement sends the one list the new element carries")
			.Which.Entries.Should().HaveCount(1, because: "that list has one entry");
		replaced.Should().BeEmpty(
			because: "the element the batch ends with is a different element, so the old element's rows were not removed by the replace rule");
		EmailBlockExpectation.BuildRemovedAttachmentsWarning(replaced).Should().BeNull(
			because: "no replace-rule removal line is emitted for rows that left with the removed element");
		surviving.Should().ContainSingle(
				because: "when the read-back reports the same UId, in any casing, the element survived and lost its two rows")
			.Which.Count.Should().Be(2, because: "both rows the list did not name are counted");
	}

	[TestCase("removeElement", 0)]
	[TestCase("addElement", 1)]
	[Description("A removeElement forgets the list sent earlier for that element, and an addElement of the same name replaces it with the list its own element carries: after removeElement nothing is tracked, after an addElement that sends one entry exactly that entry is.")]
	public void AttachmentElementsFromOperations_ShouldResetTheList_OnRemoveOrAddOfTheSameName(string op,
			int expectedEntries) {
		// Arrange
		string second = op == "removeElement"
			? """{"op":"removeElement","elementName":"se1"}"""
			: """{"op":"addElement","element":{"name":"se1","type":"sendEmail","email":{"attachments":[{"processParameter":"C"}]}}}""";
		string operations = "[" + """
			{"op":"setElement","elementName":"SE1","elementUpdate":{"email":{"attachments":[{"processParameter":"A"},{"processParameter":"B"}]}}}
			""" + "," + second + "]";

		// Act
		IReadOnlyList<EmailBlockExpectation.SentAttachments> sent =
			EmailBlockExpectation.AttachmentElementsFromOperations(operations);

		// Assert
		sent.Sum(list => list.Entries.Count).Should().Be(expectedEntries,
			because: "the earlier two-entry list belongs to an element the batch removed or replaced");
		sent.Should().NotContain(list => list.Entries.Any(entry => entry.ProcessParameter == "A"),
			because: "the list sent before the remove or the re-add is not what the element ends with");
	}

	[Test]
	[Description("A removal the server's reply already reported - its notice starts \"Send email '<name>': removed attachment '\" - is left out of clio's own removal reporting, matched on the element's described name even when the caller named the element by its UId; an element the reply is silent about stays in.")]
	public void NotReportedByServer_ShouldLeaveOutTheElementsTheServerReported() {
		// Arrange
		const string uid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55";
		DescribeProcessResult described = new() {
			Elements = [.. WithAttachments("SE1").Elements, .. WithAttachments("SE2").Elements]
		};
		described.Elements[0].Uid = uid;
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed = [
			new(uid, [Row("Attachments2", processParameter: "Docs")], Requested: false),
			new("SE2", [Row("Attachments1", processParameter: "Doc")], Requested: true)
		];
		IReadOnlyList<string> serverWarnings = [
			"Send email 'SE1': removed attachment 'Attachments2' (it read 'Docs'): the sent list no longer names it.",
			"Send email 'SE2': Attachments1 attaches the single file in 'Doc'."
		];

		// Act
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> left =
			EmailBlockExpectation.NotReportedByServer(removed, described, serverWarnings);
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> unfiltered =
			EmailBlockExpectation.NotReportedByServer(removed, described, null);

		// Assert
		left.Should().ContainSingle(because: "the reply reported SE1's removal and nothing about SE2's")
			.Which.Element.Should().Be("SE2",
				because: "a notice about SE2 that is not a removal notice does not stand in for one");
		unfiltered.Should().BeEquivalentTo(removed,
			because: "a reply with no warnings reported no removal, so clio reports every one it found");
	}

	[Test]
	[Description("The attachment read-back warnings carry their public markers, so an e2e assertion on a marker fails to compile rather than passing forever when the wording moves.")]
	public void AttachmentWarnings_ShouldCarryTheirMarkers() {
		// Arrange
		IReadOnlyList<EmailBlockExpectation.RemovedAttachments> removed = [
			new("SE1", [Row("Attachments2", processParameter: "Docs")], Requested: false),
			new("SE2", [Row("Attachments1", processParameter: "Doc")], Requested: true)
		];

		// Act
		string? unlanded = EmailBlockExpectation.BuildAttachmentWarning([new("SE1", 2, 0)]);
		string? replaced = EmailBlockExpectation.BuildRemovedAttachmentsWarning(removed);
		string? cleared = EmailBlockExpectation.BuildClearedAttachmentsNote(removed);

		// Assert
		unlanded.Should().Contain(EmailBlockExpectation.UnlandedAttachmentsWarningMarker,
			because: "the fewer-landed warning is built from its marker");
		replaced.Should().Contain(EmailBlockExpectation.RemovedAttachmentsWarningMarker,
			because: "the replace-rule warning is built from its marker");
		cleared.Should().Contain(EmailBlockExpectation.ClearedAttachmentsNoteMarker,
			because: "the empty-list note is built from its marker");
	}

	#endregion

}
