using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class ListKnowledgeExamplesCommandTests : BaseCommandTests<ListKnowledgeExamplesOptions> {
	private ListKnowledgeExamplesCommand _command = null!;
	private ILogger _logger = null!;
	private IKnowledgeReferenceExampleService _service = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeReferenceExampleService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
		services.AddTransient<ListKnowledgeExamplesCommand>();
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ListKnowledgeExamplesCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Maps every CLI discovery filter and emits complete reference metadata as JSON when requested.")]
	public void Execute_ShouldMapFiltersAndEmitJson_WhenJsonIsRequested() {
		// Arrange
		_service.List(Arg.Any<KnowledgeReferenceExampleQuery>()).Returns(new KnowledgeReferenceExampleListResult(
			true,
			[Example()],
			[]));
		ListKnowledgeExamplesOptions options = new() {
			Source = "partner",
			Search = "pub/sub",
			Capability = "atf",
			Status = "published",
			Json = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the catalog discovery completed without diagnostics");
		_service.Received(1).List(Arg.Is<KnowledgeReferenceExampleQuery>(query =>
			query.SourceAlias == "partner"
			&& query.SearchText == "pub/sub"
			&& query.Capability == "atf"
			&& query.Status == "published"));
		_logger.Received(1).WriteLine(Arg.Is<string>(value =>
			value.Contains("https://github.com/example/pubsub", StringComparison.Ordinal)
			&& value.Contains("\"sourceAlias\": \"partner\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Prints a human-readable example block and reports malformed catalog diagnostics without hiding valid entries.")]
	public void Execute_ShouldPrintExamplesAndDiagnostics_WhenHumanOutputIsRequested() {
		// Arrange
		const string diagnostic = "Catalog item 'bad' is invalid YAML.";
		_service.List(Arg.Any<KnowledgeReferenceExampleQuery>()).Returns(new KnowledgeReferenceExampleListResult(
			false,
			[Example()],
			[diagnostic]));

		// Act
		int exitCode = _command.Execute(new ListKnowledgeExamplesOptions());

		// Assert
		exitCode.Should().Be(1, because: "a malformed catalog entry makes the discovery result incomplete");
		_logger.Received(1).WriteInfo("1. Google Pub/Sub reference");
		_logger.Received().WriteLine("  Description: Google Pub/Sub integration");
		_logger.Received().WriteLine("  ID: google-pubsub");
		_logger.Received().WriteLine("  Catalog: partner / com.partner.examples");
		_logger.Received().WriteLine("  Status: published");
		_logger.Received().WriteLine("  Repository: https://github.com/example/pubsub");
		_logger.Received().WriteLine($"  Revision: {new string('a', 40)}");
		_logger.Received().WriteLine("    - app-lifecycle");
		_logger.Received().WriteLine("    - atf");
		_logger.Received(1).WriteError(diagnostic);
	}

	private static KnowledgeReferenceExample Example() => new(
		"partner",
		"com.partner.examples",
		10,
		"supplement",
		42,
		"sha256:test-bundle",
		"example-pubsub",
		0,
		"google-pubsub",
		"Google Pub/Sub reference",
		"published",
		new KnowledgeReferenceExampleUseCase("pubsub", "Google Pub/Sub integration"),
		new KnowledgeReferenceExampleSource(
			"https://github.com/example/pubsub",
			new string('a', 40),
			"main"),
		new Dictionary<string, string>(StringComparer.Ordinal) { ["workspace"] = "workspace" },
		["atf", "app-lifecycle"],
		new KnowledgeReferenceExampleCompatibility("validated", "Creatio 8.1+"),
		new KnowledgeReferenceExampleTrust("ATF", "vetted"),
		["Example only"]);
}
