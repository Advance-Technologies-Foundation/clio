using System;
using System.Threading.Tasks;

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
	/// Creatio request is made; an empty value means "generate without a prefix" and is honoured, with a
	/// warning that names the consequence.
	/// </param>
	/// <returns>
	/// The prefix to prepend, or <see cref="string.Empty"/> when no prefix applies. Never
	/// <see langword="null"/>.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The requested prefix is not <see langword="null"/>, not empty, and not usable inside a generated
	/// identifier - a whitespace-only value included.
	/// </exception>
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

	/// <summary>
	/// Wall-clock budget for the environment read, in seconds.
	/// </summary>
	/// <remarks>
	/// add-package is otherwise a local command, and this read is the only thing that can make it pause.
	/// A host that accepts the TCP connection and never answers was measured at ~115 s before this budget
	/// existed - the incidental ceiling of the HTTP stack underneath, not a clio setting - which reads as
	/// a hang. A single sys-setting read that has not answered in half a minute is not going to; the
	/// caller gets the same warned, unprefixed package they would have got a minute and a half later.
	/// Mirrors the finite-preflight constant already used by <c>SysSettingsManager</c>.
	/// </remarks>
	internal const int DefaultReadBudgetSeconds = 30;

	#endregion

	#region Constants: Private

	// Names both paths that actually enforce the rule, and neither more than measurement supports. An
	// install through push-pkg and a compile-configuration both ACCEPT an unprefixed schema (measured on
	// a 10.1.725 stand), so telling the user to "check by compiling" would send them to a green result
	// and a still-broken package.
	private const string ConsequenceMessage =
		"Creatio refuses a schema whose code does not start with the target environment's SchemaNamePrefix: "
		+ "the source-code schema designer rejects it (measured), and that is also what blocks the "
		+ "file-system package-load path issue #1309 reported.";

	private const string SupplyPrefixMessage =
		"Supply the prefix directly instead: --schema-name-prefix <prefix> on the command line, "
		+ "schema-name-prefix in MCP. That path makes no Creatio request.";

	#endregion

	#region Fields: Private

	private readonly TimeSpan _readBudget;
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
		Func<EnvironmentSettings, ISysSettingsManager> sysSettingsManagerFactory, ILogger logger)
		: this(environmentSettings, sysSettingsManagerFactory, logger,
			TimeSpan.FromSeconds(DefaultReadBudgetSeconds)) { }

	#endregion

	#region Constructors: Internal

	/// <summary>
	/// Initializes a resolver with an explicit read budget. Exists so a test can prove the timeout branch
	/// without waiting <see cref="DefaultReadBudgetSeconds"/> seconds for it.
	/// </summary>
	internal SchemaNamePrefixResolver(EnvironmentSettings environmentSettings,
		Func<EnvironmentSettings, ISysSettingsManager> sysSettingsManagerFactory, ILogger logger,
		TimeSpan readBudget) {
		sysSettingsManagerFactory.CheckArgumentNull(nameof(sysSettingsManagerFactory));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_sysSettingsManagerFactory = sysSettingsManagerFactory;
		_logger = logger;
		_readBudget = readBudget;
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Tells whether a prefix can be prepended to a schema name without producing an invalid C# identifier.
	/// </summary>
	/// <param name="prefix">Candidate prefix.</param>
	/// <returns><see langword="true"/> when the prefix is empty or a valid identifier fragment.</returns>
	internal static bool IsValidPrefix(string prefix) =>
		string.IsNullOrEmpty(prefix) || ClioIdentifier.IsIdentifierFragment(prefix);

	/// <summary>
	/// Tells whether a caller-supplied prefix is a request this resolver can honour. Omitted is valid,
	/// and so is a literally empty value - that is the documented way to ask for an unprefixed schema. A
	/// value that is nothing but whitespace is not: it is a typo, and honouring it would hand the caller
	/// exactly the unprefixed schema this option exists to prevent.
	/// </summary>
	/// <param name="requestedPrefix">Prefix as the caller supplied it, before trimming.</param>
	/// <returns><see langword="true"/> when the value can be honoured.</returns>
	/// <remarks>
	/// Lives here rather than in a command because this type owns the contract: <see cref="Resolve"/> and
	/// <c>IPackageCreator.Create</c> are both public seams, so a rule enforced only by one caller is a
	/// rule the next caller silently bypasses.
	/// </remarks>
	internal static bool IsValidRequestedPrefix(string requestedPrefix) {
		if (requestedPrefix is null) {
			return true;
		}
		if (requestedPrefix.Length > 0 && requestedPrefix.Trim().Length == 0) {
			return false;
		}
		return IsValidPrefix(requestedPrefix.Trim());
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public string Resolve(string explicitPrefix) {
		if (explicitPrefix is not null) {
			if (!IsValidRequestedPrefix(explicitPrefix)) {
				throw new ArgumentException(InvalidPrefixMessage, nameof(explicitPrefix));
			}
			string requestedPrefix = explicitPrefix.Trim();
			if (requestedPrefix.Length == 0) {
				// Honoured, but never silently. On the command line an empty value takes deliberate
				// quoting; over MCP an empty string is a routine way for a client to express "not
				// provided", and the result would be the very unprefixed schema this option exists to
				// prevent. One line costs nothing and is the only signal either caller gets.
				_logger.WriteWarning(
					"An explicitly empty schema-name prefix was requested, so the generated schema gets no "
					+ $"prefix and no environment is contacted. {ConsequenceMessage} Omit the argument "
					+ "instead to read the prefix from the target environment.");
			}
			return requestedPrefix;
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
		// can pause at all, and an unexplained pause reads as a hang.
		_logger.WriteInfo($"Reading the SchemaNamePrefix system setting from {_environmentSettings.Uri}...");
		string prefix;
		try {
			if (!TryReadWithinBudget(out prefix)) {
				_logger.WriteWarning(
					"Timed out reading the SchemaNamePrefix system setting from "
					+ $"{_environmentSettings.Uri} after {_readBudget.TotalSeconds:0.##} seconds. The "
					+ $"generated schema gets no prefix. {ConsequenceMessage} {SupplyPrefixMessage}");
				return string.Empty;
			}
		}
		catch (Exception exception)
			when (SysSettingCodes.ClassifyReadFailure(exception) != SchemaNamePrefixReadFailure.Cancelled) {
			// An unreachable or rejecting environment must not stop local package generation; it only costs
			// the caller the prefix. A genuine cancellation is excluded above: a cancelled read that
			// degraded to a warning would hand the caller a completed, mis-generated package instead of
			// stopping. A transport timeout is NOT a cancellation for this purpose even though it arrives
			// as one - SysSettingCodes.ClassifyReadFailure owns that distinction.
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

	/// <summary>
	/// Runs the environment read under a finite wall-clock budget.
	/// </summary>
	/// <param name="prefix">Value read, when the read finished within budget.</param>
	/// <returns><see langword="false"/> when the budget expired first.</returns>
	/// <remarks>
	/// The budget is enforced by waiting, not by a cancellation token, on purpose: an expiry that
	/// surfaced as an <see cref="OperationCanceledException"/> would be indistinguishable from a caller
	/// cancelling the command, and <see cref="ReadFromEnvironment"/> must degrade for the first and stop
	/// for the second. <see cref="Task.WaitAny(Task[], TimeSpan)"/> rather than
	/// <see cref="Task.Wait(TimeSpan)"/> because Wait raises an <see cref="AggregateException"/> for a
	/// task that faulted inside the budget, which would hide the real exception type from the caller's
	/// catch filter; the awaiter rethrow below preserves it.
	/// A read that overruns is abandoned rather than aborted - a thread cannot be interrupted safely -
	/// and its eventual fault is observed so it cannot resurface as an unhandled exception.
	/// </remarks>
	private bool TryReadWithinBudget(out string prefix) {
		// The factory itself is part of the read: it can build a connection for the environment.
		Task<string> readTask = Task.Run(() =>
			SysSettingCodes.ReadSchemaNamePrefix(_sysSettingsManagerFactory(_environmentSettings)));
		if (Task.WaitAny([readTask], _readBudget) < 0) {
			_ = readTask.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
			prefix = string.Empty;
			return false;
		}
		prefix = readTask.GetAwaiter().GetResult();
		return true;
	}

	private static string DescribeReadFailure(Exception exception) =>
		SysSettingCodes.ClassifyReadFailure(exception) switch {
			SchemaNamePrefixReadFailure.Network =>
				"Network error reading the SchemaNamePrefix system setting",
			SchemaNamePrefixReadFailure.Authentication =>
				"Authentication error reading the SchemaNamePrefix system setting",
			_ => "Failed to read the SchemaNamePrefix system setting"
		};

	#endregion

}

#endregion
