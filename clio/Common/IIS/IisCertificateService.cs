using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Web.Administration;

namespace Clio.Common.IIS;

/// <summary>
/// Immutable metadata for a certificate that may be used by an IIS HTTPS binding.
/// </summary>
/// <param name="Thumbprint">Normalized SHA-1 thumbprint.</param>
/// <param name="Subject">Certificate subject.</param>
/// <param name="DnsNames">DNS identities extracted from SAN or common name.</param>
/// <param name="NotBefore">Beginning of the certificate validity window.</param>
/// <param name="NotAfter">End of the certificate validity window.</param>
/// <param name="HasPrivateKey">Whether the certificate exposes a private key.</param>
/// <param name="SupportsServerAuthentication">Whether the EKU permits TLS server authentication.</param>
public sealed record IisCertificateInfo(
	string Thumbprint,
	string Subject,
	IReadOnlyList<string> DnsNames,
	DateTimeOffset NotBefore,
	DateTimeOffset NotAfter,
	bool HasPrivateKey,
	bool SupportsServerAuthentication);

/// <summary>
/// Result of resolving an IIS HTTPS certificate for a host.
/// </summary>
/// <param name="Certificate">Selected certificate, or <c>null</c> when no usable candidate exists.</param>
/// <param name="PinnedCertificateUnavailable">Whether a configured pin was not among usable candidates.</param>
public sealed record IisCertificateSelection(IisCertificateInfo Certificate, bool PinnedCertificateUnavailable);

/// <summary>
/// Reads certificate metadata from the platform certificate store.
/// </summary>
public interface IIisCertificateProvider {
	/// <summary>Returns certificates from the local machine personal store.</summary>
	IReadOnlyList<IisCertificateInfo> GetCertificates();
}

/// <summary>
/// Resolves usable IIS certificates for a host and applies the persisted pin preference.
/// </summary>
public interface IIisCertificateResolver {
	/// <summary>Returns usable certificates for <paramref name="hostName"/> in deterministic order.</summary>
	IReadOnlyList<IisCertificateInfo> GetUsableCertificates(string hostName, DateTimeOffset now);

	/// <summary>Resolves the preferred certificate, or returns a result whose certificate is null.</summary>
	IisCertificateSelection Resolve(string hostName, string pinnedThumbprint, DateTimeOffset now);
}

/// <summary>
/// Attaches a local-machine certificate to the HTTPS binding of an IIS site.
/// </summary>
public interface IIisCertificateBindingService {
	/// <summary>Attaches <paramref name="thumbprint"/> from the <c>My</c> store.</summary>
	void Attach(string siteName, string thumbprint);
}

/// <summary>
/// Reads the Windows local-machine personal certificate store.
/// </summary>
public sealed class WindowsIisCertificateProvider : IIisCertificateProvider {
	private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
	private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";

	/// <inheritdoc />
	public IReadOnlyList<IisCertificateInfo> GetCertificates() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return [];
		}
		using X509Store store = new(StoreName.My, StoreLocation.LocalMachine);
		store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
		List<IisCertificateInfo> certificates = [];
		foreach (X509Certificate2 certificate in store.Certificates) {
			using (certificate) {
				certificates.Add(ToInfo(certificate));
			}
		}
		return certificates;
	}

	internal static IisCertificateInfo ToInfo(X509Certificate2 certificate) {
		X509SubjectAlternativeNameExtension[] subjectAlternativeNames = certificate.Extensions
			.OfType<X509SubjectAlternativeNameExtension>()
			.ToArray();
		string[] dnsNames = subjectAlternativeNames
			.SelectMany(extension => extension.EnumerateDnsNames())
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (subjectAlternativeNames.Length == 0) {
			string commonName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
			if (!string.IsNullOrWhiteSpace(commonName)) {
				dnsNames = [commonName];
			}
		}

		X509EnhancedKeyUsageExtension eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
		bool supportsServerAuthentication = eku is null
			|| eku.EnhancedKeyUsages.Cast<Oid>().Any(oid =>
				oid.Value is ServerAuthenticationOid or AnyExtendedKeyUsageOid);
		return new IisCertificateInfo(
			NormalizeThumbprint(certificate.Thumbprint),
			certificate.Subject,
			dnsNames,
			certificate.NotBefore,
			certificate.NotAfter,
			certificate.HasPrivateKey,
			supportsServerAuthentication);
	}

	internal static string NormalizeThumbprint(string thumbprint) =>
		string.Concat((thumbprint ?? string.Empty).Where(Uri.IsHexDigit)).ToUpperInvariant();
}

