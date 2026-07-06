namespace Clio.Common
{
	using System.Collections.Generic;

	#region Class: CommandErrorCodes

	/// <summary>
	/// Central registry of stable, machine-readable error codes for the unified command <c>--json</c>
	/// envelope (BL-1), and the mapping from each code to a stable process exit code.
	/// </summary>
	/// <remarks>
	/// This registry <b>extends</b> the pre-existing version-requirement taxonomy defined on
	/// <see cref="CreatioVersionRequirementException"/> rather than redefining it — the version codes are
	/// re-exported here so envelope emitters and tests reference a single place. Add new envelope error
	/// codes here (as kebab-case constants) together with their exit-code mapping so the stable set never
	/// drifts across commands.
	/// </remarks>
	public static class CommandErrorCodes
	{

		#region Constants: Public

		/// <summary>Fallback code for an unclassified failure (an unexpected exception inside a command).</summary>
		public const string UnexpectedError = "unexpected-error";

		/// <summary>One or more probes in a <c>healthcheck</c> run failed.</summary>
		public const string HealthCheckFailed = "healthcheck-failed";

		/// <summary>The requested environment is not registered (e.g. <c>list-environments &lt;name&gt;</c>).</summary>
		public const string EnvironmentNotFound = "environment-not-found";

		/// <summary>No environments are registered at all.</summary>
		public const string NoEnvironmentsRegistered = "no-environments-registered";

		/// <summary>Re-export of <see cref="CreatioVersionRequirementException.VersionTooOldCode"/>.</summary>
		public const string VersionTooOld = CreatioVersionRequirementException.VersionTooOldCode;

		/// <summary>Re-export of <see cref="CreatioVersionRequirementException.VersionUndeterminableCode"/>.</summary>
		public const string VersionUndeterminable = CreatioVersionRequirementException.VersionUndeterminableCode;

		/// <summary>Re-export of <see cref="CreatioVersionRequirementException.VersionCheckFailedCode"/>.</summary>
		public const string VersionCheckFailed = CreatioVersionRequirementException.VersionCheckFailedCode;

		#endregion

		#region Fields: Private

		private const int GenericFailureExitCode = 1;

		// Distinct exit code for version-requirement refusals — must stay in sync with
		// Program.CreatioVersionRequirementExitCode (the Program-level gate returns this before a command
		// runs; the value is duplicated here only so the code→exit mapping is complete and self-describing).
		private const int VersionRequirementExitCode = 78;

		private static readonly IReadOnlyDictionary<string, int> ExitCodeByErrorCode =
			new Dictionary<string, int> {
				[UnexpectedError] = GenericFailureExitCode,
				[HealthCheckFailed] = GenericFailureExitCode,
				[EnvironmentNotFound] = GenericFailureExitCode,
				[NoEnvironmentsRegistered] = GenericFailureExitCode,
				[VersionTooOld] = VersionRequirementExitCode,
				[VersionUndeterminable] = VersionRequirementExitCode,
				[VersionCheckFailed] = VersionRequirementExitCode,
			};

		#endregion

		#region Methods: Public

		/// <summary>
		/// Maps a stable error code to its documented, non-zero process exit code. Unknown or <c>null</c>
		/// codes fall back to the generic failure code (<c>1</c>).
		/// </summary>
		/// <param name="errorCode">A stable error code from this registry.</param>
		/// <returns>The documented process exit code for <paramref name="errorCode"/>.</returns>
		public static int ToExitCode(string errorCode) =>
			errorCode is not null && ExitCodeByErrorCode.TryGetValue(errorCode, out int exitCode)
				? exitCode
				: GenericFailureExitCode;

		#endregion

	}

	#endregion

}
