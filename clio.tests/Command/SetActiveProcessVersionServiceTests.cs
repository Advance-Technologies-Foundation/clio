using System;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// HTTP-layer tests for <see cref="SetActiveProcessVersionService"/>: the wrapped
/// <c>{"request":{name|uid}}</c> body, the resolved route, and each response branch. The tool tests
/// substitute the command, so this is the only coverage of the actual clio→server contract for activation.
/// </summary>
/// <remarks>
/// The failure branches carry most of the weight here. The platform logs and SWALLOWS a failure to deactivate
/// a sibling, so "activation failed" without naming what IS active leaves the caller unable to tell whether
/// the environment runs the old version, the new one, or two at once with package order deciding.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SetActiveProcessVersionServiceTests {

	private const string Env = "sandbox";
	private const string ActivateUrl = "http://sandbox/0/rest/ProcessDesignService/SetActiveProcessVersion";

	private const string SuccessBody =
		"{\"SetActiveProcessVersionResult\":{\"success\":true,\"activeVersionName\":\"UsrProcCustom2\","
		+ "\"activeVersionSchemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"deactivationFailureCount\":0}}";

	private static SetActiveProcessVersionService CreateService(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.SetActiveProcessVersion, env).Returns(ActivateUrl);
		return new SetActiveProcessVersionService(settings, factory, urlBuilder, Substitute.For<ILogger>());
	}

	[Test]
	[Description("Posts the version identity wrapped under 'request' to the SetActiveProcessVersion route and returns the version the server READ BACK, not the one that was asked for.")]
	public void SetActiveVersion_ShouldPostWrappedRequest_AndReturnTheReadBackVersion_OnSuccess() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ActivateUrl, Arg.Any<string>()).Returns(SuccessBody);
		SetActiveProcessVersionService service = CreateService(client);

		// Act
		SetActiveProcessVersionResult result =
			service.SetActiveVersion(Env, new SetActiveProcessVersionRequest("UsrProcCustom2", null));

		// Assert
		result.ActiveVersionName.Should().Be("UsrProcCustom2",
			because: "the reported name comes from the manager AFTER the write; echoing the request would make "
				+ "a swallowed deactivation failure invisible");
		result.ActiveVersionSchemaUId.Should().Be("5c58c4c4-134b-4744-9c67-96d9c69c9d55",
			because: "the caller needs the identity of what is ACTUALLY active, not of what it asked for");
		result.DeactivationFailureCount.Should().Be(0,
			because: "a clean activation leaves exactly one member of the family flagged active");
		client.Received(1).ExecutePostRequest(ActivateUrl, Arg.Is<string>(body =>
			Wrapped(body)["name"].GetValue<string>() == "UsrProcCustom2"));
	}

	[Test]
	[Description("A version identified by uid is sent as 'uid' and no 'name' member — the two are alternatives, and sending an empty name alongside would make the server pick between an identity and an empty string.")]
	public void SetActiveVersion_ShouldSendUidAlone_WhenIdentifiedByUid() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ActivateUrl, Arg.Any<string>()).Returns(SuccessBody);
		SetActiveProcessVersionService service = CreateService(client);

		// Act
		service.SetActiveVersion(Env,
			new SetActiveProcessVersionRequest(null, "5c58c4c4-134b-4744-9c67-96d9c69c9d55"));

		// Assert
		// NSubstitute's Received() takes no reason argument, so the assertion's why lives here: the request
		// carries exactly the identity the caller supplied, with nothing standing in for the other one.
		client.Received(1).ExecutePostRequest(ActivateUrl, Arg.Is<string>(body =>
			Wrapped(body)["uid"].GetValue<string>() == "5c58c4c4-134b-4744-9c67-96d9c69c9d55"
			&& Wrapped(body)["name"] == null));
	}

	[Test]
	[Description("A read-back mismatch FAILS and names the version the environment actually reports as actual — the whole reason the result is read back rather than echoed.")]
	public void SetActiveVersion_ShouldFailNamingTheActualVersion_WhenTheReadBackDisagrees() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ActivateUrl, Arg.Any<string>()).Returns(
			"{\"SetActiveProcessVersionResult\":{\"success\":false,"
			+ "\"errorMessage\":\"The requested version is not the active one after the write.\","
			+ "\"activeVersionName\":\"UsrProcCustom1\","
			+ "\"activeVersionSchemaUId\":\"11111111-2222-3333-4444-555555555555\"}}");
		SetActiveProcessVersionService service = CreateService(client);

		// Act
		Action act = () => service.SetActiveVersion(Env,
			new SetActiveProcessVersionRequest("UsrProcCustom2", null));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an activation that did not take effect is an error, not a warning on a success")
			.WithMessage("*not the active one*")
			.WithMessage("*UsrProcCustom1*");
	}

	[Test]
	[Description("Siblings left flagged active are named in the failure with what it means: the platform swallowed a deactivation failure, so package order — not this call — decides which version runs.")]
	public void SetActiveVersion_ShouldSayPackageOrderDecides_WhenSiblingsRemainActive() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ActivateUrl, Arg.Any<string>()).Returns(
			"{\"SetActiveProcessVersionResult\":{\"success\":false,\"errorMessage\":\"Sibling still active.\","
			+ "\"activeVersionName\":\"UsrProcCustom2\",\"deactivationFailureCount\":2}}");
		SetActiveProcessVersionService service = CreateService(client);

		// Act
		Action act = () => service.SetActiveVersion(Env,
			new SetActiveProcessVersionRequest("UsrProcCustom2", null));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "two members flagged active is an unresolved state the caller has to act on")
			.WithMessage("*2 sibling(s)*")
			.WithMessage("*package order*");
	}

	[Test]
	[Description("Reads the server's warnings[] off a SUCCESSFUL activation — notably that the write re-saved every member of the family, which is a write nobody asked for by name.")]
	public void SetActiveVersion_ShouldReadWarnings_WhenServerReportsThem() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ActivateUrl, Arg.Any<string>()).Returns(
			"{\"SetActiveProcessVersionResult\":{\"success\":true,\"activeVersionName\":\"UsrProcCustom2\","
			+ "\"warnings\":[\"Activation re-saved all 3 members of the family.\"]}}");
		SetActiveProcessVersionService service = CreateService(client);

		// Act
		SetActiveProcessVersionResult result =
			service.SetActiveVersion(Env, new SetActiveProcessVersionRequest("UsrProcCustom2", null));

		// Assert
		result.Warnings.Should().ContainSingle(
				because: "the server raised exactly one notice on this activation")
			.Which.Should().Contain("re-saved",
				because: "a family-wide re-save is a side effect the caller did not request and would not "
					+ "otherwise see");
	}

	[Test]
	[Description("Carries a non-zero deactivation count out of a SUCCESSFUL activation. Nothing in this repository proves the platform cannot answer success:true beside a swallowed sibling failure — the DTO member exists because it was considered reachable — and the count was previously only ever paired with success:false, so the combination was untested rather than shown impossible.")]
	public void SetActiveVersion_ShouldReportSiblingsStillActive_WhenTheActivationSucceededAnyway() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(ActivateUrl, Arg.Any<string>()).Returns(
			"{\"SetActiveProcessVersionResult\":{\"success\":true,\"activeVersionName\":\"UsrProcCustom2\","
			+ "\"activeVersionSchemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\","
			+ "\"deactivationFailureCount\":2}}");
		SetActiveProcessVersionService service = CreateService(client);

		// Act
		SetActiveProcessVersionResult result =
			service.SetActiveVersion(Env, new SetActiveProcessVersionRequest("UsrProcCustom2", null));

		// Assert
		result.DeactivationFailureCount.Should().Be(2,
			because: "this is the one signal the read-back design exists to expose, and dropping it on the "
				+ "success path hands the caller an unqualified success over a family where package order "
				+ "decides what runs");
		result.ActiveVersionName.Should().Be("UsrProcCustom2",
			because: "the values the server did establish still travel beside the count");
	}

	[Test]
	[Description("Refuses a request with no version identity before any HTTP call.")]
	public void SetActiveVersion_ShouldThrow_WhenNoIdentityGiven() {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		SetActiveProcessVersionService service = CreateService(client);

		Action act = () => service.SetActiveVersion(Env, new SetActiveProcessVersionRequest(null, null));

		act.Should().Throw<ArgumentException>(because: "a version (name or uid) is required");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	// The service wraps the request under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
