using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClioRing.Services;

/// <summary>
/// Resolves the trusted <c>dotnet</c> host executable by absolute path from well-known locations
/// (<c>DOTNET_HOST_PATH</c>, <c>DOTNET_ROOT</c>/<c>DOTNET_ROOT_X64</c>, Program Files on Windows) instead
/// of trusting a bare <c>"dotnet"</c> name to whatever <c>CreateProcess</c>'s search order (calling-process
/// directory first, then <c>PATH</c>) turns up. Shared by <see cref="ClioToolUpdateService"/> (global-tool
/// update/inventory) and <see cref="DevClioLaunch"/> (Development runtime mode launch) so both consumers of
/// the trusted host stay in one place.
/// </summary>
internal static class DotNetHostResolver {
	/// <summary>
	/// Resolves an existing, absolute <c>dotnet</c>/<c>dotnet.exe</c> host path from the trusted candidate
	/// locations, or <c>null</c> when none of them exist.
	/// </summary>
	/// <returns>The absolute host path, or <c>null</c> when unresolved.</returns>
	internal static string? TryResolve() {
		string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
		var candidates = new List<string?> {
			Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
			CombineRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"), executableName),
			CombineRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"), executableName)
		};
		if (OperatingSystem.IsWindows()) {
			candidates.Add(CombineRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				"dotnet", executableName));
		}
		return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate))
			.Select(candidate => Path.GetFullPath(candidate!))
			.FirstOrDefault(File.Exists);
	}

	/// <summary>
	/// Resolves the trusted <c>dotnet</c> host the same way as <see cref="TryResolve"/>, falling back to
	/// the standard Program Files install location (Windows) or the standard install-script location
	/// (non-Windows) as a best-guess absolute path when none of the trusted candidates exist yet. Never
	/// falls back to a bare <c>"dotnet"</c> name that would be resolved via <c>PATH</c>.
	/// </summary>
	/// <returns>An absolute <c>dotnet</c> host path — resolved when possible, otherwise a guessed default.</returns>
	internal static string ResolveOrDefault() {
		return TryResolve() ?? Path.GetFullPath(OperatingSystem.IsWindows()
			? CombineRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet",
				"dotnet.exe")!
			: "/usr/local/share/dotnet/dotnet");
	}

	private static string? CombineRoot(string? root, params string[] parts) =>
		string.IsNullOrWhiteSpace(root) ? null : Path.Combine(new[] { root }.Concat(parts).ToArray());
}
