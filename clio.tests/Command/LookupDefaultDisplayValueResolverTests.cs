using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class LookupDefaultDisplayValueResolverTests
{
	private const string ReferenceSchema = "UsrEng91318Color";
	private static readonly Guid RecordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");

	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IRuntimeEntitySchemaReader _runtimeEntitySchemaReader = null!;
	private ILogger _logger = null!;
	private ILookupDefaultDisplayValueResolver _resolver = null!;

	[SetUp]
	public void Setup() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://localhost/0/DataService/Select");
		_resolver = new LookupDefaultDisplayValueResolver(
			_applicationClient, _serviceUrlBuilder, _runtimeEntitySchemaReader, _logger);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
	}

	private void ArrangeDisplayColumn(string? primaryDisplayColumnName) {
		_runtimeEntitySchemaReader.GetByName(ReferenceSchema).Returns(new RuntimeEntitySchemaResult(
			UId: Guid.NewGuid(),
			Name: ReferenceSchema,
			PrimaryColumnUId: Guid.NewGuid(),
			PrimaryDisplayColumnName: primaryDisplayColumnName,
			PrimaryDisplayColumnUId: null,
			Columns: new List<RuntimeEntitySchemaColumnResult>()));
	}

	private void ArrangeSelectResponse(string responseJson) {
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(responseJson);
	}

	[Test]
	[Description("Returns the referenced record display value when the SelectQuery finds exactly one row.")]
	public void Resolve_ShouldReturnDisplayValue_WhenRecordFound() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse($"{{\"success\":true,\"rows\":[{{\"Id\":\"{RecordId:D}\",\"DisplayValue\":\"Green\"}}]}}");

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.DisplayValue.Should().Be("Green",
			because: "a found referenced record must surface its display value so an agent can verify the default");
		result.RecordResolution.Should().BeNull(
			because: "no marker is emitted when the display value resolves successfully");
	}

	[Test]
	[Description("Returns the not-found-or-no-access marker when the SelectQuery returns no rows.")]
	public void Resolve_ShouldReturnNotFoundMarker_WhenNoRowReturned() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse("{\"success\":true,\"rows\":[]}");

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.DisplayValue.Should().BeNull(
			because: "no row means there is no display value to report");
		result.RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.NotFoundMarker,
			because: "an empty result is reported honestly as not-found-or-no-access (deleted vs hidden are indistinguishable)");
	}

	[Test]
	[Description("Returns the no-access marker when the referenced entity read is denied by security.")]
	public void Resolve_ShouldReturnNoAccessMarker_WhenSecurityDenied() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse(
			"{\"success\":false,\"errorInfo\":{\"message\":\"Current user does not have permission for the \\\"UsrEng91318Color\\\" object\"}}");

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.NoAccessMarker,
			because: "a schema-level security denial must degrade to the no-access marker, not fail the readback");
		result.DisplayValue.Should().BeNull(
			because: "a denied read yields no display value");
	}

	[Test]
	[Description("Returns the display-column-unavailable marker and skips the data query when the referenced schema has no display column.")]
	public void Resolve_ShouldReturnDisplayColumnUnavailableMarker_WhenNoDisplayColumn() {
		// Arrange
		ArrangeDisplayColumn(null);

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.DisplayColumnUnavailableMarker,
			because: "a referenced schema without a resolvable display column (e.g. ImageLookup -> SysImage) cannot yield a text display value");
		_applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Description("Degrades silently with both fields null when the reference schema name is empty.")]
	public void Resolve_ShouldDegradeSilently_WhenReferenceSchemaNameEmpty() {
		// Act
		LookupDefaultResolution result = _resolver.Resolve(string.Empty, RecordId, new RemoteCommandOptions());

		// Assert
		result.DisplayValue.Should().BeNull(because: "an empty reference schema cannot be queried");
		result.RecordResolution.Should().BeNull(
			because: "a non-applicable enrichment degrades silently so the readback stays GUID-only with no regression");
		_runtimeEntitySchemaReader.DidNotReceive().GetByName(Arg.Any<string>());
	}

	[Test]
	[Description("Warns and reports not-found-or-no-access when the SelectQuery fails with a non-security error.")]
	public void Resolve_ShouldWarnAndReportNotFound_WhenSelectQueryFailsGenerically() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse("{\"success\":false,\"errorInfo\":{\"message\":\"Unexpected backend failure\"}}");

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.NotFoundMarker,
			because: "a generic query failure must not throw; it degrades to a marker so the readback survives");
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("Unexpected backend failure")));
	}
}
