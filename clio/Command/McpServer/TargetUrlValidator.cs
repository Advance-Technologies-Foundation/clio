using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Clio.Command.McpServer;

/// <summary>
/// Egress guard for the caller-influenced per-request passthrough target URL (Story 6, FR-17).
/// Blocks link-local / cloud-metadata / loopback targets <b>always</b> — regardless of any
/// operator-configured allowlist — and, when an allowlist is configured, additionally requires
/// the target origin to be on it. It is invoked in the credential-resolution path <b>before</b>
/// any client is constructed or any credential is forwarded, so a hostile <c>url</c> cannot be
/// used as a credential-redirection lever (CWE-918 SSRF).
/// </summary>
public interface ITargetUrlValidator
{
	/// <summary>
	/// Validates a per-request passthrough target URL. Returns normally when the URL is allowed;
	/// throws a caller-actionable <see cref="TargetUrlNotAllowedException"/> naming the reason
	/// (blocked address class / not on allowlist) when it is rejected. No credential value is ever
	/// passed to, or echoed by, this method — it sees only the URL (FR-11).
	/// </summary>
	/// <param name="url">The caller-supplied absolute target URL to validate.</param>
	/// <exception cref="TargetUrlNotAllowedException">The URL is not an absolute http/https URL,
	/// targets a blocked address class, or is not on the configured origin allowlist.</exception>
	void EnsureAllowed(string url);
}

/// <summary>
/// Thrown by <see cref="ITargetUrlValidator.EnsureAllowed"/> when a per-request passthrough
/// target URL is rejected. The message names the rejection reason and never carries a credential.
/// </summary>
public sealed class TargetUrlNotAllowedException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="TargetUrlNotAllowedException"/> class.</summary>
	/// <param name="message">A caller-actionable message naming the rejection reason.</param>
	public TargetUrlNotAllowedException(string message)
		: base(message) {
	}
}

/// <summary>
/// Default <see cref="ITargetUrlValidator"/>. Constructed from the server's bound host and an
/// already-resolved set of allowed base URLs, so it is free of option/config reading and fully
/// unit-testable with literal hosts (no live DNS).
/// </summary>
/// <remarks>
/// <para>
/// The baseline address-class blocks apply only to <b>IP-literal</b> hosts (including
/// IPv4-mapped IPv6 literals, which are normalized to their IPv4 form before the checks).
/// Alternative encodings that <see cref="Uri"/> <em>canonicalizes</em> to a dotted-quad literal —
/// decimal (<c>http://2130706433/</c>), hex (<c>http://0x7f000001/</c>), and octal
/// (<c>http://0177.0.0.1/</c>) integer addresses — are classified as <see cref="UriHostNameType.IPv4"/>
/// and therefore covered by the baseline blocks. A single trailing dot (e.g.
/// <c>http://169.254.169.254./</c>) makes <see cref="Uri"/> classify the host as
/// <see cref="UriHostNameType.Dns"/>; <see cref="TryGetIpLiteral"/> strips exactly one trailing dot
/// and re-attempts the literal parse so that class is blocked too, without relying on
/// <see cref="Uri"/>'s per-version host classification.
/// </para>
/// <para>
/// A genuinely non-literal host is NOT DNS-resolved here: resolving it would (a) require live DNS in
/// the hot path and (b) only shift, not close, the window — the client that ultimately dials the
/// target does its own resolution, so a name that resolves to a blocked IP <em>after</em> this check
/// (DNS-rebinding TOCTOU) is a documented residual that is <b>out of scope for v1</b>. See Story 14 docs.
/// </para>
/// </remarks>
public sealed class TargetUrlValidator : ITargetUrlValidator
{
	// 169.254.169.254 — the IMDS cloud-metadata endpoint. Built from octets rather than a parsed
	// string literal so the SSRF-critical address is not a hardcoded IP literal; the resulting
	// IPv4 address is byte-identical to IPAddress.Parse("169.254.169.254").
	private static readonly IPAddress CloudMetadataAddress = new(new byte[] { 169, 254, 169, 254 });

	private readonly bool _boundHostIsLoopback;
	private readonly HashSet<string> _allowedOrigins;

