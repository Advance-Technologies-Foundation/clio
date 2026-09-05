namespace Clio.Package
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text.RegularExpressions;
	using Clio.Common.Responses;

	/// <summary>
	/// Reads a Creatio package installation log and tells apart the two things the platform reports
	/// through the same channel: schemas the platform deliberately skipped because they were edited on
	/// the environment (a warning), and an installation that actually failed (an error).
	/// </summary>
	/// <remarks>
	/// The platform answers <c>InstallPackage</c> with <c>success:false</c> and the generic message
	/// "Packages installation failed" even when the only problem in the run was a skipped locally
	/// modified schema. Without this classification clio turns a completed installation into a bare
	/// failure line and a non-zero exit code.
	/// </remarks>
	internal static class InstallLogAnalyzer
	{

		#region Constants: Public

		/// <summary>
		/// Message the platform writes to the installation log when the whole run succeeded. Only
		/// <c>--fail-on-error</c> requires it; see <see cref="IsSuccessMessagePresent"/>.
		/// </summary>
		public const string SuccessMessage = "application installed successfully";

		#endregion

		#region Constants: Private

		private const string InstallationFinishedMarker = "Package installation finished";
		private const string LocallyModifiedMarker = "has been modified locally";
		private const string GenericFailureMessage = "Packages installation failed";

		#endregion

		#region Fields: Private

		private static readonly Regex LocallyModifiedSchemaRegex = new Regex(
			"Unable to install\\s+\\w+\\s+\"(?<schema>[^\"]+)\"",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		#endregion

		#region Methods: Private

		private static string NormalizeMessage(string value) =>
			value?.Trim().TrimEnd('.').Trim() ?? string.Empty;

		#endregion

		#region Methods: Public

		/// <summary>
		/// Returns the log lines reporting a schema the platform refused to overwrite because the element
		/// was modified on the environment.
		/// </summary>
		/// <param name="installLog">Installation log produced by the current run.</param>
		/// <returns>The matching lines, trimmed; an empty collection when there are none.</returns>
		public static IReadOnlyList<string> GetLocallyModifiedSchemaLines(string installLog) {
			if (string.IsNullOrWhiteSpace(installLog)) {
				return Array.Empty<string>();
			}
			var lines = new List<string>();
			using var reader = new StringReader(installLog);
			string line;
			while ((line = reader.ReadLine()) != null) {
				if (line.IndexOf(LocallyModifiedMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
					lines.Add(line.Trim());
				}
			}
			return lines;
		}

		/// <summary>
		/// Returns the names of the schemas skipped because they were modified on the environment.
		/// </summary>
		/// <param name="installLog">Installation log produced by the current run.</param>
		/// <returns>Distinct schema names in the order they appear; an empty collection when there are none.</returns>
		public static IReadOnlyList<string> GetLocallyModifiedSchemaNames(string installLog) {
			var names = new List<string>();
			foreach (string line in GetLocallyModifiedSchemaLines(installLog)) {
				Match match = LocallyModifiedSchemaRegex.Match(line);
				string name = match.Success ? match.Groups["schema"].Value : null;
				if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
					names.Add(name);
				}
			}
			return names;
		}

		/// <summary>
		/// Determines whether the platform reported that the installation ran to its end.
		/// </summary>
		/// <param name="installLog">Installation log produced by the current run.</param>
		/// <returns><c>true</c> when the completion marker is present.</returns>
		public static bool IsInstallationCompleted(string installLog) =>
			!string.IsNullOrWhiteSpace(installLog)
			&& installLog.IndexOf(InstallationFinishedMarker, StringComparison.OrdinalIgnoreCase) >= 0;

		/// <summary>
		/// Determines whether the log carries the platform's overall success message.
		/// </summary>
		/// <param name="installLog">Complete installation log read from the environment.</param>
		/// <returns><c>true</c> when <see cref="SuccessMessage"/> is present.</returns>
		public static bool IsSuccessMessagePresent(string installLog) =>
			!string.IsNullOrWhiteSpace(installLog)
			&& installLog.IndexOf(SuccessMessage, StringComparison.OrdinalIgnoreCase) >= 0;

		/// <summary>
		/// Determines whether the service answered with exactly the generic failure message, which is what
		/// the platform sends for a run whose only problem was a skipped locally modified schema.
		/// </summary>
		/// <param name="response">Deserialized service response; may be <c>null</c>.</param>
		/// <returns><c>true</c> only for the generic "Packages installation failed" message.</returns>
		/// <remarks>
		/// A response with no message at all is deliberately NOT generic. That shape is what the platform
		/// sends for real failures whose detail lives only in the log (an invalid archive, for example), and
		/// treating it as generic would widen the downgrade well past the case it was written for.
		/// </remarks>
		public static bool IsGenericInstallationFailure(BaseResponse response) {
			string message = response?.ErrorInfo?.Message;
			return !string.IsNullOrWhiteSpace(message)
				&& NormalizeMessage(message).Equals(GenericFailureMessage, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Decides whether a failure reported by the installation service is in fact a completed
		/// installation whose only problem was a schema skipped because it was modified on the environment.
		/// </summary>
		/// <param name="response">Deserialized service response; may be <c>null</c>.</param>
		/// <param name="installLog">Installation log produced by the current run.</param>
		/// <param name="failOnError">Whether <c>--fail-on-error</c> was requested.</param>
		/// <returns><c>true</c> when the run must be treated as a success with warnings.</returns>
		/// <remarks>
		/// All four conditions are required: the run reached the completion marker, at least one locally
		/// modified schema was skipped, the response carries only the generic failure message, and the
		/// caller did not ask for the strict <c>--fail-on-error</c> mode.
		/// </remarks>
		public static bool ShouldTreatAsSuccess(BaseResponse response, string installLog, bool failOnError) =>
			!failOnError
			&& IsInstallationCompleted(installLog)
			&& GetLocallyModifiedSchemaLines(installLog).Count > 0
			&& IsGenericInstallationFailure(response);

		/// <summary>
		/// Builds a human-readable reason for a reported installation failure, so the final line never
		/// reads as a bare "Error".
		/// </summary>
		/// <param name="response">Deserialized service response; may be <c>null</c>.</param>
		/// <param name="installLog">Installation log produced by the current run.</param>
		/// <param name="successMessageCheckPassed">
		/// Whether the <c>--fail-on-error</c> requirement for <see cref="SuccessMessage"/> was satisfied.
		/// Pass <c>true</c> when the check was not requested.
		/// </param>
		/// <returns>A non-empty description of what failed.</returns>
		public static string DescribeFailure(BaseResponse response, string installLog,
			bool successMessageCheckPassed = true) {
			string message = response?.ErrorInfo?.Message;
			if (!string.IsNullOrWhiteSpace(message)) {
				string errorCode = response?.ErrorInfo?.ErrorCode;
				return string.IsNullOrWhiteSpace(errorCode)
					? message.Trim()
					: $"{errorCode.Trim()}: {message.Trim()}";
			}
			if (!successMessageCheckPassed) {
				return $"--fail-on-error is set and the installation log does not contain \"{SuccessMessage}\".";
			}
			string logTail = GetMeaningfulLogTail(installLog);
			return string.IsNullOrWhiteSpace(logTail)
				? "the installation service reported a failure without an error message."
				: $"the installation service reported a failure without an error message. Last log lines: {logTail}";
		}

		/// <summary>
		/// Returns the last non-empty log lines, joined by " | ", for use in a failure message.
		/// </summary>
		/// <param name="installLog">Installation log produced by the current run.</param>
		/// <param name="lineCount">How many trailing lines to keep.</param>
		/// <returns>The joined lines, or an empty string when the log carries nothing usable.</returns>
		public static string GetMeaningfulLogTail(string installLog, int lineCount = 3) {
			if (string.IsNullOrWhiteSpace(installLog) || lineCount <= 0) {
				return string.Empty;
			}
			string[] lines = installLog
				.Split(new[] {"\r\n", "\n", "\r"}, StringSplitOptions.None)
				.Select(line => line.Trim())
				.Where(line => !string.IsNullOrWhiteSpace(line))
				.ToArray();
			return string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - lineCount)));
		}

		#endregion

	}
}
