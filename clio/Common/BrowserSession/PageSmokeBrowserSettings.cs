using System;
using Newtonsoft.Json;

namespace Clio.Common.BrowserSession;

/// <summary>
/// The user's answer to page-smoke's browser question, kept in clio settings under <c>page-smoke-browser</c> so it is
/// asked once. Delete the key (or answer again through page-smoke's <c>browser</c> argument) to change it.
/// </summary>
public sealed class PageSmokeBrowserSettings {

	/// <summary>Use a browser already on the machine (<see cref="Path"/>): an installed one, or one Playwright or Puppeteer downloaded.</summary>
	public const string Existing = "existing";

	/// <summary>Download the pinned headless browser into <c>~/.clio/browsers/</c>.</summary>
	public const string Download = "download";

	/// <summary>page-smoke stays off; the agent continues without a UI check.</summary>
	public const string None = "none";

	/// <summary>The answer: <see cref="Existing"/>, <see cref="Download"/> or <see cref="None"/>.</summary>
	[JsonProperty("choice")]
	public string Choice { get; set; }

	/// <summary>With <see cref="Existing"/>: the browser executable the user chose.</summary>
	[JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
	public string Path { get; set; }

	/// <summary>When the user answered.</summary>
	[JsonProperty("decided-at", NullValueHandling = NullValueHandling.Ignore)]
	public DateTimeOffset? DecidedAt { get; set; }

	/// <summary>Whether <paramref name="choice"/> is one of the three answers.</summary>
	/// <param name="choice">The value to check.</param>
	public static bool IsKnown(string choice) => choice is Existing or Download or None;
}