	/// <summary>
	/// Initializes a new instance of the <see cref="TargetUrlValidator"/> class.
	/// </summary>
	/// <param name="boundHost">
	/// The host the MCP HTTP server is bound to (<c>options.Host</c>). A loopback target is
	/// permitted only when this bound host is itself loopback (local-dev scenario).
	/// </param>
	/// <param name="allowedBaseUrls">
	/// The operator-configured allowed base URLs (already split, trimmed, non-empty). Each is
	/// normalized to its origin (scheme+host+port). When empty (flag unset), no allowlist check is
	/// applied — the baseline address-class blocks still apply and any other reachable host is
	/// permitted. When non-empty but <b>every</b> entry fails to parse into a valid absolute
	/// http/https origin, construction fails-closed with an <see cref="ArgumentException"/> rather
	/// than silently degrading to baseline-only.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="allowedBaseUrls"/> is non-empty but yields no valid origin (operator typo).
	/// </exception>
	public TargetUrlValidator(string boundHost, IEnumerable<string> allowedBaseUrls) {
		_boundHostIsLoopback = IsLoopbackIpOrLocalhost(boundHost);
		_allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool anyConfigured = false;
		if (allowedBaseUrls is not null) {
			foreach (string baseUrl in allowedBaseUrls) {
				anyConfigured = true;
				if (TryGetOrigin(baseUrl, out string origin)) {
					_allowedOrigins.Add(origin);
				}
			}
		}

		// Fail-closed (Story 12 / Story-6 follow-up, FR-14/FR-17): a NON-EMPTY --allowed-base-urls
		// whose entries ALL fail to parse into a valid absolute http/https origin is almost certainly
		// an operator typo. Silently keeping an empty origin set would fail-OPEN to baseline-only and
		// silently disable the allowlist the operator intended to enforce — so reject it loudly at
		// construction (startup) instead. An UNSET flag (no entries) is the legitimate baseline-only
		// case and is left untouched.
		if (anyConfigured && _allowedOrigins.Count == 0) {
			throw new ArgumentException(
				"Error: --allowed-base-urls was set but contained no valid absolute http/https origin; "
				+ "fix the value or omit the flag to fall back to baseline-only egress protection.");
		}
	}

	/// <inheritdoc />
	public void EnsureAllowed(string url) {
		// Rule 1: absolute http/https required (AC-01).
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
			throw new TargetUrlNotAllowedException(
				"Error: target url must be an absolute http/https URL");
		}

		// Rule 2: baseline address-class blocks (ALWAYS, regardless of allowlist). These apply
		// only to IP-literal hosts. A non-IP hostname is not resolved here — see the class-level
		// remarks for the documented DNS-rebinding TOCTOU residual (out of scope for v1).
		if (TryGetIpLiteral(uri, out IPAddress ip)) {
			EnsureIpNotBlocked(ip);
		}

