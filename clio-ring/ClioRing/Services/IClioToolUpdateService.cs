using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ClioRing.Services;

/// <summary>Outcome of a user-approved clio tool update attempt.</summary>
public enum ClioToolUpdateOutcome {
	/// <summary>The requested version was installed and verified.</summary>
	Success,

	/// <summary>Trusted clio processes are still using the installed tool.</summary>
	Blocked,

	/// <summary>The installed version changed externally and the cached check must be refreshed.</summary>
	RefreshRequired,

	/// <summary>The update failed without a trusted running clio process to terminate.</summary>
	Failed
}

/// <summary>Available-version snapshot returned by the update checker.</summary>
/// <param name="InstalledVersion">Installed stable global-tool version.</param>
/// <param name="AvailableVersion">Latest listed stable NuGet version.</param>
/// <param name="TargetPath">Trusted global-tool shim path.</param>
public sealed record ClioToolUpdateCheck(string InstalledVersion, string AvailableVersion, string TargetPath) {
	/// <summary>True when the listed stable version is newer than the installed version.</summary>
	public bool IsUpdateAvailable => ClioToolVersion.Compare(AvailableVersion, InstalledVersion) > 0;
}

/// <summary>Identity snapshot for one trusted Release clio process.</summary>
/// <param name="ProcessId">Operating-system process identifier.</param>
/// <param name="StartTimeUtcTicks">Start identity used to reject PID reuse.</param>
/// <param name="ExecutablePath">Canonical executable path.</param>
/// <param name="CommandSummary">Secret-free command classification.</param>
/// <param name="ParentSummary">Parent application name and PID when available.</param>
public sealed record ClioToolProcess(int ProcessId, long StartTimeUtcTicks, string ExecutablePath,
	string CommandSummary, string ParentSummary);

/// <summary>Result of an update or explicit terminate-and-retry operation.</summary>
/// <param name="Outcome">Terminal operation classification.</param>
/// <param name="Message">Secret-free user-facing detail.</param>
/// <param name="Processes">Immutable trusted-process snapshot when blocked.</param>
public sealed record ClioToolUpdateResult(ClioToolUpdateOutcome Outcome, string Message,
	IReadOnlyList<ClioToolProcess> Processes);

/// <summary>Checks and updates the trusted Release clio global tool.</summary>
public interface IClioToolUpdateService {
	/// <summary>Checks the installed tool against the latest listed stable NuGet version.</summary>
	Task<ClioToolUpdateCheck?> CheckAsync(CancellationToken cancellationToken = default,
		bool force = false);

	/// <summary>Installs the exact version from a previously presented update snapshot.</summary>
	Task<ClioToolUpdateResult> UpdateAsync(ClioToolUpdateCheck update,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Revalidates and terminates only the trusted processes from the confirmation snapshot, then retries once.
	/// </summary>
	Task<ClioToolUpdateResult> TerminateAndRetryAsync(ClioToolUpdateCheck update,
		IReadOnlyList<ClioToolProcess> confirmedProcesses, CancellationToken cancellationToken = default);
}

internal static class ClioToolVersion {
	public static int Compare(string left, string right) {
		if (!TryParse(left, out int[] leftParts) || !TryParse(right, out int[] rightParts)) {
			return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
		}
		int count = Math.Max(leftParts.Length, rightParts.Length);
		for (int i = 0; i < count; i++) {
			int leftPart = i < leftParts.Length ? leftParts[i] : 0;
			int rightPart = i < rightParts.Length ? rightParts[i] : 0;
			int comparison = leftPart.CompareTo(rightPart);
			if (comparison != 0) {
				return comparison;
			}
		}
		return 0;
	}

	public static bool IsStable(string value) => TryParse(value, out _);

	private static bool TryParse(string value, out int[] parts) {
		parts = Array.Empty<int>();
		if (string.IsNullOrWhiteSpace(value) || value.Contains('-', StringComparison.Ordinal)
			|| value.Contains('+', StringComparison.Ordinal)) {
			return false;
		}
		string[] tokens = value.Split('.');
		if (tokens.Length is < 2 or > 4) {
			return false;
		}
		var parsed = new int[tokens.Length];
		for (int i = 0; i < tokens.Length; i++) {
			if (!int.TryParse(tokens[i], System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture, out parsed[i]) || parsed[i] < 0) {
				return false;
			}
		}
		parts = parsed;
		return true;
	}
}
