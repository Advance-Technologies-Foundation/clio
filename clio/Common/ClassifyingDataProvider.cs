using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using ATF.Repository;
using ATF.Repository.Providers;

namespace Clio.Common;

/// <summary>
/// An <see cref="IDataProvider"/> decorator that turns an unsuccessful ATF response into an exception.
/// </summary>
/// <remarks>
/// <para>
/// ATF.Repository's <c>RemoteDataProvider</c> never throws: every method wraps its work in a
/// <c>catch</c> and returns a response whose <c>Success</c> is <see langword="false"/>, whose
/// <c>ErrorMessage</c> holds the exception text, and whose payload is empty. The consumer side then
/// drops the flag - <c>AppDataContextFactory.GetAppDataContext(provider).Models&lt;T&gt;()</c> reads
/// <c>items.Success ? items.Items : new List&lt;...&gt;()</c> - so a rejected read arrives at a command
/// as a legitimate empty collection and the command reports success (defect issue #1222).
/// </para>
/// <para>
/// This decorator is the only barrier between that behaviour and every clio command, so it wraps the
/// provider at BOTH construction sites in <c>BindingsModule</c> (the active-environment registration and
/// the per-environment <c>ISysSettingsManager</c> factory). A <c>Success == false</c> response must never
/// reach a caller as an empty result.
/// </para>
/// <para>
/// <see cref="IDataProvider.GetSysSettingValue{T}"/> and <see cref="IDataProvider.GetFeatureEnabled"/>
/// return a plain value with no <c>Success</c> flag to inspect, and their provider implementations do
/// <b>not</b> catch - a login page instead of JSON surfaces as a raw <c>JsonReaderException</c>. Those
/// two are therefore classified from the thrown exception with the same rules, so an expired password
/// reaches the operator as an authentication failure rather than as a parser error.
/// </para>
/// </remarks>
public sealed class ClassifyingDataProvider : IDataProvider {

	// The stand-in when the name a diagnosis would quote is not available. Named rather than repeated
	// inline so the four sites that build an operation label cannot drift apart.
	private const string UnknownName = "unknown";

	/// <summary>Cap on the server-controlled detail embedded in an exception message.</summary>
	private const int MaxFailureDetailLength = 300;

	/// <summary>
	/// Stand-in detail for a failure the provider reported with no text at all. <c>ConvertBatchResponse</c>
	/// sets <c>ErrorMessage</c> to <see cref="string.Empty"/> when the batch carries no
	/// <c>ResponseStatus</c>, and <c>new ExecuteResponse()</c> leaves it <see langword="null"/>, so
	/// without this the message would end at a bare colon and name no cause.
	/// </summary>
	private const string UnreportedFailureDetail =
		"the environment reported an unsuccessful response without an error message.";

	private readonly IDataProvider _inner;

