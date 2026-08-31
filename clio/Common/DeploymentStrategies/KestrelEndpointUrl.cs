using System;
using System.Net;
using System.Net.Sockets;

namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Rewrites Kestrel endpoint URLs without relying on URI parsing that rejects legacy unbracketed IPv6 URLs.
/// </summary>
internal static class KestrelEndpointUrl
{
	/// <summary>
	/// Replaces the authority host while preserving the endpoint port and path.
	/// </summary>
	/// <param name="url">The Kestrel endpoint URL.</param>
	/// <param name="bindHost">The host to use in the rewritten URL.</param>
	/// <returns>The rewritten URL, or <see langword="null"/> when the authority is unsupported.</returns>
	internal static string? ReplaceHost(string url, string bindHost)
	{
		int separatorIndex = url.IndexOf("://", StringComparison.Ordinal);
		if (separatorIndex <= 0)
		{
			return null;
		}

		int authorityStart = separatorIndex + 3;
		int authorityEnd = FindAuthorityEnd(url, authorityStart);
		string authority = url[authorityStart..authorityEnd];
		if (string.IsNullOrWhiteSpace(authority) || authority.Contains('@'))
		{
			return null;
		}

		string port = string.Empty;
		if (!TryGetExplicitPort(authority, out port))
		{
			if (authority.StartsWith("[", StringComparison.Ordinal))
			{
				return null;
			}

			int firstColon = authority.IndexOf(':');
			int lastColon = authority.LastIndexOf(':');
			if (firstColon >= 0 && firstColon == lastColon)
			{
				return null;
			}
		}

		return $"{url[..authorityStart]}{bindHost}{port}{url[authorityEnd..]}";
	}

	/// <summary>
	/// Gets an explicitly specified port from a Kestrel endpoint URL, if one exists.
	/// </summary>
	/// <param name="url">The Kestrel endpoint URL.</param>
	/// <param name="scheme">The endpoint scheme used for the default-port fallback.</param>
	/// <returns>The explicit port, or the scheme's default port.</returns>
	internal static int GetPort(string url, string scheme)
	{
		int separatorIndex = url.IndexOf("://", StringComparison.Ordinal);
		if (separatorIndex <= 0)
		{
			return GetDefaultPort(scheme);
		}

		int authorityStart = separatorIndex + 3;
		int authorityEnd = FindAuthorityEnd(url, authorityStart);
		string authority = url[authorityStart..authorityEnd];
		return TryGetExplicitPort(authority, out string portText) && int.TryParse(portText[1..], out int port)
			? port
			: GetDefaultPort(scheme);
	}

	/// <summary>
	/// Replaces the port in an already validated Kestrel endpoint URL.
	/// </summary>
	/// <param name="url">The Kestrel endpoint URL.</param>
	/// <param name="port">The port to use.</param>
	/// <returns>The URL with the requested port.</returns>
	internal static string ReplacePort(string url, int port)
	{
		int separatorIndex = url.IndexOf("://", StringComparison.Ordinal);
		int authorityStart = separatorIndex + 3;
		int authorityEnd = FindAuthorityEnd(url, authorityStart);
		string authority = url[authorityStart..authorityEnd];
		string host = authority;

		if (authority.StartsWith("[", StringComparison.Ordinal))
		{
			int closingBracket = authority.IndexOf(']');
			host = authority[..(closingBracket + 1)];
		}
		else
		{
			if (TryGetExplicitPort(authority, out string portText))
			{
				host = authority[..^portText.Length];
			}
			else if (authority.Contains(':'))
			{
				host = $"[{authority}]";
			}
		}

		return $"{url[..authorityStart]}{host}:{port}{url[authorityEnd..]}";
	}

	private static int FindAuthorityEnd(string url, int authorityStart)
	{
		for (int index = authorityStart; index < url.Length; index++)
		{
			if (url[index] is '/' or '?' or '#')
			{
				return index;
			}
		}

		return url.Length;
	}

	private static bool IsDigits(string value)
	{
		if (value.Length == 0)
		{
			return false;
		}

		foreach (char character in value)
		{
			if (!char.IsDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryGetExplicitPort(string authority, out string port)
	{
		port = string.Empty;
		if (authority.StartsWith("[", StringComparison.Ordinal))
		{
			int closingBracket = authority.IndexOf(']');
			if (closingBracket < 0)
			{
				return false;
			}

			string suffix = authority[(closingBracket + 1)..];
			if (suffix.Length == 0)
			{
				return false;
			}

			if (!suffix.StartsWith(":", StringComparison.Ordinal) || !IsDigits(suffix[1..]))
			{
				return false;
			}

			port = suffix;
			return true;
		}

		int lastColon = authority.LastIndexOf(':');
		if (lastColon < 0)
		{
			return false;
		}

		string candidatePort = authority[(lastColon + 1)..];
		if (!IsDigits(candidatePort))
		{
			return false;
		}

		int firstColon = authority.IndexOf(':');
		if (firstColon != lastColon
			&& IPAddress.TryParse(authority, out IPAddress? address)
			&& address.AddressFamily == AddressFamily.InterNetworkV6
			&& !IsLegacyUnbracketedPort(authority, candidatePort))
		{
			return false;
		}

		port = authority[lastColon..];
		return true;
	}

	private static bool IsLegacyUnbracketedPort(string authority, string candidatePort) =>
		authority.StartsWith("::", StringComparison.Ordinal)
		&& candidatePort.Length >= 4
		&& int.TryParse(candidatePort, out int port)
		&& port is > 0 and <= 65535;

	private static int GetDefaultPort(string scheme) =>
		string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
