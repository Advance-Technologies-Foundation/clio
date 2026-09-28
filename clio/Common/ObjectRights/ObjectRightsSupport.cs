using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Common.ObjectRights;

/// <summary>Rules shared by the object-rights commands, the service client and the connected-object resolver.</summary>
public static class ObjectRightsSupport {

	private static readonly Regex SchemaNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private static readonly string[] OperationOrder = { "read", "create", "edit", "delete" };

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
	/// attributed to the object being read rather than end the run. Programming errors (NullReferenceException,
	/// ArgumentException, ...) are deliberately NOT service failures. An HTTP timeout surfaces as
	/// TaskCanceledException, hence OperationCanceledException.
	/// </summary>
	/// <param name="exception">The exception a service call threw.</param>
	/// <returns><see langword="true"/> for a service failure.</returns>
	public static bool IsServiceFailure(Exception exception) =>
		exception is InvalidOperationException or HttpRequestException or TimeoutException or IOException
			or JsonException or UnauthorizedAccessException or ResponseTooLargeException or OperationCanceledException;

	/// <summary>
	/// Whether <paramref name="exception"/> is a hang (a timeout) rather than a fault answered by the server. Probing
	/// the next candidate after a timeout only multiplies the wait, so the probe loop stops on it.
	/// </summary>
	/// <param name="exception">The exception a service call threw.</param>
	/// <returns><see langword="true"/> for a timeout.</returns>
	public static bool IsTimeout(Exception exception) =>
		exception is TimeoutException or OperationCanceledException;

	/// <summary>
	/// The operations a set of rows adds up to, in grid order (read, create, edit, delete). A grantee can hold
	/// several rows; the writer changes all of them, so every reader reports their union.
	/// </summary>
	/// <param name="rows">The rows of one grantee.</param>
	/// <returns>The distinct operation names held by any of the rows.</returns>
	public static IReadOnlyList<string> HeldOperations(IEnumerable<RoleOperationRights> rows) =>
		rows.SelectMany(row => row.OperationNames()).Distinct()
			.OrderBy(op => Array.IndexOf(OperationOrder, op)).ToArray();
}
