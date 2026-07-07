using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.Responses;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class EntitySchemaPublishHelperTests
{
	private IRemoteEntitySchemaDesignerClient _client = null!;
	private ILogger _logger = null!;
	private RemoteCommandOptions _options = null!;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_logger = Substitute.For<ILogger>();
		_options = new RemoteCommandOptions();
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Publishes the configuration and then requests the OData rebuild, logging the rebuild-requested message.")]
	public void PublishAndRebuildOData_ShouldPublishThenRequestRebuild_WhenBothSucceed() {
		// Arrange — capture the order the client is called in.
		List<string> calls = [];
		_client.When(c => c.PublishConfigurationChanges(_options)).Do(_ => calls.Add("publish"));
		_client.When(c => c.RunODataBuild(_options)).Do(_ => calls.Add("rebuild"));

		// Act
		EntitySchemaPublishHelper.PublishAndRebuildOData(_client, _logger, _options, "UsrVehicle", "was created and saved");

		// Assert
		calls.Should().Equal(["publish", "rebuild"],
			because: "a saved change is invisible over OData until it is published, then the OData assembly rebuilt");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("OData entities rebuild requested", StringComparison.Ordinal)
			&& message.Contains("UsrVehicle", StringComparison.Ordinal)));
		// because: a successful rebuild request must report which entity was made reachable
	}

	[Test]
	[Description("Throws an actionable error that names what was saved when publishing the configuration fails, and never reaches the rebuild.")]
	public void PublishAndRebuildOData_ShouldThrow_WhenPublishFails() {
		// Arrange
		_client.PublishConfigurationChanges(_options).Returns(_ => throw new InvalidOperationException("Compilation failed."));

		// Act
		Action act = () => EntitySchemaPublishHelper.PublishAndRebuildOData(_client, _logger, _options, "UsrVehicle", "columns were saved");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*columns were saved, but publishing the configuration failed*Compilation failed.*",
				because: "a publish failure leaves the change invisible and must surface with context");
		_client.DidNotReceive().RunODataBuild(Arg.Any<RemoteCommandOptions>());
		// because: the rebuild is pointless when publishing did not complete
	}

	[TestCase(typeof(InvalidOperationException))]
	[TestCase(typeof(HttpRequestException))]
	[TestCase(typeof(WebException))]
	[TestCase(typeof(SocketException))]
	[TestCase(typeof(IOException))]
	[TestCase(typeof(OperationCanceledException))]
	[TestCase(typeof(Newtonsoft.Json.JsonException))]
	[Description("Warns and does not throw when the rebuild request fails with an expected transport/IO/parse fault.")]
	public void PublishAndRebuildOData_ShouldWarnAndNotThrow_WhenRebuildFailsWithExpectedFault(Type faultType) {
		// Arrange
		Exception fault = (Exception)Activator.CreateInstance(faultType)!;
		_client.RunODataBuild(_options).Returns(_ => throw fault);

		// Act
		Action act = () => EntitySchemaPublishHelper.PublishAndRebuildOData(_client, _logger, _options, "UsrVehicle", "was created and saved");

		// Assert
		act.Should().NotThrow(
			because: $"{faultType.Name} is an expected rebuild-request fault and must not fail an already-published change");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(EntitySchemaPublishHelper.ODataBuildRequestFailedWarningFragment, StringComparison.Ordinal)));
		// because: an expected rebuild-request fault must be surfaced as a warning, not swallowed silently
	}

	[Test]
	[Description("Warns and does not throw when the rebuild fault arrives wrapped in an AggregateException, as the Creatio client surfaces transport faults.")]
	public void PublishAndRebuildOData_ShouldWarnAndNotThrow_WhenRebuildFaultIsWrappedInAggregate() {
		// Arrange
		_client.RunODataBuild(_options).Returns(_ => throw new AggregateException(new HttpRequestException("reset")));

		// Act
		Action act = () => EntitySchemaPublishHelper.PublishAndRebuildOData(_client, _logger, _options, "UsrVehicle", "was created and saved");

		// Assert
		act.Should().NotThrow(because: "a wrapped transport fault must be unwrapped and treated as expected");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(EntitySchemaPublishHelper.ODataBuildRequestFailedWarningFragment, StringComparison.Ordinal)));
		// because: the wrapped transport fault must still produce the rebuild-request warning
	}

	[Test]
	[Description("Rethrows when the rebuild fails with an unexpected fault so genuine programming errors are not swallowed.")]
	public void PublishAndRebuildOData_ShouldRethrow_WhenRebuildFailsWithUnexpectedFault() {
		// Arrange
		_client.RunODataBuild(_options).Returns(_ => throw new ArgumentException("bug"));

		// Act
		Action act = () => EntitySchemaPublishHelper.PublishAndRebuildOData(_client, _logger, _options, "UsrVehicle", "was created and saved");

		// Assert
		act.Should().Throw<ArgumentException>(because: "an unexpected fault is not a rebuild-request failure and must surface");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		// because: an unexpected fault must not be reported as an expected rebuild-request warning
	}

	[Test]
	[Description("Rethrows an empty AggregateException rather than swallowing it, since it carries no diagnosable fault.")]
	public void PublishAndRebuildOData_ShouldRethrow_WhenRebuildFailsWithEmptyAggregate() {
		// Arrange
		_client.RunODataBuild(_options).Returns(_ => throw new AggregateException());

		// Act
		Action act = () => EntitySchemaPublishHelper.PublishAndRebuildOData(_client, _logger, _options, "UsrVehicle", "was created and saved");

		// Assert
		act.Should().Throw<AggregateException>(because: "an empty aggregate has no diagnosable fault and must not be silently swallowed");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		// because: an empty aggregate is not classified as expected, so no warning should be emitted
	}
}
