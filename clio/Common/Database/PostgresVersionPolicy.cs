using System;
using System.Text.RegularExpressions;

namespace Clio.Common.Database;

/// <summary>
/// Defines PostgreSQL version policy and parsing helpers for assertions.
/// </summary>
public static class PostgresVersionPolicy
{
	/// <summary>
	/// Minimum supported PostgreSQL major version.
	/// </summary>
	public const int MinimumSupportedMajorVersion = 16;

	/// <summary>
	/// Tries to parse PostgreSQL major version from <c>SELECT version()</c> result.
	/// </summary>
	/// <param name="versionText">Raw version string returned by PostgreSQL.</param>
	/// <param name="majorVersion">Parsed major version.</param>
	/// <returns><c>true</c> when major version is parsed; otherwise <c>false</c>.</returns>
	public static bool TryParseMajorVersion(string versionText, out int majorVersion)
	{
		majorVersion = 0;
		if (string.IsNullOrWhiteSpace(versionText))
		{
			return false;
		}

		Match match = Regex.Match(versionText, @"(?i)\bpostgresql\s+(\d+)");
		if (!match.Success || match.Groups.Count < 2)
		{
			return false;
		}

		return int.TryParse(match.Groups[1].Value, out majorVersion);
	}

	/// <summary>
	/// Checks whether parsed PostgreSQL major version is supported.
	/// </summary>
	/// <param name="majorVersion">Parsed PostgreSQL major version.</param>
	/// <returns><c>true</c> when version is supported by clio assertions.</returns>
	public static bool IsSupportedMajorVersion(int majorVersion)
	{
		return majorVersion >= MinimumSupportedMajorVersion;
	}

	/// <summary>
	/// Creates a standard PostgreSQL version floor violation message.
	/// </summary>
	/// <param name="actualVersionText">Actual version string returned by server.</param>
	/// <returns>Human-readable error text.</returns>
	public static string BuildUnsupportedVersionError(string actualVersionText)
	{
		return $"Unsupported PostgreSQL version '{actualVersionText}'. Minimum supported major version is {MinimumSupportedMajorVersion}.";
	}
}
