using System.Collections.Generic;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Pins the WIRING of the template-landed check (ENG-95986): the intent carries the templated elements from
/// both payload shapes, and the reporter turns an unlanded template into exactly one warning while staying silent
/// for a landed one. The pure halves are covered in <see cref="EmailBlockExpectationTests"/>; this fixture proves
/// they are actually reached from the command path, which no pure-function test can.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class BlockExpectationIntentTemplateTests {

	/// <summary>
	/// A logger substitute that RECORDS what it was told, so the assertions below are made against the collected
	/// warnings rather than through the mock's own verification API. Both read the same behaviour; only this form
	/// is visible to the analyzers that count a test's assertions, and it reports the actual text on a failure
	/// instead of "expected 1 call, received 0".
	/// </summary>
	private static ILogger RecordingLogger(List<string> warnings) {
		ILogger logger = Substitute.For<ILogger>();
		logger.When(instance => instance.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));
		return logger;
	}

	[Test]
	[Description("The create-path intent lists the elements whose descriptor email block names a template, beside the plain email-block list.")]
	public void FromDescriptor_ShouldCarryTemplatedEmailElements() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","elements":[
				{"name":"Custom1","type":"sendEmail","email":{"body":"<p>x</p>"}},
				{"name":"Tpl1","type":"sendEmail","email":{"template":"Case closure notification"}}]}
			""";

		// Act
		BlockExpectationIntent intent = BlockExpectationIntent.FromDescriptor(descriptor);

		// Assert
		intent.TemplatedEmail.Should().BeEquivalentTo(["Tpl1"],
			because: "only the element that sent a template can have it discarded by an older server");
		intent.ConfiguredEmail.Should().BeEquivalentTo(["Custom1", "Tpl1"],
			because: "the templated list is a SUBSET of the email-block list, not a replacement for it");
		intent.IsEmpty.Should().BeFalse(because: "a payload with email blocks has something to verify");
	}

	[Test]
	[Description("The modify-path intent lists templated elements from setElement operations, and drops one that a later operation in the same batch switches back to a custom message.")]
	public void FromOperations_ShouldCarryTemplatedEmailElements_AndHonourASwitchBack() {
		// Arrange
		const string operations = """
			[{"op":"setElement","elementName":"Keep","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"Switched","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"Switched","elementUpdate":{"email":{"body":"<p>custom</p>"}}}]
			""";

		// Act
		BlockExpectationIntent intent = BlockExpectationIntent.FromOperations(operations);

		// Assert
		intent.TemplatedEmail.Should().BeEquivalentTo(["Keep"],
			because: "the element switched back to custom ends the batch without a template by contract, so its "
				+ "missing template is the requested state");
	}

	[Test]
	[Description("ReportDescribed emits the template warning for an element whose read-back lacks the template it was sent, and nothing template-related for one whose template landed.")]
	public void ReportDescribed_ShouldWarnOnUnlandedTemplate_AndStaySilentWhenItLanded() {
		// Arrange
		List<string> warnings = [];
		ILogger logger = RecordingLogger(warnings);
		BlockExpectationIntent intent = BlockExpectationIntent.FromOperations("""
			[{"op":"setElement","elementName":"Landed","elementUpdate":{"email":{"template":"Welcome"}}},
			 {"op":"setElement","elementName":"Dropped","elementUpdate":{"email":{"template":"Welcome"}}}]
			""");
		DescribeProcessResult described = new() {
			Elements = [
				new DescribedElement { Name = "Landed", Email = new DescribedEmail { MessageSource = "template", Template = "80cdb129-de99-433f-8783-9d71c205607b" } },
				new DescribedElement { Name = "Dropped", Email = new DescribedEmail { Subject = "only a subject came back" } }
			]
		};

		// Act
		BlockExpectationReporter.ReportDescribed(logger, described, intent);

		// Assert
		warnings.Should().ContainSingle(warning => warning.Contains(EmailBlockExpectation.TemplateWarningMarker),
				because: "exactly one template warning is emitted, however many elements the batch templated")
			.Which.Should().Contain("'Dropped'",
				because: "the warning must name the element whose template did not read back")
			.And.NotContain("'Landed'",
				because: "an element whose template landed is not evidence of a discarding server and must not be accused");
	}

	[Test]
	[Description("ReportDescribed does not double-report: an element whose whole email block is missing gets the block-dropped warning, not the template one as well.")]
	public void ReportDescribed_ShouldNotAlsoWarnOnTemplate_WhenTheWholeBlockIsMissing() {
		// Arrange
		List<string> warnings = [];
		ILogger logger = RecordingLogger(warnings);
		BlockExpectationIntent intent = BlockExpectationIntent.FromOperations("""
			[{"op":"setElement","elementName":"Gone","elementUpdate":{"email":{"template":"Welcome"}}}]
			""");
		DescribeProcessResult described = new() {
			Elements = [new DescribedElement { Name = "Gone", Email = null }]
		};

		// Act
		BlockExpectationReporter.ReportDescribed(logger, described, intent);

		// Assert
		warnings.Should().ContainSingle(warning => warning.Contains("does NOT carry the 'email' configuration"),
			because: "a dropped block is reported once, by the block-landed check that owns it");
		warnings.Should().NotContain(warning => warning.Contains(EmailBlockExpectation.TemplateWarningMarker),
			because: "the template check must not pile a second accusation onto an element whose whole block is already reported missing");
	}
}
