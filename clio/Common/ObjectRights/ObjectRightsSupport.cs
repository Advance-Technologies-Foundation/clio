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

	// These objects expose the role/user directory, security configuration or platform metadata: widening access to
	// one of them makes that data readable through DataService wherever record permissions do not also protect it.
	// So set-object-rights asks for --allow-security-object before a grant beyond read, or a disable, on one of them,
	// and the connected listing of get-object-rights — the step before granting — never offers them.
	private static readonly string[] SecurityObjectPrefixes =
		{ "SysAdmin", "SysUser", "SysSchema", "SysPackage", "SysSettings", "SysLic", "SysProcess", "Vw" };

	private static readonly string[] SecurityObjectSuffixes = { "Right", "Rights" };

	/// <summary>
	/// The security/system object families as help and tool descriptions name them. One constant, so the text cannot
	/// drift from the prefixes and suffixes <see cref="IsSecurityOrSystemObject"/> matches; a test checks it names
	/// every entry.
	/// </summary>
	public const string SecurityObjectFamiliesText =
		"SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, Vw*, *Right/*Rights";

	internal static IReadOnlyList<string> SecurityObjectPrefixList => SecurityObjectPrefixes;

	internal static IReadOnlyList<string> SecurityObjectSuffixList => SecurityObjectSuffixes;

	/// <summary>
	/// Whether <paramref name="schemaName"/> is a security or system object: set-object-rights grants it beyond read,
	/// or disables it, only with <c>--allow-security-object</c>, and get-object-rights never reads it as a connected
	/// object. A guard-rail against accidental writes, not a security boundary.
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

	private const int MaxDisplayLength = 200;

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
}
