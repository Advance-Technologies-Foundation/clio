using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.UserEnvironment;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Decides which browser page-smoke uses, and asks the user before using one or downloading one: CHROME_PATH wins
/// without asking; otherwise the user's remembered answer; otherwise a <c>browser-consent-required</c> refusal that
/// lists the browsers already on the machine (headless shells other tools cached first, then installed browsers), a
/// download of the pinned headless shell, and "no".
/// </summary>
public interface IPageSmokeBrowserResolver {

	/// <summary>Resolves the browser for one call.</summary>
	/// <param name="choice">The user's answer passed on this call (<c>existing</c>, <c>download</c>, <c>none</c>); null when none was passed.</param>
	/// <param name="browserPath">With <c>existing</c>: which of the offered browsers (required).</param>
	/// <param name="ct">Cancels a download.</param>
	/// <returns>The browser to use, or why there is none yet.</returns>
	Task<PageSmokeBrowserResolution> ResolveAsync(string choice, string browserPath, CancellationToken ct);
}

/// <summary>The outcome of <see cref="IPageSmokeBrowserResolver.ResolveAsync"/>.</summary>
/// <param name="BrowserPath">The browser to run; null when the check cannot run on this call.</param>
/// <param name="Code">Why there is no browser: <c>browser-consent-required</c>, <c>browser-declined</c>,
/// <c>browser-ready</c> (downloaded on this call), <c>browser-not-found</c> or <c>browser-download-failed</c>.</param>
/// <param name="Message">What happened and what the caller does next.</param>
/// <param name="Consent">The question to put to the user, with its choices; set with <c>browser-consent-required</c> and <c>browser-not-found</c>.</param>
public sealed record PageSmokeBrowserResolution(string BrowserPath, string Code, string Message, PageSmokeBrowserConsent Consent);

/// <summary>The question page-smoke needs the user to answer before it uses or downloads a browser.</summary>
public sealed class PageSmokeBrowserConsent {

	/// <summary>Why a browser is needed.</summary>
	[JsonPropertyName("why")]
	public string Why { get; init; }

	/// <summary>The question, worded to be relayed to the user verbatim.</summary>
	[JsonPropertyName("question")]
	public string Question { get; init; }

	/// <summary>The answers, in the order offered; the first is the recommended one.</summary>
	[JsonPropertyName("choices")]
	public IReadOnlyList<PageSmokeBrowserChoice> Choices { get; init; }
}

/// <summary>One answer to <see cref="PageSmokeBrowserConsent"/>: the arguments to call page-smoke again with.</summary>
public sealed class PageSmokeBrowserChoice {

	/// <summary>The value of page-smoke's <c>browser</c> argument: existing, download or none.</summary>
	[JsonPropertyName("browser")]
	public string Browser { get; init; }

