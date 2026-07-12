using System;
using System.IO;
using ClioLauncher.Ipc;

namespace ClioLauncher.Services;

/// <summary>
/// Outcome of validating a dev-clio path override: whether it is usable and a friendly, jargon-free
/// message to show the user when it is not.
/// </summary>
/// <param name="IsValid">True when the path points at a usable clio build.</param>
/// <param name="Message">A friendly explanation when <see cref="IsValid"/> is false; empty otherwise.</param>
public readonly record struct DevClioValidation(bool IsValid, string Message);

/// <summary>
/// Pure helpers for the dev-clio path override: validate a user-supplied path and turn a valid one into
/// the <see cref="ClioIpcSettings"/> used to launch the clio MCP child. Kept free of I/O beyond the
/// existence check so it is trivially unit-testable and shared by the settings VM and the composition root.
/// </summary>
public static class DevClioLaunch {
	/// <summary>
	/// Validates a dev-clio path override. A blank path is valid (it simply clears the override). A
	/// non-blank path must exist on disk and be a <c>clio.dll</c> or <c>clio.exe</c>.
	/// </summary>
	/// <param name="path">The candidate dev-clio build path (may be null/blank).</param>
	/// <returns>The validation outcome with a friendly message when invalid.</returns>
	public static DevClioValidation Validate(string? path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return new DevClioValidation(true, string.Empty);
		}

		string trimmed = path.Trim();
		if (!File.Exists(trimmed)) {
			return new DevClioValidation(false,
				"Can't find a file there. Point this at your dev clio.dll or clio.exe.");
		}

		if (!LooksLikeClio(trimmed)) {
			return new DevClioValidation(false,
				"That file doesn't look like a clio build. Choose a clio.dll or clio.exe.");
		}

		return new DevClioValidation(true, string.Empty);
	}

	/// <summary>
	/// Builds the launch configuration for a validated dev-clio path: a <c>.dll</c> is driven by
	/// <c>dotnet</c>; a <c>.exe</c> is launched directly. Both append the <c>mcp-server</c> verb.
	/// </summary>
	/// <param name="path">A dev-clio build path that has passed <see cref="Validate"/>.</param>
	/// <returns>The <see cref="ClioIpcSettings"/> that launches the dev clio.</returns>
	public static ClioIpcSettings Build(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			throw new ArgumentException("A dev-clio path is required.", nameof(path));
		}

		string trimmed = path.Trim();
		bool isDll = trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
		return isDll
			? new ClioIpcSettings { Command = "dotnet", Args = new[] { trimmed, "mcp-server" } }
			: new ClioIpcSettings { Command = trimmed, Args = new[] { "mcp-server" } };
	}

	private static bool LooksLikeClio(string path) {
		string name = Path.GetFileName(path);
		return name.Equals("clio.dll", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("clio.exe", StringComparison.OrdinalIgnoreCase);
	}
}
