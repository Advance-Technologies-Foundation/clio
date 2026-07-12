using System;
using System.Collections.Generic;

namespace ClioLauncher.Services;

/// <summary>Coarse network location of an environment, inferred from its URL host.</summary>
public enum EnvLocation {
	/// <summary>localhost / 127.* / *.tscrm.com.</summary>
	Local,

	/// <summary>*.creatio.com and any other remote host.</summary>
	Cloud
}

/// <summary>
/// A registered clio environment with only the NON-sensitive metadata the launcher surfaces
/// (name, url host, .NET flavour, local/cloud). Never carries login/password.
/// </summary>
public sealed record ClioEnvironment(string Name, string? Uri, bool IsNetCore) {
	/// <summary>Host component of <see cref="Uri"/> (empty when unparseable).</summary>
	public string Host {
		get {
			if (System.Uri.TryCreate(Uri, UriKind.Absolute, out Uri? parsed)) {
				return parsed.Host;
			}

			return string.Empty;
		}
	}

	/// <summary>Host suffixes that denote an on-network/local Creatio (internal dev/test infra).</summary>
	private static readonly string[] LocalHostSuffixes = { ".tscrm.com" };

	/// <summary>Exact hosts that denote a local machine.</summary>
	private static readonly string[] LocalHostExact = { "localhost", "::1" };

	/// <summary>
	/// Network location classified from the RESOLVED host only — NOT from the URL scheme.
	/// HTTPS does not imply cloud (many local/*.tscrm.com envs use HTTPS). Rules (explicit):
	///   Local  = no host / localhost / ::1 / 127.* / *.tscrm.com (internal infra).
	///   Cloud  = everything else (e.g. *.creatio.com and any other remote host).
	/// To add more local networks, extend <see cref="LocalHostSuffixes"/> / <see cref="LocalHostExact"/>.
	/// </summary>
	public EnvLocation Location {
		get {
			string host = Host;

			// No parseable host → not remotely reachable → treat as Local.
			if (host.Length == 0) {
				return EnvLocation.Local;
			}

			if (host.StartsWith("127.", StringComparison.Ordinal)) {
				return EnvLocation.Local;
			}

			foreach (string exact in LocalHostExact) {
				if (host.Equals(exact, StringComparison.OrdinalIgnoreCase)) {
					return EnvLocation.Local;
				}
			}

			foreach (string suffix in LocalHostSuffixes) {
				if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
					return EnvLocation.Local;
				}
			}

			return EnvLocation.Cloud;
		}
	}

	/// <summary>"Local" / "Cloud".</summary>
	public string LocationLabel => Location == EnvLocation.Local ? "Local" : "Cloud";

	/// <summary>".NET" flavour label.</summary>
	public string FrameworkLabel => IsNetCore ? "NetCore" : "Framework";
}

/// <summary>Which standard stream a <see cref="ClioOutputLine"/> came from.</summary>
public enum ClioStream {
	/// <summary>Standard output.</summary>
	Stdout,

	/// <summary>Standard error.</summary>
	Stderr
}

/// <summary>A single line streamed from a running clio child process.</summary>
/// <param name="Stream">Origin stream.</param>
/// <param name="Text">The line text (without trailing newline).</param>
/// <param name="TimestampTicks"><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> stamp when the line arrived.</param>
public readonly record struct ClioOutputLine(ClioStream Stream, string Text, long TimestampTicks);

/// <summary>A fully described clio invocation.</summary>
public sealed record ClioInvocation {
	/// <summary>The clio verb, e.g. <c>get-info</c> (may also be a raw switch like <c>--version</c>).</summary>
	public required string Verb { get; init; }

	/// <summary>Extra arguments appended verbatim after the verb.</summary>
	public IReadOnlyList<string> Args { get; init; } = new List<string>();

	/// <summary>Optional environment; when set, <c>-e &lt;EnvName&gt;</c> is appended.</summary>
	public string? EnvName { get; init; }
}

/// <summary>Result of a completed clio run. Raw output is always preserved alongside the exit code.</summary>
/// <param name="ExitCode">Process exit code (-1 if it was cancelled/killed).</param>
/// <param name="RawStdout">Complete captured standard output.</param>
/// <param name="RawStderr">Complete captured standard error.</param>
/// <param name="Cancelled">True when the run was cancelled and the process tree was killed.</param>
public readonly record struct ClioRunResult(int ExitCode, string RawStdout, string RawStderr, bool Cancelled);
