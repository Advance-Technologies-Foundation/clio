namespace Clio.Command.Localization;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

/// <summary>
/// Checks, before a designer save, that every culture a caption write targets is a <c>SysCulture</c> row of
/// the environment. The platform drops a localizable value whose culture it does not have and still answers
/// <c>success:true</c>, so without this check the caller is told a translation was stored when it was not.
/// </summary>
public interface ICultureAvailabilityGuard {

	/// <summary>
	/// Reads the environment's cultures once and checks every requested culture against them,
	/// case-insensitively. A culture present but inactive is reported through <see cref="ILogger.WriteWarning"/>
	/// and does not stop the write. An empty or blank-only <paramref name="cultureNames"/> does not read the
	/// catalog at all.
	/// </summary>
	/// <param name="cultureNames">Culture names the write is about to store values under.</param>
	/// <exception cref="InvalidOperationException">A requested culture is not a <c>SysCulture</c> row (the
	/// message names the Languages section and lists the available cultures), or the <c>SysCulture</c> read
	/// failed.</exception>
	void EnsureAvailable(IEnumerable<string> cultureNames);

	/// <summary>
	/// Resolves one culture against the environment's <c>SysCulture</c> rows, case-insensitively, and returns
	/// the canonical row together with the inactive-culture warning instead of logging it, so the caller can
	/// put the warning into its own structured result.
	/// </summary>
	/// <param name="cultureName">Culture name supplied by the caller.</param>
	/// <returns>The canonical culture and, when it is inactive, the warning text; otherwise a <see langword="null"/> warning.</returns>
	/// <exception cref="ArgumentException"><paramref name="cultureName"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">The culture is not a <c>SysCulture</c> row (the message names
	/// the Languages section and lists the available cultures), or the <c>SysCulture</c> read failed.</exception>
	CultureResolution Resolve(string cultureName);
}

/// <summary>
/// A culture resolved against the environment, with the warning the caller must surface.
/// </summary>
/// <param name="Culture">The <c>SysCulture</c> row in its canonical spelling.</param>
/// <param name="Warning">The inactive-culture warning, or <see langword="null"/> when the culture is active.</param>
public sealed record CultureResolution(CreatioCulture Culture, string Warning);

/// <inheritdoc />
public sealed class CultureAvailabilityGuard : ICultureAvailabilityGuard {

	private readonly ICreatioCultureCatalog _cultureCatalog;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CultureAvailabilityGuard"/> class.
	/// </summary>
	/// <param name="cultureCatalog">Reader of the environment's <c>SysCulture</c> rows.</param>
	/// <param name="logger">Logger that carries the inactive-culture warning to the CLI and MCP output.</param>
	public CultureAvailabilityGuard(ICreatioCultureCatalog cultureCatalog, ILogger logger) {
		_cultureCatalog = cultureCatalog;
		_logger = logger;
	}

	/// <inheritdoc />
	public void EnsureAvailable(IEnumerable<string> cultureNames) {
		List<string> requested = (cultureNames ?? [])
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (requested.Count == 0) {
			return;
		}

		// One read for the whole write: Find() would re-read SysCulture for every culture.
		IReadOnlyList<CreatioCulture> cultures = _cultureCatalog.GetCultures();
		List<CreatioCulture> matches = [];
		foreach (string cultureName in requested) {
			CreatioCulture match = cultures.FirstOrDefault(culture =>
				string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase));
			if (match is null) {
				throw new InvalidOperationException(CultureMessages.FormatCultureAbsent(cultureName, cultures));
			}
			matches.Add(match);
		}
		// Warn only once the whole write is known to proceed, so a refused write carries no warnings.
		foreach (CreatioCulture inactive in matches.Where(culture => !culture.Active)) {
			_logger.WriteWarning(CultureMessages.FormatCultureInactive(inactive.Name));
		}
	}

	/// <inheritdoc />
	public CultureResolution Resolve(string cultureName) {
		if (string.IsNullOrWhiteSpace(cultureName)) {
			throw new ArgumentException("Culture name is required.", nameof(cultureName));
		}
		string trimmed = cultureName.Trim();
		IReadOnlyList<CreatioCulture> cultures = _cultureCatalog.GetCultures();
		CreatioCulture match = cultures.FirstOrDefault(culture =>
			string.Equals(culture.Name, trimmed, StringComparison.OrdinalIgnoreCase));
		if (match is null) {
			throw new InvalidOperationException(CultureMessages.FormatCultureAbsent(trimmed, cultures));
		}
		return new CultureResolution(match, match.Active ? null : CultureMessages.FormatCultureInactive(match.Name));
	}
}