	/// <summary>Wraps <paramref name="inner"/> so its unsuccessful responses become exceptions.</summary>
	/// <param name="inner">The provider that performs the actual data access.</param>
	public ClassifyingDataProvider(IDataProvider inner) =>
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));

	/// <summary>Reads an entity schema's default values, failing instead of reporting an empty set.</summary>
	/// <param name="schemaName">The entity schema whose defaults are read.</param>
	/// <returns>The provider's successful response.</returns>
	public IDefaultValuesResponse GetDefaultValues(string schemaName) {
		string operation = $"reading default values for entity schema '{schemaName}'";
		IDefaultValuesResponse response = Guard(() => _inner.GetDefaultValues(schemaName), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <summary>Runs a select query, failing instead of reporting an empty row set.</summary>
	/// <param name="selectQuery">The query to run.</param>
	/// <returns>The provider's successful response.</returns>
	public IItemsResponse GetItems(ISelectQuery selectQuery) {
		string operation = $"reading records from entity schema '{selectQuery?.RootSchemaName ?? UnknownName}'";
		IItemsResponse response = Guard(() => _inner.GetItems(selectQuery), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <summary>Applies a batch of writes, failing instead of reporting an unsuccessful result.</summary>
	/// <param name="queries">The writes to apply.</param>
	/// <returns>The provider's successful response.</returns>
	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) {
		string operation = $"saving records to entity schema(s) '{DescribeSchemas(queries)}'";
		IExecuteResponse response = Guard(() => _inner.BatchExecute(queries), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <summary>Reads a sys-setting value, classifying anything the provider throws.</summary>
	/// <typeparam name="T">The value type to read.</typeparam>
	/// <param name="sysSettingCode">The sys-setting code.</param>
	/// <returns>The value the environment holds.</returns>
	public T GetSysSettingValue<T>(string sysSettingCode) =>
		Guard(() => _inner.GetSysSettingValue<T>(sysSettingCode),
			$"reading sys-setting '{sysSettingCode}'");

	/// <summary>Reads a feature state, classifying anything the provider throws.</summary>
	/// <param name="featureCode">The feature code.</param>
	/// <returns><see langword="true"/> when the feature is on.</returns>
	public bool GetFeatureEnabled(string featureCode) =>
		Guard(() => _inner.GetFeatureEnabled(featureCode),
			$"reading the state of feature '{featureCode}'");

	/// <summary>Runs a business process, failing instead of reporting an unsuccessful response.</summary>
	/// <param name="request">The process and its parameters.</param>
	/// <returns>The provider's successful response.</returns>
	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) {
		string operation = $"running process '{request?.ProcessSchemaName ?? UnknownName}'";
		IExecuteProcessResponse response = Guard(() => _inner.ExecuteProcess(request), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <summary>
	/// Runs an inner call and gives a thrown failure the SAME diagnosis the <c>Success == false</c> path
	/// produces - but only when it can improve on it.
	/// </summary>
	/// <remarks>
	/// A transport fault is RETHROWN UNCHANGED, deliberately. An earlier revision wrapped everything
	/// non-authentication into an <see cref="InvalidOperationException"/>, which erased the exception type
	/// and made the <c>HttpRequestException</c> / <c>WebException</c> / <c>SocketException</c> arms of
	/// <c>SysSettingsCommand.CategorizeError</c> and <c>SchemaNamePrefixTool</c> unreachable: a refused
	/// connection reported "Failed reading records from entity schema 'X': Connection refused..." instead
	/// of "Network error reading sys-setting.". Only two rewrites earn their place here - the
	/// authentication verdict, and the ambiguous non-JSON page - because in both cases the composed
	/// message says something the original exception does not.
	/// </remarks>
	private static TResult Guard<TResult>(Func<TResult> call, string operation) {
		try {
			return call();
		} catch (OperationCanceledException) {
			//Cancellation is the caller's own decision, not a provider failure; rewriting it would hide
			//a co-operative shutdown behind a diagnosis about credentials.
			throw;
		} catch (AuthenticationException) {
			//Already the strongest available diagnosis, whichever way CategorizeError later reads it
			//(it asks the same classifier, so a TLS handshake keeps its own answer).
			throw;
		} catch (Exception exception) {
			string detail = Sanitize(exception.Message);
			if (AuthenticationFailureClassifier.IsAuthenticationFailure(exception)) {
				throw new AuthenticationException(AuthenticationMessage(operation, detail), exception);
			}
			//Prose is consulted ONLY when there is no typed status to read. A typed status is
			//authoritative in both directions, so a typed 404 whose body happens to mention a standalone
			//401 must stay a routing failure.
			if (!AuthenticationFailureClassifier.HasTypedStatus(exception)
				&& AuthenticationFailureClassifier.ClassifyProviderErrorMessage(detail)
					== AuthenticationFailureClassifier.ProviderFailureVerdict.NonJsonPage) {
				throw new DataProviderFailureException(NonJsonPageMessage(operation, detail), exception);
			}
			throw;
		}
	}

	/// <summary>Returns <paramref name="response"/> when the call succeeded, and throws otherwise.</summary>
	/// <remarks>
	/// The flag and the message are read through delegates rather than passed as values, because a
	/// <see langword="null"/> response has to be diagnosed HERE. ATF's own <c>LoadDataCollection</c>
	/// guards with <c>items != null &amp;&amp; items.Success</c>, so a null response is reachable - and
	/// evaluating <c>response.Success</c> at the call site would raise a bare
	/// <see cref="NullReferenceException"/> instead of a named failure.
	/// <para>
	/// This is the one path that DOES wrap into an <see cref="InvalidOperationException"/>, because there
	/// is no original exception to preserve: the provider caught it and kept only its text.
	/// </para>
	/// </remarks>
	/// <exception cref="AuthenticationException">The provider's error names rejected credentials.</exception>
	/// <exception cref="InvalidOperationException">
	/// The provider failed for any other reason, or returned no response at all.
	/// </exception>
	private static TResponse EnsureSuccess<TResponse>(TResponse response, Func<TResponse, bool> readSuccess,
		Func<TResponse, string> readErrorMessage, string operation) where TResponse : class {
		if (response is null) {
			throw new DataProviderFailureException(
				$"Failed {operation}: the data provider returned no response.");
		}
		if (readSuccess(response)) {
			return response;
		}
		//Capped BEFORE classification: the text is server-controlled and every rule is a scan over it.
		string detail = Sanitize(readErrorMessage(response));
		throw AuthenticationFailureClassifier.ClassifyProviderErrorMessage(detail) switch {
			AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication
				=> new AuthenticationException(AuthenticationMessage(operation, detail)),
			AuthenticationFailureClassifier.ProviderFailureVerdict.NonJsonPage
				=> new DataProviderFailureException(NonJsonPageMessage(operation, detail)),
			var _ => new DataProviderFailureException(GenericMessage(operation, detail))
		};
	}

	private static string AuthenticationMessage(string operation, string detail) =>
		$"Authentication failed while {operation}: {detail} "
		+ "Verify the environment credentials (for an expired password, repair the registered profile) "
		+ "and retry.";

	/// <summary>
	/// The honest message for the HTML-where-JSON signal on its own: it names BOTH causes, because the
	/// message the provider preserved cannot tell them apart. Claiming one would send the operator to
	/// repair working credentials whenever the real problem was a proxy, a gateway or a wrong path.
	/// </summary>
	private static string NonJsonPageMessage(string operation, string detail) =>
		$"Failed {operation}: the environment answered with a non-JSON page where a DataService response "
		+ "was expected - either the session was rejected (expired password / login redirect) or the URL "
		+ $"does not reach Creatio (proxy, gateway, wrong path). Detail: {detail}";

	private static string GenericMessage(string operation, string detail) =>
		$"Failed {operation}: {detail}";

	/// <summary>
	/// Normalizes a server-controlled detail before it is embedded in an exception message, and before it
	/// is classified. Delegates the character neutralization and the length cap to the shared
	/// <see cref="TextUtilities.SanitizeForDisplay"/>; an absent detail becomes
	/// <see cref="UnreportedFailureDetail"/> rather than leaving the message ending at a colon.
	/// </summary>
	private static string Sanitize(string detail) {
		if (string.IsNullOrWhiteSpace(detail)) {
			return UnreportedFailureDetail;
		}
		string cleaned = TextUtilities.SanitizeForDisplay(detail, MaxFailureDetailLength).Trim();
		return cleaned.Length == 0 ? UnreportedFailureDetail : cleaned;
	}

	/// <summary>Names the schemas a batch touches, for the diagnostic.</summary>
	private static string DescribeSchemas(List<IBaseQuery> queries) {
		if (queries is null || queries.Count == 0) {
			return UnknownName;
		}
		string[] names = queries
			.Select(query => query?.RootSchemaName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		return names.Length == 0 ? UnknownName : string.Join(", ", names);
	}
}
