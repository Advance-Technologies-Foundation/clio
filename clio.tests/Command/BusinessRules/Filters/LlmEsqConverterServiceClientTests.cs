using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class LlmEsqConverterServiceClientTests {

	private const string Envelope =
		"{\"items\":{\"abc\":{\"filterType\":4}},\"rootSchemaName\":\"City\",\"filterType\":6}";

	private IApplicationClient _applicationClient = null!;
	private LlmEsqConverterServiceClient _client = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_client = new LlmEsqConverterServiceClient(_applicationClient);
	}

	[Test]
	[Category("Unit")]
	[Description("Posts the friendly filter wrapped under filterRequests[0] with the supplied rootSchemaName.")]
	public void ConvertToEsqFilter_Should_Post_Wrapped_Request() {
		// Arrange
		string captured = string.Empty;
		_applicationClient
			.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs(call => {
				captured = call.ArgAt<string>(2);
				return WrapResult($"[{Envelope}]");
			});
		FriendlyFilterGroup filter = new(
			"AND",
			[new FriendlyFilterLeaf("Country", "EQUAL", JsonDocument.Parse("\"a-guid\"").RootElement)],
			[]);

		// Act
		_client.ConvertToEsqFilter("City", filter);

		// Assert
		using JsonDocument body = JsonDocument.Parse(captured);
		JsonElement requests = body.RootElement.GetProperty("filterRequests");
		requests.GetArrayLength().Should().Be(1);
		JsonElement request = requests[0];
		request.GetProperty("rootSchemaName").GetString().Should().Be("City");
		request.GetProperty("filter").GetProperty("logicalOperation").GetString().Should().Be("AND");
		request.GetProperty("filter").GetProperty("filters").GetArrayLength().Should().Be(1);
		request.GetProperty("filter").GetProperty("filters")[0]
			.GetProperty("columnPath").GetString().Should().Be("Country");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps backward-reference filters to columnPath + subFilters per the CrtCopilot contract.")]
	public void ConvertToEsqFilter_Should_Map_Backward_Reference_Filter() {
		// Arrange
		string captured = string.Empty;
		_applicationClient
			.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs(call => {
				captured = call.ArgAt<string>(2);
				return WrapResult($"[{Envelope}]");
			});
		FriendlyFilterGroup nested = new(
			"AND",
			[new FriendlyFilterLeaf("Name", "EQUAL", JsonDocument.Parse("\"x\"").RootElement)],
			[]);
		FriendlyFilterGroup root = new(
			"AND",
			[],
			[new BackwardReferenceFilter("[Order:Customer]", nested)]);

		// Act
		_client.ConvertToEsqFilter("Account", root);

		// Assert
		using JsonDocument body = JsonDocument.Parse(captured);
		JsonElement brfArray = body.RootElement
			.GetProperty("filterRequests")[0]
			.GetProperty("filter")
			.GetProperty("backwardReferenceFilters");
		brfArray.GetArrayLength().Should().Be(1);
		JsonElement brf = brfArray[0];
		brf.GetProperty("columnPath").GetString().Should().Be("[Order:Customer]");
		brf.GetProperty("subFilters").GetProperty("logicalOperation").GetString().Should().Be("AND");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the first element of the wrapped JSON-string array verbatim as the envelope.")]
	public void ConvertToEsqFilter_Should_Return_Wrapped_First_Filter() {
		// Arrange
		_applicationClient.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs(WrapResult($"[{Envelope}]"));

		// Act
		string emitted = _client.ConvertToEsqFilter("City", BuildEmptyGroup());

		// Assert
		emitted.Should().Be(Envelope);
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts unwrapped JSON arrays as a defensive fallback; returns the first element verbatim.")]
	public void ConvertToEsqFilter_Should_Return_Unwrapped_First_Filter() {
		// Arrange
		_applicationClient.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs($"[{Envelope}]");

		// Act
		string emitted = _client.ConvertToEsqFilter("City", BuildEmptyGroup());

		// Assert
		emitted.Should().Be(Envelope);
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces an HTML error page as filter.server-rejected.")]
	public void ConvertToEsqFilter_Should_Throw_ServerRejected_On_Html_Response() {
		// Arrange
		_applicationClient.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs("<html><body>500 Internal Server Error</body></html>");

		// Act
		System.Action act = () => _client.ConvertToEsqFilter("City", BuildEmptyGroup());

		// Assert
		BusinessRuleFilterException exception = act.Should()
			.Throw<BusinessRuleFilterException>()
			.Which;
		exception.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.ServerRejected);
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces an empty response body as filter.server-rejected.")]
	public void ConvertToEsqFilter_Should_Throw_ServerRejected_On_Empty_Response() {
		// Arrange
		_applicationClient.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs(string.Empty);

		// Act
		System.Action act = () => _client.ConvertToEsqFilter("City", BuildEmptyGroup());

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.ServerRejected);
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a non-array payload as filter.server-rejected.")]
	public void ConvertToEsqFilter_Should_Throw_ServerRejected_When_Result_Is_Not_Array() {
		// Arrange
		_applicationClient.CallConfigurationService(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.ReturnsForAnyArgs(WrapResult("{}"));

		// Act
		System.Action act = () => _client.ConvertToEsqFilter("City", BuildEmptyGroup());

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.ServerRejected);
	}

	private static FriendlyFilterGroup BuildEmptyGroup() =>
		new("AND", new List<FriendlyFilterLeaf>(), new List<BackwardReferenceFilter>());

	private static string WrapResult(string innerJson) {
		// WCF Wrapped response: outer object with the result-property whose value is the
		// JSON-serialized string of the underlying return value.
		string inner = JsonSerializer.Serialize(innerJson);
		return "{\"ConvertToEsqFiltersResult\":" + inner + "}";
	}
}
