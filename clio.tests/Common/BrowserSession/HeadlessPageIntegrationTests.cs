using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using ClioFileSystem = Clio.Common.FileSystem;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103032: the headless launcher, the CDP session and the Freedom UI page scripts against a REAL Chromium and a
/// static page that reproduces the Freedom UI DOM contract. Ignored where no Chromium-based browser is installed.
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class HeadlessPageIntegrationTests {

	private const string FixtureHtml = """
		<!doctype html>
		<html><head><title>Fixture</title></head><body>
		<crt-root>
		  <crt-navigation-schema-outlet>
		    <crt-label element-name="PageTitle">Order 42</crt-label>
		    <crt-button element-name="SaveButton"><button>Save</button></crt-button>
		    <crt-button element-name="CloseButton"><button aria-label="Close"><mat-icon>close</mat-icon></button></crt-button>
		    <crt-button element-name="RunButton"><button>Run check</button></crt-button>
		    <crt-button element-name="HiddenButton" style="display:none"><button>Hidden</button></crt-button>
		    <crt-button element-name="ArchiveButton"><button disabled>Archive</button></crt-button>
		    <crt-input element-name="Name"><label>Name</label><input value="Order 1" required></crt-input>
		    <crt-checkbox element-name="Active"><label>Active</label><input type="checkbox" checked></crt-checkbox>
		    <crt-combobox element-name="Owner"><label>Owner</label><crt-input><input value="Supervisor" readonly></crt-input>
		      <crt-button element-name="OwnerClear"><button>x</button></crt-button></crt-combobox>
		    <crt-password-input element-name="Secret"><label>Secret</label><input type="password" value="p@ss"></crt-password-input>
		  </crt-navigation-schema-outlet>
		  <crt-mask><div class="crt-mask-wrap"><div class="crt-mask-container" style="display:none">loading</div></div></crt-mask>
		</crt-root>
		<script>
		  document.querySelector('[element-name=RunButton] button').addEventListener('click', () => {
		    location.hash = '#Card/UsrOrder_FormPage/edit/1';
		    console.error('handler failed: boom');
		    setTimeout(() => { throw new Error('async boom'); }, 10);
		  });
		</script>
		</body></html>
		""";

	private string _fixtureDirectory;
	private Uri _fixtureUrl;

	[SetUp]
	public void SetUp() {
		_fixtureDirectory = Path.Combine(Path.GetTempPath(), "clio-page-smoke-fixture-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_fixtureDirectory);
		string file = Path.Combine(_fixtureDirectory, "page.html");
		File.WriteAllText(file, FixtureHtml);
		_fixtureUrl = new Uri(file);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_fixtureDirectory)) {
			Directory.Delete(_fixtureDirectory, recursive: true);
		}
	}

	[Test]
	[Description("A real headless Chromium reports the fixture page ready, lists only its visible top-level buttons and fields, clicks a button, and the collector sees the console error, the uncaught exception and the hash navigation that followed.")]
	public async Task HeadlessPage_Should_Read_Click_And_Collect_On_A_Freedom_Ui_Shaped_Page() {
		// Arrange
		ClioFileSystem fileSystem = new(new System.IO.Abstractions.FileSystem());
		HeadlessBrowserLauncher launcher = CreateLauncher(fileSystem);
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
		HashSet<string> profilesBefore = Profiles();
		IHeadlessBrowser browser = await LaunchOrIgnoreAsync(launcher, timeout.Token);
		bool profileCreated = Profiles().Except(profilesBefore).Any();
		try {
			await using ICdpSession cdp = await new CdpSessionFactory().ConnectAsync(browser.PageWebSocketUrl, timeout.Token);
			PageEventCollector events = new(url => url);
			cdp.EventReceived += events.OnEvent;
			await cdp.SendAsync("Runtime.enable", null, timeout.Token);
			await cdp.SendAsync("Log.enable", null, timeout.Token);
			await cdp.SendAsync("Page.enable", null, timeout.Token);
			await cdp.SendAsync("Page.navigate", new { url = _fixtureUrl.AbsoluteUri }, timeout.Token);
			PageReadyState ready = await PollReadyAsync(cdp, timeout.Token);

			// Act
			PageInventory inventory = await EvaluateAsync<PageInventory>(cdp, FreedomUiPageScripts.InventoryScript, timeout.Token);
			int mark = events.Mark();
			PageClickOutcome missing = await EvaluateAsync<PageClickOutcome>(cdp, FreedomUiPageScripts.BuildClickScript("Nope"), timeout.Token);
			PageClickOutcome disabled = await EvaluateAsync<PageClickOutcome>(cdp, FreedomUiPageScripts.BuildClickScript("archive"), timeout.Token);
			PageClickOutcome clicked = await EvaluateAsync<PageClickOutcome>(cdp, FreedomUiPageScripts.BuildClickScript("run CHECK"), timeout.Token);
			await Task.Delay(500, timeout.Token);
			IReadOnlyList<PageConsoleError> errors = events.ErrorsSince(mark, 10, out _);
			IReadOnlyList<string> navigations = events.NavigationsSince(mark);

			// Assert
			ready.State.Should().Be("ready", because: "the fixture has crt-root, the outlet, named elements and a hidden mask");
			inventory.Title.Should().Be("Order 42", because: "the title is the PageTitle element");
			inventory.Scope.Should().Be("page", because: "the outlet exists");
			inventory.Buttons.Select(button => button.Name).Should().Equal(
				["SaveButton", "CloseButton", "RunButton", "ArchiveButton"],
				because: "a hidden button and a button inside a field component are not page buttons");
			inventory.Buttons.Single(button => button.Name == "CloseButton").Caption.Should().Be("Close",
				because: "an icon's ligature text is not a caption, so the aria-label is used");
			inventory.Buttons.Single(button => button.Name == "ArchiveButton").Disabled.Should().BeTrue(
				because: "its native button is disabled");
			inventory.Fields.Select(field => field.Name).Should().Equal(["Name", "Active", "Owner", "Secret"],
				because: "the input nested in the combobox belongs to the combobox");
			PageField name = inventory.Fields.Single(field => field.Name == "Name");
			name.Caption.Should().Be("Name", because: "the label is the caption");
			name.Value.Should().Be("Order 1", because: "the shown value is reported");
			name.Required.Should().BeTrue(because: "the input is required");
			inventory.Fields.Single(field => field.Name == "Active").Value.Should().Be("true",
				because: "a checkbox reports its checked state");
			inventory.Fields.Single(field => field.Name == "Owner").Readonly.Should().BeTrue(
				because: "the combobox's own input is read-only");
			inventory.Fields.Single(field => field.Name == "Secret").Value.Should().BeNull(
				because: "a password value is never reported");
			missing.Outcome.Should().Be("not-found", because: "no button has that caption or name");
			missing.Candidates.Should().NotBeEmpty(because: "the caller is shown what it could have meant");
			disabled.Outcome.Should().Be("disabled", because: "a disabled button is never clicked");
			clicked.Outcome.Should().Be("clicked", because: "the caption matches case-insensitively");
			clicked.Name.Should().Be("RunButton", because: "the clicked button is named");
			errors.Select(error => error.Kind).Should().Contain(["console", "exception"],
				because: "both the console.error and the uncaught exception after the click are collected");
			errors.Should().OnlyContain(error => error.Text.Contains("boom"), because: "the page's own messages are reported");
			navigations.Should().ContainSingle(url => url.EndsWith("#Card/UsrOrder_FormPage/edit/1"),
				because: "a hash change is a navigation within the document");
		}
		finally {
			await browser.DisposeAsync();
		}
		profileCreated.Should().BeTrue(because: "every launch gets a fresh temporary profile");
		Profiles().Except(profilesBefore).Should().BeEmpty(because: "disposing the browser kills it and deletes its profile");
	}

	private static HashSet<string> Profiles() =>
		Directory.GetDirectories(Path.GetTempPath(), HeadlessBrowserLauncher.ProfilePrefix + "*").ToHashSet();

	private static HeadlessBrowserLauncher CreateLauncher(ClioFileSystem fileSystem) {
		IHttpClientFactory httpClientFactory = new ServiceCollection().AddHttpClient().BuildServiceProvider()
			.GetRequiredService<IHttpClientFactory>();
		return new HeadlessBrowserLauncher(new ChromiumLocator(fileSystem), new ProcessExecutor(Substitute.For<ILogger>()),
			fileSystem, httpClientFactory);
	}

	private static async Task<IHeadlessBrowser> LaunchOrIgnoreAsync(HeadlessBrowserLauncher launcher, CancellationToken ct) {
		try {
			return await launcher.LaunchAsync(ct);
		}
		catch (ChromiumNotFoundException) {
			Assert.Ignore("No Chromium-based browser on this machine.");
			throw;
		}
	}

	private static async Task<PageReadyState> PollReadyAsync(ICdpSession cdp, CancellationToken ct) {
		PageReadyState state = null;
		for (int attempt = 0; attempt < 40; attempt++) {
			try {
				state = await EvaluateAsync<PageReadyState>(cdp, FreedomUiPageScripts.ReadyScript, ct);
				if (state.State == "ready") {
					return state;
				}
			}
			catch (CdpCommandException) {
				// The context is replaced while the page navigates.
			}
			await Task.Delay(250, ct);
		}
		return state;
	}

	private static async Task<T> EvaluateAsync<T>(ICdpSession cdp, string script, CancellationToken ct) {
		JsonElement answer = await cdp.SendAsync("Runtime.evaluate", new { expression = script, returnByValue = true }, ct);
		answer.TryGetProperty("exceptionDetails", out JsonElement details).Should().BeFalse(
			because: "the script must not throw: " + details);
		return FreedomUiPageScripts.Read<T>(answer.GetProperty("result").GetProperty("value"));
	}
}
