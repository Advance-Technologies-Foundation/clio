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
		ILogger logger = Substitute.For<ILogger>();
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
		logger.Received(1).WriteWarning(Arg.Is<string>(text =>
			text.Contains(EmailBlockExpectation.TemplateWarningMarker) && text.Contains("'Dropped'")));
		logger.DidNotReceive().WriteWarning(Arg.Is<string>(text =>
			text.Contains(EmailBlockExpectation.TemplateWarningMarker) && text.Contains("'Landed'")));
	}

	[Test]
	[Description("ReportDescribed does not double-report: an element whose whole email block is missing gets the block-dropped warning, not the template one as well.")]
	public void ReportDescribed_ShouldNotAlsoWarnOnTemplate_WhenTheWholeBlockIsMissing() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		BlockExpectationIntent intent = BlockExpectationIntent.FromOperations("""
			[{"op":"setElement","elementName":"Gone","elementUpdate":{"email":{"template":"Welcome"}}}]
			""");
		DescribeProcessResult described = new() {
			Elements = [new DescribedElement { Name = "Gone", Email = null }]
		};

		// Act
		BlockExpectationReporter.ReportDescribed(logger, described, intent);

		// Assert
		logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("does NOT carry the 'email' configuration")));
		logger.DidNotReceive().WriteWarning(Arg.Is<string>(text => text.Contains(EmailBlockExpectation.TemplateWarningMarker)));
	}
}