	/// <summary>With <c>existing</c>: the value of <c>browser-path</c>.</summary>
	[JsonPropertyName("browser-path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string BrowserPath { get; init; }

	/// <summary>What the user is agreeing to.</summary>
	[JsonPropertyName("description")]
	public string Description { get; init; }
}

/// <inheritdoc cref="IPageSmokeBrowserResolver" />
public sealed class PageSmokeBrowserResolver(
	IChromiumLocator chromiumLocator,
	IBrowserCacheLocator cacheLocator,
	IHeadlessShellInstaller installer,
	IHeadlessShellSource source,
	ISettingsRepository settingsRepository,
	IFileSystem fileSystem,
	TimeProvider timeProvider) : IPageSmokeBrowserResolver {

	internal const string ConsentRequiredCode = "browser-consent-required";
	internal const string DeclinedCode = "browser-declined";
	internal const string ReadyCode = "browser-ready";
	internal const string NotFoundCode = "browser-not-found";
	internal const string DownloadFailedCode = "browser-download-failed";

	internal const string Why = "page-smoke opens the page in a headless Chromium-based browser on the machine clio runs on, "
		+ "to report its buttons, fields and console errors (and what a click did).";

	private const string RelayRule = "This refusal is not the user's answer: put browserConsent.question to the user verbatim "
		+ "and never choose for them, then call page-smoke again with the arguments of the choice they picked "
		+ "(browserConsent.choices). The answer is remembered, so the user is asked once.";

	/// <inheritdoc />
	public async Task<PageSmokeBrowserResolution> ResolveAsync(string choice, string browserPath, CancellationToken ct) {
		// The user's "no" - given now or remembered - wins even over CHROME_PATH: it is a refusal, not a preference.
		if (choice == PageSmokeBrowserSettings.None) {
			Remember(PageSmokeBrowserSettings.None, null);
			return new PageSmokeBrowserResolution(null, DeclinedCode,
				"Remembered: page-smoke will not use or download a browser, so the check was not run; continue without "
				+ "a UI check.", null);
		}
		PageSmokeBrowserSettings stored = choice is null ? settingsRepository.GetPageSmokeBrowser() : null;
		if (stored?.Choice == PageSmokeBrowserSettings.None) {
			return new PageSmokeBrowserResolution(null, DeclinedCode,
				"The user chose earlier that page-smoke must not use or download a browser, so the check was not run; "
				+ "continue without a UI check. Only if the user now asks for it, put browserConsent's question to them "
				+ "again by calling page-smoke with browser=existing or browser=download.", null);
		}
		string overridden = chromiumLocator.ChromePathOverride();
		if (overridden is not null) {
			return Ready(overridden);
		}
		if (choice == PageSmokeBrowserSettings.Download) {
			return await ChooseDownloadAsync(ct).ConfigureAwait(false);
		}
		if (choice is not null) {
			return ChooseExisting(browserPath);
		}
		switch (stored?.Choice) {
			case PageSmokeBrowserSettings.Download:
				string downloaded = installer.FindInstalled();
				return downloaded is not null ? Ready(downloaded) : await DownloadAsync(ct).ConfigureAwait(false);
			case PageSmokeBrowserSettings.Existing:
				// Re-checked against what is offered now, so an edited setting cannot start an arbitrary file and a
				// browser that went away (or a cache build that was replaced) brings the question back.
				return IsOffered(stored.Path) && FileExists(stored.Path)
					? Ready(stored.Path)
					: AskUser("The browser the user chose earlier (" + stored.Path + ") is no longer offered. ");
			default:
				return AskUser(string.Empty);
		}
	}

	private async Task<PageSmokeBrowserResolution> ChooseDownloadAsync(CancellationToken ct) {
		if (installer.DownloadArchive is null) {
			return new PageSmokeBrowserResolution(null, NotFoundCode,
				$"There is no {source.DisplayName} build for this machine, so browser=download cannot be used and nothing "
				+ "was remembered. " + RelayRule, BuildConsent(FindExisting()));
		}
		Remember(PageSmokeBrowserSettings.Download, null);
		string downloaded = installer.FindInstalled();
		return downloaded is not null ? Ready(downloaded) : await DownloadAsync(ct).ConfigureAwait(false);
	}

	private bool IsOffered(string path) => path is not null && FindExisting().Any(browser => SamePath(browser.Path, path));

	private PageSmokeBrowserResolution ChooseExisting(string browserPath) {
		IReadOnlyList<InstalledBrowser> existing = FindExisting();
		if (existing.Count == 0) {
			return new PageSmokeBrowserResolution(null, NotFoundCode,
				"No Chromium-based browser was found on the machine clio runs on, so browser=existing cannot be used. "
				+ RelayRule, BuildConsent(existing));
		}
		InstalledBrowser chosen = existing.FirstOrDefault(browser => SamePath(browser.Path, browserPath));
		if (chosen is null) {
			return new PageSmokeBrowserResolution(null, NotFoundCode,
				"browser-path must be one of the browsers page-smoke offers (browserConsent.choices); an arbitrary "
				+ "executable is never started. " + RelayRule, BuildConsent(existing));
		}
		Remember(PageSmokeBrowserSettings.Existing, chosen.Path);
		return Ready(chosen.Path);
	}

	private async Task<PageSmokeBrowserResolution> DownloadAsync(CancellationToken ct) {
		try {
			string executable = await installer.InstallAsync(ct).ConfigureAwait(false);
			return new PageSmokeBrowserResolution(null, ReadyCode,
				$"Downloaded {source.DisplayName} {source.Version} to {executable}. The page was not checked on this call, "
				+ "so the download stays inside the call's time; call page-smoke again with the same arguments, without "
				+ "browser, to run the check.", null);
		}
		catch (HeadlessShellInstallException exception) {
			return new PageSmokeBrowserResolution(null, DownloadFailedCode,
				exception.Message + " The user's choice (download) is kept, so calling page-smoke again retries; ask the "
				+ "user before switching to browser=existing or browser=none.", null);
		}
	}

	private PageSmokeBrowserResolution AskUser(string prefix) =>
		new(null, ConsentRequiredCode,
			prefix + "page-smoke needs a browser and the user has not chosen one yet, so the check was not run. " + RelayRule,
			BuildConsent(FindExisting()));

	private PageSmokeBrowserConsent BuildConsent(IReadOnlyList<InstalledBrowser> existing) {
		List<PageSmokeBrowserChoice> choices = [
			.. existing.Select(browser => new PageSmokeBrowserChoice {
				Browser = PageSmokeBrowserSettings.Existing,
				BrowserPath = browser.Path,
				Description = $"Use {Describe(browser)}, already on this machine."
			})
		];
		HeadlessShellRelease.Archive archive = installer.DownloadArchive;
		if (archive is not null) {
			choices.Add(new PageSmokeBrowserChoice {
				Browser = PageSmokeBrowserSettings.Download,
				Description = $"Download {source.DisplayName} {source.Version} (about {Megabytes(archive.Size)} MB, once) "
					+ $"into {installer.InstallDirectory}."
			});
		}
		choices.Add(new PageSmokeBrowserChoice {
			Browser = PageSmokeBrowserSettings.None,
			Description = "No: page-smoke stays off and the work continues without a UI check."
		});
		StringBuilder question = new("page-smoke needs a Chromium-based browser to open the page headless. Which should it use?");
		for (int index = 0; index < choices.Count; index++) {
			question.Append(' ').Append(index + 1).Append(") ").Append(choices[index].Description);
		}
		question.Append(" The answer is remembered; to change it later, delete page-smoke-browser from clio's settings or ask again.");
		return new PageSmokeBrowserConsent { Why = Why, Question = question.ToString(), Choices = choices };
	}

	// Headless shells other tools already downloaded come first (the recommended choice), then installed browsers.
	private IReadOnlyList<InstalledBrowser> FindExisting() {
		IReadOnlyList<InstalledBrowser> cached = cacheLocator.FindCached();
		return [
			.. cached,
			.. chromiumLocator.FindInstalled().Where(installed => !cached.Any(browser => SamePath(browser.Path, installed.Path)))
		];
	}

	private static string Describe(InstalledBrowser browser) =>
		browser.Version is null ? $"{browser.Name} at {browser.Path}" : $"{browser.Name} {browser.Version} at {browser.Path}";

	private static long Megabytes(long bytes) => (bytes + 512 * 1024) / (1024 * 1024);

	private void Remember(string choice, string path) =>
		settingsRepository.SetPageSmokeBrowser(new PageSmokeBrowserSettings {
			Choice = choice,
			Path = path,
			DecidedAt = timeProvider.GetUtcNow()
		});

	private static bool SamePath(string left, string right) =>
		string.Equals(left, right, RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal);

	private bool FileExists(string path) => fileSystem.ExistsFile(path);

	private static PageSmokeBrowserResolution Ready(string path) => new(path, null, null, null);
}
