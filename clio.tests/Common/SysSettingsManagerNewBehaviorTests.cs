using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using mockFs = System.IO.Abstractions;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class SysSettingsManagerNewBehaviorTests {

	#region Helpers

	private static readonly Guid AllUsersAdminUnitId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private static readonly mockFs.IFileSystem FileSystem
		= TestFileSystem.MockExamplesFolder("deployments-manifest");

	private static EnvironmentSettings EnvironmentSettings => new() {
		Uri = "https://localhost",
		Login = "Supervisor",
		Password = "Supervisor",
		IsNetCore = false
	};

	// A neutral DataService envelope for the substituted client. Reads go through the data provider,
	// so this only stands in for the write endpoints a test does not assert on.
	private const string AcceptedDataServiceResponse = "{\"rows\":[],\"success\":true}";

	/// <summary>The repository root, four levels above the test output directory.</summary>
	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	private static IApplicationClient BuildAcceptedClient() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(AcceptedDataServiceResponse);
		return applicationClient;
	}

	/// <summary>
	/// The data provider a rejected session actually produces: ATF's provider swallows the failure into
	/// Success = false with an empty payload, and only ClassifyingDataProvider turns that into a failure
	/// the caller cannot mistake for an empty result.
	/// </summary>
	private static IDataProvider BuildRejectedProvider(string errorMessage = ExpiredCredentialsError) =>
		new ClassifyingDataProvider(new UnsuccessfulDataProvider(errorMessage));

	/// <summary>What ATF reports when Creatio answers a rejected credential with its login page.</summary>
	private const string LoginPageParserError =
		"Unexpected character encountered while parsing value: <. Path \'\', line 0, position 0.";

	/// <summary>What ATF reports when the platform names the credential outcome in prose.</summary>
	private const string ExpiredCredentialsError = "5: Your password has expired.";

	/// <summary>
	/// The login page Creatio serves under HTTP 200 for a rejected session. Unlike the read path, a write
	/// keeps this body, so its auth-routing marker makes the rejection provable.
	/// </summary>
	private const string LoginPageBody =
		"<!DOCTYPE html><html><head><title>Creatio</title></head>"
		+ "<body><form action=\"/Login/NuiLogin.aspx\"></form></body></html>";

	private static ISysSettingsManager BuildSut(IDataProvider dataProvider,
		IApplicationClient applicationClient = null) {
		BindingsModule bm = new(FileSystem);
		IServiceProvider container = bm.Register(EnvironmentSettings);
		return new SysSettingsManager(
			applicationClient ?? BuildAcceptedClient(),
			container.GetRequiredService<IServiceUrlBuilder>(),
			dataProvider,
			container.GetRequiredService<IWorkingDirectoriesProvider>(),
			container.GetRequiredService<IFileSystem>(),
			FileSystem,
			Substitute.For<ILogger>());
	}

	private static DataProviderMock SetupSysSettingsMock(
		Guid settingId, string code, string valueTypeName,
		Dictionary<string, object> valueRow = null) {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", settingId },
				{ "Code", code },
				{ "Name", code },
				{ "ValueTypeName", valueTypeName },
				{ "Description", "" },
				{ "IsCacheable", true },
				{ "IsPersonal", false },
				{ "IsSSPAvailable", false }
			}
		});
		List<Dictionary<string, object>> values = [];
		if (valueRow is not null) {
			Dictionary<string, object> defaults = new() {
				{ "Id", Guid.NewGuid() },
				{ "SysSettings", settingId },
				{ "SysAdminUnit", AllUsersAdminUnitId },
				{ "IsDef", true },
				{ "TextValue", string.Empty },
				{ "IntegerValue", 0 },
				{ "FloatValue", 0m },
				{ "BooleanValue", false },
				{ "DateTimeValue", new DateTime(1900, 1, 1) },
				{ "GuidValue", Guid.Empty }
			};
			foreach (KeyValuePair<string, object> kv in valueRow) {
				defaults[kv.Key] = kv.Value;
			}
			values.Add(defaults);
		}
		providerMock.MockItems("SysSettingsValue").Returns(values);
		return providerMock;
	}

	#endregion

	#region GetSysSettingValueByCode — provider-first / model fallback

	[Test]
	[Description("Provider-first ordering: when the data provider already exposes a non-empty value, it is returned without consulting the typed model fallback.")]
	public void GetSysSettingValueByCode_PrefersProviderValue_WhenProviderReturnsNonEmpty() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("MyText").Returns("provider-value");
		ISysSettingsManager sut = BuildSut(dataProvider);

		sut.GetSysSettingValueByCode("MyText").Should().Be("provider-value",
			because: "provider-first ordering preserves legacy behavior for text/personal settings");
	}

	[Test]
	[Description("Typed model fallback: Boolean settings round-trip through SysSettingsValue.BooleanValue formatted as lower-case 'true' / 'false'.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForBoolean() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyBool", "Boolean",
			new() { { "BooleanValue", true } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyBool").Should().Be("true",
			because: "Boolean values must be returned as invariant lowercase string");
	}

	[Test]
	[Description("Typed model fallback: Integer settings round-trip through SysSettingsValue.IntegerValue formatted with InvariantCulture.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForInteger() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyInt", "Integer",
			new() { { "IntegerValue", 42 } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyInt").Should().Be("42",
			because: "Integer values are emitted via InvariantCulture, with no thousands separator or culture-specific suffix");
	}

	[Test]
	[Description("Typed model fallback: Float / Money / Decimal / Currency settings reuse the FloatValue column and InvariantCulture formatting.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForFloat() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyFloat", "Float",
			new() { { "FloatValue", 3.14m } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyFloat").Should().Be("3.14",
			because: "Float/Money/Decimal/Currency must use invariant culture (period, not comma)");
	}

	[Test]
	[Description("Typed model fallback: Money is the canonical Creatio alias for Currency and must reuse the FloatValue formatting path.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForMoney() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyMoney", "Money",
			new() { { "FloatValue", 1500.5m } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyMoney").Should().Be("1500.5",
			because: "Money is treated as Float on the read side and InvariantCulture renders the decimal separator as a period");
	}

	[Test]
	[Description("Typed model fallback: Date settings format DateTimeValue as 'yyyy-MM-dd' under InvariantCulture.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForDate() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyDate", "Date",
			new() { { "DateTimeValue", new DateTime(2026, 1, 15) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyDate").Should().Be("2026-01-15",
			because: "Date formatting uses 'yyyy-MM-dd' under InvariantCulture so the wire representation is stable across locales");
	}

	[Test]
	[Description("Typed model fallback: Time settings format DateTimeValue as 'HH:mm:ss' under InvariantCulture.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForTime() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyTime", "Time",
			new() { { "DateTimeValue", new DateTime(1900, 1, 1, 14, 30, 0) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyTime").Should().Be("14:30:00",
			because: "Time formatting uses 'HH:mm:ss' under InvariantCulture so the wire representation is stable across locales");
	}

	[Test]
	[Description("Typed model fallback: DateTime settings format DateTimeValue with the round-trip 'o' specifier so Kind information is preserved.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForDateTime() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyDt", "DateTime",
			new() { { "DateTimeValue", new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyDt").Should().Contain("2026-02-01",
			because: "DateTime should be formatted as ISO 8601 round-trip");
	}

	[Test]
	[Description("Typed model fallback: Lookup settings expose the GUID stored in SysSettingsValue.GuidValue.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForLookup() {
		Guid id = Guid.NewGuid();
		Guid guidValue = new("2cfdcf5d-744b-4e0a-b6d0-fbd905fea8ed");
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyLookup", "Lookup",
			new() { { "GuidValue", guidValue } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyLookup").Should().Be(guidValue.ToString(),
			because: "Lookup values surface as the underlying GUID; the platform stores the foreign-key on SysSettingsValue.GuidValue");
	}

	[Test]
	[Description("When a setting exists but has no SysSettingsValue rows, the manager returns an empty string rather than throwing.")]
	public void GetSysSettingValueByCode_ReturnsEmpty_WhenNoValueRowExists() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "EmptyInt", "Integer", valueRow: null);
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("EmptyInt").Should().BeEmpty(
			because: "missing SysSettingsValue rows should produce an empty result");
	}

	#endregion

	#region FindSchemaUIdByName

	[Test]
	[Description("FindSchemaUIdByName resolves a schema name to its UId via the data provider's SysSchema model.")]
	public void FindSchemaUIdByName_ReturnsUId_WhenSchemaExists() {
		DataProviderMock providerMock = new();
		Guid expectedUId = Guid.NewGuid();
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", Guid.NewGuid() },
				{ "UId", expectedUId },
				{ "Name", "UsrPhoneFormat" }
			}
		});
		ISysSettingsManager sut = BuildSut(providerMock);

		Guid? actual = sut.FindSchemaUIdByName("UsrPhoneFormat");

		actual.Should().Be(expectedUId,
			because: "FindSchemaUIdByName resolves the schema UId via SysSchema model and must return the value stored in UId");
	}

	[Test]
	[Description("FindSchemaUIdByName returns null (rather than throwing) when no SysSchema row matches the requested name.")]
	public void FindSchemaUIdByName_ReturnsNull_WhenSchemaMissing() {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>>());
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.FindSchemaUIdByName("Nonexistent").Should().BeNull(
			because: "the lookup helper must return null (not throw) for codes that resolve to no SysSchema row");
	}

	[Test]
	[Description("FindSchemaUIdByName treats an empty / whitespace name as a missing lookup and returns null without contacting the provider.")]
	public void FindSchemaUIdByName_ReturnsNull_ForBlankInput() {
		ISysSettingsManager sut = BuildSut(new DataProviderMock());

		sut.FindSchemaUIdByName(null).Should().BeNull(
			because: "a null name is invalid input and the helper short-circuits without contacting the provider");
		sut.FindSchemaUIdByName(string.Empty).Should().BeNull(
			because: "an empty name is invalid input and the helper short-circuits without contacting the provider");
		sut.FindSchemaUIdByName("   ").Should().BeNull(
			because: "a whitespace-only name is invalid input and the helper short-circuits without contacting the provider");
	}

	#endregion

	#region Authentication failure handling

	[Test]
	[Description("list-sys-settings fails closed when the data provider reports a rejected session, instead of exposing ATF's empty collection as a real (empty) catalog.")]
	public void GetAllSysSettingsWithValues_ShouldThrowAuthenticationException_WhenCredentialsAreRejected() {
		// Arrange
		ISysSettingsManager sut = BuildSut(BuildRejectedProvider());

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues(includeBinary: true);

		// Assert
		AuthenticationException exception = act.Should().Throw<AuthenticationException>(
			because: "Models<T>() drops the response's Success flag, so without the classifying decorator a rejected read reaches the caller as an empty list (issue #1222)").Which;
		exception.Message.Should().Contain("password has expired",
			because: "the actionable platform cause must survive the fail-closed authentication mapping");
		exception.Message.Should().Contain("Verify the environment credentials",
			because: "an automation caller needs a recovery action rather than a false empty-list success");
	}

	[Test]
	[Description("list-sys-settings fails closed on the shape a rejected session really produces - Creatio's login page under HTTP 200 - but names BOTH causes, because ATF keeps only the parser message and a gateway page produces the identical text.")]
	public void GetAllSysSettingsWithValues_ShouldNameBothCauses_ForALoginPageResponse() {
		// Arrange
		ISysSettingsManager sut = BuildSut(BuildRejectedProvider(LoginPageParserError));

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues(includeBinary: true);

		// Assert
		Exception thrown = act.Should().Throw<InvalidOperationException>(
			because: "an HTML body where the DataService contract requires JSON must stop the read - returning an empty catalog is the defect (issue #1222)").Which;
		thrown.Message.Should().Contain("session was rejected",
			because: "an expired password is the most likely cause and has to be offered");
		thrown.Message.Should().Contain("proxy, gateway, wrong path",
			because: "the read path cannot see the body, so it must not claim the credential cause outright");
	}

	[Test]
	[Description("update-sys-setting fails before it writes when the data provider reports a rejected session, so an expired password is not reduced to a generic write failure.")]
	public void UpdateSysSetting_ShouldThrowAuthenticationException_WhenCredentialsAreRejected() {
		// Arrange
		IApplicationClient applicationClient = BuildAcceptedClient();
		ISysSettingsManager sut = BuildSut(BuildRejectedProvider(), applicationClient);

		// Act
		Action act = () => sut.UpdateSysSetting("UsrAuthFailure", "value");

		// Assert
		AuthenticationException exception = act.Should().Throw<AuthenticationException>(
			because: "the update reads the setting's type first, and that read is where a rejected session is detectable").Which;
		exception.Message.Should().Contain("password has expired",
			because: "the actionable platform cause must be preserved so the operator knows what to fix");
		exception.Message.Should().Contain("Verify the environment credentials",
			because: "auth errors must carry a recovery action, not just a type marker");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the rejected read must stop the update before any write request is sent");
	}

	[Test]
	[Description("update-sys-setting for a Lookup fails closed too: its reference-schema resolution is the same rejected read.")]
	public void UpdateSysSetting_ShouldThrowAuthenticationException_ForALookupValue() {
		// Arrange
		IApplicationClient applicationClient = BuildAcceptedClient();
		ISysSettingsManager sut = BuildSut(BuildRejectedProvider(), applicationClient);

		// Act
		Action act = () => sut.UpdateSysSetting("UsrAuthLookup", "Contact", "Lookup");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "a Lookup write resolves the setting through the provider before posting, so it must fail closed on a rejected session as well");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "no lookup value may be written on an unproven session");
	}

	[Test]
	[Description("create-sys-setting fails closed for a Text setting when the initial value cannot be applied because the session was rejected.")]
	public void TryCreateSysSetting_ShouldReportAuthenticationFailure_ForAText() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(InsertSuccessJson);
		ISysSettingsManager manager = BuildSut(BuildRejectedProvider(), applicationClient);
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		SysSettingCreateResult result = command.TryCreateSysSetting(
			new CreateSysSettingArgs("local", "UsrAuthCreate", "UsrAuthCreate", "Text", Value: "seed"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a create whose value could not be applied on a rejected session must not be reported as done");
		result.Error.Should().Be("Authentication error creating sys-setting.",
			because: "the caller has to be told the credentials are the problem rather than the payload");
	}

	[Test]
	[Description("create-sys-setting fails closed for a Lookup setting: resolving the reference schema is a provider read, so a rejected session stops the create before anything is written.")]
	public void TryCreateSysSetting_ShouldReportAuthenticationFailure_ForALookup() {
		// Arrange
		IApplicationClient applicationClient = BuildAcceptedClient();
		ISysSettingsManager manager = BuildSut(BuildRejectedProvider(), applicationClient);
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		SysSettingCreateResult result = command.TryCreateSysSetting(
			new CreateSysSettingArgs("local", "UsrAuthLookup", "UsrAuthLookup", "Lookup",
				ReferenceSchemaName: "Contact"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a schema lookup that failed because the session was rejected must not be reported as 'schema not found' or as a success");
		result.Error.Should().Be("Authentication error creating sys-setting.",
			because: "the credential cause has to reach the caller");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the rejected read must stop the create before the insert request is sent");
	}

	[Test]
	[Description("update-sys-setting surfaces the credential cause through the MCP result envelope rather than a generic failure.")]
	public void TryUpdateSysSetting_ShouldReportAuthenticationFailure_WhenCredentialsAreRejected() {
		// Arrange
		ISysSettingsManager manager = BuildSut(BuildRejectedProvider());
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		SysSettingUpdateResult result = command.TryUpdateSysSetting(
			new UpdateSysSettingArgs("local", "UsrAuthFailure", "value"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a write on a rejected session did not happen and must not be reported as done");
		result.Error.Should().Be("Authentication error updating sys-setting.",
			because: "the MCP caller needs the credential diagnosis, not a generic write failure");
	}

	[Test]
	[Description("An HTTP 401 thrown out of the provider maps to an authentication failure instead of leaking a generic error to MCP callers.")]
	public void GetAllSysSettingsWithValues_ShouldMapHttpUnauthorizedException() {
		// Arrange
		IDataProvider dataProvider = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).")));
		ISysSettingsManager sut = BuildSut(dataProvider);

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues();

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "an HTTP 401 means the stored Creatio credentials were rejected and must not appear as an empty list");
	}

	[Test]
	[Description("A refused connection whose message carries a port containing the digits 401 stays a network error: the read must not be wrapped as an authentication failure and send the operator off to repair working credentials.")]
	public void GetAllSysSettingsWithValues_ShouldNotTreatAPortContaining401AsRejectedCredentials() {
		// Arrange - a bare Contains("401") used to read :40124 as a 401.
		IDataProvider dataProvider = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException("Connection refused at http://localhost:40124")));
		ISysSettingsManager sut = BuildSut(dataProvider);

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues();

		// Assert
		Exception thrown = act.Should().Throw<HttpRequestException>(
			because: "the transport fault keeps its own type so CategorizeError can report 'Network error ...' rather than a composed generic message").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "a port is not a status code, and misclassifying it hides the real cause");
	}

	[Test]
	[Description("A correlation id that happens to contain 401 between letters stays a network error, for the same reason a port does.")]
	public void GetAllSysSettingsWithValues_ShouldNotTreatACorrelationIdContaining401AsRejectedCredentials() {
		// Arrange
		IDataProvider dataProvider = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException("Upstream failure. Correlation id x401y")));
		ISysSettingsManager sut = BuildSut(dataProvider);

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues();

		// Assert
		Exception thrown = act.Should().Throw<HttpRequestException>(
			because: "the upstream failure must stop the read and keep its transport type").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "401 surrounded by letters is part of an identifier, not a status code");
	}

	[Test]
	[Description("A standalone 401 in the transport prose is still rejected credentials, so tightening the token did not simply switch the signal off.")]
	public void GetAllSysSettingsWithValues_ShouldStillTreatAStandalone401AsRejectedCredentials() {
		// Arrange
		IDataProvider dataProvider = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException("The remote server returned an error: 401.")));
		ISysSettingsManager sut = BuildSut(dataProvider);

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues();

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "a genuine 401 must keep its diagnosis; the narrowed match does not remove the signal");
	}

	[Test]
	[Description("A provider failure that names no credential problem is reported as a failure - never as an empty list - but keeps its own diagnosis.")]
	public void GetAllSysSettingsWithValues_ShouldReportAGenericProviderFailureAsAFailure() {
		// Arrange
		ISysSettingsManager sut = BuildSut(BuildRejectedProvider("SqlException: deadlock victim"));

		// Act
		Action act = () => sut.GetAllSysSettingsWithValues();

		// Assert
		Exception thrown = act.Should().Throw<InvalidOperationException>(
			because: "an unsuccessful response must never be handed back as a legitimate empty catalog").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "a deadlock is not a credential failure");
		thrown.Message.Should().Contain("deadlock victim",
			because: "the platform's own text is the only diagnosable detail available");
	}

	[Test]
	[Description("The CLI update overload logs the credential diagnosis: a rejected session must reach the operator as an authentication failure, not the opaque 'is not updated.' line.")]
	public void TryUpdateSysSetting_Cli_ShouldLogAuthenticationFailure_WhenCredentialsAreRejected() {
		// Arrange
		ISysSettingsManager manager = BuildSut(BuildRejectedProvider());
		ILogger logger = Substitute.For<ILogger>();
		List<string> loggedErrors = [];
		logger.When(value => value.WriteError(Arg.Any<string>()))
			.Do(call => loggedErrors.Add(call.ArgAt<string>(0)));
		SysSettingsCommand command = new(manager, logger, Substitute.For<IFileSystem>());

		// Act
		command.TryUpdateSysSetting(new SysSettingsOptions {
			Code = "UsrAuthFailure", Value = "value", Type = "Text"
		});

		// Assert
		loggedErrors.Should().ContainSingle(message =>
				message.Contains("UsrAuthFailure") && message.Contains("Authentication error updating sys-setting."),
			because: "a rejected session must reach the operator as an authentication failure naming the setting, not the opaque 'is not updated.' line");
	}

	[Test]
	[Description("The CLI update overload logs the network diagnosis for a refused connection, so a transport fault is not reported as a value the environment refused.")]
	public void TryUpdateSysSetting_Cli_ShouldLogANetworkError_ForARefusedConnection() {
		// Arrange
		IDataProvider dataProvider = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException("Connection refused at http://localhost:40124")));
		ISysSettingsManager manager = BuildSut(dataProvider);
		ILogger logger = Substitute.For<ILogger>();
		List<string> loggedErrors = [];
		logger.When(value => value.WriteError(Arg.Any<string>()))
			.Do(call => loggedErrors.Add(call.ArgAt<string>(0)));
		SysSettingsCommand command = new(manager, logger, Substitute.For<IFileSystem>());

		// Act
		command.TryUpdateSysSetting(new SysSettingsOptions {
			Code = "UsrNetworkFailure", Value = "value", Type = "Text"
		});

		// Assert
		loggedErrors.Should().ContainSingle(message =>
				message.Contains("UsrNetworkFailure") && message.Contains("Network error updating sys-setting."),
			because: "a refused connection is a transport fault and must not be reported as a value the environment refused");
	}


	#endregion

	#region InsertSysSetting — referenceSchemaUId + new type aliases

	// A gateway/WAF/404 page that is NOT the Creatio login page: ThrowIfSessionRejected only fires when the
	// body PROVES a rejected session, so this shape is the one that reaches JsonSerializer.Deserialize on the
	// write path. It is what makes SysSettingsCommand.CategorizeError's JsonException arm reachable, and
	// nothing exercised it before.
	private const string NonJsonGatewayPage = "<html><head><title>404 Not Found</title></head><body>404</body></html>";

	[Test]
	[Description("A non-JSON gateway/404 answer to InsertSysSettingRequest surfaces as JsonException rather than a parsed response, so the write path reaches the JsonException arm of SysSettingsCommand.CategorizeError instead of the uncategorized \"Failed creating sys-setting.\".")]
	public void InsertSysSetting_ThrowsJsonException_WhenWriteEndpointAnswersWithANonJsonPage() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(NonJsonGatewayPage);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(NonJsonGatewayPage);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		Action act = () => sut.InsertSysSetting("Plain", "UsrPlain", "Text");

		act.Should().Throw<JsonException>(
			because: "a proxy/gateway page is not a rejected session, so ThrowIfSessionRejected lets it through to the deserializer - and the JsonException it raises is what CategorizeError classifies");
	}

	private const string InsertSuccessJson =
		"""{"responseStatus":{"ErrorCode":"","Message":"","Errors":[]},"id":"acf40078-ba48-4285-9f3b-44ebafa28cac","rowsAffected":1,"nextPrcElReady":false,"success":true}""";

	[Test]
	[Description("Insert serializes the supplied referenceSchemaUId into the JSON payload so the platform creates a Lookup setting bound to the chosen entity schema.")]
	public void InsertSysSetting_SerializesReferenceSchemaUId_WhenProvidedForLookup() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns(InsertSuccessJson);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(InsertSuccessJson);
		Guid refUId = new("b80eb7bb-193c-4bb2-ad51-e0beb1670278");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.InsertSysSetting("Lookup Setting", "UsrLookupSetting", "Lookup",
			referenceSchemaUId: refUId);

		capturedBody.Should().NotBeNull(
			because: "the platform request must be issued and its body captured for inspection");
		capturedBody.Should().Contain("\"referenceSchemaUId\":\"b80eb7bb-193c-4bb2-ad51-e0beb1670278\"",
			because: "Lookup sys-settings must carry the reference schema UId so the picker can render");
		capturedBody.Should().Contain("\"valueTypeName\":\"Lookup\"",
			because: "the platform expects the Creatio internal type name 'Lookup' on the wire");
	}

	[Test]
	[Description("Insert omits the referenceSchemaUId from the JSON payload when null or Guid.Empty so non-Lookup settings do not declare an unintended reference.")]
	public void InsertSysSetting_OmitsReferenceSchemaUId_WhenNotProvided() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns(InsertSuccessJson);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(InsertSuccessJson);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.InsertSysSetting("Plain", "UsrPlain", "Text");

		capturedBody.Should().NotContain("referenceSchemaUId",
			because: "null reference schema UId is skipped by the serializer to preserve legacy payload shape");
	}

	[TestCase("Money", "Money")]
	[TestCase("Float", "Float")]
	[TestCase("Binary", "Binary")]
	[TestCase("Currency", "Money")]
	[TestCase("Decimal", "Float")]
	[Description("Insert accepts the legacy aliases Currency and Decimal and maps them to the canonical Creatio internal names Money and Float on the wire.")]
	public void InsertSysSetting_MapsTypeAliasesToCreatioInternalNames(string input, string expected) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns(InsertSuccessJson);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(InsertSuccessJson);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.InsertSysSetting("N", "UsrCode", input);

		capturedBody.Should().Contain($"\"valueTypeName\":\"{expected}\"",
			because: "the serialized type must use the Creatio internal name regardless of caller alias");
	}

	#endregion

	#region UpdateSysSetting — saveResult parsing

	[Test]
	[Description("Update parses the saveResult dictionary by code instead of relying on the unreliable top-level success flag — a per-code true means the value landed.")]
	public void UpdateSysSetting_ReturnsTrue_WhenSaveResultReportsSuccessForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"UsrAny":true},"rowsAffected":-1,"nextPrcElReady":false,"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"""{"saveResult":{"UsrAny":true},"rowsAffected":-1,"nextPrcElReady":false,"success":false}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeTrue(
			because: "saveResult[code] is the authoritative per-setting result; top-level success is unreliable");
	}

	[Test]
	[Description("Update returns false when the platform reports saveResult[code] = false, surfacing the platform's error message when available.")]
	public void UpdateSysSetting_ReturnsFalse_WhenSaveResultReportsFailureForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"UsrAny":false},"rowsAffected":-1,"nextPrcElReady":false,"success":false,"responseStatus":{"ErrorCode":"","Message":"denied","Errors":[]}}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"""{"saveResult":{"UsrAny":false},"rowsAffected":-1,"nextPrcElReady":false,"success":false,"responseStatus":{"ErrorCode":"","Message":"denied","Errors":[]}}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse(
			because: "a per-code saveResult of false means the platform actively rejected the value");
	}

	[Test]
	[Description("Update returns false when the saveResult payload does not contain the requested code — the platform did not acknowledge the per-code outcome.")]
	public void UpdateSysSetting_ReturnsFalse_WhenSaveResultMissingForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"OtherCode":true},"rowsAffected":-1,"nextPrcElReady":false,"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				"""{"saveResult":{"OtherCode":true},"rowsAffected":-1,"nextPrcElReady":false,"success":false}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse(
			because: "a saveResult that does not include the requested code is treated as failure");
	}

	[Test]
	[Description("Update returns false when the platform returns an empty response body so the caller does not infer success from a missing acknowledgement.")]
	public void UpdateSysSetting_ReturnsFalse_WhenResponseIsEmpty() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(string.Empty);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		// The credentials are fine here; it is the WRITE acknowledgement that is missing, so the
		// probe route answers normally and only the update comes back empty.
		applicationClient
			.ExecutePostRequest(Arg.Is<string>(url => url.Contains("SelectQuery")), Arg.Any<string>())
			.Returns(AcceptedDataServiceResponse);
		applicationClient
			.ExecutePostRequest(Arg.Is<string>(url => url.Contains("SelectQuery")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(AcceptedDataServiceResponse);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse(
			because: "an empty response body means the platform did not acknowledge the request and the caller must not infer success");
	}

	#endregion

	#region CBinary sanity

	[Test]
	[Description("CBinary subclass should report 'Binary' as its value-type-name for serialization parity with other typed settings.")]
	public void CBinary_ExposesBinaryValueTypeName() {
		CBinary sut = new("Name", "Code", value: null, isCacheable: true,
			description: "", isPersonal: false);
		sut.ValueTypeName.Should().Be("Binary",
			because: "platform-side InsertSysSettingRequest expects the Creatio internal type name 'Binary' for binary settings");
	}

	#endregion

	#region UpdateSysSetting — code validation & safe JSON encoding

	[Test]
	[Description("UpdateSysSetting must reject codes containing non-identifier characters before contacting the platform.")]
	public void UpdateSysSetting_RejectsCode_WithInvalidIdentifierCharacters() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		bool result = sut.UpdateSysSetting("Usr\"Inject", "value");

		result.Should().BeFalse(
			because: "an agent-supplied code with a quote character could otherwise break the request JSON payload");
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("UpdateSysSetting must encode string values via JsonSerializer so quotes and control characters cannot corrupt the request body.")]
	public void UpdateSysSetting_EscapesQuotesInValuePayload() {
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrEscapeCode", "Text");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"UsrEscapeCode":true},"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"saveResult":{"UsrEscapeCode":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		sut.UpdateSysSetting("UsrEscapeCode", "with \"quote\" and \\ slash").Should().BeTrue();

		capturedBody.Should().NotBeNull();
		capturedBody.Should().NotContain("\"quote\"",
			because: "embedded quotes must be encoded by the serializer; leaving them literal would close the JSON value early");
		capturedBody.Should().MatchRegex(@"(\\u0022|\\"")quote(\\u0022|\\"")",
			because: "the serializer escapes inner quotes either as \\u0022 or \\\" depending on its encoder settings");
		capturedBody.Should().Contain("\\\\ slash",
			because: "backslashes must be JSON-escaped through JsonSerializer to avoid request corruption");
	}

	[Test]
	[Description("Fails loudly with a clear error instead of a NullReferenceException when the sys-setting does not exist but the caller explicitly requests Lookup handling for a non-Guid value (sonar csharpsquid:S2259).")]
	public void UpdateSysSetting_LookupType_ReturnsFalse_WhenSettingDoesNotExist() {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>>());
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		bool result = sut.UpdateSysSetting("UsrMissingCode", "Not A Guid", "Lookup");

		result.Should().BeFalse(
			because: "there is no sys-setting to resolve a reference schema against, so the update must fail closed instead of throwing");
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion

	#region TryListSysSettings — SecureText masking

	[Test]
	[Description("TryListSysSettings masks SecureText values to a placeholder so the catalog response cannot be used to harvest stored secrets.")]
	public void TryListSysSettings_Masks_SecureText_Values() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrApiSecret", "SecureText",
			valueRow: new Dictionary<string, object> {
				{ "SysAdminUnit", AllUsersAdminUnitId },
				{ "TextValue", "ENCRYPTED_BASE64_CIPHERTEXT_PAYLOAD" }
			});
		ISysSettingsManager managerForTryList = BuildSut(providerMock);
		SysSettingsCommand command = new(managerForTryList, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		SysSettingsListResult result = command.TryListSysSettings(new ListSysSettingsArgs("local"));

		result.Success.Should().BeTrue();
		result.Settings.Should().ContainSingle(item => item.Code == "UsrApiSecret")
			.Which.Value.Should().Be("***",
				because: "SecureText values must be masked in the catalog response so callers cannot harvest stored secrets through list-sys-settings");
	}

	[Test]
	[Description("TryListSysSettings returns an empty value (not a mask placeholder) for SecureText settings that have no stored value yet, so the caller can still distinguish 'has secret' from 'no secret'.")]
	public void TryListSysSettings_Returns_Empty_For_Unconfigured_SecureText() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrEmptySecret", "SecureText", valueRow: null);
		ISysSettingsManager managerForTryList = BuildSut(providerMock);
		SysSettingsCommand command = new(managerForTryList, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		SysSettingsListResult result = command.TryListSysSettings(new ListSysSettingsArgs("local"));

		result.Settings.Should().ContainSingle(item => item.Code == "UsrEmptySecret")
			.Which.Value.Should().BeEmpty(
				because: "unconfigured SecureText settings should expose an empty value, not a misleading mask placeholder");
	}

	#endregion

	#region TryListSysSettings — Binary discovery

	[Test]
	[Description("TryListSysSettings includes Binary-type settings for discovery, showing their value as <binary> because the blob cannot be read back.")]
	public void TryListSysSettings_Includes_Binary_With_Placeholder() {
		// Arrange
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", Guid.NewGuid() }, { "Code", "UsrPlainText" }, { "Name", "Plain" },
				{ "ValueTypeName", "Text" }, { "Description", "" },
				{ "IsCacheable", true }, { "IsPersonal", false }, { "IsSSPAvailable", false }
			},
			new() {
				{ "Id", Guid.NewGuid() }, { "Code", "UsrBlob" }, { "Name", "Blob" },
				{ "ValueTypeName", "Binary" }, { "Description", "" },
				{ "IsCacheable", true }, { "IsPersonal", false }, { "IsSSPAvailable", false }
			}
		});
		providerMock.MockItems("SysSettingsValue").Returns(new List<Dictionary<string, object>>());
		ISysSettingsManager managerForTryList = BuildSut(providerMock);
		SysSettingsCommand command = new(managerForTryList, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		SysSettingsListResult result = command.TryListSysSettings(new ListSysSettingsArgs("local"));

		// Assert
		result.Success.Should().BeTrue(
			because: "list-sys-settings completes normally and now surfaces Binary settings for discovery");
		result.Settings.Should().HaveCount(2,
			because: "both the Text and the Binary setting must be discoverable through the catalog");
		SysSettingItem binary = result.Settings.Single(s => s.Code == "UsrBlob");
		binary.ValueTypeName.Should().Be("Binary",
			because: "the Binary setting's type is surfaced so a caller can recognize it as a blob/logo setting");
		binary.Value.Should().Be("<binary>",
			because: "the blob value cannot be read back, so the value column shows a placeholder rather than an empty or misleading string");
	}

	#endregion

	#region GetEntityIdByDisplayValue — safe JSON encoding

	private const string SelectIdByDisplayValueTemplate = """
		{
		  "rootSchemaName": "{{rootSchemaName}}",
		  "filters": {
		    "isEnabled": true,
		    "trimDateTimeParameterToDate": false,
		    "filterType": 6,
		    "logicalOperation": 0,
		    "items": {
		      "8caf69f4-9583-4e77-86c0-716c07ce4ec7": {
		        "filterType": 1,
		        "comparisonType": 3,
		        "isEnabled": true,
		        "trimDateTimeParameterToDate": false,
		        "leftExpression": { "expressionType": 1, "functionType": 1, "macrosType": 35 },
		        "isAggregative": false,
		        "dataValueType": 1,
		        "rightExpression": {
		          "expressionType": 2,
		          "parameter": { "dataValueType": 1, "value": "{{diplayvalue}}", "className": "Terrasoft.Parameter" },
		          "className": "Terrasoft.ParameterExpression"
		        },
		        "className": "Terrasoft.CompareFilter"
		      }
		    }
		  },
		  "useLocalization": true,
		  "columns": { "items": { "Id": { "expression": { "expressionType": 0, "columnPath": "Id" } } } }
		}
		""";

	private static SysSettingsManager BuildSutWithStubbedTemplate(IDataProvider dataProvider,
		IApplicationClient applicationClient, string templateContent) {
		BindingsModule bm = new(FileSystem);
		IServiceProvider container = bm.Register(EnvironmentSettings);
		IFileSystem filesystem = Substitute.For<IFileSystem>();
		filesystem.ReadAllText(Arg.Any<string>()).Returns(templateContent);
		return new SysSettingsManager(
			applicationClient,
			container.GetRequiredService<IServiceUrlBuilder>(),
			dataProvider,
			container.GetRequiredService<IWorkingDirectoriesProvider>(),
			filesystem,
			FileSystem,
			Substitute.For<ILogger>());
	}

	[Test]
	[Description("Lookup display-name resolution must JSON-encode caller-supplied values through Newtonsoft so quotes/backslashes cannot break out of the SelectQuery JSON string literal.")]
	public void UpdateSysSetting_LookupDisplayName_EscapesQuotesAndBackslashesInSelectQuery() {
		Guid settingId = Guid.NewGuid();
		Guid refSchemaUId = Guid.NewGuid();
		Guid resolvedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", settingId }, { "Code", "UsrLookupCode" }, { "Name", "UsrLookupCode" },
				{ "ValueTypeName", "Lookup" }, { "Description", "" },
				{ "IsCacheable", true }, { "IsPersonal", false }, { "IsSSPAvailable", false },
				{ "ReferenceSchemaUId", refSchemaUId }
			}
		});
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>> {
			new() { { "Id", Guid.NewGuid() }, { "UId", refSchemaUId }, { "Name", "UsrPhoneFormat" } }
		});
		providerMock.MockItems("SysSettingsValue").Returns(new List<Dictionary<string, object>>());

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		List<string> capturedBodies = [];
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBodies.Add(b)))
			.Returns(_ => capturedBodies.Count == 1
				? $$"""{"rows":[{"Id":"{{resolvedId}}"}]}"""
				: """{"saveResult":{"UsrLookupCode":true},"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => capturedBodies.Count == 1
				? $$"""{"rows":[{"Id":"{{resolvedId}}"}]}"""
				: """{"saveResult":{"UsrLookupCode":true},"success":false}""");
		SysSettingsManager sut = BuildSutWithStubbedTemplate(providerMock, applicationClient,
			SelectIdByDisplayValueTemplate);

		sut.UpdateSysSetting("UsrLookupCode", "Display \"name\" with \\slash", "Lookup")
			.Should().BeTrue(
				because: "after the display name resolves through SelectQuery the per-code update should succeed end-to-end");

		capturedBodies.Should().HaveCountGreaterOrEqualTo(1,
			because: "Lookup display-name resolution issues a SelectQuery request before the value update");
		capturedBodies[0].Should().Contain("\\\"name\\\"",
			because: "Newtonsoft JSON encoding must escape inner quotes inside the SelectQuery body");
		capturedBodies[0].Should().Contain("\\\\slash",
			because: "Newtonsoft JSON encoding must escape backslashes inside the SelectQuery body");
		capturedBodies[0].Should().NotContain("{{diplayvalue}}",
			because: "the templating placeholder must be replaced, never sent literally to the platform");
	}

	[Test]
	[Description("When multiple lookup rows share a display name, GetEntityIdByDisplayValue must fail loudly with InvalidOperationException so the caller is told to disambiguate by GUID — silently picking rows[0] would write the wrong record.")]
	public void UpdateSysSetting_LookupDisplayName_RejectsAmbiguousMatches() {
		Guid settingId = Guid.NewGuid();
		Guid refSchemaUId = Guid.NewGuid();
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", settingId }, { "Code", "UsrLookupCode" }, { "Name", "UsrLookupCode" },
				{ "ValueTypeName", "Lookup" }, { "Description", "" },
				{ "IsCacheable", true }, { "IsPersonal", false }, { "IsSSPAvailable", false },
				{ "ReferenceSchemaUId", refSchemaUId }
			}
		});
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>> {
			new() { { "Id", Guid.NewGuid() }, { "UId", refSchemaUId }, { "Name", "UsrPhoneFormat" } }
		});
		providerMock.MockItems("SysSettingsValue").Returns(new List<Dictionary<string, object>>());

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("""{"rows":[{"Id":"11111111-1111-1111-1111-111111111111"},{"Id":"22222222-2222-2222-2222-222222222222"}]}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"rows":[{"Id":"11111111-1111-1111-1111-111111111111"},{"Id":"22222222-2222-2222-2222-222222222222"}]}""");
		SysSettingsManager sut = BuildSutWithStubbedTemplate(providerMock, applicationClient,
			SelectIdByDisplayValueTemplate);

		System.Action act = () => sut.UpdateSysSetting("UsrLookupCode", "Duplicated display", "Lookup");

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Ambiguous lookup display value*",
				because: "multi-row matches are non-deterministic — silently picking rows[0] would set the sys-setting to the wrong record");
	}

	[Test]
	[Description("If the SelectQuery template ever drops the expected parameter path, GetEntityIdByDisplayValue must fail loudly instead of silently sending a request with the un-replaced placeholder.")]
	public void UpdateSysSetting_LookupDisplayName_FailsLoud_WhenTemplateMissesParameterPath() {
		Guid settingId = Guid.NewGuid();
		Guid refSchemaUId = Guid.NewGuid();
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", settingId }, { "Code", "UsrLookupCode" }, { "Name", "UsrLookupCode" },
				{ "ValueTypeName", "Lookup" }, { "Description", "" },
				{ "IsCacheable", true }, { "IsPersonal", false }, { "IsSSPAvailable", false },
				{ "ReferenceSchemaUId", refSchemaUId }
			}
		});
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>> {
			new() { { "Id", Guid.NewGuid() }, { "UId", refSchemaUId }, { "Name", "UsrPhoneFormat" } }
		});
		providerMock.MockItems("SysSettingsValue").Returns(new List<Dictionary<string, object>>());

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		SysSettingsManager sut = BuildSutWithStubbedTemplate(providerMock, applicationClient,
			templateContent: """{"rootSchemaName":"{{rootSchemaName}}","filters":{"items":{}}}""");

		System.Action act = () => sut.UpdateSysSetting("UsrLookupCode", "AnyDisplay", "Lookup");

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*template is malformed*",
				because: "a missing parameter path must surface immediately so a malformed template cannot send an injection-vulnerable placeholder request");
	}

	#endregion

	#region GetAllUsersDefaultByCode — explicit All-Users-only read

	[Test]
	[Description("GetAllUsersDefaultByCode returns empty when only personal-user rows exist, so the MCP get-sys-setting contract never leaks another user's value.")]
	public void GetAllUsersDefaultByCode_ReturnsEmpty_WhenOnlyPersonalValuesExist() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrAllUsersOnly", "Text",
			valueRow: new Dictionary<string, object> {
				{ "SysAdminUnit", Guid.NewGuid() },
				{ "TextValue", "personal-value" }
			});
		ISysSettingsManager sut = BuildSut(providerMock);

		string value = sut.GetAllUsersDefaultByCode("UsrAllUsersOnly");

		value.Should().BeEmpty(
			because: "GetAllUsersDefaultByCode must skip rows belonging to specific users so the MCP contract holds even when a per-user override exists");
	}

	[Test]
	[Description("GetAllUsersDefaultByCode returns the All-Users row's formatted value when present.")]
	public void GetAllUsersDefaultByCode_ReturnsAllUsersValue_WhenAllUsersRowExists() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrPlain", "Text",
			valueRow: new Dictionary<string, object> {
				{ "SysAdminUnit", AllUsersAdminUnitId },
				{ "TextValue", "all-users-value" }
			});
		ISysSettingsManager sut = BuildSut(providerMock);

		string value = sut.GetAllUsersDefaultByCode("UsrPlain");

		value.Should().Be("all-users-value",
			because: "the All-Users-only path must return the All-Users row formatted by the same FormatTypedValue used elsewhere");
	}

	[Test]
	[Description("GetAllUsersDefaultWithType returns the resolved value-type-name alongside the All-Users value so callers (specifically the MCP tool layer) can apply type-aware policy like SecureText masking without a second round-trip.")]
	public void GetAllUsersDefaultWithType_ReturnsValueAndType_WhenAllUsersRowExists() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrSecretCode", "SecureText",
			valueRow: new Dictionary<string, object> {
				{ "SysAdminUnit", AllUsersAdminUnitId },
				{ "TextValue", "ENCRYPTED_BASE64" }
			});
		ISysSettingsManager sut = BuildSut(providerMock);

		(string value, string typeName) = sut.GetAllUsersDefaultWithType("UsrSecretCode");

		value.Should().Be("ENCRYPTED_BASE64",
			because: "the manager returns the raw stored value; masking is applied by the tool layer that consumes this method");
		typeName.Should().Be("SecureText",
			because: "the resolved value-type-name must accompany the value so the tool layer can decide whether to mask");
	}

	[Test]
	[Description("GetAllUsersDefaultWithType returns empty value and null type-name for unknown codes, so the tool layer treats them as 'no value' without misclassifying the type.")]
	public void GetAllUsersDefaultWithType_ReturnsEmptyValueAndNullType_WhenSettingMissing() {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>>());
		ISysSettingsManager sut = BuildSut(providerMock);

		(string value, string typeName) = sut.GetAllUsersDefaultWithType("UsrMissing");

		value.Should().BeEmpty(
			because: "an unknown code should surface as empty value, not as a magic sentinel");
		typeName.Should().BeNull(
			because: "no setting → no type — the tool layer must be able to short-circuit before applying type-specific policy");
	}

	#endregion

	#region UpdateSysSetting — Money / Float numeric branches

	[Test]
	[Description("Money is the new Creatio internal alias for Currency and must accept decimal values on the update path.")]
	public void UpdateSysSetting_MoneyType_SerializesDecimalValue() {
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrMoneyCode", "Money");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"UsrMoneyCode":true},"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"saveResult":{"UsrMoneyCode":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		sut.UpdateSysSetting("UsrMoneyCode", "19.95").Should().BeTrue(
			because: "Money settings reuse the decimal serialization branch alongside Currency/Decimal/Float");
		capturedBody.Should().Contain("\"UsrMoneyCode\":19.95",
			because: "decimal payloads are emitted as JSON numbers, not strings");
	}

	[Test]
	[Description("Float is the new Creatio internal alias for Decimal and must accept decimal values on the update path.")]
	public void UpdateSysSetting_FloatType_SerializesDecimalValue() {
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrFloatCode", "Float");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"UsrFloatCode":true},"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"saveResult":{"UsrFloatCode":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		sut.UpdateSysSetting("UsrFloatCode", "3.14").Should().BeTrue(
			because: "Float settings reuse the decimal serialization branch alongside Currency/Decimal/Money");
		capturedBody.Should().Contain("\"UsrFloatCode\":3.14",
			because: "decimal payloads are emitted as JSON numbers, not strings");
	}

	[Test]
	[Description("Binary settings (e.g. LogoImage) send the Base64 payload verbatim as a JSON string through PostSysSettingsValues.")]
	public void UpdateSysSetting_BinaryType_SendsBase64StringValue() {
		// Arrange
		string base64 = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]); // "iVBORw==" — PNG signature bytes
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "LogoImage", "Binary");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"LogoImage":true},"success":false}""");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"saveResult":{"LogoImage":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		// Act
		bool updated = sut.UpdateSysSetting("LogoImage", base64, "Binary");

		// Assert
		updated.Should().BeTrue(
			because: "the platform's PostSysSettingsValues endpoint accepts a Binary value as a Base64 string");
		capturedBody.Should().Contain($"\"LogoImage\":\"{base64}\"",
			because: "the Base64 blob must be emitted verbatim as a JSON string inside sysSettingsValues");
	}

	[Test]
	[Description("Binary updates reject a malformed (non-Base64) payload before contacting the platform.")]
	public void UpdateSysSetting_BinaryType_RejectsInvalidBase64() {
		// Arrange
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "LogoImage", "Binary");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		// Act
		bool updated = sut.UpdateSysSetting("LogoImage", "not valid base64!!!", "Binary");

		// Assert
		updated.Should().BeFalse(
			because: "a Binary value that is not valid Base64 must fail fast rather than post a bad payload");
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Binary updates reject an oversized inline Base64 value before contacting the platform, so the size cap cannot be bypassed via the inline value path.")]
	public void UpdateSysSetting_BinaryType_RejectsOversizedValue() {
		// Arrange
		byte[] tooBig = new byte[(int)SysSettingsManager.MaxBinaryValueBytes + 1];
		string base64 = Convert.ToBase64String(tooBig);
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "LogoImage", "Binary");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		// Act
		bool updated = sut.UpdateSysSetting("LogoImage", base64, "Binary");

		// Assert
		updated.Should().BeFalse(
			because: "a Binary payload over the decoded-byte cap must be rejected regardless of input form");
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion

	#region GetFileSecurityPolicy — fail-closed mode resolution

	// Platform-fixed FileSecurityMode lookup ids.
	private const string FileSecurityDisabledId = "9801C625-FAFB-4ED3-9383-C3C942A5C1E3";
	private const string FileSecurityAllowListId = "C6CA9A2F-3A4A-4D51-B67B-DE36852CB916";
	private const string FileSecurityDenyListId = "60849C6E-24B4-45DF-9AAD-2F69D419823C";

	private static DataProviderMock SetupFileSecurityModeMock(string valueTypeName, string rawValue) {
		Guid settingId = Guid.NewGuid();
		bool isLookup = valueTypeName == "Lookup";
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", settingId }, { "Code", "FileSecurityMode" }, { "Name", "FileSecurityMode" },
				{ "ValueTypeName", valueTypeName }, { "Description", "" },
				{ "IsCacheable", true }, { "IsPersonal", false }, { "IsSSPAvailable", false }
			}
		});
		providerMock.MockItems("SysSettingsValue").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", Guid.NewGuid() }, { "SysSettings", settingId }, { "SysAdminUnit", AllUsersAdminUnitId },
				{ "IsDef", true }, { "TextValue", isLookup ? string.Empty : rawValue },
				{ "IntegerValue", 0 }, { "FloatValue", 0m }, { "BooleanValue", false },
				{ "DateTimeValue", new DateTime(1900, 1, 1) },
				{ "GuidValue", isLookup ? Guid.Parse(rawValue) : Guid.Empty }
			}
		});
		return providerMock;
	}

	[TestCase(FileSecurityDisabledId, FileSecurityMode.Disabled)]
	[TestCase(FileSecurityAllowListId, FileSecurityMode.AllowList)]
	[TestCase(FileSecurityDenyListId, FileSecurityMode.DenyList)]
	[Description("GetFileSecurityPolicy maps each of the three known FileSecurityMode lookup ids to its mode.")]
	public void GetFileSecurityPolicy_Resolves_Known_Mode_Ids(string modeGuid, FileSecurityMode expected) {
		// Arrange
		ISysSettingsManager sut = BuildSut(SetupFileSecurityModeMock("Lookup", modeGuid));

		// Act & Assert
		sut.GetFileSecurityPolicy().Mode.Should().Be(expected,
			because: "each documented FileSecurityMode id must resolve to its corresponding mode");
	}

	[Test]
	[Description("GetFileSecurityPolicy fails closed to Unknown when the FileSecurityMode value is missing, rather than defaulting to Disabled.")]
	public void GetFileSecurityPolicy_Missing_Mode_Is_Unknown() {
		// Arrange
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>>());
		providerMock.MockItems("SysSettingsValue").Returns(new List<Dictionary<string, object>>());
		ISysSettingsManager sut = BuildSut(providerMock);

		// Act & Assert
		sut.GetFileSecurityPolicy().Mode.Should().Be(FileSecurityMode.Unknown,
			because: "a missing mode must fail closed (Unknown), never be treated as Disabled");
	}

	[TestCase("not-a-guid", "Text")]
	[TestCase("11111111-1111-1111-1111-111111111111", "Lookup")]
	[Description("GetFileSecurityPolicy fails closed to Unknown for a malformed value or an unrecognized mode id.")]
	public void GetFileSecurityPolicy_Malformed_Or_Unknown_Mode_Is_Unknown(string rawValue, string valueTypeName) {
		// Arrange
		ISysSettingsManager sut = BuildSut(SetupFileSecurityModeMock(valueTypeName, rawValue));

		// Act & Assert
		sut.GetFileSecurityPolicy().Mode.Should().Be(FileSecurityMode.Unknown,
			because: "a malformed or unrecognized FileSecurityMode must fail closed to Unknown");
	}

	#endregion

	#region GetSysSettingValueByCode — All-Users-only fallback

	[Test]
	[Description("GetSysSettingValueByCode must return empty when only personal-user values exist; falling back to a non-All-Users row would mislead callers expecting the global default.")]
	public void GetSysSettingValueByCode_ReturnsEmpty_WhenOnlyPersonalValuesExist() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrPersonalOnly", "Text",
			valueRow: new Dictionary<string, object> {
				{ "SysAdminUnit", Guid.NewGuid() },
				{ "TextValue", "personal-value" }
			});
		ISysSettingsManager sut = BuildSut(providerMock);

		string value = sut.GetSysSettingValueByCode("UsrPersonalOnly");

		value.Should().BeEmpty(
			because: "the MCP get-sys-setting flow advertises the All-Users default; falling back to a personal row would leak another user's value");
	}

	[Test]
	[Description("A refused connection reaches the MCP envelope as 'Network error ...' rather than a composed generic message: the decorator rethrows the transport fault unchanged so CategorizeError can still switch on its type.")]
	public void TryUpdateSysSetting_ShouldReportANetworkError_ForARefusedConnection() {
		// Arrange
		IDataProvider dataProvider = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException("Connection refused at http://localhost:40124")));
		ISysSettingsManager manager = BuildSut(dataProvider);
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		SysSettingUpdateResult result = command.TryUpdateSysSetting(
			new UpdateSysSettingArgs("local", "UsrNetworkFailure", "value"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a write that never reached the environment must not be reported as done");
		result.Error.Should().Be("Network error updating sys-setting.",
			because: "wrapping the transport fault into an InvalidOperationException erased its type and made this arm of CategorizeError unreachable");
	}

	[Test]
	[Description("A rejected session on the create WRITE is provable, because that path still holds the raw response body: create-sys-setting must report the credential diagnosis rather than a generic create failure.")]
	public void TryCreateSysSetting_ShouldReportAuthenticationFailure_WhenTheWritePostReturnsTheLoginPage() {
		// Arrange - the read succeeds; only the write endpoint answers with the login page.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(LoginPageBody);
		ISysSettingsManager manager = BuildSut(new DataProviderMock(), applicationClient);
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		SysSettingCreateResult result = command.TryCreateSysSetting(
			new CreateSysSettingArgs("local", "UsrWriteAuth", "UsrWriteAuth", "Text"));

		// Assert
		result.Success.Should().BeFalse(
			because: "nothing was created, so the create must not be reported as done");
		result.Error.Should().Be("Authentication error creating sys-setting.",
			because: "the write path has the RAW body and can prove the session was rejected - it used to fall through to 'Failed creating sys-setting.' because the login page is not JSON");
	}

	[Test]
	[Description("The same is true of the update write: PostSysSettingsValues answering with the login page is a credential failure, not the 'Invalid response format' the JSON path used to report.")]
	public void UpdateSysSetting_ShouldThrowAuthenticationException_WhenTheWritePostReturnsTheLoginPage() {
		// Arrange
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrWriteAuth", "Text");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(LoginPageBody);
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		// Act
		Action act = () => sut.UpdateSysSetting("UsrWriteAuth", "value");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "the raw body carries Creatio's auth-routing markers, so this is a definite rejection rather than the ambiguous non-JSON answer the read path sees")
			.Which.Message.Should().Contain("Verify the environment credentials",
				because: "a definite credential verdict must carry the recovery action");
	}

	[Test]
	[Description("A DataService ErrorCode 5 fault envelope on the write is also a credential failure, even though it is valid JSON that the deserializer would happily accept.")]
	public void UpdateSysSetting_ShouldThrowAuthenticationException_ForAnErrorCodeFiveWriteResponse() {
		// Arrange
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrWriteAuth", "Text");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(
			"{\"responseStatus\":{\"ErrorCode\":\"5\",\"Message\":\"Your password has expired.\"},\"success\":false}");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		// Act
		Action act = () => sut.UpdateSysSetting("UsrWriteAuth", "value");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "valid JSON that carries ErrorCode 5 is the platform naming a rejected credential, and it would otherwise be reduced to a generic failed save");
	}

	#endregion

	#region Legacy provider-only read

	[Test]
	[Description("The legacy GetSysSettingValueByCode path fails on a rejected session instead of handing back the provider's empty value as a real empty setting.")]
	public void GetSysSettingValueByCode_ShouldThrowAuthenticationException_WhenCredentialsAreRejected() {
		// Arrange
		ISysSettingsManager sut = BuildSut(BuildRejectedProvider());

		// Act
		Action act = () => sut.GetSysSettingValueByCode("SchemaNamePrefix");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "an empty provider value is indistinguishable from a rejected read, so the failure has to be raised where the response's Success flag is still visible")
			.WithMessage("*password has expired*");
	}

	[Test]
	[Description("get-syssetting no longer exits 0 with an empty value on rejected credentials: the authentication failure reaches the caller.")]
	public void SysSettingsCommand_Get_ShouldThrowAuthenticationException_WhenCredentialsAreRejected() {
		// Arrange
		ISysSettingsManager manager = BuildSut(BuildRejectedProvider());
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), Substitute.For<IFileSystem>());

		// Act
		Action act = () => command.Execute(new SysSettingsOptions { Code = "MaxFileSize", IsGet = true });

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "reporting exit 0 and an empty value for a rejected read is the defect this fixes");
	}

	[Test]
	[Description("get-schema-name-prefix reports an authentication failure instead of success:true with an empty prefix when the credentials are rejected.")]
	public void GetSchemaNamePrefix_ShouldReportAuthenticationFailure_WhenCredentialsAreRejected() {
		// Arrange
		SysSettingsManager manager = (SysSettingsManager)BuildSut(BuildRejectedProvider());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("local"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an empty prefix caused by rejected credentials must not be reported as a successful read");
		result.Error.Should().Be("Authentication error reading SchemaNamePrefix.",
			because: "the caller needs to know the credentials are the problem, not that no prefix is configured");
	}

	[Test]
	[Description("get-schema-name-prefix fails closed on the login-page shape too, and reports both causes: the read path holds only the parser message, so it cannot prove the session was the problem.")]
	public void GetSchemaNamePrefix_ShouldNameBothCauses_ForALoginPageResponse() {
		// Arrange
		SysSettingsManager manager = (SysSettingsManager)BuildSut(BuildRejectedProvider(LoginPageParserError));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("local"));

		// Assert
		result.Success.Should().BeFalse(
			because: "GetSysSettingValue has no Success flag and its provider does not catch, so without the decorator this arrived as a raw JsonReaderException and the tool reported an empty prefix");
		result.Error.Should().Contain("session was rejected",
			because: "an expired password is one of the two causes and the caller needs to see it");
		result.Error.Should().Contain("proxy, gateway, wrong path",
			because: "the other cause is equally consistent with what the provider reported");
	}

	#endregion

	#region Per-environment factory wiring

	[Test]
	[Description("The per-environment sys-settings factory installs the no-reauth executor for an access-token profile, so a login-page response cannot trigger CreatioClient.Login() across the bearer credential boundary.")]
	public void PerEnvironmentFactory_ShouldUseNoReauthExecutor_ForAccessTokenProfile() {
		// Arrange
		EnvironmentSettings bearerSettings = new() {
			Uri = "https://localhost",
			AccessToken = "bearer-token",
			IsNetCore = true
		};

		// Act
		IReauthExecutor reauthExecutor = ResolveFactoryReauthExecutor(bearerSettings);

		// Assert
		reauthExecutor.Should().BeOfType<NoReauthExecutor>(
			because: "a bearer profile must never fall back to a login/password re-authentication");
	}

	[Test]
	[Description("The per-environment sys-settings factory keeps the adapter's own login-capable executor for a login/password profile, so session-expiry recovery is unchanged there.")]
	public void PerEnvironmentFactory_ShouldKeepLoginCapableExecutor_ForPasswordProfile() {
		// Arrange
		EnvironmentSettings passwordSettings = new() {
			Uri = "https://localhost",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = false
		};

		// Act
		IReauthExecutor reauthExecutor = ResolveFactoryReauthExecutor(passwordSettings);

		// Assert
		reauthExecutor.Should().NotBeOfType<NoReauthExecutor>(
			because: "the non-bearer path must keep recovering from server-side session expiry");
	}

	[Test]
	[TestCase("token", null, true, TestName = "AccessTokenProfile")]
	[TestCase(null, "clio-client", true, TestName = "OAuthClientProfile")]
	[TestCase(null, null, false, TestName = "LoginPasswordProfile")]
	[Description("An OAuth client-credentials profile counts as token authentication alongside an access token, because neither carries a username or password for the forms-login reauthentication path.")]
	public void UsesTokenAuthentication_ShouldTreatAnOAuthClientAsAToken(
		string accessToken, string clientId, bool expected) {
		// Arrange
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			AccessToken = accessToken,
			ClientId = clientId,
			Login = clientId is null && accessToken is null ? "Supervisor" : null,
			Password = clientId is null && accessToken is null ? "Supervisor" : null
		};
		System.Reflection.MethodInfo predicate = typeof(BindingsModule).GetMethod(
			"UsesTokenAuthentication",
			System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
		predicate.Should().NotBeNull(
			because: "BindingsModule.UsesTokenAuthentication is the single rule both adapter wirings ask");

		// Act
		bool usesToken = (bool)predicate!.Invoke(null, [settings]);

		// Assert
		usesToken.Should().Be(expected,
			because: "an OAuth client has no username/password, so a login-page response must not send it down CreatioClient.Login()");
	}

	[Test]
	[Description("Both places that choose the Creatio client adapter ask UsesTokenAuthentication, so an OAuth profile cannot regain the login-capable executor at one of them. The factory path itself cannot be exercised here: building an OAuth RemoteDataProvider fetches a token over the network.")]
	public void AdapterWiring_ShouldSelectTheExecutor_ThroughTheSharedTokenRule() {
		// Arrange
		string bindingsSourcePath = Path.Combine(RepositoryRoot, "clio", "BindingsModule.cs");
		File.Exists(bindingsSourcePath).Should().BeTrue(
			because: $"this guard reads the adapter wiring from {bindingsSourcePath}");
		string source = File.ReadAllText(bindingsSourcePath);

		// Assert
		source.Should().Contain("IApplicationClient applicationClient = UsesTokenAuthentication(envSettings)",
			because: "the per-environment sys-settings factory must pick the no-login executor for every token shape");
		source.Should().Contain("return UsesTokenAuthentication(activeSettings)",
			because: "the active-environment registration must pick it by the same rule");
	}

	private static IReauthExecutor ResolveFactoryReauthExecutor(EnvironmentSettings envSettings) {
		BindingsModule bm = new(FileSystem);
		IServiceProvider container = bm.Register(EnvironmentSettings);
		Func<EnvironmentSettings, ISysSettingsManager> factory =
			container.GetRequiredService<Func<EnvironmentSettings, ISysSettingsManager>>();
		//The client stays lazy, so reading the wiring costs no HTTP call.
		ISysSettingsManager manager = factory(envSettings);
		object applicationClient = ReadPrivateField(manager, "_creatioClient");
		applicationClient.Should().BeOfType<CreatioClientAdapter>(
			because: "the factory wires the sys-settings manager onto the Creatio client adapter");
		return (IReauthExecutor)ReadPrivateField(applicationClient, "_reauthExecutor");
	}

	private static object ReadPrivateField(object instance, string fieldName) {
		System.Reflection.FieldInfo field = instance.GetType().GetField(
			fieldName,
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		field.Should().NotBeNull(
			because: $"{instance.GetType().Name}.{fieldName} is what the wiring assertion reads");
		return field!.GetValue(instance);
	}

	#endregion

}
