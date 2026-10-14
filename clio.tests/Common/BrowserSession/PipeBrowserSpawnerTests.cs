using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>ENG-103330: how the browser is given its DevTools pipe on each platform.</summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class PipeBrowserSpawnerTests {

	[Test]
	[Category("Unit")]
	[Description("The pipe switch goes before a trailing URL, and at the end when the last argument is itself a switch.")]
	public void IndexBeforeTrailingUrl_Should_Keep_A_Trailing_Url_Last() {
		// Arrange
		string[] withUrl = ["--headless=new", "about:blank"];
		string[] switchesOnly = ["--headless=new"];

		// Act
		int beforeUrl = PipeBrowserSpawner.IndexBeforeTrailingUrl(withUrl);
		int atEnd = PipeBrowserSpawner.IndexBeforeTrailingUrl(switchesOnly);
		int empty = PipeBrowserSpawner.IndexBeforeTrailingUrl([]);

		// Assert
		beforeUrl.Should().Be(1, because: "Chromium reads the trailing non-switch argument as the page to open");
		atEnd.Should().Be(1, because: "with no URL the switch is simply appended");
		empty.Should().Be(0, because: "an empty list takes the switch as its only argument");
	}

	[Test]
	[Category("Unit")]
	[Description("The scratch descriptors the pipe ends are staged through are above 4 and never one of the ends, so neither end is overwritten before it reaches 3 or 4.")]
	public void ScratchDescriptors_Should_Avoid_The_Reserved_Numbers_And_Both_Ends() {
		// Arrange
		(int Read, int Write)[] ends = [(3, 4), (4, 3), (5, 6), (6, 5), (5, 7), (40, 41)];

		// Act
		List<(int Read, int Write)> scratch = [];
		foreach ((int read, int write) in ends) {
			scratch.Add(UnixPipeSpawn.ScratchDescriptors(read, write));
		}

		// Assert
		for (int index = 0; index < ends.Length; index++) {
			(int read, int write) = ends[index];
			(int first, int second) = scratch[index];
			new[] { first, second }.Should().OnlyContain(fd => fd > 4 && fd != read && fd != write,
				because: $"staging ({read}, {write}) through 3, 4 or one of the ends would clobber an end before it is moved");
			first.Should().NotBe(second, because: "each end needs its own scratch descriptor");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The Windows command line round-trips through the CRT's argv rules: spaces, quotes and trailing backslashes are quoted, plain switches are not.")]
	public void BuildCommandLine_Should_Quote_Like_The_Crt_Splits() {
		// Arrange
		string executable = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
		string[] arguments = [@"--user-data-dir=C:\Temp\clio profile\", "--headless=new", "say \"hi\"", ""];

		// Act
		string commandLine = WindowsPipeSpawn.BuildCommandLine(executable, arguments);

		// Assert
		commandLine.Should().Be(
			"\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" "
			+ "\"--user-data-dir=C:\\Temp\\clio profile\\\\\" --headless=new \"say \\\"hi\\\"\" \"\"",
			because: "a path with a space must stay one argument, and the backslash before the closing quote is doubled");
	}

	[Test]
	[Category("Integration")]
	[Platform(Exclude = "Win", Reason = "The Unix contract (descriptors 3 and 4) is exercised here; Windows takes inherited handles, covered by the real-Chromium tests.")]
	[Description("posix_spawn gives the child the pipe on descriptors 3 (read) and 4 (write), nothing on the scratch descriptors, the pipe switch in its argv, and /dev/null as stdout; the child is reaped on disposal.")]
	public async Task Start_Should_Hand_The_Child_Its_Pipe_On_Descriptors_3_And_4() {
		// Arrange
		const string script = """
			for fd in 5 6 7 8 9; do if [ -e /dev/fd/$fd ]; then printf 'leaked %s;' "$fd" >&4; fi; done
			printf 'argv %s;' "$0" >&4
			if [ /dev/fd/1 -ef /dev/null ] && [ /dev/fd/2 -ef /dev/null ]; then printf 'quiet;' >&4; fi
			cat <&3 >&4
			""";
		PipeBrowserSpawner spawner = new();

		// Act
		using PipeBrowserProcess process = spawner.Start("/bin/sh", ["-c", script, "sh"]);
		await process.ToBrowser.WriteAsync(Encoding.ASCII.GetBytes("hello"));
		await process.ToBrowser.FlushAsync();
		process.ToBrowser.Dispose();
		string output = await ReadToEndAsync(process.FromBrowser);
		bool exited = process.WaitForExit(10_000);

		// Assert
		output.Should().Be($"argv {PipeBrowserSpawner.PipeSwitch};quiet;hello",
			because: "the child reads commands on 3, writes on 4, sees the pipe switch, has /dev/null for stdout and stderr (clio's stdout is the MCP stream), and inherits no scratch descriptor");
		exited.Should().BeTrue(because: "the child sees the end of its command pipe once clio closes it (no copy of clio's end leaked into it), ends, and is reaped by clio");
		process.StartedAtUtc.Should().NotBeNull(because: "the start time identifies the process for the kill");
	}

	private static async Task<string> ReadToEndAsync(Stream stream) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		using MemoryStream collected = new();
		byte[] buffer = new byte[256];
		while (true) {
			int read = await stream.ReadAsync(buffer, timeout.Token);
			if (read == 0) {
				return Encoding.ASCII.GetString(collected.ToArray());
			}
			collected.Write(buffer, 0, read);
		}
	}
}
