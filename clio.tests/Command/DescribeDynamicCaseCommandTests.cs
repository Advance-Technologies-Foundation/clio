using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Pins the reporting of <see cref="DescribeDynamicCaseCommand"/>: the exit code, whether the verdict is an error, the
/// verdict's wording and what is printed besides it.
/// </summary>
/// <remarks>
/// On <see cref="BaseClioModuleTests"/> rather than <c>BaseCommandTests</c>: the options type is MCP-only and has no
/// verb, so the README check that base class adds cannot apply to it.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
public sealed class DescribeDynamicCaseCommandTests : BaseClioModuleTests {

	private IDynamicCaseService _service;
	private ILogger _logger;
	private DescribeDynamicCaseCommand _command;
	// The verdict of a refusal points at the document, which carries the message once.
	private const string ErrorMessagePointer = "; the result's errorMessage says why and what to do next.";

	private readonly List<(string Kind, string Text)> _printed = [];

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IDynamicCaseService>();
		_logger = Substitute.For<ILogger>();
		_logger.When(logger => logger.WriteInfo(Arg.Any<string>())).Do(call => _printed.Add(("info", call.Arg<string>())));
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => _printed.Add(("warning", call.Arg<string>())));
		_logger.When(logger => logger.WriteError(Arg.Any<string>())).Do(call => _printed.Add(("error", call.Arg<string>())));
		containerBuilder.AddTransient(_ => _service);
		containerBuilder.AddTransient(_ => _logger);
	}

	public override void Setup() {
		_printed.Clear();
		base.Setup();
		_command = Container.GetRequiredService<DescribeDynamicCaseCommand>();
	}

	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static DescribeDynamicCaseOptions Options() => new() { Environment = "sandbox", EntitySchemaName = "UsrOrder" };

	private static DynamicCaseInfo Case(string name, bool enabled) => new() {
		SchemaName = name, Enabled = enabled, Status = "existing",
		Stages = [new DynamicCaseStage { Name = "Stage1" }, new DynamicCaseStage { Name = "Stage2" }]
	};

	private string Verdict() => _printed.First().Text;

	private string VerdictKind() => _printed.First().Kind;

	private int Run(DescribeDynamicCaseResult result) {
		_service.Describe(Arg.Any<DescribeDynamicCaseOptions>()).Returns(result);
		return _command.Execute(Options());
	}

	[TestCase(false, true, "Dynamic case 'UsrOrderCaseCustom1' read: 2 stages, an inactive version of an enabled family; "
		+ "nothing was changed.")]
	[TestCase(false, false, "Dynamic case 'UsrOrderCaseCustom1' read: 2 stages, an inactive version of a disabled "
		+ "family; nothing was changed.")]
	[TestCase(true, true, "Dynamic case 'UsrOrderCaseCustom1' read: 2 stages, enabled; nothing was changed.")]
	[Description("A case read by the name of an inactive member says it is an inactive version, with the family's enabled flag as the family's; the active member reads as before.")]
	public void Verdict_ShouldNameAnInactiveVersion_WhenTheMemberIsNotActive(bool active, bool enabled,
		string expected) {
		// Arrange
		DynamicCaseInfo read = Case("UsrOrderCaseCustom1", enabled);
		DescribeDynamicCaseResult result = new() {
			Success = true,
			Cases = [new DynamicCaseInfo {
				SchemaName = read.SchemaName, Enabled = read.Enabled, Status = read.Status, Stages = read.Stages,
				ActiveVersion = active,
				Versions = [
					new DynamicCaseVersionMember { Name = "UsrOrderCase", Active = !active },
					new DynamicCaseVersionMember { Name = "UsrOrderCaseCustom1", Active = active }
				]
			}],
			Notes = [], Warnings = []
		};

		// Act
		string verdict = DescribeDynamicCaseCommand.Verdict(result);

		// Assert
		verdict.Should().Be(expected, because: "enabled is the family's flag, so an inactive member must not read as "
			+ "the case in force");
	}

	[Test]
	[Description("A case without versions is never called inactive, even when the answer carried no activeVersion: only a family has inactive members.")]
	public void Verdict_ShouldNotCallACaseInactive_WhenItListsNoVersions() {
		// Arrange
		DescribeDynamicCaseResult result = new() {
			Success = true, Cases = [Case("UsrOrderCase", enabled: true)], Notes = [], Warnings = []
		};

		// Act
		string verdict = DescribeDynamicCaseCommand.Verdict(result);

		// Assert
		verdict.Should().Be("Dynamic case 'UsrOrderCase' read: 2 stages, enabled; nothing was changed.",
			because: "a missing activeVersion reads as false, which alone must not turn a case into an inactive version");
	}

	[Test]
	[Description("One case read: exit 0, an info verdict naming the case, its stage count and whether it is enabled, then the result document that alone carries the notes and warnings; the verdict counts the warnings.")]
	public void Execute_ShouldReportTheCase_WhenOneCaseIsRead() {
		// Arrange
		DescribeDynamicCaseResult result = new() {
			Success = true, Cases = [Case("UsrOrderCase", enabled: false)], Notes = ["note"], Warnings = ["warning"]
		};

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "the case was read");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Be("Dynamic case 'UsrOrderCase' read: 2 stages, disabled; nothing was changed. 1 warning in "
			+ "the result: show it to the user.", because: "the verdict names the case and its state, and that a "
			+ "warning waits in the document");
		JsonNode.Parse(_printed[1].Text)!["cases"]!.AsArray().Should().ContainSingle(
			because: "the result document follows the verdict");
		_printed.Should().HaveCount(2,
			because: "the verdict and the document only; a second copy of each note costs the agent tokens");
		JsonNode.Parse(_printed[1].Text)!["notes"]!.AsArray().Select(node => node!.GetValue<string>()).Should()
			.Contain("note", because: "the document is the one copy the agent relays");
		JsonNode.Parse(_printed[1].Text)!["warnings"]!.AsArray().Select(node => node!.GetValue<string>()).Should()
			.Contain("warning", because: "warnings must not be missed");
		_printed.Should().NotContain(entry => entry.Kind == "warning",
			because: "a warning is not repeated as its own log line");
	}

	[TestCase(0, "")]
	[TestCase(2, " 2 warnings in the result: show them to the user.")]
	[Description("The describe verdict counts the result's non-blank warnings and asks to show them; a read names no write, and with no warning it adds nothing.")]
	public void Execute_ShouldCountTheWarningsInTheVerdict(int count, string suffix) {
		// Arrange
		DescribeDynamicCaseResult result = new() {
			Success = true, Cases = [Case("UsrOrderCase", enabled: true)], Notes = [], Warnings = [" "]
		};
		for (int index = 1; index <= count; index++) {
			result.Warnings.Add($"Warning {index}.");
		}

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be(DescribeDynamicCaseCommand.Verdict(result) + suffix,
			because: "the verdict is the one top-level line, so it must say that warnings wait in the document");
	}

	[TestCase(0, "No dynamic case was found; nothing was changed.")]
	[TestCase(2, "2 dynamic cases read; nothing was changed.")]
	[Description("No case or several cases: exit 0 and a verdict that counts them.")]
	public void Execute_ShouldCountTheCases_WhenNoneOrSeveralAreRead(int count, string verdict) {
		// Arrange
		DescribeDynamicCaseResult result = new() {
			Success = true,
			Cases = [.. Enumerable.Range(1, count).Select(index => Case($"UsrCase{index}", enabled: true))]
		};

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "reading an object is a success even when it has no case");
		Verdict().Should().Be(verdict, because: "the verdict counts what was read");
	}

	[Test]
	[Description("A refusal: exit 1 and an error verdict carrying the code and the message.")]
	public void Execute_ShouldReportTheRefusal_WhenTheDescribeFails() {
		// Arrange
		DescribeDynamicCaseResult result = new() {
			Success = false, ErrorCode = "case-ambiguous", ErrorMessage = "UsrOrder has 2 cases.", Cases = []
		};

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "a refusal is not a success");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Be("Refused [case-ambiguous]" + ErrorMessagePointer,
			because: "the verdict names the code and points at the reason");
		_printed.Should().HaveCount(2, because: "the log holds the verdict and the result document only");
		_printed.Count(entry => entry.Text.Contains("UsrOrder has 2 cases.")).Should().Be(1,
			because: "the refusal's message is printed once, in the result document, not again in the verdict");
		JsonNode.Parse(_printed[1].Text)!["errorMessage"]!.GetValue<string>().Should().Be("UsrOrder has 2 cases.",
			because: "the document carries the reason and the remedy");
	}

	[Test]
	[Description("A missing environment is refused before the service is called.")]
	public void Execute_ShouldRefuse_WhenTheEnvironmentIsMissing() {
		// Arrange
		DescribeDynamicCaseOptions options = Options();
		options.Environment = null;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "there is no environment to read from");
		Verdict().Should().Contain("environment-name is required", because: "the missing option is named");
		_service.DidNotReceiveWithAnyArgs().Describe(default!);
	}

	[Test]
	[Description("An unexpected exception from the service is printed as an error with exit 1 rather than escaping.")]
	public void Execute_ShouldReportAnException_AsAnError() {
		// Arrange
		_service.Describe(Arg.Any<DescribeDynamicCaseOptions>()).Throws(new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the call failed");
		_printed.Should().ContainSingle(entry => entry.Kind == "error" && entry.Text == "boom",
			because: "the exception text is the error");
	}
}
