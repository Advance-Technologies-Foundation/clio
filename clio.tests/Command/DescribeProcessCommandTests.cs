using System;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class DescribeProcessCommandTests : BaseCommandTests<DescribeProcessOptions> {
	private IProcessSchemaReader _schemaReader;
	private IProcessGraphExtractor _extractor;
	private ILogger _logger;
	private DescribeProcessCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_schemaReader = Substitute.For<IProcessSchemaReader>();
		_extractor = Substitute.For<IProcessGraphExtractor>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_schemaReader);
		containerBuilder.AddSingleton(_extractor);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DescribeProcessCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_schemaReader.ClearReceivedCalls();
		_extractor.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Category("Unit")]
	[Description("Reads the process and writes the structured graph JSON, returning zero, when one identity is given.")]
	public void Execute_ShouldWriteStructuredGraphAndReturnZero_WhenProcessFound() {
		// Arrange
		_schemaReader.Read(Arg.Any<ProcessIdentity>()).Returns(new ProcessSchemaResponse());
		_extractor.Extract(Arg.Any<ProcessSchemaResponse>(), Arg.Any<string>())
			.Returns(new ProcessDescription("UsrProcess_493d4c9", "AI PoC Read Contact", "uid", [], [], []));
		DescribeProcessOptions options = new() { Environment = "dev", ProcessCode = "UsrProcess_493d4c9" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		_logger.Received(1).WriteInfo(Arg.Is<string>(json => json.Contains("UsrProcess_493d4c9")));
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Prints a user-friendly Error and returns non-zero when the process cannot be resolved.")]
	public void Execute_ShouldPrintErrorAndReturnNonZero_WhenProcessNotFound() {
		// Arrange
		_schemaReader.Read(Arg.Any<ProcessIdentity>())
			.Returns(Error.Failure("ResolveId", "process not found (code 'missing')"));
		DescribeProcessOptions options = new() { Environment = "dev", ProcessCode = "missing" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a missing process is a hard error");
		_logger.Received(1).WriteError(Arg.Is<string>(value =>
			value.StartsWith("Error:", StringComparison.Ordinal) && value.Contains("not found")));
	}

	[Test]
	[Category("Unit")]
	[Description("Requires exactly one identity: with none provided it errors before reading the schema.")]
	public void Execute_ShouldErrorWithoutReading_WhenNoIdentityProvided() {
		// Arrange
		DescribeProcessOptions options = new() { Environment = "dev" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "exactly one process identity is required");
		_logger.Received(1).WriteError(Arg.Is<string>(value => value.StartsWith("Error:", StringComparison.Ordinal)));
		_schemaReader.DidNotReceive().Read(Arg.Any<ProcessIdentity>());
	}

	[Test]
	[Category("Unit")]
	[Description("Requires exactly one identity: with more than one provided it errors before reading the schema.")]
	public void Execute_ShouldErrorWithoutReading_WhenMultipleIdentitiesProvided() {
		// Arrange
		DescribeProcessOptions options = new() { Environment = "dev", ProcessCode = "x", ProcessCaption = "y" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "providing two identities is ambiguous");
		_schemaReader.DidNotReceive().Read(Arg.Any<ProcessIdentity>());
	}
}
