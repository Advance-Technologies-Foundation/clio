using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace Clio.Common;

#region Interface: ISchemaNamePrefixResolver

/// <summary>
/// Resolves the schema-name prefix that locally generated Creatio schemas must carry so the target
/// environment accepts them.
/// </summary>
/// <remarks>
/// Creatio validates a custom schema's code against the <c>SchemaNamePrefix</c> system setting of the
/// environment the package is loaded into. The value is environment-specific: <c>Usr</c> is only the
/// out-of-the-box default, and an environment may configure any other value or none at all.
/// </remarks>
public interface ISchemaNamePrefixResolver {

	#region Methods: Public

	/// <summary>
	/// Resolves the prefix to prepend to a generated schema name.
	/// </summary>
	/// <param name="explicitPrefix">
	/// Prefix supplied by the caller. When not <see langword="null"/> it wins over the environment and no
	/// Creatio request is made; an empty value means "generate without a prefix" and is honoured silently.
	/// </param>
	/// <returns>
	/// The prefix to prepend, or <see cref="string.Empty"/> when no prefix applies. Never
	/// <see langword="null"/>.
	/// </returns>
	string Resolve(string explicitPrefix);

	#endregion

}

#endregion

#region Class: SchemaNamePrefixResolver

/// <inheritdoc cref="ISchemaNamePrefixResolver"/>
public class SchemaNamePrefixResolver : ISchemaNamePrefixResolver {

	#region Constants: Internal

	internal const string InvalidPrefixMessage =
		"Schema name prefix must start with a letter or underscore and contain only letters, digits, " +
		"and underscores, because it becomes part of a generated C# class name.";

	#endregion

	#region Constants: Private

	// Deliberately names the path that actually enforces the rule. An install through push-pkg and a
	// compile-configuration both ACCEPT an unprefixed schema (measured on a 10.1.725 stand), so telling
	// the user to "check by compiling" would send them to a green result and a still-broken package.
	private const string ConsequenceMessage =
		"Creatio refuses a schema whose code does not start with the target environment's SchemaNamePrefix "
		+ "when it loads the package from the file system, which is what blocks compilation from the "
		+ "Creatio UI.";

	private const string SupplyPrefixMessage =
		"Supply the prefix directly instead: --schema-name-prefix <prefix> on the command line, "
		+ "schema-name-prefix in MCP. That path makes no Creatio request.";

	#endregion

	#region Fields: Private

	private static readonly Regex PrefixPattern = new("\\A[A-Za-z_][A-Za-z0-9_]*\\z",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private readonly EnvironmentSettings _environmentSettings;
	private readonly ILogger _logger;
	private readonly Func<EnvironmentSettings, ISysSettingsManager> _sysSettingsManagerFactory;

	#endregion

	#region Constructors: Public

	/// <summary>Initializes a resolver bound to the environment the current command resolved.</summary>
	/// <param name="environmentSettings">Environment the command runs against; may carry no URI.</param>
	/// <param name="sysSettingsManagerFactory">Factory for a manager scoped to one environment.</param>
	/// <param name="logger">Logger used to report how the prefix was resolved.</param>
	public SchemaNamePrefixResolver(EnvironmentSettings environmentSettings,
		Func<EnvironmentSettings, ISysSettingsManager> sysSettingsManagerFactory, ILogger logger) {
		sysSettingsManagerFactory.CheckArgumentNull(nameof(sysSettingsManagerFactory));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_sysSettingsManagerFactory = sysSettingsManagerFactory;
		_logger = logger;
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Tells whether a prefix can be prepended to a schema name without producing an invalid C# identifier.
	/// </summary>
	/// <param name="prefix">Candidate prefix.</param>
	/// <returns><see langword="true"/> when the prefix is empty or a valid identifier fragment.</returns>
	internal static bool IsValidPrefix(string prefix) =>
		string.IsNullOrEmpty(prefix) || PrefixPattern.IsMatch(prefix);

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public string Resolve(string explicitPrefix) {
		if (explicitPrefix is not null) {
			return explicitPrefix.Trim();
		}
		if (_environmentSettings is null || string.IsNullOrWhiteSpace(_environmentSettings.Uri)) {
			// Generating unprefixed is deliberate: add-package does not require an environment, and both
			// the offline command line and the environment-less MCP call must keep working. This warning is
			// the only signal the caller gets before Creatio refuses the schema, so it names the cause, the
			// consequence and the way out.
			_logger.WriteWarning(
				"No Creatio environment was resolved, so the generated schema gets no schema-name prefix. "
				+ "An environment name that is not registered, and a comma-separated list of environments, "
				+ $"both resolve none. {ConsequenceMessage} {SupplyPrefixMessage}");
			return string.Empty;
		}
		return ReadFromEnvironment();
	}

	#endregion

	#region Methods: Private

	private string ReadFromEnvironment() {
		// Announced before the request, not after: this read is the only reason an otherwise local command
		// can pause for a minute, and an unexplained pause reads as a hang.
		_logger.WriteInfo($"Reading the SchemaNamePrefix system setting from {_environmentSettings.Uri}...");
		string prefix;
		try {
			prefix = SysSettingCodes.ReadSchemaNamePrefix(_sysSettingsManagerFactory(_environmentSettings));
		}
		catch (Exception exception) when (exception is not OperationCanceledException) {
			// An unreachable or rejecting environment must not stop local package generation; it only costs
			// the caller the prefix. Cancellation is excluded above: a cancelled read that degraded to a
			// warning would hand the caller a completed, mis-generated package instead of stopping.
			// The failure is reported by CATEGORY, never by the raw exception text:
			// a sys-setting read surfaces the server's own response body, which can carry a login page,
			// a redirect carrying a token or a connection string, and this message reaches MCP clients.
			_logger.WriteWarning(
				$"{DescribeReadFailure(exception)} from {_environmentSettings.Uri}. The generated schema "
				+ $"gets no prefix. {ConsequenceMessage} {SupplyPrefixMessage}");
			return string.Empty;
		}
		if (string.IsNullOrEmpty(prefix)) {
			_logger.WriteInfo(
				$"{_environmentSettings.Uri} configures an empty SchemaNamePrefix, so the generated schema "
				+ "gets no prefix.");
			return string.Empty;
		}
		if (!IsValidPrefix(prefix)) {
			_logger.WriteWarning(
				$"The SchemaNamePrefix value '{prefix}' read from {_environmentSettings.Uri} is not usable "
				+ $"in a generated C# class name. {InvalidPrefixMessage} The generated schema gets no prefix. "
				+ SupplyPrefixMessage);
			return string.Empty;
		}
		// Names BOTH the value and where it came from. A prefix read from the wrong environment - the
		// active one rather than the one the package is destined for - produces a schema Creatio refuses
		// just as surely as no prefix at all, and nothing else in the output would show the mismatch.
		_logger.WriteInfo(
			$"Using schema-name prefix '{prefix}', read from the SchemaNamePrefix system setting of "
			+ $"{_environmentSettings.Uri}.");
		return prefix;
	}

	private static string DescribeReadFailure(Exception exception) =>
		exception switch {
			HttpRequestException or WebException or SocketException =>
				"Network error reading the SchemaNamePrefix system setting",
			UnauthorizedAccessException or AuthenticationException =>
				"Authentication error reading the SchemaNamePrefix system setting",
			_ => "Failed to read the SchemaNamePrefix system setting"
		};

	#endregion

}

#endregion
