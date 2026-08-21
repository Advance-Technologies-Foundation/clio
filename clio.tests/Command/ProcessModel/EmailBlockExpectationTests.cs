using System.Collections.Generic;
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
[Category("Unit")]
public class EmailBlockExpectationTests {

	#region Methods: FromDescriptor

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
		EmailBlockExpectation.FromDescriptor("not json at all").Should().BeEmpty();
		EmailBlockExpectation.FromDescriptor("").Should().BeEmpty();
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
		EmailBlockExpectation.FromOperations("""{"op":"setElement"}""").Should().BeEmpty();
		EmailBlockExpectation.FromOperations("broken").Should().BeEmpty();
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
		warning.Should().NotBeNull();
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
	}

	[Test]
	[Description("No dropped elements produces no warning, so a healthy environment stays silent instead of emitting a reassuring non-message on every build.")]
	public void BuildWarning_ShouldReturnNull_WhenNothingWasDropped() {
		// Arrange, Act, Assert
		EmailBlockExpectation.BuildWarning([]).Should().BeNull();
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
		warning.Should().NotBeNull();
		warning.Should().Contain("ParamMail", because: "the caller has to know which element's body did not resolve");
		warning.Should().Contain("did NOT resolve",
			because: "the observation is the point: the placeholders were stored, not resolved");
		warning.Should().Contain("install-process-builder",
			because: "a warning without the remedy leaves the caller stuck");
		EmailBlockExpectation.BuildMacroWarning([]).Should().BeNull(
			because: "a healthy build must stay silent rather than emit a reassuring non-message");
	}

	#endregion

}