		// Rule 3: optional origin allowlist. When configured, the target origin must be on it;
		// when empty, this check is skipped (baseline blocks above still apply) (AC-03/AC-04).
		if (_allowedOrigins.Count > 0) {
			string origin = GetOrigin(uri);
			if (!_allowedOrigins.Contains(origin)) {
				throw new TargetUrlNotAllowedException(
					"Error: target origin is not on the configured allowlist");
			}
		}
	}

	private void EnsureIpNotBlocked(IPAddress ip) {
		if (ip.Equals(CloudMetadataAddress)) {
			throw new TargetUrlNotAllowedException(
				"Error: target url is blocked: cloud-metadata address (169.254.169.254)");
		}

		if (IsIpv4LinkLocal(ip)) {
			throw new TargetUrlNotAllowedException(
				"Error: target url is blocked: IPv4 link-local address (169.254.0.0/16)");
		}

		if (IsIpv6LinkLocal(ip)) {
			throw new TargetUrlNotAllowedException(
				"Error: target url is blocked: IPv6 link-local address (fe80::/10)");
		}

		if (IPAddress.IsLoopback(ip) && !_boundHostIsLoopback) {
			throw new TargetUrlNotAllowedException(
				"Error: target url is blocked: loopback address (allowed only when the server is bound to loopback)");
		}
	}

	private static bool IsIpv4LinkLocal(IPAddress ip) {
		if (ip.AddressFamily != AddressFamily.InterNetwork) {
			return false;
		}

		byte[] bytes = ip.GetAddressBytes();
		return bytes[0] == 169 && bytes[1] == 254;
	}

	private static bool IsIpv6LinkLocal(IPAddress ip) {
		if (ip.AddressFamily != AddressFamily.InterNetworkV6) {
			return false;
		}

		// fe80::/10 — first 10 bits are 1111 1110 10.
		byte[] bytes = ip.GetAddressBytes();
		return bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
	}

	private static bool TryGetIpLiteral(Uri uri, out IPAddress ip) {
		// Primary path: Uri already classified the host as an IP literal. This covers dotted-quad,
		// bracketed IPv6, and the integer/hex/octal encodings Uri canonicalizes to a dotted-quad
		// (HostNameType == IPv4). DnsSafeHost strips the IPv6 brackets (and any scope id) so
		// IPAddress.TryParse accepts it.
		if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
			&& IPAddress.TryParse(uri.DnsSafeHost, out ip)) {
			ip = NormalizeIpv4Mapped(ip);
			return true;
		}

		// Fallback path: a single trailing dot (e.g. "169.254.169.254." / "127.0.0.1.") makes Uri
		// classify the host as Dns, so the primary path misses it — a real SSRF bypass in
		// no-allowlist mode. Strip EXACTLY one trailing dot (not TrimEnd, which would eat several)
		// and re-attempt the literal parse so the baseline blocks still apply. This closes the class
		// without depending on Uri's per-version host classification.
		string host = uri.Host;
		if (host.Length > 0 && host[^1] == '.'
			&& IPAddress.TryParse(host[..^1], out ip)) {
			ip = NormalizeIpv4Mapped(ip);
			return true;
		}

		ip = null;
		return false;
	}

	// Normalize IPv4-mapped IPv6 literals (e.g. ::ffff:169.254.169.254) down to their IPv4 form so
	// the address-class checks (metadata / link-local / loopback) see the real target and cannot be
	// bypassed by the dual-stack encoding.
	private static IPAddress NormalizeIpv4Mapped(IPAddress ip) =>
		ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

	// Loopback test for the SERVER'S BOUND HOST: the literal "localhost" OR any string that parses
	// to a loopback IP (127.0.0.0/8, ::1). Deliberately BROADER than McpHttpServerCommand's
	// IsLoopbackAlias (which matches only a fixed alias set): this gate decides whether an outbound
	// loopback TARGET is permitted, so it must recognize e.g. 127.0.0.2 as loopback. The two guard
	// different security controls and are intentionally NOT the same predicate.
	private static bool IsLoopbackIpOrLocalhost(string host) {
		if (string.IsNullOrWhiteSpace(host)) {
			return false;
		}

		string trimmed = host.Trim().Trim('[', ']');
		if (string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}

		return IPAddress.TryParse(trimmed, out IPAddress ip) && IPAddress.IsLoopback(ip);
	}

	private static bool TryGetOrigin(string url, out string origin) {
		if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
			&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
			origin = GetOrigin(uri);
			return true;
		}

		origin = null;
		return false;
	}

	// scheme://host[:port] with the scheme's default port omitted, so origins compare cleanly
	// (https://acme.creatio.com and https://acme.creatio.com:443 are the same origin).
	private static string GetOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);
}

/// <summary>
/// Resolves the origin allowlist for the MCP HTTP egress guard from the
/// <c>--allowed-base-urls</c> CLI flag (a comma-separated set). Each entry is trimmed and
/// empty entries are dropped; the raw entries are handed to <see cref="TargetUrlValidator"/>,
/// which normalizes them to origins.
/// </summary>
public static class AllowedBaseUrlsConfiguration
{
	/// <summary>
	/// Splits the comma-separated <c>--allowed-base-urls</c> value into a trimmed, non-empty list.
	/// </summary>
	/// <param name="flagValue">The <c>--allowed-base-urls</c> flag value (comma-separated set); may be <see langword="null"/>.</param>
	/// <returns>The trimmed, non-empty entries (possibly empty).</returns>
	public static IReadOnlyList<string> Resolve(string flagValue) => CommaSet.Split(flagValue);
}
