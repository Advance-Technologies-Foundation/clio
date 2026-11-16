using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Common;

/// <summary>
/// Reads a secret that a host placed in clio's secrets folder, by NAME. A command argument carries the name of the
/// file, never the secret and never a path, so the secret stays out of the call, the process environment, the logs
/// and the output.
/// </summary>
public interface IClioSecretFileReader {

	/// <summary>The one folder a secret is read from: <c>&lt;clio home&gt;/secrets</c>.</summary>
	string SecretsFolder { get; }

	/// <summary>Reads the secret stored in the file <paramref name="name"/> of <see cref="SecretsFolder"/>.</summary>
	/// <param name="name">The file's name, as the host gave it.</param>
	/// <param name="argumentName">The argument that carried the name, for the refusal text.</param>
	/// <returns>The secret, without a trailing line break.</returns>
	/// <exception cref="ArgumentException">
	/// The name is not a plain file name; the folder or the file is a link; the file is missing, readable by other
	/// users, empty, too large, not UTF-8 text or more than one line. The message names the reference, never the
	/// content, and repeats the name only when it is a valid one.
	/// </exception>
	string Read(string name, string argumentName);
}

/// <summary>
/// Default <see cref="IClioSecretFileReader"/> over <c>&lt;clio home&gt;/secrets</c>
/// (<see cref="ClioRuntimePaths.Home"/>, which honors <c>CLIO_HOME</c>).
/// </summary>
/// <remarks>
/// <para>
/// Why a file and not an environment variable: an MCP host runs clio as a long-lived process, and on macOS another
/// process of the same user can read that process's environment (<c>ps eww</c>). A file in clio's home folder has the
/// protection clio's own settings have, which hold the environment's sign-in, and it is read only at call time.
/// </para>
/// <para>
/// Fails closed. The name must be a plain file name, so it cannot point outside the folder. Neither the folder nor the
/// file may be a link, so the read cannot be redirected. On macOS and Linux the file must be readable by its owner
/// only (mode 600 or stricter). On Windows clio relies on the per-user ACL of <c>%LOCALAPPDATA%</c>, as
/// <see cref="FileSecurityHardening"/> does. The check and the read are not atomic; the folder is the user's own and
/// an agent's tools are kept out of it, so a swap in between needs the user's own rights.
/// </para>
/// </remarks>
public sealed class ClioSecretFileReader : IClioSecretFileReader {

	/// <summary>The folder's name under clio's home.</summary>
	public const string FolderName = "secrets";

	/// <summary>The longest name accepted.</summary>
	internal const int MaxNameLength = 128;

	/// <summary>The largest file accepted: a secret, not a document.</summary>
	internal const int MaxSecretBytes = 4096;

	private static readonly Regex NamePattern = new(
		"^[A-Za-z0-9_-][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>Reads from <c>&lt;clio home&gt;/secrets</c>.</summary>
	public ClioSecretFileReader() : this(Path.Combine(ClioRuntimePaths.Home, FolderName)) {
	}

	/// <summary>Reads from <paramref name="secretsFolder"/>; for tests.</summary>
	/// <param name="secretsFolder">The folder to read from.</param>
	internal ClioSecretFileReader(string secretsFolder) {
		SecretsFolder = secretsFolder;
	}

	/// <inheritdoc />
	public string SecretsFolder { get; }

	/// <summary>
	/// Whether <paramref name="name"/> is a plain file name clio accepts: letters, digits, <c>.</c>, <c>-</c> and
	/// <c>_</c>, not starting with <c>.</c>, at most <see cref="MaxNameLength"/> characters.
	/// </summary>
	/// <param name="name">The name to check.</param>
	public static bool IsValidName(string name) =>
		!string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && NamePattern.IsMatch(name);

	/// <summary>The refusal for a name that is not a plain file name; it does not repeat the value.</summary>
	/// <param name="argumentName">The argument that carried the name.</param>
	public static string InvalidNameMessage(string argumentName) =>
		$"{argumentName} must be the name of a file in clio's secrets folder (<clio home>/{FolderName}): letters, digits, "
		+ $"'.', '-' or '_', not starting with '.', at most {MaxNameLength} characters. It is a name the host gives, never "
		+ "a path and never the secret itself.";

	/// <inheritdoc />
	public string Read(string name, string argumentName) {
		if (!IsValidName(name)) {
			throw new ArgumentException(InvalidNameMessage(argumentName));
		}
		string folder = Path.GetFullPath(SecretsFolder);
		DirectoryInfo folderInfo = new(folder);
		if (IsLink(folderInfo)) {
			throw new ArgumentException($"clio's secrets folder ({folder}) is a link; clio reads a secret only from a real folder.");
		}
		string path = Path.GetFullPath(Path.Combine(folder, name));
		if (!string.Equals(Path.GetDirectoryName(path), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)) {
			throw new ArgumentException(InvalidNameMessage(argumentName));
		}
		FileInfo file = new(path);
		if (IsLink(file)) {
			throw new ArgumentException($"{argumentName} '{name}' is a link; clio reads a secret only from a regular file.");
		}
		if (!file.Exists) {
			throw new ArgumentException($"{argumentName} '{name}' was not found in clio's secrets folder ({folder}).");
		}
		if (!new FileSecurityHardening().IsOwnerOnly(path)) {
			throw new ArgumentException(
				$"{argumentName} '{name}' can be read by other users; restrict it to its owner (chmod 600).");
		}
		if (file.Length > MaxSecretBytes) {
			throw new ArgumentException(
				$"{argumentName} '{name}' is larger than {MaxSecretBytes} bytes; the file must hold only the secret.");
		}
		string secret;
		try {
			secret = StrictUtf8.GetString(File.ReadAllBytes(path));
		} catch (DecoderFallbackException) {
			throw new ArgumentException($"{argumentName} '{name}' is not UTF-8 text.");
		}
		secret = secret.TrimStart('﻿').TrimEnd('\r', '\n');
		if (secret.Length == 0) {
			throw new ArgumentException($"{argumentName} '{name}' is empty.");
		}
		if (secret.Contains('\n') || secret.Contains('\r')) {
			throw new ArgumentException($"{argumentName} '{name}' holds more than one line; the file must hold only the secret.");
		}
		return secret;
	}

	private static bool IsLink(FileSystemInfo info) =>
		info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint));
}
