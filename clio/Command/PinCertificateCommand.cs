using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Common;
using Clio.Common.IIS;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// Options for selecting the preferred local IIS certificate.
/// </summary>
[Verb("pin-certificate", HelpText = "Select the preferred local-machine certificate for IIS HTTPS deployments")]
public sealed class PinCertificateOptions {
	/// <summary>Gets or sets the certificate thumbprint to persist.</summary>
	[Option("thumbprint", SetName = "pin", Required = false,
		HelpText = "Thumbprint of an installed LocalMachine/My certificate matching this host.")]
	public string Thumbprint { get; set; }

	/// <summary>Gets or sets a value indicating whether the persisted certificate preference is removed.</summary>
	[Option("clear", SetName = "clear", Required = false, HelpText = "Remove the pinned IIS certificate preference.")]
	public bool Clear { get; set; }
}

/// <summary>
/// Prompts a user to select one certificate from an eligible local-machine certificate list.
/// </summary>
public interface ICertificateSelectionPrompt {
	/// <summary>Returns the selected thumbprint, or <c>null</c> when the selection is cancelled.</summary>
	string Select(IReadOnlyList<IisCertificateInfo> certificates);
}

/// <summary>
/// Renders certificate choices in the console and reads a numeric selection.
/// </summary>
public sealed class ConsoleCertificateSelectionPrompt(ILogger logger) : ICertificateSelectionPrompt {
	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	/// <inheritdoc />
	public string Select(IReadOnlyList<IisCertificateInfo> certificates) {
		ConsoleTable table = new() { Columns = { "#", "Subject", "DNS names", "Expires", "Thumbprint" } };
		for (int index = 0; index < certificates.Count; index++) {
			IisCertificateInfo certificate = certificates[index];
			table.Rows.Add([
				(index + 1).ToString(CultureInfo.InvariantCulture),
				certificate.Subject,
				string.Join(", ", certificate.DnsNames),
				certificate.NotAfter.ToString("u", CultureInfo.InvariantCulture),
				certificate.Thumbprint
			]);
		}
		_logger.PrintTable(table);
		_logger.WriteLine("Select a certificate number, or press Enter to cancel:");
		string input = Console.ReadLine()?.Trim();
		return int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int selected)
			&& selected >= 1 && selected <= certificates.Count
			? certificates[selected - 1].Thumbprint
			: null;
	}
}

/// <summary>
/// Validates and persists the preferred certificate used to resolve ambiguous IIS HTTPS candidates.
/// </summary>
public sealed class PinCertificateCommand(
	ISettingsRepository settingsRepository,
	IIisCertificateResolver certificateResolver,
	ICertificateSelectionPrompt selectionPrompt,
	ILogger logger) : Command<PinCertificateOptions> {
	private readonly ISettingsRepository _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	private readonly IIisCertificateResolver _certificateResolver = certificateResolver ?? throw new ArgumentNullException(nameof(certificateResolver));
	private readonly ICertificateSelectionPrompt _selectionPrompt = selectionPrompt ?? throw new ArgumentNullException(nameof(selectionPrompt));
	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	/// <inheritdoc />
	public override int Execute(PinCertificateOptions options) {
		if (options.Clear && !string.IsNullOrWhiteSpace(options.Thumbprint)) {
			_logger.WriteError("Specify either --thumbprint or --clear, not both.");
			return 1;
		}
		if (options.Clear) {
			_settingsRepository.SetPinnedIisCertificateThumbprint(null);
			_logger.WriteInfo("The pinned IIS certificate was cleared.");
			return 0;
		}
		bool hasExplicitThumbprint = !string.IsNullOrWhiteSpace(options.Thumbprint);
		string normalizedExplicitThumbprint = hasExplicitThumbprint
			? WindowsIisCertificateProvider.NormalizeThumbprint(options.Thumbprint)
			: null;
		if (hasExplicitThumbprint && (!HasOnlyThumbprintCharacters(options.Thumbprint)
			|| normalizedExplicitThumbprint.Length != 40)) {
			_logger.WriteError("Certificate thumbprint must contain exactly 40 hexadecimal characters.");
			return 1;
		}

		string hostName = InstallerHelper.FetFQDN();
		IReadOnlyList<IisCertificateInfo> certificates = _certificateResolver.GetUsableCertificates(hostName, DateTimeOffset.Now);
		if (certificates.Count == 0) {
			if (hasExplicitThumbprint) {
				_logger.WriteError($"Certificate '{normalizedExplicitThumbprint}' is not a usable LocalMachine/My server certificate for '{hostName}'.");
				return 1;
			}
			_logger.WriteWarning($"No usable LocalMachine/My server certificate matches '{hostName}'. IIS HTTPS deployments will fall back to HTTP.");
			return 0;
		}

		string requestedThumbprint = hasExplicitThumbprint
			? normalizedExplicitThumbprint
			: _selectionPrompt.Select(certificates);
		if (string.IsNullOrWhiteSpace(requestedThumbprint)) {
			_logger.WriteInfo("Certificate selection was cancelled; the existing pin was not changed.");
			return 0;
		}
		IisCertificateInfo selected = certificates.FirstOrDefault(certificate =>
			string.Equals(certificate.Thumbprint, requestedThumbprint, StringComparison.OrdinalIgnoreCase));
		if (selected is null) {
			_logger.WriteError($"Certificate '{requestedThumbprint}' is not a usable LocalMachine/My server certificate for '{hostName}'.");
			return 1;
		}

		_settingsRepository.SetPinnedIisCertificateThumbprint(selected.Thumbprint);
		_logger.WriteInfo($"Pinned IIS certificate {selected.Thumbprint} ({selected.Subject}, expires {selected.NotAfter:u}).");
		return 0;
	}

	private static bool HasOnlyThumbprintCharacters(string value) => value.All(character =>
		Uri.IsHexDigit(character)
		|| char.IsWhiteSpace(character)
		|| character is ':' or '-' or '\u200E' or '\u200F');
}
