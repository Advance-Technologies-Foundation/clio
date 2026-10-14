using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103287: page-smoke asks before it uses or downloads a browser. CHROME_PATH wins without asking; otherwise the
/// remembered answer applies; otherwise a browser-consent-required answer offers the browsers already on the machine
/// (a cached headless shell first), a download, and no.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class PageSmokeBrowserResolverTests {

	private const string CachedShell = "/cache/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell";
	private const string InstalledEdge = "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
	private const string DownloadedShell = "/home/.clio/browsers/155/chrome-headless-shell";

	private IChromiumLocator _locator;
	private IBrowserCacheLocator _cache;
	private IHeadlessShellInstaller _installer;
	private ISettingsRepository _settings;
	private IFileSystem _fileSystem;
	private PageSmokeBrowserResolver _sut;

	[SetUp]
	public void SetUp() {
		_locator = Substitute.For<IChromiumLocator>();
		_locator.ChromePathOverride().Returns((string)null);
		_locator.FindInstalled().Returns([new InstalledBrowser("Microsoft Edge", "130.0.2849.80", InstalledEdge)]);
		_cache = Substitute.For<IBrowserCacheLocator>();
		_cache.FindCached().Returns([new InstalledBrowser("chrome-headless-shell (Playwright cache)", "Playwright build 1243", CachedShell)]);
		_installer = Substitute.For<IHeadlessShellInstaller>();
		_installer.InstallDirectory.Returns("/home/.clio/browsers/155");
		_installer.FindInstalled().Returns((string)null);
		_installer.DownloadArchive.Returns(new HeadlessShellRelease.Archive(102_390_341, "pinned"));
		_settings = Substitute.For<ISettingsRepository>();
		_fileSystem = Substitute.For<IFileSystem>();
		_sut = new PageSmokeBrowserResolver(_locator, _cache, _installer, new ChromeForTestingHeadlessShellSource(_ => null),
			_settings, _fileSystem, TimeProvider.System);
	}

	private void Stored(string choice, string path = null) =>
		_settings.GetPageSmokeBrowser().Returns(new PageSmokeBrowserSettings { Choice = choice, Path = path });

	[Test]
	[Description("CHROME_PATH, when it names a file, is used without asking and without remembering anything.")]
	public async Task ResolveAsync_Should_Use_Chrome_Path_Without_Asking() {
		// Arrange
		_locator.ChromePathOverride().Returns("/opt/custom/chrome");

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().Be("/opt/custom/chrome", because: "the operator's override wins");
		_settings.DidNotReceiveWithAnyArgs().SetPageSmokeBrowser(default);
	}

	[Test]
	[Description("The user's 'no' wins even over CHROME_PATH, given now or remembered: it is a refusal, not a preference.")]
	public async Task ResolveAsync_Should_Honour_No_Over_Chrome_Path() {
		// Arrange
		_locator.ChromePathOverride().Returns("/opt/custom/chrome");

		// Act
		PageSmokeBrowserResolution answered = await _sut.ResolveAsync("none", null, CancellationToken.None);
		Stored(PageSmokeBrowserSettings.None);
		PageSmokeBrowserResolution remembered = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		answered.BrowserPath.Should().BeNull(because: "no browser is started against the user's refusal");
		answered.Code.Should().Be(PageSmokeBrowserResolver.DeclinedCode, because: "the user said no");
		remembered.Code.Should().Be(PageSmokeBrowserResolver.DeclinedCode, because: "the remembered no wins too");
	}

	[Test]
	[Description("Without a remembered answer the call is refused with the question: the cached headless shell first (recommended), then the installed browser with its version, then the download with its size and folder, then no.")]
	public async Task ResolveAsync_Should_Ask_When_The_User_Has_Not_Chosen() {
		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().BeNull(because: "nothing is used before the user answers");
		resolution.Code.Should().Be(PageSmokeBrowserResolver.ConsentRequiredCode, because: "the agent must ask the user");
		resolution.Message.Should().Contain("verbatim").And.Contain("never choose for them",
			because: "the refusal is not the user's consent");
		resolution.Consent.Choices.Select(choice => (choice.Browser, choice.BrowserPath)).Should().Equal(
			[("existing", CachedShell), ("existing", InstalledEdge), ("download", null), ("none", null)],
			because: "a browser another tool already downloaded is offered first, a download only after what is on the machine");
		resolution.Consent.Choices[1].Description.Should().Contain("Microsoft Edge 130.0.2849.80 at " + InstalledEdge,
			because: "the user sees name, version and path");
		resolution.Consent.Choices[2].Description.Should().Contain("Chrome for Testing").And.Contain(" MB, once")
			.And.Contain("/home/.clio/browsers/155", because: "the user learns what is downloaded, how big it is and where it goes");
		resolution.Consent.Question.Should().Contain("1) Use chrome-headless-shell (Playwright cache)",
			because: "the question lists the choices in order");
		_installer.DidNotReceiveWithAnyArgs().InstallAsync(default);
	}

	[Test]
	[Description("browser=existing with an offered browser-path uses that browser and remembers it.")]
	public async Task ResolveAsync_Should_Use_And_Remember_The_Chosen_Existing_Browser() {
		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync("existing", CachedShell, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().Be(CachedShell, because: "the user picked it");
		_settings.Received(1).SetPageSmokeBrowser(Arg.Is<PageSmokeBrowserSettings>(stored =>
			stored.Choice == "existing" && stored.Path == CachedShell && stored.DecidedAt != null));
	}

	[Test]
	[Description("browser-path must be one of the offered browsers: an arbitrary executable is refused and nothing is remembered.")]
	public async Task ResolveAsync_Should_Refuse_A_Browser_Path_That_Was_Not_Offered() {
		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync("existing", "/tmp/evil", CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().BeNull(because: "page-smoke starts only a browser it found");
		resolution.Code.Should().Be(PageSmokeBrowserResolver.NotFoundCode, because: "the path matches no offered browser");
		_settings.DidNotReceiveWithAnyArgs().SetPageSmokeBrowser(default);
	}

	[Test]
	[Description("browser=existing on a machine with no browser offers the download and no instead.")]
	public async Task ResolveAsync_Should_Offer_The_Download_When_No_Browser_Exists() {
		// Arrange
		_locator.FindInstalled().Returns([]);
		_cache.FindCached().Returns([]);

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync("existing", InstalledEdge, CancellationToken.None);

		// Assert
		resolution.Code.Should().Be(PageSmokeBrowserResolver.NotFoundCode, because: "there is nothing to use");
		resolution.Consent.Choices.Select(choice => choice.Browser).Should().Equal(["download", "none"],
			because: "the remaining choices are put to the user again");
	}

	[Test]
	[Description("browser=none is remembered, and later calls answer browser-declined without asking again.")]
	public async Task ResolveAsync_Should_Remember_No_And_Not_Ask_Again() {
		// Act
		PageSmokeBrowserResolution answered = await _sut.ResolveAsync("none", null, CancellationToken.None);
		Stored(PageSmokeBrowserSettings.None);
		PageSmokeBrowserResolution later = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		answered.Code.Should().Be(PageSmokeBrowserResolver.DeclinedCode, because: "the user said no");
		_settings.Received(1).SetPageSmokeBrowser(Arg.Is<PageSmokeBrowserSettings>(stored => stored.Choice == "none"));
		later.Code.Should().Be(PageSmokeBrowserResolver.DeclinedCode, because: "an explicit no is remembered too");
		later.Consent.Should().BeNull(because: "the question is not asked again");
	}

	[Test]
	[Description("browser=download downloads the pinned shell, remembers the choice, and asks for a second call so the download and the check never share one call's time.")]
	public async Task ResolveAsync_Should_Download_And_Ask_For_A_Second_Call() {
		// Arrange
		_installer.InstallAsync(Arg.Any<CancellationToken>()).Returns(DownloadedShell);

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync("download", null, CancellationToken.None);

		// Assert
		resolution.Code.Should().Be(PageSmokeBrowserResolver.ReadyCode, because: "the browser is ready for the next call");
		resolution.BrowserPath.Should().BeNull(because: "the check is not run in the same call as a 100 MB download");
		_settings.Received(1).SetPageSmokeBrowser(Arg.Is<PageSmokeBrowserSettings>(stored => stored.Choice == "download"));
	}

	[Test]
	[Description("A remembered download that is already installed is used at once.")]
	public async Task ResolveAsync_Should_Use_The_Downloaded_Shell() {
		// Arrange
		Stored(PageSmokeBrowserSettings.Download);
		_installer.FindInstalled().Returns(DownloadedShell);

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().Be(DownloadedShell, because: "the user already chose it");
		await _installer.DidNotReceiveWithAnyArgs().InstallAsync(default);
	}

	[Test]
	[Description("A failed download is answered as browser-download-failed with the installer's reason, and the choice is kept.")]
	public async Task ResolveAsync_Should_Report_A_Failed_Download() {
		// Arrange
		Stored(PageSmokeBrowserSettings.Download);
		_installer.InstallAsync(Arg.Any<CancellationToken>())
			.Returns<string>(_ => throw new HeadlessShellInstallException("offline"));

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.Code.Should().Be(PageSmokeBrowserResolver.DownloadFailedCode, because: "the download did not complete");
		resolution.Message.Should().StartWith("offline", because: "the installer's reason is kept");
		_settings.DidNotReceiveWithAnyArgs().SetPageSmokeBrowser(default);
	}

	[Test]
	[Description("A remembered browser is used while it exists, and the user is asked again once it is gone.")]
	public async Task ResolveAsync_Should_Ask_Again_When_The_Remembered_Browser_Is_Gone() {
		// Arrange
		Stored(PageSmokeBrowserSettings.Existing, InstalledEdge);
		_fileSystem.ExistsFile(InstalledEdge).Returns(true, false);

		// Act
		PageSmokeBrowserResolution present = await _sut.ResolveAsync(null, null, CancellationToken.None);
		PageSmokeBrowserResolution gone = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		present.BrowserPath.Should().Be(InstalledEdge, because: "the remembered browser is still there");
		gone.Code.Should().Be(PageSmokeBrowserResolver.ConsentRequiredCode, because: "another browser needs the user's answer");
		gone.Message.Should().Contain("no longer offered", because: "the user learns why they are asked again");
	}

	[Test]
	[Description("A remembered path that page-smoke does not offer (an edited setting) is never started; the user is asked again.")]
	public async Task ResolveAsync_Should_Not_Start_A_Remembered_Path_That_Is_Not_Offered() {
		// Arrange
		Stored(PageSmokeBrowserSettings.Existing, "/tmp/evil");
		_fileSystem.ExistsFile("/tmp/evil").Returns(true);

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().BeNull(because: "only an offered browser is ever started");
		resolution.Code.Should().Be(PageSmokeBrowserResolver.ConsentRequiredCode, because: "the user chooses again");
	}

	[Test]
	[Description("browser=download on a platform without a pinned build is refused and NOT remembered, so later calls do not fail for good.")]
	public async Task ResolveAsync_Should_Not_Remember_A_Download_That_Cannot_Happen() {
		// Arrange
		_installer.DownloadArchive.Returns((HeadlessShellRelease.Archive)null);

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync("download", null, CancellationToken.None);

		// Assert
		resolution.Code.Should().Be(PageSmokeBrowserResolver.NotFoundCode, because: "there is nothing to download here");
		resolution.Consent.Choices.Select(choice => choice.Browser).Should().NotContain("download",
			because: "a download is not offered where none exists");
		_settings.DidNotReceiveWithAnyArgs().SetPageSmokeBrowser(default);
		await _installer.DidNotReceiveWithAnyArgs().InstallAsync(default);
	}
	[Test]
	[Description("An answer the user gives on the call is honoured and remembered even when CHROME_PATH is set.")]
	public async Task ResolveAsync_Should_Honour_An_Explicit_Answer_Over_Chrome_Path() {
		// Arrange
		_locator.ChromePathOverride().Returns("/opt/custom/chrome");

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync("existing", InstalledEdge, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().Be(InstalledEdge, because: "the user's answer is not silently replaced");
		_settings.Received(1).SetPageSmokeBrowser(Arg.Is<PageSmokeBrowserSettings>(stored => stored.Path == InstalledEdge));
	}

	[Test]
	[Description("When CHROME_PATH replaces a remembered answer, the resolution says so, and the reply carries it as a warning.")]
	public async Task ResolveAsync_Should_Say_When_Chrome_Path_Replaced_A_Remembered_Answer() {
		// Arrange
		_locator.ChromePathOverride().Returns("/opt/custom/chrome");
		Stored(PageSmokeBrowserSettings.Existing, InstalledEdge);

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.BrowserPath.Should().Be("/opt/custom/chrome", because: "the operator's override applies");
		resolution.Note.Should().Contain("CHROME_PATH").And.Contain("remembered answer (existing)",
			because: "the agent and the user learn why their answer was not used");
	}

	[Test]
	[Description("A download that stopped part-way is answered browser-downloading with the progress, not as a failure.")]
	public async Task ResolveAsync_Should_Report_A_Partial_Download_As_Progress() {
		// Arrange
		Stored(PageSmokeBrowserSettings.Download);
		_installer.InstallAsync(Arg.Any<CancellationToken>()).Returns<string>(_ =>
			throw new HeadlessShellDownloadIncompleteException("Downloaded 40 of 98 MB", 40L << 20, 98L << 20));

		// Act
		PageSmokeBrowserResolution resolution = await _sut.ResolveAsync(null, null, CancellationToken.None);

		// Assert
		resolution.Code.Should().Be(PageSmokeBrowserResolver.DownloadingCode, because: "calling again resumes it");
		resolution.Download.Should().Be(new PageSmokeBrowserDownload(40L << 20, 98L << 20),
			because: "the agent can tell the user how far it got");
	}
}
