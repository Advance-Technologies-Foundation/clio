using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
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

	/// <summary>
	/// What a request TIMEOUT looks like once ATF has swallowed it. Verified with ilspycmd against
	/// creatio.client 2.0.2: the synchronous <c>CreatioClient.ExecutePostRequest</c> passes
	/// <c>CancellationToken.None</c>, so the only cancellation source in the stack is
	/// <c>CreateTimeout</c>'s <c>CancelAfter</c>. Reaching the authentication step's 100 000 ms cap
	/// produces <c>WebException(WebExceptionStatus.Timeout)</c> whose message is the cancellation prose,
	/// and <c>RemoteDataProvider.GetItems</c> concatenates the message with its inner message.
	/// Reproduced live against a listener that accepts and never answers: after 100 s clio reported
	/// <c>Failed reading records from entity schema 'VwWebServiceV2': The operation was canceled.The
	/// operation was canceled.</c>
	/// </summary>
	private const string SwallowedTimeoutError = "The operation was canceled.The operation was canceled.";

	/// <summary>
	/// The other timeout wording: the request itself outlives its cap (1 800 000 ms for a select,
	/// 600 000 ms for a batch), <c>ReadResponseBody</c> observes it through <c>Task.Result</c>, and the
	/// resulting <c>AggregateException(TaskCanceledException)</c> is what ATF swallows.
	/// </summary>
	private const string SwallowedTaskCanceledError =
		"One or more errors occurred. (A task was canceled.)A task was canceled.";

	/// <summary>Mirrors ClassifyingDataProvider.MaxFailureDetailLength, which is private to it.</summary>
	private const int MaxFailureDetailLength = 300;

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
		exception.Message.Should().Contain("The password for the registered user has expired.",
			because: "issue #1333: the cause survives the classification as a FIXED LOCAL sentence - the "
			+ "platform's own prose must not be copied into a caller-visible field");
		exception.Message.Should().NotContain("Your password has expired",
			because: "server-authored text can carry a token, an address, bidi controls or an "
			+ "instruction-shaped sentence, and this message reaches the CLI, the log and an MCP envelope");
		exception.Should().BeOfType<SessionRejectedException>(
			because: "the raw excerpt has to survive somewhere the operator can reach it at debug verbosity");
		((SessionRejectedException)exception).ServerDetail.Should().Contain("Your password has expired",
			because: "the correlation ID is the bridge back to what the server actually said");
		exception.Message.Should().Contain("Verify the environment credentials",
			because: "an automation caller needs a recovery action, not just an exception type");
		exception.Message.Should().Contain("SysSettings",
			because: "naming the entity schema is what makes the failure attributable to an operation");
	}

	[Test]
	[Description("The HTML-where-JSON signal alone cannot prove a rejected session - a 404, a WAF block and a gateway page produce the same Newtonsoft message - so the read path must fail while naming BOTH causes rather than claiming one.")]
	public void GetItems_ShouldNameBothCauses_WhenOnlyTheNonJsonPageSignalIsPresent() {
		// Arrange
		IDataProvider sut = BuildFailing(LoginPageParserFailure);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("SysSchema"));

		// Assert
		Exception thrown = act.Should().Throw<InvalidOperationException>(
			because: "the read must still fail closed - an unsuccessful response may never become an empty collection (issue #1222)").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "ATF keeps only the parser's message, never the body, so no login-page marker is available to corroborate a credential verdict");
		thrown.Message.Should().Contain("session was rejected",
			because: "an expired password is one of the two real causes and the operator has to see it");
		thrown.Message.Should().Contain("proxy, gateway, wrong path",
			because: "the other cause is equally likely and naming only the first sends the operator to repair a working login");
	}

	[Test]
	[Description("A parser failure that arrives WITH a corroborating credential marker is an authentication failure, so requiring corroboration did not switch the signal off.")]
	public void GetItems_ShouldThrowAuthenticationException_WhenTheParserFailureIsCorroborated() {
		// Arrange
		IDataProvider sut = BuildFailing(
			"Unexpected character encountered while parsing value: <. Your password has expired.");

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("SysSettings"));

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "prose naming the credential outcome beside the parser failure removes the ambiguity")
			.Which.Message.Should().Contain("Verify the environment credentials",
				because: "a definite credential verdict must carry the recovery action");
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
		exception.Should().BeOfType<DataProviderFailureException>(
			because: "the distinct type is what lets a consumer tell 'the provider failed and its message is the whole diagnosis' apart from clio's own argument/state InvalidOperationExceptions - SchemaNamePrefixTool surfaces this message but keeps its generic label for the rest");
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
	[Description("GetSysSettingValue has no Success flag and its provider does not catch, so a rejected read arrives as a raw parser exception; the decorator must still turn it into a named failure that names both causes rather than letting it escape as a JsonReaderException.")]
	public void GetSysSettingValue_ShouldClassifyAThrownParserFailure() {
		// Arrange
		IDataProvider sut = BuildFailing(LoginPageParserFailure);

		// Act
		Action act = () => sut.GetSysSettingValue<string>("SchemaNamePrefix");

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
			because: "get-schema-name-prefix reads this member first, and a raw JsonReaderException there is what made it report success:true with an empty prefix on expired credentials").Which;
		exception.Message.Should().Contain("SchemaNamePrefix",
			because: "the sys-setting being read has to be named");
		exception.Message.Should().Contain("session was rejected",
			because: "an expired password is one of the two causes and must be offered to the operator");
		exception.Message.Should().Contain("proxy, gateway, wrong path",
			because: "the message alone cannot distinguish a login page from a gateway page, so it must not claim one");
	}

	[Test]
	[Description("A corroborated credential failure on the flagless member IS an authentication failure, so the prefix tool still reports the credential diagnosis when the platform names it.")]
	public void GetSysSettingValue_ShouldThrowAuthenticationException_WhenTheFailureNamesACredential() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException(
				"Response status code does not indicate success: 401 (Unauthorized).",
				null,
				HttpStatusCode.Unauthorized)));

		// Act
		Action act = () => sut.GetSysSettingValue<string>("SchemaNamePrefix");

		// Assert
		act.Should().Throw<AuthenticationException>(
			because: "a typed 401 is a definite rejected credential, whichever provider member reported it");
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
	[Description("A thrown transport fault is rethrown UNCHANGED: wrapping it into an InvalidOperationException erased the type and made the 'Network error ...' arms of SysSettingsCommand.CategorizeError and SchemaNamePrefixTool unreachable.")]
	public void GetItems_ShouldRethrowATransportFaultWithItsOriginalType() {
		// Arrange
		HttpRequestException original = new("Connection refused at http://localhost:40124");
		IDataProvider sut = new ClassifyingDataProvider(new ThrowingDataProvider(() => original));

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		Exception thrown = act.Should().Throw<HttpRequestException>(
			because: "CategorizeError switches on the exception TYPE to say 'Network error ...', so the type is load-bearing and must survive the decorator").Which;
		thrown.Should().BeSameAs(original,
			because: "rethrowing the original preserves its stack and inner chain for the command-layer classifier");
	}

	[Test]
	[Description("A typed 404 whose prose happens to carry a standalone 401 stays a routing failure: a typed status is authoritative in both directions and must not be overridden by the message.")]
	public void GetItems_ShouldNotConsultProse_WhenATypedStatusIsPresent() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new HttpRequestException(
				"Not found. The remote server returned an error: 401.", null, HttpStatusCode.NotFound)));

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		Exception thrown = act.Should().Throw<HttpRequestException>(
			because: "a routing failure keeps its own type and diagnosis").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "a typed 404 is authoritative; falling through to prose is what turned a wrong path into 'repair your credentials'");
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
			because: "a Success=false response has no original exception to preserve, so it is wrapped - but it must still fail").Which;
		thrown.Should().NotBeOfType<AuthenticationException>(
			because: "AuthenticationException is the framework type for a TLS handshake too, and misreporting it hides the certificate");
	}

	[Test]
	[Description("An AuthenticationException thrown by the provider itself is rethrown unchanged, so a TLS handshake keeps whatever diagnosis it arrived with.")]
	public void GetItems_ShouldRethrowAnAuthenticationExceptionUnchanged() {
		// Arrange
		AuthenticationException original = new("The remote certificate is invalid.");
		IDataProvider sut = new ClassifyingDataProvider(new ThrowingDataProvider(() => original));

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		act.Should().Throw<AuthenticationException>().Which.Should().BeSameAs(original,
			because: "it is already the strongest available diagnosis, and CategorizeError asks the same classifier about it");
	}

	[Test]
	[Description("Cancellation is the caller's own decision and must reach it unchanged rather than being rewritten into a diagnosis about credentials. This guards the DECORATOR CONTRACT for a shape the production stack does not produce: IDataProvider takes no CancellationToken and the synchronous Creatio client passes CancellationToken.None, so no caller token can reach the transport (issue #1377).")]
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
	[Description("An unsuccessful read whose text says the operation was canceled is a TRANSPORT TIMEOUT, not a caller cancellation, and must stay a data-provider failure (issue #1377).")]
	public void GetItems_ShouldKeepASwallowedTimeoutAsAProviderFailure() {
		// Arrange
		IDataProvider sut = BuildFailing(SwallowedTimeoutError);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("VwWebServiceV2"));

		// Assert
		Exception thrown = act.Should().Throw<DataProviderFailureException>(
			because: "no caller token can reach this call - IDataProvider carries none and the synchronous CreatioClient.ExecutePostRequest passes CancellationToken.None, so CreateTimeout's CancelAfter is the only cancellation source and this text can only mean the 100 000 / 1 800 000 ms cap was reached").Which;
		thrown.Should().NotBeAssignableTo<OperationCanceledException>(
			because: "re-raising it as a cancellation would hide every timeout and make CompilationHistoryPoller's 'exception is not OperationCanceledException' filter tolerate a dead environment forever");
		thrown.Message.Should().Contain("VwWebServiceV2",
			because: "the operator still needs the failing read named");
	}

	[Test]
	[Description("The same rule on the process path: a run that came back unsuccessful with cancellation prose timed out and must stay a data-provider failure (issue #1377).")]
	public void ExecuteProcess_ShouldKeepASwallowedTimeoutAsAProviderFailure() {
		// Arrange
		IDataProvider sut = BuildFailing(SwallowedTimeoutError);
		IExecuteProcessRequest request = Substitute.For<IExecuteProcessRequest>();
		request.ProcessSchemaName.Returns("UsrTestProcess");

		// Act
		Action act = () => sut.ExecuteProcess(request);

		// Assert
		Exception thrown = act.Should().Throw<DataProviderFailureException>(
			because: "RemoteDataProvider.ExecuteProcess catches every exception into Success = false exactly as GetItems does, and the cancellation prose there has the same single cause - an expired request cap").Which;
		thrown.Should().NotBeAssignableTo<OperationCanceledException>(
			because: "a timed-out process run is a failure the caller must see, not a shutdown it asked for");
		thrown.Message.Should().Contain("UsrTestProcess",
			because: "the failing process has to be named");
	}

	[Test]
	[Description("The wording the request cap itself produces - AggregateException(TaskCanceledException) via Task.Result - is also a timeout and must not be reported as a cancellation (issue #1377).")]
	public void GetItems_ShouldKeepASwallowedTaskCancellationAsAProviderFailure() {
		// Arrange
		IDataProvider sut = BuildFailing(SwallowedTaskCanceledError);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("Contact"));

		// Assert
		act.Should().Throw<DataProviderFailureException>(
			because: "ReadResponseBody observes the send through Task.Result, so a request that outlived its 1 800 000 ms cap reaches ErrorMessage in this shape - still a timeout")
			.Which.Should().NotBeAssignableTo<OperationCanceledException>(
				because: "the two timeout wordings must be classified identically");
	}

	[Test]
	[Description("A thrown AggregateException(TaskCanceledException) - the shape Task.Result produces on the value-returning members - must travel out unchanged rather than being rewritten into a verdict about credentials (issue #1377).")]
	public void GetSysSettingValue_ShouldNotRewriteAThrownTaskCancellationIntoAnAuthenticationVerdict() {
		// Arrange
		IDataProvider sut = new ClassifyingDataProvider(new ThrowingDataProvider(
			() => new AggregateException(new TaskCanceledException("A task was canceled."))));

		// Act
		Action act = () => sut.GetSysSettingValue<string>("SchemaNamePrefix");

		// Assert
		Exception thrown = act.Should().Throw<AggregateException>(
			because: "a transport fault is rethrown unchanged so the network arms of SysSettingsCommand.CategorizeFailure stay reachable").Which;
		thrown.Should().NotBeAssignableTo<AuthenticationException>(
			because: "nothing in a task-cancellation names a credential, and claiming one would send the operator to repair a working password");
		thrown.Should().NotBeOfType<DataProviderFailureException>(
			because: "there is an original exception to preserve here, so the non-JSON-page rewrite must not fire either");
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
		message.Length.Should().BeLessThan(MaxFailureDetailLength * 2,
			because: $"the detail is capped at {MaxFailureDetailLength} characters so a multi-megabyte payload cannot be amplified downstream, leaving room only for the fixed message text");
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
