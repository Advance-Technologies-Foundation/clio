using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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
	[Description("Reports display-column-unavailable (no throw) when display-column discovery fails with a transport fault.")]
	public void Resolve_ShouldReturnDisplayColumnUnavailable_WhenDisplayColumnLookupThrowsTransport() {
		// Arrange
		_runtimeEntitySchemaReader.GetByName(ReferenceSchema)
			.Returns(_ => throw new HttpRequestException("connection reset"));

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.DisplayColumnUnavailableMarker,
			because: "a transport fault while discovering the display column must degrade, not fail the readback");
		_applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Description("Degrades to a GUID-only resolution (no throw) when the display-value SelectQuery times out.")]
	public void Resolve_ShouldDegradeSilently_WhenSelectQueryTimesOut() {
		// Arrange
		ArrangeDisplayColumn("Name");
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(_ => throw new TaskCanceledException("timed out"));

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.DisplayValue.Should().BeNull(because: "a timed-out enrichment query yields no display value");
		result.RecordResolution.Should().BeNull(
			because: "a timeout degrades to GUID-only (no marker) so enrichment never fails the readback");
	}

	[Test]
	[Description("Degrades to a GUID-only resolution (no throw) when the display-value response is malformed JSON.")]
	public void Resolve_ShouldDegradeSilently_WhenSelectQueryReturnsMalformedJson() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse("<<<not-json>>>");

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.DisplayValue.Should().BeNull(because: "a malformed response yields no display value");
		result.RecordResolution.Should().BeNull(
			because: "a parse fault degrades to GUID-only (no marker) so enrichment never fails the readback");
	}

	[Test]
	[Description("Degrades to a GUID-only resolution (no marker) and warns when the SelectQuery answers with an HTML page — a non-JSON body states nothing about the record, so claiming not-found-or-no-access would be an invented claim (ENG-93365).")]
	public void Resolve_ShouldDegradeSilentlyAndWarn_WhenSelectQueryReturnsHtmlPage() {
		// Arrange — the guard promotes this body to NonJsonServiceResponseException, which derives from
		// InvalidOperationException; without the dedicated catch it falls through to the generic
		// InvalidOperationException handler and is reported as not-found-or-no-access instead.
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse("<!DOCTYPE html><html><body>Server Error in '/' Application.</body></html>");

		// Act
		LookupDefaultResolution result = _resolver.Resolve(ReferenceSchema, RecordId, new RemoteCommandOptions());

		// Assert
		result.DisplayValue.Should().BeNull(because: "an unusable response yields no display value");
		result.RecordResolution.Should().BeNull(
			because: "a non-JSON body says nothing about the record, so no not-found/no-access marker may be claimed");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("HTML page instead of JSON")));
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

	private static readonly Guid RecordId2 = Guid.Parse("e2c7fb69-7b99-5dc8-cffb-8b52dbb1bf61");

	[Test]
	[Description("ResolveMany resolves several records of one reference schema to their display values in a single batched query.")]
	public void ResolveMany_ShouldResolveMultipleRecords_InOneQuery() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse(
			$"{{\"success\":true,\"rows\":[" +
			$"{{\"Id\":\"{RecordId:D}\",\"DisplayValue\":\"Green\"}}," +
			$"{{\"Id\":\"{RecordId2:D}\",\"DisplayValue\":\"Blue\"}}]}}");

		// Act
		IReadOnlyDictionary<Guid, LookupDefaultResolution> result =
			_resolver.ResolveMany(ReferenceSchema, [RecordId, RecordId2], new RemoteCommandOptions());

		// Assert
		result[RecordId].DisplayValue.Should().Be("Green", because: "each requested record must map to its own display value");
		result[RecordId2].DisplayValue.Should().Be("Blue", because: "each requested record must map to its own display value");
		_applicationClient.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Description("ResolveMany keys on the parsed Guid, so a record whose Id is returned in a braced, upper-case form still resolves (no raw-string miss).")]
	public void ResolveMany_ShouldResolve_WhenIdReturnedInDifferentGuidForm() {
		// Arrange - the endpoint returns the Id in braced upper-case form; the requested id is the plain 'D' form.
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse(
			$"{{\"success\":true,\"rows\":[{{\"Id\":\"{{{RecordId.ToString("D").ToUpperInvariant()}}}\",\"DisplayValue\":\"Green\"}}]}}");

		// Act
		IReadOnlyDictionary<Guid, LookupDefaultResolution> result =
			_resolver.ResolveMany(ReferenceSchema, [RecordId], new RemoteCommandOptions());

		// Assert
		result[RecordId].DisplayValue.Should().Be("Green",
			because: "the returned Id is parsed to Guid, so a braces/case form difference must still resolve, not silently degrade to GUID-only");
	}

	[Test]
	[Description("ResolveMany splits an id set larger than the per-query cap across multiple IN queries and unions the results, so a record from every chunk resolves.")]
	public void ResolveMany_ShouldUnionResultsAcrossChunks_WhenAboveCap() {
		// Arrange - 401 distinct ids force two queries (cap is 400). A fixed response carries the FIRST and LAST id, so
		// each is only satisfiable from a DIFFERENT chunk's query — proving the per-chunk results are unioned, not overwritten.
		ArrangeDisplayColumn("Name");
		Guid[] ids = Enumerable.Range(0, 401)
			.Select(i => Guid.Parse($"11110000-0000-0000-0000-{i:D12}")).ToArray();
		ArrangeSelectResponse(
			$"{{\"success\":true,\"rows\":[" +
			$"{{\"Id\":\"{ids[0]:D}\",\"DisplayValue\":\"First\"}}," +
			$"{{\"Id\":\"{ids[400]:D}\",\"DisplayValue\":\"Last\"}}]}}");

		// Act
		IReadOnlyDictionary<Guid, LookupDefaultResolution> result =
			_resolver.ResolveMany(ReferenceSchema, ids, new RemoteCommandOptions());

		// Assert
		_applicationClient.Received(2).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
		result[ids[0]].DisplayValue.Should().Be("First", because: "an id in the first chunk resolves from the first query");
		result[ids[400]].DisplayValue.Should().Be("Last",
			because: "an id in the second chunk resolves from the second query, proving results union across chunks");
	}

	[Test]
	[Description("ResolveMany marks ids with no returned row as not-found-or-no-access, preserving the single Resolve fail-soft contract.")]
	public void ResolveMany_ShouldMarkNotFound_ForIdsWithNoRow() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse("{\"success\":true,\"rows\":[]}");

		// Act
		IReadOnlyDictionary<Guid, LookupDefaultResolution> result =
			_resolver.ResolveMany(ReferenceSchema, [RecordId], new RemoteCommandOptions());

		// Assert
		result[RecordId].DisplayValue.Should().BeNull(because: "no row means no display value");
		result[RecordId].RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.NotFoundMarker,
			because: "a requested id with no returned row degrades to the same honest marker as the single Resolve");
	}

	[Test]
	[Description("ResolveMany marks every id display-column-unavailable and issues no query when the reference schema has no resolvable display column.")]
	public void ResolveMany_ShouldMarkDisplayColumnUnavailable_AndSkipQuery_WhenNoDisplayColumn() {
		// Arrange
		ArrangeDisplayColumn(null);

		// Act
		IReadOnlyDictionary<Guid, LookupDefaultResolution> result =
			_resolver.ResolveMany(ReferenceSchema, [RecordId, RecordId2], new RemoteCommandOptions());

		// Assert
		result[RecordId].RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.DisplayColumnUnavailableMarker,
			because: "a reference schema with no resolvable display column cannot yield captions for any id");
		result[RecordId2].RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.DisplayColumnUnavailableMarker,
			because: "the marker applies to every requested id");
		_applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
	}

	[Test]
	[Description("ResolveMany marks every id in the chunk no-access when the batched read is denied by security.")]
	public void ResolveMany_ShouldMarkNoAccess_WhenSecurityDenied() {
		// Arrange
		ArrangeDisplayColumn("Name");
		ArrangeSelectResponse(
			"{\"success\":false,\"errorInfo\":{\"message\":\"Current user does not have permission for the \\\"UsrEng91318Color\\\" object\"}}");

		// Act
		IReadOnlyDictionary<Guid, LookupDefaultResolution> result =
			_resolver.ResolveMany(ReferenceSchema, [RecordId, RecordId2], new RemoteCommandOptions());

		// Assert
		result[RecordId].RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.NoAccessMarker,
			because: "a schema-level security denial applies to every id in the failed batch");
		result[RecordId2].RecordResolution.Should().Be(LookupDefaultDisplayValueResolver.NoAccessMarker,
			because: "a schema-level security denial applies to every id in the failed batch");
	}
}
