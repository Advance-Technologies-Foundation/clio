using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// HTTP-layer tests for <see cref="ModifyBusinessProcessService"/>: the wrapped <c>{"request":{name|uid, operations}}</c>
/// body, the resolved ModifyProcess route, and each response branch. The command tests substitute the service, so
/// this is the only coverage of the actual clio→server contract for modify.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ModifyBusinessProcessServiceTests {

	private const string Env = "sandbox";
	private const string ModifyUrl = "http://sandbox/0/rest/ProcessDesignService/ModifyProcess";
	private const string Operations = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]";

	private static ModifyBusinessProcessService CreateService(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyProcess, env).Returns(ModifyUrl);
		return new ModifyBusinessProcessService(settings, factory, urlBuilder, Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>());
	}

	[Test]
	[Description("ENG-103085: a filter nested inside setElement's readData STOPS the edit before anything is posted, and the refusal points to the setFilter operation.")]
	public void ModifyProcess_ShouldNotPost_WhenABlockCarriesAKeyTheServerWouldDrop() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ModifyBusinessProcessService service = CreateService(client);
		const string operations = "[{\"op\":\"setElement\",\"elementName\":\"Read1\",\"elementUpdate\":"
			+ "{\"readData\":{\"source\":\"Contact\",\"filter\":{}}}}]";

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, operations));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*'elementUpdate.readData.filter'*setFilter*",
			because: "the refusal names the misplaced key and the operation that sets a filter");
		client.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Posts the process identity + operations array wrapped under 'request' to the ModifyProcess route and returns the applied-operation count on success.")]
	public void ModifyProcess_ShouldPostWrappedRequestToModifyRoute_AndReturnResult_OnSuccess() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":true,\"schemaName\":\"UsrProc\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"appliedOperations\":1}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		ModifyBusinessProcessResult result = service.ModifyProcess(Env,
			new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		result.AppliedOperations.Should().Be(1, because: "the applied-operation count is read from the server result");
		result.SchemaName.Should().Be("UsrProc", because: "the edited schema name is returned");
		client.Received(1).ExecutePostRequest(ModifyUrl, Arg.Is<string>(body =>
			Wrapped(body)["name"].GetValue<string>() == "UsrProc" && Wrapped(body)["operations"] is JsonArray));
	}

	[Test]
	[Description("confirmLayoutChange is posted on EVERY edit, false included. The server reads an absent "
		+ "member as a caller built before the layout gate existed - one that cannot answer a refusal - and "
		+ "applies the edit rather than blocking it. Sending the member only when true would therefore put "
		+ "this client in that class on exactly the requests where the user has NOT agreed, and the gate "
		+ "would never fire for clio at all.")]
	public void ModifyProcess_ShouldPostTheConfirmationMember_EvenWhenItIsFalse() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":true,\"schemaName\":\"UsrProc\",\"appliedOperations\":1}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		client.Received(1).ExecutePostRequest(ModifyUrl, Arg.Is<string>(body =>
			Wrapped(body)["confirmLayoutChange"] != null
			&& Wrapped(body)["confirmLayoutChange"].GetValue<bool>() == false));
	}

	[Test]
	[Description("Reads the server's warnings[] off a SUCCESSFUL edit — the channel that carries the two outcomes which apply but are not what a caller assumes, and which an undeclared member would drop in silence.")]
	public void ModifyProcess_ShouldReadWarnings_WhenServerReportsThemOnASuccessfulEdit() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":true,\"schemaName\":\"UsrProc\",\"appliedOperations\":1,"
			+ "\"warnings\":[\"Connection 'OmniChat' is not registered\"]}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		ModifyBusinessProcessResult result = service.ModifyProcess(Env,
			new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		result.Warnings.Should().ContainSingle(because: "a warning the server reported must reach the caller, and an "
			+ "undeclared member deserializes to null with nothing red anywhere");
		result.Warnings[0].Should().Contain("OmniChat",
			because: "the warning names WHICH connection is affected, which is the only actionable part of it");
	}

	[Test]
	[Description("Leaves Warnings null when the server reports none, so the absent case cannot be mistaken for an empty-but-present list by a caller that enumerates it.")]
	public void ModifyProcess_ShouldLeaveWarningsNull_WhenServerReportsNone() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":true,\"schemaName\":\"UsrProc\",\"appliedOperations\":1}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		ModifyBusinessProcessResult result = service.ModifyProcess(Env,
			new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		result.Warnings.Should().BeNull(
			because: "the server omits the member when there is nothing to say, and the command handles that with ?? []");
	}

	[Test]
	[Description("Surfaces the server's errorMessage when the ModifyProcess result reports success=false (an aborted edit).")]
	public void ModifyProcess_ShouldThrowWithServerMessage_WhenSuccessFalse() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":false,\"errorMessage\":\"Element 'X' was not found.\"}}");
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		act.Should().Throw<InvalidOperationException>(because: "an aborted edit must surface the server message")
			.WithMessage("*Element 'X' was not found*");
	}

	[Test]
	[Description("A failed edit that names the refusing operation carries that index to the caller. The server split appliedOperations into a count plus a nullable failedOperationIndex precisely so a caller need not bisect the batch against a live environment - and clio's success record is built only on success, so this throw is the only path the index can travel. Without this, the split would end at clio's boundary.")]
	public void ModifyProcess_ShouldNameTheRefusingOperation_WhenTheServerReportsAnIndex() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":false,\"errorMessage\":\"Element 'X' was not found.\","
			+ "\"appliedOperations\":2,\"failedOperationIndex\":2}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an aborted edit still fails the call")
			.WithMessage("*index 2*",
				because: "the caller has to learn WHICH operation refused, which is the whole reason the server "
					+ "reports an index separately from the completion count");
	}

	[Test]
	[Description("A failure that still names the schema came AFTER the save - the tracing switch failed behind a saved edit (ENG-102111). The edit exists, so the layout report is relayed as the elements it touched, never as the re-send offer, which would invite repeating a batch that is already saved; and the edit's warnings travel with the error.")]
	public void ModifyProcess_ShouldReportTheSavedEditsLayoutAndWarnings_WhenTheFailureCameAfterTheSave() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":false,\"schemaName\":\"UsrProc\","
			+ "\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"appliedOperations\":1,\"failedOperationIndex\":1,"
			+ "\"errorMessage\":\"The edit WAS saved, but switching process tracing failed.\","
			+ "\"warnings\":[\"Script task 'Calc' needs a compile.\"],"
			+ "\"layoutChange\":{\"reason\":\"redraw\",\"elements\":[\"Calc\"]}}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the tracing switch failed")
			.Which.Message;
		message.Should().Contain("Affected elements: Calc.",
			because: "the saved edit's layout report still names what moved");
		message.Should().NotContain("ASK THE USER",
			because: "the edit is saved, so offering to re-send the same operations would apply them twice");
		message.Should().Contain("Script task 'Calc' needs a compile.",
			because: "the warnings describe an edit that exists");
	}

	[Test]
	[Description("A layout refusal comes BEFORE the save, so it names no schema: it is still relayed as the question it is - the elements plus the re-send offer - and the warnings of an edit that was never saved are not. Pins the other side of the schemaUId condition, which a flipped test would turn into a silently dropped question (ENG-102111).")]
	public void ModifyProcess_ShouldRelayTheLayoutQuestion_AndNoWarnings_WhenNothingWasSaved() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":false,\"appliedOperations\":1,"
			+ "\"errorMessage\":\"This edit re-draws the diagram.\","
			+ "\"warnings\":[\"Script task 'Calc' needs a compile.\"],"
			+ "\"layoutChange\":{\"reason\":\"redraw\",\"elements\":[\"Calc\"]}}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the edit was refused")
			.Which.Message;
		message.Should().Contain("ASK THE USER",
			because: "nothing was saved, so the refusal is a question the caller has to relay");
		message.Should().NotContain("needs a compile",
			because: "warnings of an edit that was never saved would describe a state that does not exist");
	}

	[Test]
	[Description("A failed edit whose failure blames no single operation says nothing about an index. The server sends no index when the failure came after the operation loop, and an older CrtProcessBuilder never sends the field - both mean 'no operation is to blame', so inventing 'index 0' from a missing value would recreate the exact ambiguity the split removed.")]
	public void ModifyProcess_ShouldNotInventAnIndex_WhenTheServerReportsNone() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessResult\":{\"success\":false,\"errorMessage\":\"The schema is invalid.\","
			+ "\"appliedOperations\":2}}");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an aborted edit still fails the call")
			.Which.Message.Should().NotContain("index",
				because: "an absent index must stay absent - a plain int default would have said 'index 0', "
					+ "which is the ambiguity the nullable field exists to prevent");
	}

	[Test]
	[Description("Throws a clear error when the response envelope has no ModifyProcessResult payload.")]
	public void ModifyProcess_ShouldThrow_WhenResponseShapeUnexpected() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns("{}");
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		act.Should().Throw<InvalidOperationException>(because: "a missing result payload is an unexpected server response")
			.WithMessage("*unexpected response shape*");
	}

	[Test]
	[Description("A setElement that names its element in 'element' - the addElement OBJECT on the server - is stopped before anything is posted, and the refusal points to 'elementName'. Sent, it made the server fail the whole request and the caller saw only 'could not read the response'.")]
	public void ModifyProcess_ShouldNotPost_WhenSetElementNamesTheElementInElement() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		ModifyBusinessProcessService service = CreateService(client);
		const string operations = "[{\"op\":\"setElement\",\"element\":\"SendInvoiceEmail\",\"elementUpdate\":"
			+ "{\"email\":{\"attachments\":[{\"sourceElement\":\"Report1\",\"sourceElementParameter\":\"ReportFiles\"}]}}}]";

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a name in 'element' cannot be read by the server, so the request must not be sent")
			.WithMessage("Nothing was sent:*operations[0] (setElement)*'element'*\"elementName\": \"SendInvoiceEmail\"*",
				because: "the refusal names the operation and shows the key to use with the caller's own name");
		client.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("When the server answers WCF's request-deserialization error string instead of the envelope, the edit is reported as NOT applied and the server's text is quoted - the request never reached an operation, so the outcome is known.")]
	public void ModifyProcess_ShouldReportNothingApplied_WhenTheServerCouldNotReadTheRequest() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"\"The formatter threw an exception while trying to deserialize the message: There was an error while "
			+ "trying to deserialize parameter http://tempuri.org/:request.\"");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the server answered an error, not a result")
			.WithMessage("*could not read the request, so the edit was NOT applied*get-tool-contract*"
				+ "The server said: The formatter threw an exception*",
				because: "a request the server could not read ran no operation, and the quoted text says what failed");
	}

	[Test]
	[Description("Any other bare error string keeps the outcome UNKNOWN - it may come after the save - but now quotes the server's text instead of dropping it.")]
	public void ModifyProcess_ShouldKeepTheOutcomeUnknown_AndQuoteTheServer_WhenAnotherErrorStringComesBack() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns("\"Object reference not set to an instance.\"");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the body is not the result envelope")
			.WithMessage("*UNKNOWN*The server said: Object reference not set to an instance.",
				because: "a failure after the save may have written the edit, so only a re-read can tell");
	}

	[Test]
	[Description("A request-deserialization fault longer than 400 characters is still reported as NOT applied, and the quoted server text is cut at 400 characters with an ellipsis so a long fault does not flood the message.")]
	public void ModifyProcess_ShouldCapTheQuotedServerText_At400Characters() {
		// Arrange
		string longFault = "The formatter threw an exception while trying to deserialize the message: "
			+ new string('x', 500);
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns($"\"{longFault}\"");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		InvalidOperationException thrown = act.Should().Throw<InvalidOperationException>(
			because: "the server answered an error, not a result").Which;
		thrown.Message.Should().Contain("the edit was NOT applied",
			because: "the cut does not change what the fault says about the outcome");
		thrown.Message.Should().EndWith($" The server said: {longFault[..400]}...",
			because: "the server's text is quoted and cut at 400 characters");
	}

	[Test]
	[Description("Rejects a request with neither a process name nor a uid before any server call.")]
	public void ModifyProcess_ShouldThrow_WhenNeitherNameNorUid() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest(null, null, Operations));

		act.Should().Throw<ArgumentException>(because: "a modify target (name or uid) is required");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("Rejects operations content that is not a JSON array.")]
	public void ModifyProcess_ShouldThrow_WhenOperationsNotArray() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		ModifyBusinessProcessService service = CreateService(client);

		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, "{}"));

		act.Should().Throw<InvalidOperationException>(because: "operations must be a JSON array of operations")
			.WithMessage("*array*");
	}

	// The service wraps the request under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
	[Test]
	[Description("A response body that is not the ModifyProcess envelope is reported as an UNREADABLE response whose outcome is unknown, not as a raw System.Text.Json message. A server-side serialization failure returns exactly such a body, and it arrives on the one path where the caller most needs to know whether the edit landed — feeding a described approval block back verbatim reached it on ENG-92713.")]
	public void ModifyProcess_ShouldReportAnUnreadableResponse_WhenTheBodyIsNotTheEnvelope() {
		// Arrange — the shape a server-side deserialization failure returns: valid JSON, wrong document
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		// Root is not an object at all, which is what the parser reported on the real hit (Path: $). An error
		// DOCUMENT that happens to be an object deserializes into an all-null envelope instead and is already
		// covered by the "unexpected response shape" branch; only a body the parser cannot map reaches this guard.
		client.ExecutePostRequest(ModifyUrl, Arg.Any<string>()).Returns(
			"[{\"ExceptionType\":\"System.Runtime.Serialization.SerializationException\"}]");
		ModifyBusinessProcessService service = CreateService(client);

		// Act
		Action act = () => service.ModifyProcess(Env, new ModifyBusinessProcessRequest("UsrProc", null, Operations));

		// Assert
		InvalidOperationException thrown = act.Should().Throw<InvalidOperationException>(
			because: "an unreadable response is a real outcome and has to be named as one").Which;
		thrown.Message.Should().Contain("UNKNOWN",
			because: "the write may or may not have landed, and saying so is the only honest report — the caller "
				+ "has to re-read before retrying rather than assume either way");
		thrown.Message.Should().NotContain("BytePositionInLine",
			because: "a .NET parser message names a type and a byte offset, which is written for a developer "
				+ "reading a stack trace and is unusable to the agent that receives it");
		thrown.InnerException.Should().BeOfType<JsonException>(
			because: "the parser failure is kept for a developer who does want it, just not as the message");
	}

}
