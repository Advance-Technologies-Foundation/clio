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
/// HTTP-layer tests for <see cref="ModifyProcessAsNewVersionService"/>: the wrapped
/// <c>{"request":{name|uid, packageName?, operations}}</c> body, the resolved route, and each response branch.
/// The tool tests substitute the command, so this is the only coverage of the actual clio→server contract for
/// saving a version.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ModifyProcessAsNewVersionServiceTests {

	private const string Env = "sandbox";
	private const string VersionUrl = "http://sandbox/0/rest/ProcessDesignService/ModifyProcessAsNewVersion";
	private const string Operations = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]";

	private const string SuccessBody =
		"{\"ModifyProcessAsNewVersionResult\":{\"success\":true,\"versionName\":\"UsrProcCustom2\","
		+ "\"versionSchemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"version\":2,\"isActiveVersion\":false,"
		+ "\"versionRootSchemaUId\":\"11111111-2222-3333-4444-555555555555\",\"appliedOperations\":1}}";

	private static ModifyProcessAsNewVersionService CreateService(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyProcessAsNewVersion, env).Returns(VersionUrl);
		return new ModifyProcessAsNewVersionService(settings, factory, urlBuilder,
			Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>());
	}

	[Test]
	[Description("Posts the source identity + operations wrapped under 'request' to the ModifyProcessAsNewVersion route and reports every fact the platform allocated: the version's UId, the name IT composed, the number, the family root and the applied-operation count.")]
	public void ModifyAsNewVersion_ShouldPostWrappedRequest_AndReturnEveryAllocatedFact_OnSuccess() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(SuccessBody);
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		ModifyProcessAsNewVersionResult result = service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		result.VersionName.Should().Be("UsrProcCustom2",
			because: "the name is composed SERVER-side from root + package + number, so it can only be read back");
		result.VersionSchemaUId.Should().Be("5c58c4c4-134b-4744-9c67-96d9c69c9d55",
			because: "the UId is how the caller addresses this version afterwards - by name is ambiguous across packages");
		result.Version.Should().Be(2, because: "the number is allocated by the platform, never computed here");
		result.VersionRootSchemaUId.Should().Be("11111111-2222-3333-4444-555555555555",
			because: "the root is the key that groups the family, and the caller needs it to find the siblings");
		result.AppliedOperations.Should().Be(1,
			because: "both edit entry points report the same counter, so one payload reads alike on either");
		client.Received(1).ExecutePostRequest(VersionUrl, Arg.Is<string>(body =>
			Wrapped(body)["name"].GetValue<string>() == "UsrProc" && Wrapped(body)["operations"] is JsonArray));
	}

	[Test]
	[Description("isActiveVersion is carried through as the server reported it rather than assumed false, because it is the one fact that says whether what the environment executes changed.")]
	public void ModifyAsNewVersion_ShouldReportIsActiveVersion_AsTheServerAnsweredIt() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(SuccessBody);
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		ModifyProcessAsNewVersionResult result = service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		result.IsActiveVersion.Should().BeFalse(
			because: "a created version is inactive, and reporting it lets a caller SEE that creating one did not "
				+ "change what runs");
	}

	[Test]
	[Description("The optional package name is sent only when supplied — an empty one must not become a packageName member, because the server treats absence as 'you choose' and an empty string as a package that does not exist.")]
	public void ModifyAsNewVersion_ShouldSendPackageName_OnlyWhenSupplied() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(SuccessBody);
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, "UsrAntonTest", Operations));
		service.ModifyAsNewVersion(Env, new ModifyProcessAsNewVersionRequest("UsrProc", null, "  ", Operations));

		// Assert
		client.Received(1).ExecutePostRequest(VersionUrl, Arg.Is<string>(body =>
			Wrapped(body)["packageName"].GetValue<string>() == "UsrAntonTest"));
		client.Received(1).ExecutePostRequest(VersionUrl, Arg.Is<string>(body =>
			Wrapped(body)["packageName"] == null));
	}

	[Test]
	[Description("An ABSENT operations array is legal and is sent as an empty one: a version with no edits is a plain snapshot of the source, which is how an agent takes a restore point. The in-place edit refuses the same input, and copying that refusal here would remove the snapshot gesture.")]
	public void ModifyAsNewVersion_ShouldSendAnEmptyOperationsArray_WhenNoOperationsGiven() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(SuccessBody);
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		service.ModifyAsNewVersion(Env, new ModifyProcessAsNewVersionRequest("UsrProc", null, null, null));

		// Assert
		client.Received(1).ExecutePostRequest(VersionUrl, Arg.Is<string>(body =>
			Wrapped(body)["operations"] is JsonArray && ((JsonArray)Wrapped(body)["operations"]).Count == 0));
	}

	[Test]
	[Description("A failure that still names a version says so. Past a successful save the version EXISTS and the platform offers no way to delete one, so a bare error message would leave the caller unable to address something that is really on their environment.")]
	public void ModifyAsNewVersion_ShouldSayTheVersionExists_WhenAFailureStillNamesOne() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessAsNewVersionResult\":{\"success\":false,\"errorMessage\":\"Read-back disagreed.\","
			+ "\"versionName\":\"UsrProcCustom2\",\"versionSchemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\"}}");
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a failed save is an error to the caller even when a version came out of it")
			.WithMessage("*Read-back disagreed.*")
			.WithMessage("*UsrProcCustom2*")
			.WithMessage("*cannot be deleted*");
	}

	[Test]
	[Description("A failure raised BEFORE the save names no version, and the message stays exactly what the server said — appending 'the version exists' there would send the caller looking for something that was never created.")]
	public void ModifyAsNewVersion_ShouldRelayTheServerMessageUnchanged_WhenNothingWasCreated() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessAsNewVersionResult\":{\"success\":false,"
			+ "\"errorMessage\":\"Package 'Custom' is locked.\"}}");
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the server refused before saving anything")
			.WithMessage("Package 'Custom' is locked.");
	}

	[Test]
	[Description("A failed version save that names the refusing operation carries that index to the caller. The server writes failedOperationIndex at all eleven of its refusal sites, and this throw is the only path it can travel - the success record is built only on success. Without the DTO member System.Text.Json discards it silently, so a 40-operation batch refused at index 17 answered with a bare sentence and the agent bisected against a live environment.")]
	public void ModifyAsNewVersion_ShouldNameTheRefusingOperation_WhenTheServerReportsAnIndex() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessAsNewVersionResult\":{\"success\":false,\"errorMessage\":\"Element 'X' was not found.\","
			+ "\"appliedOperations\":2,\"failedOperationIndex\":2}}");
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		InvalidOperationException thrown = act.Should().Throw<InvalidOperationException>(
			because: "a refused version save still fails the call").Which;
		thrown.Message.Should().Contain("index 2",
			because: "the caller has to learn WHICH operation refused, which is the whole reason the server "
				+ "reports an index separately from the completion count");
		thrown.Message.Should().Contain("2 operation(s)",
			because: "the count is the other half of the same diagnosis and the recovery route for a caller "
				+ "that gets no index at all - reporting one and dropping the other tells half a story");
	}

	[Test]
	[Description("A failed version save whose failure blames no single operation says nothing about an index. The server sends none when the failure came after the operation loop - a read-only package, the save itself - and an older package never sends the field, so inventing 'index 0' from a missing value would name a descriptor that was never the problem.")]
	public void ModifyAsNewVersion_ShouldNotInventAnIndex_WhenTheServerReportsNone() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessAsNewVersionResult\":{\"success\":false,\"errorMessage\":\"The schema is invalid.\","
			+ "\"appliedOperations\":2}}");
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a refused version save still fails the call")
			.Which.Message.Should().NotContain("index",
				because: "an absent index must stay absent - a plain int default would have said 'index 0', "
					+ "which is the ambiguity the nullable field exists to prevent");
	}

	[Test]
	[Description("A body clio cannot parse is reported as an unreadable response rather than as a parser error. AGENTS.md records that a wrong ProcessDesignService path answers with an HTML error page, so this is reachable. On THIS path the stakes are higher than on the in-place edit: the unexplained artifact may be a version that is already persisted and that the platform offers no way to delete, so the message has to say that a retry would allocate a second one.")]
	public void ModifyAsNewVersion_ShouldReportAnUnreadableResponse_WhenTheBodyIsNotTheEnvelope() {
		// Arrange — the shape a server-side deserialization failure returns: valid JSON, wrong document
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(
			"[{\"ExceptionType\":\"System.Runtime.Serialization.SerializationException\"}]");
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		InvalidOperationException thrown = act.Should().Throw<InvalidOperationException>(
			because: "an unreadable response is a real outcome and has to be named as one").Which;
		thrown.Message.Should().Contain("UNKNOWN",
			because: "whether a version was created cannot be determined from a body clio could not read, and "
				+ "saying so is the only honest report");
		thrown.Message.Should().Contain("cannot be deleted",
			because: "a version that DID persist is permanent, which is what makes this different from the "
				+ "in-place path and what the caller must know before acting");
		thrown.Message.Should().NotContain("BytePositionInLine",
			because: "a .NET parser message names a type and a byte offset, written for a developer reading a "
				+ "stack trace and unusable to the agent that receives it");
		thrown.InnerException.Should().BeOfType<JsonException>(
			because: "the parser detail is kept for a developer rather than discarded");
	}

	[Test]
	[Description("Reads the server's warnings[] off a SUCCESSFUL save — the channel carrying outcomes that applied but are not what the caller assumes, which an undeclared member would drop in silence.")]
	public void ModifyAsNewVersion_ShouldReadWarnings_WhenServerReportsThem() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(VersionUrl, Arg.Any<string>()).Returns(
			"{\"ModifyProcessAsNewVersionResult\":{\"success\":true,\"versionName\":\"UsrProcCustom2\","
			+ "\"version\":2,\"appliedOperations\":1,\"warnings\":[\"Connection 'OmniChat' is not registered\"]}}");
		ModifyProcessAsNewVersionService service = CreateService(client);

		// Act
		ModifyProcessAsNewVersionResult result = service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, Operations));

		// Assert
		result.Warnings.Should().ContainSingle(
				because: "the server raised exactly one notice on this save")
			.Which.Should().Contain("OmniChat",
				because: "an undeclared member would drop it in silence, and the caller would never learn the "
					+ "save was not quite what they asked for");
	}

	[Test]
	[Description("Refuses a request with no source identity before any HTTP call.")]
	public void ModifyAsNewVersion_ShouldThrow_WhenNoIdentityGiven() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		ModifyProcessAsNewVersionService service = CreateService(client);

		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest(null, null, null, Operations));

		act.Should().Throw<ArgumentException>(because: "a source (name or uid) is required");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("Rejects operations content that is not a JSON array.")]
	public void ModifyAsNewVersion_ShouldThrow_WhenOperationsNotArray() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		ModifyProcessAsNewVersionService service = CreateService(client);

		Action act = () => service.ModifyAsNewVersion(Env,
			new ModifyProcessAsNewVersionRequest("UsrProc", null, null, "{}"));

		act.Should().Throw<InvalidOperationException>(because: "operations must be a JSON array of operations")
			.WithMessage("*array*");
	}

	// The service wraps the request under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
