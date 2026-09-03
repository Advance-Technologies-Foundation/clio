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
public sealed class EntitySchemaPublisherTests
{
	private IRemoteEntitySchemaDesignerClient _client = null!;
	private IODataBuildGate _oDataBuildGate = null!;
	private ILogger _logger = null!;
	private RemoteCommandOptions _options = null!;
	private EntitySchemaPublisher _publisher = null!;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_oDataBuildGate = Substitute.For<IODataBuildGate>();
		_logger = Substitute.For<ILogger>();
		_options = new RemoteCommandOptions();
		_publisher = new EntitySchemaPublisher(_client, _oDataBuildGate, _logger);
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_oDataBuildGate.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Waits for the OData build gate to go idle before publishing the configuration, since a publish that starts while a background build holds the metadata file open fails on a sharing violation.")]
	public void PublishSavedChanges_ShouldWaitForGate_BeforePublishing() {
		// Arrange — capture the order the gate and client are invoked in.
		List<string> calls = [];
		_oDataBuildGate.When(g => g.WaitUntilIdle(_options, "UsrVehicle")).Do(_ => calls.Add("wait"));
		_client.When(c => c.PublishConfigurationChanges(_options)).Do(_ => calls.Add("publish"));

		// Act
		_publisher.PublishSavedChanges(_options, "UsrVehicle", "was created and saved", ODataContractImpact.Changed);

		// Assert
		calls.Should().Equal(["wait", "publish"],
			because: "publishing while a build still holds the configuration file open fails on a sharing violation, so the gate must be awaited first");
	}

	[Test]
	[Description("Publishes the configuration and then requests the OData rebuild when the mutation changed the OData contract.")]
	public void PublishSavedChanges_ShouldPublishThenRebuild_WhenImpactIsChanged() {
		// Arrange — capture the order the client is called in.
		List<string> calls = [];
		_client.When(c => c.PublishConfigurationChanges(_options)).Do(_ => calls.Add("publish"));
		_client.When(c => c.RunODataBuild(_options)).Do(_ => calls.Add("rebuild"));

		// Act
		_publisher.PublishSavedChanges(_options, "UsrVehicle", "was created and saved", ODataContractImpact.Changed);

		// Assert
		calls.Should().Equal(["publish", "rebuild"],
			because: "a saved change that alters the OData contract is invisible over OData until it is published, then the OData assembly rebuilt");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("OData entities rebuild requested", StringComparison.Ordinal)
			&& message.Contains("UsrVehicle", StringComparison.Ordinal)));
		// because: a successful rebuild request must report which entity was made reachable
	}

	[Test]
	[Description("Publishes the configuration but never requests an OData rebuild when the mutation left the OData contract unchanged, since a rebuild would only reproduce the document already on disk.")]
	public void PublishSavedChanges_ShouldPublishAndNotRebuild_WhenImpactIsUnchanged() {
		// Arrange - capture the order the client is called in.
		List<string> calls = [];
		_client.When(c => c.PublishConfigurationChanges(_options)).Do(_ => calls.Add("publish"));
		_client.When(c => c.RunODataBuild(Arg.Any<RemoteCommandOptions>())).Do(_ => calls.Add("rebuild"));

		// Act
		_publisher.PublishSavedChanges(_options, "UsrVehicle", "schema properties were saved", ODataContractImpact.Unchanged);

		// Assert
		calls.Should().Equal(["publish"],
			because: "the configuration must still be published so the saved change compiles into it, while an unchanged OData contract makes the 90-120s rebuild pointless and must not be requested");
	}

	[Test]
	[Description("Throws an actionable error that names what was saved when publishing the configuration fails, and never reaches the rebuild.")]
	public void PublishSavedChanges_ShouldThrow_WhenPublishFails() {
		// Arrange
		_client.PublishConfigurationChanges(_options).Returns(_ => throw new InvalidOperationException("Compilation failed."));

		// Act
		Action act = () => _publisher.PublishSavedChanges(_options, "UsrVehicle", "columns were saved", ODataContractImpact.Changed);

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
	public void PublishSavedChanges_ShouldWarnAndNotThrow_WhenRebuildFailsWithExpectedFault(Type faultType) {
		// Arrange
		Exception fault = (Exception)Activator.CreateInstance(faultType)!;
		_client.RunODataBuild(_options).Returns(_ => throw fault);

		// Act
		Action act = () => _publisher.PublishSavedChanges(_options, "UsrVehicle", "was created and saved", ODataContractImpact.Changed);

		// Assert
		act.Should().NotThrow(
			because: $"{faultType.Name} is an expected rebuild-request fault and must not fail an already-published change");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(EntitySchemaPublisher.ODataBuildRequestFailedWarningFragment, StringComparison.Ordinal)));
		// because: an expected rebuild-request fault must be surfaced as a warning, not swallowed silently
	}

	[Test]
	[Description("Warns and does not throw when the rebuild fault arrives wrapped in an AggregateException, as the Creatio client surfaces transport faults.")]
	public void PublishSavedChanges_ShouldWarnAndNotThrow_WhenRebuildFaultIsWrappedInAggregate() {
		// Arrange
		_client.RunODataBuild(_options).Returns(_ => throw new AggregateException(new HttpRequestException("reset")));

		// Act
		Action act = () => _publisher.PublishSavedChanges(_options, "UsrVehicle", "was created and saved", ODataContractImpact.Changed);

		// Assert
		act.Should().NotThrow(because: "a wrapped transport fault must be unwrapped and treated as expected");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(EntitySchemaPublisher.ODataBuildRequestFailedWarningFragment, StringComparison.Ordinal)));
		// because: the wrapped transport fault must still produce the rebuild-request warning
	}

	[Test]
	[Description("Rethrows when the rebuild fails with an unexpected fault so genuine programming errors are not swallowed.")]
	public void PublishSavedChanges_ShouldRethrow_WhenRebuildFailsWithUnexpectedFault() {
		// Arrange
		_client.RunODataBuild(_options).Returns(_ => throw new ArgumentException("bug"));

		// Act
		Action act = () => _publisher.PublishSavedChanges(_options, "UsrVehicle", "was created and saved", ODataContractImpact.Changed);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an unexpected fault is not a rebuild-request failure and must surface");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		// because: an unexpected fault must not be reported as an expected rebuild-request warning
	}

	[Test]
	[Description("Rethrows an empty AggregateException rather than swallowing it, since it carries no diagnosable fault.")]
	public void PublishSavedChanges_ShouldRethrow_WhenRebuildFailsWithEmptyAggregate() {
		// Arrange
		_client.RunODataBuild(_options).Returns(_ => throw new AggregateException());

		// Act
		Action act = () => _publisher.PublishSavedChanges(_options, "UsrVehicle", "was created and saved", ODataContractImpact.Changed);

		// Assert
		act.Should().Throw<AggregateException>(because: "an empty aggregate has no diagnosable fault and must not be silently swallowed");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		// because: an empty aggregate is not classified as expected, so no warning should be emitted
	}
}
