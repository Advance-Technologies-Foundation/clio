using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class ClassifyingDataProviderTests {

	#region Helpers

	/// <summary>Newtonsoft's prose when the body is the Creatio login page rather than JSON.</summary>
	private const string LoginPageParserFailure =
		"Unexpected character encountered while parsing value: <. Path '', line 0, position 0.";

	private const string ExpiredPasswordError = "5: Your password has expired.";

	private const string GenericProviderError = "SqlException: deadlock victim";

	private static IDataProvider BuildFailing(string errorMessage) =>
		new ClassifyingDataProvider(new UnsuccessfulDataProvider(errorMessage));

	private static ISelectQuery BuildSelectQuery(string rootSchemaName) {
		ISelectQuery selectQuery = Substitute.For<ISelectQuery>();
		selectQuery.RootSchemaName.Returns(rootSchemaName);
		return selectQuery;
	}

	private static List<IBaseQuery> BuildQueries(string rootSchemaName) {
		IBaseQuery query = Substitute.For<IBaseQuery>();
		query.RootSchemaName.Returns(rootSchemaName);
		return [query];
	}

	#endregion

	[Test]
	[Description("A rejected read arrives as an unsuccessful response with an empty Items list; the decorator must raise an authentication failure rather than let that emptiness reach the caller as a legitimate result.")]
	public void GetItems_ShouldThrowAuthenticationException_WhenTheErrorNamesAnExpiredPassword() {
		// Arrange
		IDataProvider sut = BuildFailing(ExpiredPasswordError);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("SysSettings"));

		// Assert
		AuthenticationException exception = act.Should().Throw<AuthenticationException>(
			because: "an unsuccessful ATF response is dropped to an empty collection by Models<T>(), so the decorator is the only barrier between a rejected read and a false empty success").Which;
		exception.Message.Should().Contain("Your password has expired",
			because: "the actionable platform cause has to survive the classification");
		exception.Message.Should().Contain("Verify the environment credentials",
			because: "an automation caller needs a recovery action, not just an exception type");
		exception.Message.Should().Contain("SysSettings",
			because: "naming the entity schema is what makes the failure attributable to an operation");
	}

	[Test]
	[Description("Creatio answers a rejected or expired credential by serving the login page with HTTP 200, so the only trace that reaches clio is Newtonsoft's 'unexpected character' prose; that shape must classify as authentication.")]
	public void GetItems_ShouldThrowAuthenticationException_WhenTheErrorIsHtmlWhereJsonWasExpected() {
		// Arrange
		IDataProvider sut = BuildFailing(LoginPageParserFailure);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("SysSchema"));

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "HTML where the DataService contract requires JSON is the login page, which is how issue #1222 reached the user as an empty list")
			.Which.Message.Should().Contain("proxy or gateway",
				because: "a gateway error page produces the same shape, so the diagnostic must name that alternative rather than claim certainty");
	}

	[Test]
	[Description("A provider failure that names no credential problem must stay a generic failure, so an operator is not sent off to repair working credentials.")]
	public void GetItems_ShouldThrowInvalidOperationException_ForAGenericProviderFailure() {
		// Arrange
		IDataProvider sut = BuildFailing(GenericProviderError);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
			because: "the response was unsuccessful, so it must not reach the caller as an empty collection - but nothing in it names a credential").Which;
		exception.Should().NotBeOfType<AuthenticationException>(
			because: "a database deadlock is not a rejected credential and must keep its own diagnosis");
		exception.Message.Should().StartWith("Failed reading records from entity schema 'Contact'",
			because: "the message has to name the operation that failed");
		exception.Message.Should().Contain(GenericProviderError,
			because: "the platform's own text is the only diagnosable detail available");
	}

	[Test]
	[Description("An unsuccessful response with no error text at all must still fail, and its message must name a cause rather than trailing off after a colon.")]
	public void GetItems_ShouldNameAFallbackCause_WhenTheProviderReportsNoErrorText() {
		// Arrange
		IDataProvider sut = BuildFailing(null);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Account"));

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "ConvertBatchResponse leaves ErrorMessage empty when the response carries no ResponseStatus, and that silence must not become a success")
			.Which.Message.Should().Contain("without an error message",
				because: "a message ending at a bare colon names no cause and is not actionable");
	}

	[Test]
	[Description("A failed batch save must throw rather than be reported through a Success flag the caller may discard.")]
	public void BatchExecute_ShouldThrow_WhenTheBatchIsUnsuccessful() {
		// Arrange
		IDataProvider sut = BuildFailing(GenericProviderError);

		// Act
		Action act = () => sut.BatchExecute(BuildQueries("SysSettingsValue"));

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "AppDataContext.Save() surfaces the flag in a SaveResult that callers such as FeatureCommand compare with '== true' and otherwise ignore")
			.Which.Message.Should().Contain("SysSettingsValue",
				because: "naming the schema being written is what makes the failure attributable");
	}

	[Test]
	[Description("A failed process run must throw so a caller cannot read the empty response values as a completed process.")]
	public void ExecuteProcess_ShouldThrow_WhenTheRunIsUnsuccessful() {
		// Arrange
		IDataProvider sut = BuildFailing(GenericProviderError);
		IExecuteProcessRequest request = Substitute.For<IExecuteProcessRequest>();
		request.ProcessSchemaName.Returns("UsrTestProcess");

		// Act
		Action act = () => sut.ExecuteProcess(request);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an unsuccessful process response carries no result values, which is indistinguishable from a process that produced none")
			.Which.Message.Should().Contain("UsrTestProcess",
				because: "the failing process has to be named");
	}

	[Test]
	[Description("GetSysSettingValue has no Success flag and its provider does not catch, so a rejected read arrives as a raw parser exception; the decorator must classify it with the same rules as an unsuccessful response.")]
	public void GetSysSettingValue_ShouldClassifyAThrownParserFailureAsAuthentication() {
		// Arrange
		IDataProvider sut = BuildFailing(LoginPageParserFailure);

		// Act
		Action act = () => sut.GetSysSettingValue<string>("SchemaNamePrefix");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "get-schema-name-prefix reads this member first, and a raw JsonReaderException there is what made it report success:true with an empty prefix on expired credentials")
			.Which.Message.Should().Contain("SchemaNamePrefix",
				because: "the sys-setting being read has to be named");
	}

	[Test]
	[Description("GetFeatureEnabled has no Success flag either, so a thrown failure must be classified rather than propagated raw.")]
	public void GetFeatureEnabled_ShouldClassifyAThrownFailure() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(
			new ThrowingDataProvider(() => new HttpRequestException(
				"Response status code does not indicate success: 401 (Unauthorized).",
				null,
				HttpStatusCode.Unauthorized)));

		// Act
		Action act = () => sut.GetFeatureEnabled("UsrSomeFeature");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "a typed 401 is a rejected credential regardless of which provider member reported it");
	}

	[Test]
	[Description("A refused connection whose port contains the digits 401 stays a transport failure: wrapping it as an authentication failure sends the operator to repair working credentials.")]
	public void GetItems_ShouldNotTreatAPortContaining401AsRejectedCredentials() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(
			new ThrowingDataProvider(() =>
				new HttpRequestException("Connection refused at http://localhost:40124")));

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		Exception thrown = act.Should().Throw<InvalidOperationException>(
			because: "the failure must still stop the read, but as a transport failure rather than a credential one").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "a port is not a status code");
	}

	[Test]
	[Description("A TLS failure keeps its own diagnosis: reporting an untrusted certificate as rejected credentials replaces the only advice that leads to a fix.")]
	public void GetItems_ShouldNotTreatACertificateFailureAsRejectedCredentials() {
		// Arrange
		IDataProvider sut = BuildFailing("The remote certificate is invalid according to the validation procedure.");

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		Exception thrown = act.Should().Throw<InvalidOperationException>(
			because: "a certificate problem must stop the read and be reported as itself").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "AuthenticationException is the framework type for a TLS handshake too, and misreporting it hides the certificate");
	}

	[Test]
	[Description("Cancellation is the caller's own decision and must reach it unchanged rather than being rewritten into a diagnosis about credentials.")]
	public void GetItems_ShouldPropagateCancellationUnchanged() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(
			new ThrowingDataProvider(() => new OperationCanceledException()));

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		act.Should().Throw<OperationCanceledException>(
			because: "a co-operative shutdown is not a provider failure");
	}

	[Test]
	[Description("A server-controlled error detail is stripped of control characters and capped, so a pathological payload cannot corrupt terminal output or be amplified into every log sink.")]
	public void GetItems_ShouldSanitizeTheProviderReportedDetail() {
		// Arrange
		string hostileDetail = "bad\r\n\tthing" + new string('x', 500);
		IDataProvider sut = BuildFailing(hostileDetail);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		string message = act.Should().Throw<InvalidOperationException>().Which.Message;
		message.Should().NotContain("\r",
			because: "a control character in a server-controlled detail can corrupt a log pipeline");
		message.Should().NotContain("\t",
			because: "every control character is dropped, not only the line breaks");
		message.Length.Should().BeLessThan(500,
			because: "the detail is capped so a multi-megabyte payload cannot be amplified downstream");
	}

	[Test]
	[Description("A successful response passes through untouched, so the decorator adds no behavior on the happy path.")]
	public void GetItems_ShouldReturnTheResponse_WhenTheCallSucceeds() {
		// Arrange
		List<Dictionary<string, object>> rows = [new() { { "Id", Guid.NewGuid() } }];
		IDataProvider sut = new ClassifyingDataProvider(new SucceedingDataProvider(rows));

		// Act
		IItemsResponse response = sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		response.Success.Should().BeTrue(
			because: "a successful response must not be altered by the classification wrapper");
		response.Items.Should().BeEquivalentTo(rows,
			because: "the decorator must hand the payload through unchanged");
	}

	[Test]
	[Description("The decorator refuses a null inner provider at construction rather than failing later on the first data access.")]
	public void Constructor_ShouldRejectANullInnerProvider() {
		// Arrange
		Action act = () => _ = new ClassifyingDataProvider(null);

		// Act & Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a decorator with nothing to decorate would fail at the first read with an unrelated NullReferenceException");
	}

	[Test]
	[Description("A null response is reachable - ATF's own consumer guards for it - and must be reported as a named failure rather than as an empty collection or a NullReferenceException.")]
	public void GetItems_ShouldThrowANamedFailure_WhenTheProviderReturnsNoResponse() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(new NullResponseDataProvider());

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		Exception thrown = act.Should().Throw<InvalidOperationException>(
			because: "reading Success off a null response would raise a bare NullReferenceException, which names neither the operation nor the cause").Which;
		thrown.Should().NotBeOfType<NullReferenceException>(
			because: "the whole point of the decorator is that a provider failure arrives named");
		thrown.Message.Should().Contain("returned no response",
			because: "the operator has to be told what the provider actually did");
	}
}
