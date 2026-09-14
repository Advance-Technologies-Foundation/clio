using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace Clio.Common;

#region Enum: SchemaNamePrefixReadFailure

/// <summary>
/// Category a failed <c>SchemaNamePrefix</c> read falls into.
/// </summary>
/// <remarks>
/// The read is exposed by two independent surfaces - <see cref="SchemaNamePrefixResolver"/>, which warns
/// and degrades, and the <c>get-schema-name-prefix</c> MCP tool, which returns a structured error. They
/// render different text but must agree on WHICH category an exception belongs to, cancellation
/// included; the classification therefore lives once, here.
/// </remarks>
internal enum SchemaNamePrefixReadFailure {

	/// <summary>Transport failure, including a timeout: the environment did not answer.</summary>
	Network,

	/// <summary>The environment answered and refused the credentials.</summary>
	Authentication,

	/// <summary>The caller cancelled the read; the operation must stop rather than degrade.</summary>
	Cancelled,

	/// <summary>Anything else, including an unregistered environment name.</summary>
	Other

}

#endregion

#region Class: SysSettingCodes

internal static class SysSettingCodes {

	internal const string SchemaNamePrefix = "SchemaNamePrefix";

	internal static string ReadSchemaNamePrefix(ISysSettingsManager sysSettingsManager) {
		string value = sysSettingsManager.GetSysSettingValueByCode(SchemaNamePrefix);
		// Trimmed on both sides of the quote strip: a legacy shape that arrives as "\" Usr \"" would
		// otherwise keep its inner spaces and read as a prefix no generated identifier can use.
		return value?.Trim().Trim('"').Trim() ?? string.Empty;
	}

	/// <summary>
	/// Classifies a failed <c>SchemaNamePrefix</c> read. Single owner of the taxonomy: every consumer
	/// renders its own message but none of them decides the category on its own.
	/// </summary>
	/// <param name="exception">Exception the read threw.</param>
	/// <returns>The category the failure belongs to.</returns>
	/// <remarks>
	/// The cancellation decision is made here once, and it is not the same for the two cancellation
	/// shapes. <see cref="TaskCanceledException"/> is what a transport timeout surfaces as - nothing on
	/// this path supplies a cancellation token, so a task that "cancels" itself did so because the
	/// environment stopped answering - and it is therefore a network failure a caller may degrade past.
	/// A bare <see cref="OperationCanceledException"/> is a genuine cooperative cancellation and must
	/// stop the operation instead.
	/// </remarks>
	internal static SchemaNamePrefixReadFailure ClassifyReadFailure(Exception exception) =>
		exception switch {
			TaskCanceledException => SchemaNamePrefixReadFailure.Network,
			OperationCanceledException => SchemaNamePrefixReadFailure.Cancelled,
			HttpRequestException or WebException or SocketException => SchemaNamePrefixReadFailure.Network,
			UnauthorizedAccessException or AuthenticationException =>
				SchemaNamePrefixReadFailure.Authentication,
			_ => SchemaNamePrefixReadFailure.Other
		};

}

#endregion
