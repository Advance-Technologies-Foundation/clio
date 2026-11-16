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

	[Test]
	[Description("One case read: exit 0, an info verdict naming the case, its stage count and whether it is enabled, then the result document.")]
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
		Verdict().Should().Be("Dynamic case 'UsrOrderCase' read: 2 stages, disabled; nothing was changed.",
			because: "the verdict names the case and its state");
		JsonNode.Parse(_printed[1].Text)!["cases"]!.AsArray().Should().ContainSingle(
			because: "the result document follows the verdict");
		_printed.Should().Contain(("info", "note"), because: "notes are relayed to the user");
		_printed.Should().Contain(("warning", "warning"), because: "warnings must not be missed");
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
		Verdict().Should().Be("Refused [case-ambiguous]: UsrOrder has 2 cases.", because: "the code and reason are stated");
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
