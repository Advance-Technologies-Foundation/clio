using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Common.ObjectRights;

/// <summary>Rules shared by the object-rights commands, the service client and the connected-object resolver.</summary>
public static class ObjectRightsSupport {

	private static readonly Regex SchemaNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	// These objects expose the role/user directory, security configuration or platform metadata: widening access to
	// one of them makes that data readable through DataService wherever record permissions do not also protect it.
	// So the connected listing of get-object-rights — the step before granting — never offers them. set-object-rights
	// changes one object the caller names, so a change to one of these is named in the arguments the host shows.
	private static readonly string[] SecurityObjectPrefixes =
		{ "SysAdmin", "SysUser", "SysSchema", "SysPackage", "SysSettings", "SysLic", "SysProcess", "Vw" };

	private static readonly string[] SecurityObjectSuffixes = { "Right", "Rights" };

	/// <summary>
	/// Whether <paramref name="schemaName"/> is a security or system object, which get-object-rights never reads as a
	/// connected object. A filter on the discovery listing, not a security boundary.
	/// </summary>
	/// <param name="schemaName">The normalized entity schema name.</param>
	/// <returns><see langword="true"/> for a security or system object.</returns>
	public static bool IsSecurityOrSystemObject(string schemaName) =>
		SecurityObjectPrefixes.Any(prefix => schemaName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		|| SecurityObjectSuffixes.Any(suffix => schemaName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Renders a caller- or server-supplied name (an object name that failed validation, a role name) for output on one
	/// line: control characters and line breaks are replaced and the length is capped, so the value cannot invent a line
	/// that reads like the command's own result.
	/// </summary>
	/// <param name="value">The name to render.</param>
	/// <returns>The display-safe name.</returns>
	public static string Display(string value) =>
		TextUtilities.SanitizeForDisplay(value ?? string.Empty, MaxDisplayLength);

	/// <summary>
	/// Renders a service or exception message for output: credentials, hosts and paths are redacted first (the CLI and the
	/// log have no redaction pass of their own), then the text is kept on one line and capped, like <see cref="Display"/>.
	/// A database fault or a platform message can carry line breaks that would otherwise start a line of their own.
	/// </summary>
	/// <param name="message">The message to render.</param>
	/// <returns>The display-safe message.</returns>
	public static string DisplayError(string message) =>
		TextUtilities.SanitizeForDisplay(SensitiveErrorTextRedactor.Redact(message ?? string.Empty), MaxErrorLength);

	/// <summary>
	/// Renders an exception a service call threw, like <see cref="DisplayError"/>. Creatio's client runs the request
	/// through <c>Task.Result</c>, so a fault arrives wrapped in an <see cref="AggregateException"/>, whose message puts
	/// a generic "One or more errors occurred." before the fault's own; a wrapper around ONE fault is rendered by that
	/// fault alone. It is not <c>GetReadableMessageException</c>: that renders the command's top-level error line, while
	/// this renders a fault inside a result line, so it stays on one line and capped.
	/// </summary>
	/// <param name="exception">The exception a service call threw.</param>
	/// <returns>The display-safe message.</returns>
	public static string DisplayFailure(Exception exception) =>
		DisplayError(exception is AggregateException aggregate
			&& aggregate.Flatten().InnerExceptions is { Count: 1 } inner
				? inner[0].Message
				: exception?.Message);

	private const int MaxDisplayLength = 200;

	private const int MaxErrorLength = 500;

	// How many objects a title names in a refusal before the rest are counted; a title shared by many objects would
	// otherwise grow the message with every one of them.
	private const int TitleMatchesNamed = 5;

	/// <summary>
	/// Whether <paramref name="caption"/> is a title of its own: one the service returned, other than the object's code
	/// (case aside). Only then does the output show it next to the code.
	/// </summary>
	/// <param name="schemaName">The object's code (entity schema name).</param>
	/// <param name="caption">The object's title, when the service returned one.</param>
	/// <returns><see langword="true"/> when the title differs from the code.</returns>
	public static bool HasOwnTitle(string schemaName, string caption) =>
		!string.IsNullOrWhiteSpace(caption)
		&& !string.Equals(caption.Trim(), schemaName, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Names an object for output by its title and its code — <c>'Creatio functionality' (Feature)</c> — when it has a
	/// title of its own, otherwise by its code, quoted (<c>'Feature'</c>). A developer names an object by its title as
	/// often as by its code, and the two can name different objects, so the output shows both.
	/// </summary>
	/// <param name="schemaName">The object's code (entity schema name).</param>
	/// <param name="caption">The object's title, when the service returned one.</param>
	/// <returns>The display-safe name.</returns>
	public static string FormatObject(string schemaName, string caption) =>
		HasOwnTitle(schemaName, caption) ? $"'{DisplayTitle(caption)}' ({schemaName})" : $"'{schemaName}'";

	// A title is text anyone with schema rights can set, printed inside quotes and before the code. Its own quotes are
	// turned into a typographic apostrophe and its length is kept short, so a title such as "Orders' (UsrOrder)" cannot
	// close the quotes early and show another object's code where the real one belongs.
	private static string DisplayTitle(string caption) =>
		TextUtilities.SanitizeForDisplay(caption?.Trim() ?? string.Empty, MaxTitleLength).Replace('\'', '’');

	private const int MaxTitleLength = 100;

	/// <summary>
	/// Renders the objects a title names, as <c>'Feature' (code: Specification)</c> — the form the process tools use
	/// for the candidates of a caption — at most five of them, then how many more.
	/// </summary>
	/// <param name="matches">The objects with the title.</param>
	/// <returns>The display-safe list.</returns>
	public static string FormatTitleMatches(IReadOnlyList<ObjectTitleMatch> matches) {
		string listed = string.Join("; ", matches.Take(TitleMatchesNamed)
			.Select(match => $"'{DisplayTitle(match.Caption)}' (code: {match.Name})"));
		return matches.Count <= TitleMatchesNamed ? listed : $"{listed}; and {matches.Count - TitleMatchesNamed} more";
	}

	/// <summary>
	/// Renders one row for output: its position, the grantee's name — with its id when asked for — and its operations,
	/// e.g. <c>[1] Sales managers: read/create</c>. Both commands and the read-back use it, so a row is always rendered
	/// the same way; get-object-rights adds the grantee's id.
	/// </summary>
	/// <param name="row">The row to render.</param>
	/// <param name="withGranteeId">Also show the grantee's SysAdminUnit id.</param>
	/// <returns>The display-safe row.</returns>
	public static string FormatRow(RoleOperationRights row, bool withGranteeId = false) =>
		$"[{row.Position}] {Display(row.GranteeName)}{(withGranteeId ? $" ({row.GranteeId})" : "")}: "
		+ FormatOperations(row);

	/// <summary>Renders rows in priority order, separated by <c>"; "</c>, or <c>"none"</c> when there are none.</summary>
	/// <param name="rows">The rows to render.</param>
	/// <returns>The display-safe rows.</returns>
	public static string FormatRows(IEnumerable<RoleOperationRights> rows) {
		string[] formatted = rows.OrderBy(row => row.Position).Select(row => FormatRow(row)).ToArray();
		return formatted.Length == 0 ? "none" : string.Join("; ", formatted);
	}

	/// <summary>The operations a row grants, in grid order (<c>read/create</c>), or <c>no operations</c>.</summary>
	/// <param name="row">The row.</param>
	/// <returns>The operations text.</returns>
	public static string FormatOperations(RoleOperationRights row) {
		IReadOnlyList<string> operations = row.OperationNames();
		return operations.Count == 0 ? "no operations" : string.Join("/", operations);
	}

	/// <summary>The operations a request names, in the order it names them (<c>read/create</c>).</summary>
	/// <param name="operations">The operations.</param>
	/// <returns>The operations text, in the same spelling as a row's.</returns>
	public static string FormatOperations(IEnumerable<ObjectOperation> operations) =>
		string.Join("/", operations.Select(ObjectOperationNames.Of));

	/// <summary>The state of the "Use operation permissions" switch: <c>ON</c> or <c>OFF</c>.</summary>
	/// <param name="state">The object's state.</param>
	/// <returns>The switch text.</returns>
	public static string FormatSwitch(ObjectRightsState state) => state.AdministratedByOperations ? "ON" : "OFF";

	/// <summary>
	/// Trims <paramref name="raw"/> and accepts it only when it is a plain schema identifier. A padded or
	/// decorated name must never reach the security/system gate: the gate matches the name as a string, while
	/// SQL Server ignores trailing spaces in the <c>SysSchema.Name</c> comparison and would still find the table.
	/// </summary>
	/// <param name="raw">The entity schema name as the caller passed it.</param>
	/// <param name="schemaName">The trimmed name, when valid.</param>
	/// <returns><see langword="true"/> when the name is a valid schema identifier.</returns>
	public static bool TryNormalizeSchemaName(string raw, out string schemaName) {
		schemaName = raw?.Trim();
		return !string.IsNullOrEmpty(schemaName) && SchemaNamePattern.IsMatch(schemaName);
	}

	/// <summary>
	/// Whether <paramref name="exception"/> is a failure of the Creatio service call itself — a transport fault, a
	/// timeout, a non-JSON or empty body, an authentication rejection, an oversized response — that must be
	/// attributed to the object being read rather than end the run. Creatio's client runs the request through
	/// <c>Task.Result</c>, so a timeout or a transport fault arrives as an <see cref="AggregateException"/> (around a
	/// TaskCanceledException or an HttpRequestException): a wrapper is a service failure when every fault in it is one.
	/// InvalidOperationException is included because the shared service clients report an empty or non-JSON body and
	/// a DataService error with it (and WebException derives from it); so that a programming error that throws it (a
	/// LINQ <c>First</c> on an empty sequence, say) is not reported as a service failure, every caller guards only the
	/// service call and the parsing of its response with this filter, never the code that works on the parsed result.
	/// Other programming errors (NullReferenceException, ArgumentException, ...) are never service failures.
	/// </summary>
	/// <param name="exception">The exception a service call threw.</param>
	/// <returns><see langword="true"/> for a service failure.</returns>
	public static bool IsServiceFailure(Exception exception) =>
		exception is AggregateException aggregate
			? aggregate.Flatten().InnerExceptions is { Count: > 0 } faults && faults.All(IsSingleServiceFailure)
			: IsSingleServiceFailure(exception);

	/// <summary>
	/// Whether <paramref name="exception"/> is a hang (a timeout) rather than a fault answered by the server: a
	/// TimeoutException or a cancellation (an HTTP timeout surfaces as TaskCanceledException), a WebException with the
	/// Timeout status (how the login step reports its timeout), or an <see cref="AggregateException"/> that carries one
	/// of them. Probing the next candidate, or reading the next object, after a timeout only multiplies the wait, so
	/// the probe loop and the connected listing stop on it. Meaningful only for an exception
	/// <see cref="IsServiceFailure"/> accepts.
	/// </summary>
	/// <param name="exception">The exception a service call threw.</param>
	/// <returns><see langword="true"/> for a timeout.</returns>
	public static bool IsTimeout(Exception exception) =>
		exception is AggregateException aggregate
			? aggregate.Flatten().InnerExceptions.Any(IsSingleTimeout)
			: IsSingleTimeout(exception);

	/// <summary>
	/// Whether a request that failed with <paramref name="exception"/> may still have been carried out: no answer came
	/// back — a timeout, or a connection that broke (reset, closed) once the request may already have gone out. A fault
	/// the server answered (an HTTP error page, an in-band failure, a body that is not JSON) is not one. A write that
	/// failed this way must be read back and reported as possibly applied. Meaningful only for an exception
	/// <see cref="IsServiceFailure"/> accepts.
	/// </summary>
	/// <param name="exception">The exception a service call threw.</param>
	/// <returns><see langword="true"/> when the request may still have been carried out.</returns>
	public static bool LeavesOutcomeUnknown(Exception exception) =>
		IsTimeout(exception)
		|| (exception is AggregateException aggregate
			? aggregate.Flatten().InnerExceptions.Any(IsSingleConnectionFault)
			: IsSingleConnectionFault(exception));

	// Creatio.Client reports an HTTP error status by returning the body, not by throwing, so an HttpRequestException is a
	// fault of the connection itself; a WebException with ProtocolError carries the server's answer (a login rejection).
	private static bool IsSingleConnectionFault(Exception exception) =>
		exception is HttpRequestException or IOException or SocketException
			or WebException { Status: not WebExceptionStatus.ProtocolError };

	// WebException is named although it derives from InvalidOperationException: the transport faults are meant here, not
	// inherited by accident.
	private static bool IsSingleServiceFailure(Exception exception) =>
		exception is WebException or InvalidOperationException or HttpRequestException or TimeoutException or IOException
			or JsonException or UnauthorizedAccessException or ResponseTooLargeException or OperationCanceledException;

	private static bool IsSingleTimeout(Exception exception) =>
		exception is TimeoutException or OperationCanceledException or WebException { Status: WebExceptionStatus.Timeout }
		|| IsSocketTimeout(exception);

	// A TCP connect or read that times out (an unreachable stand, about 21 s on Windows) surfaces as a SocketException
	// inside the transport fault.
	private static bool IsSocketTimeout(Exception exception) {
		for (Exception current = exception.InnerException; current is not null; current = current.InnerException) {
			if (current is SocketException { SocketErrorCode: SocketError.TimedOut }) {
				return true;
			}
		}
		return false;
	}
}
