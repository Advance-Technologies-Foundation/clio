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

	/// <inheritdoc />
	public IDefaultValuesResponse GetDefaultValues(string schemaName) {
		string operation = $"reading default values for entity schema '{schemaName}'";
		IDefaultValuesResponse response = Guard(() => _inner.GetDefaultValues(schemaName), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <inheritdoc />
	public IItemsResponse GetItems(ISelectQuery selectQuery) {
		string operation = $"reading records from entity schema '{selectQuery?.RootSchemaName ?? "unknown"}'";
		IItemsResponse response = Guard(() => _inner.GetItems(selectQuery), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <inheritdoc />
	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) {
		string operation = $"saving records to entity schema(s) '{DescribeSchemas(queries)}'";
		IExecuteResponse response = Guard(() => _inner.BatchExecute(queries), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <inheritdoc />
	public T GetSysSettingValue<T>(string sysSettingCode) =>
		Guard(() => _inner.GetSysSettingValue<T>(sysSettingCode),
			$"reading sys-setting '{sysSettingCode}'");

	/// <inheritdoc />
	public bool GetFeatureEnabled(string featureCode) =>
		Guard(() => _inner.GetFeatureEnabled(featureCode),
			$"reading the state of feature '{featureCode}'");

	/// <inheritdoc />
	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) {
		string operation = $"running process '{request?.ProcessSchemaName ?? "unknown"}'";
		IExecuteProcessResponse response = Guard(() => _inner.ExecuteProcess(request), operation);
		return EnsureSuccess(response, r => r.Success, r => r.ErrorMessage, operation);
	}

	/// <summary>
	/// Runs an inner call and rewrites anything it throws into the same two diagnoses the
	/// <c>Success == false</c> path produces, so the caller sees one contract regardless of which of the
	/// provider's two failure shapes it hit.
	/// </summary>
	private static TResult Guard<TResult>(Func<TResult> call, string operation) {
		try {
			return call();
		} catch (OperationCanceledException) {
			//Cancellation is the caller's own decision, not a provider failure; rewriting it would hide
			//a co-operative shutdown behind a diagnosis about credentials.
			throw;
		} catch (Exception exception) {
			//The typed classifier reads status codes and exception types; the string one adds the shape a
			//rejected DataService read actually arrives in - a JSON parser error over the login page's HTML,
			//which carries no status and no exception type at all.
			throw AuthenticationFailureClassifier.IsAuthenticationFailure(exception)
				|| AuthenticationFailureClassifier.IsAuthenticationFailure(exception.Message)
				? new AuthenticationException(AuthenticationMessage(operation, exception.Message), exception)
				: new InvalidOperationException(GenericMessage(operation, exception.Message), exception);
		}
	}

	/// <summary>Returns <paramref name="response"/> when the call succeeded, and throws otherwise.</summary>
	/// <remarks>
	/// The flag and the message are read through delegates rather than passed as values, because a
	/// <see langword="null"/> response has to be diagnosed HERE. ATF's own <c>LoadDataCollection</c>
	/// guards with <c>items != null &amp;&amp; items.Success</c>, so a null response is reachable - and
	/// evaluating <c>response.Success</c> at the call site would raise a bare
	/// <see cref="NullReferenceException"/> instead of a named failure.
	/// </remarks>
	/// <exception cref="AuthenticationException">The provider's error names rejected credentials.</exception>
	/// <exception cref="InvalidOperationException">
	/// The provider failed for any other reason, or returned no response at all.
	/// </exception>
	private static TResponse EnsureSuccess<TResponse>(TResponse response, Func<TResponse, bool> readSuccess,
		Func<TResponse, string> readErrorMessage, string operation) where TResponse : class {
		if (response is null) {
			throw new InvalidOperationException(
				$"Failed {operation}: the data provider returned no response.");
		}
		if (readSuccess(response)) {
			return response;
		}
		string errorMessage = readErrorMessage(response);
		throw AuthenticationFailureClassifier.IsAuthenticationFailure(errorMessage)
			? new AuthenticationException(AuthenticationMessage(operation, errorMessage))
			: new InvalidOperationException(GenericMessage(operation, errorMessage));
	}

	private static string AuthenticationMessage(string operation, string detail) =>
		$"Authentication failed while {operation}: {Sanitize(detail)} "
		+ "Verify the environment credentials (for an expired password, repair the registered profile); "
		+ "if the credentials are valid, confirm the environment URL points at Creatio rather than at a "
		+ "proxy or gateway, and retry.";

	private static string GenericMessage(string operation, string detail) =>
		$"Failed {operation}: {Sanitize(detail)}";

	/// <summary>
	/// Normalizes a server-controlled detail before it is embedded in an exception message: every control
	/// character is dropped (so NUL, TAB and escape sequences cannot corrupt terminal output or a log
	/// pipeline) and the result is capped so a pathological payload cannot be amplified into every
	/// downstream log sink. An absent detail becomes <see cref="UnreportedFailureDetail"/> rather than
	/// leaving the message ending at a colon.
	/// </summary>
	private static string Sanitize(string detail) {
		if (string.IsNullOrWhiteSpace(detail)) {
			return UnreportedFailureDetail;
		}
		string cleaned = new string(detail.Where(character => !char.IsControl(character)).ToArray()).Trim();
		if (cleaned.Length == 0) {
			return UnreportedFailureDetail;
		}
		return cleaned.Length > MaxFailureDetailLength
			? cleaned[..MaxFailureDetailLength] + "…"
			: cleaned;
	}

	/// <summary>Names the schemas a batch touches, for the diagnostic.</summary>
	private static string DescribeSchemas(List<IBaseQuery> queries) {
		if (queries is null || queries.Count == 0) {
			return "unknown";
		}
		string[] names = queries
			.Select(query => query?.RootSchemaName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		return names.Length == 0 ? "unknown" : string.Join(", ", names);
	}
}
