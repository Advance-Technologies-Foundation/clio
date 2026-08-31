using System;

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
		if (authority.StartsWith("[", StringComparison.Ordinal))
		{
			int closingBracket = authority.IndexOf(']');
			if (closingBracket < 0)
			{
				return null;
			}

			string suffix = authority[(closingBracket + 1)..];
			if (suffix.Length > 0 && (!suffix.StartsWith(":", StringComparison.Ordinal) || !IsDigits(suffix[1..])))
			{
				return null;
			}

			port = suffix;
		}
		else
		{
			int lastColon = authority.LastIndexOf(':');
			if (lastColon >= 0)
			{
				string candidatePort = authority[(lastColon + 1)..];
				if (!IsDigits(candidatePort))
				{
					return null;
				}

				port = authority[lastColon..];
			}
		}

		return $"{url[..authorityStart]}{bindHost}{port}{url[authorityEnd..]}";
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
			int lastColon = authority.LastIndexOf(':');
			if (lastColon >= 0 && IsDigits(authority[(lastColon + 1)..]))
			{
				host = authority[..lastColon];
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
}