/// <summary>
/// Provides an empty certificate catalog on platforms without the Windows machine store.
/// </summary>
public sealed class NonWindowsIisCertificateProvider : IIisCertificateProvider {
	/// <inheritdoc />
	public IReadOnlyList<IisCertificateInfo> GetCertificates() => [];
}

/// <summary>
/// Applies IIS certificate eligibility, hostname matching, pinning, and deterministic ordering.
/// </summary>
public sealed class IisCertificateResolver(IIisCertificateProvider provider) : IIisCertificateResolver {
	private readonly IIisCertificateProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

	/// <inheritdoc />
	public IReadOnlyList<IisCertificateInfo> GetUsableCertificates(string hostName, DateTimeOffset now) {
		if (string.IsNullOrWhiteSpace(hostName)) {
			return [];
		}
		return _provider.GetCertificates()
			.Where(certificate => certificate.HasPrivateKey
				&& certificate.SupportsServerAuthentication
				&& certificate.NotBefore <= now
				&& certificate.NotAfter > now
				&& certificate.DnsNames.Any(name => MatchesHost(name, hostName)))
			.OrderByDescending(certificate => certificate.NotAfter)
			.ThenByDescending(certificate => certificate.NotBefore)
			.ThenBy(certificate => certificate.Thumbprint, StringComparer.Ordinal)
			.ToArray();
	}

	/// <inheritdoc />
	public IisCertificateSelection Resolve(string hostName, string pinnedThumbprint, DateTimeOffset now) {
		IReadOnlyList<IisCertificateInfo> candidates = GetUsableCertificates(hostName, now);
		string normalizedPin = WindowsIisCertificateProvider.NormalizeThumbprint(pinnedThumbprint);
		IisCertificateInfo pinned = candidates.FirstOrDefault(certificate =>
			string.Equals(certificate.Thumbprint, normalizedPin, StringComparison.OrdinalIgnoreCase));
		bool pinUnavailable = !string.IsNullOrWhiteSpace(normalizedPin) && pinned is null;
		return new IisCertificateSelection(pinned ?? candidates.FirstOrDefault(), pinUnavailable);
	}

	private static bool MatchesHost(string certificateName, string hostName) {
		string pattern = certificateName.Trim().TrimEnd('.');
		string host = hostName.Trim().TrimEnd('.');
		if (pattern.StartsWith("*.", StringComparison.Ordinal)) {
			string suffix = pattern[1..];
			return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
				&& host.Count(character => character == '.') == suffix.Count(character => character == '.');
		}
		return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// Attaches certificates to Windows IIS bindings through Microsoft.Web.Administration.
/// </summary>
public sealed class WindowsIisCertificateBindingService : IIisCertificateBindingService {
	/// <inheritdoc />
	public void Attach(string siteName, string thumbprint) {
		using ServerManager manager = new();
		Site site = manager.Sites[siteName] ?? throw new InvalidOperationException($"IIS site '{siteName}' was not found.");
		Binding[] httpsBindings = site.Bindings.Where(binding => binding.Protocol == "https").ToArray();
		if (httpsBindings.Length != 1 || site.Bindings.Count != 1) {
			throw new InvalidOperationException($"IIS site '{siteName}' must have exactly one HTTPS binding before attaching a certificate.");
		}
		Binding originalBinding = httpsBindings[0];
		string bindingInformation = originalBinding.BindingInformation;
		SslFlags sslFlags = originalBinding.SslFlags;
		site.Bindings.Remove(originalBinding);
		site.Bindings.Add(
			bindingInformation,
			Convert.FromHexString(WindowsIisCertificateProvider.NormalizeThumbprint(thumbprint)),
			StoreName.My.ToString(),
			sslFlags);
		manager.CommitChanges();
	}
}

/// <summary>
/// Rejects IIS certificate binding on non-Windows platforms.
/// </summary>
public sealed class NonWindowsIisCertificateBindingService : IIisCertificateBindingService {
	/// <inheritdoc />
	public void Attach(string siteName, string thumbprint) =>
		throw new PlatformNotSupportedException("IIS certificate binding is available only on Windows.");
}
