using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class DescribeProcessCommandTests : BaseCommandTests<DescribeProcessOptions> {
	private IProcessDescriber _describer;
	private ILogger _logger;
	private DescribeProcessCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_describer = Substitute.For<IProcessDescriber>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_describer);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DescribeProcessCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_describer.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Category("Unit")]
	[Description("Describes the process via the server and writes the structured graph JSON, returning zero, when one identity is given.")]
	public void Execute_ShouldWriteStructuredGraphAndReturnZero_WhenProcessFound() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Success = true,
				Name = "UsrProcess_493d4c9",
				Caption = "AI PoC Read Contact",
				SchemaUId = "uid",
				Elements = [],
				Flows = [],
				Parameters = []
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessCode = "UsrProcess_493d4c9" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		_describer.Received(1).Describe(
			Arg.Is<ProcessIdentity>(identity => identity.Code == "UsrProcess_493d4c9"), Arg.Any<string>());
		_logger.Received(1).WriteInfo(Arg.Is<string>(json => json.Contains("UsrProcess_493d4c9")));
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the requested culture to the server describer.")]
	public void Execute_ShouldForwardCulture_WhenProvided() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult { Success = true, Name = "UsrProcess_493d4c9" });
		DescribeProcessOptions options = new() {
			Environment = "dev", ProcessCode = "UsrProcess_493d4c9", Culture = "uk-UA"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		_describer.Received(1).Describe(Arg.Any<ProcessIdentity>(), "uk-UA");
	}

	[Test]
	[Category("Unit")]
	[Description("Prints a user-friendly Error and returns non-zero when the process cannot be resolved.")]
	public void Execute_ShouldPrintErrorAndReturnNonZero_WhenProcessNotFound() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
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
	[Description("Requires exactly one identity: with none provided it errors before contacting the server.")]
	public void Execute_ShouldErrorWithoutReading_WhenNoIdentityProvided() {
		// Arrange
		DescribeProcessOptions options = new() { Environment = "dev" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "exactly one process identity is required");
		_logger.Received(1).WriteError(Arg.Is<string>(value => value.StartsWith("Error:", StringComparison.Ordinal)));
		_describer.DidNotReceive().Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Requires exactly one identity: with more than one provided it errors before contacting the server.")]
	public void Execute_ShouldErrorWithoutReading_WhenMultipleIdentitiesProvided() {
		// Arrange
		DescribeProcessOptions options = new() { Environment = "dev", ProcessCode = "x", ProcessCaption = "y" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "providing two identities is ambiguous");
		_describer.DidNotReceive().Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>());
	}
}
