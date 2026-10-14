using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Starts a Chromium-based browser whose DevTools endpoint is a private pipe pair instead of a TCP port, so no other
/// process on the machine can attach to it.
/// </summary>
/// <remarks>
/// <see cref="System.Diagnostics.Process"/> cannot hand a child anything but stdin, stdout and stderr, so the browser is
/// started natively. Chromium's <c>--remote-debugging-pipe</c> contract differs per platform: on Unix it reads commands
/// from file descriptor 3 and writes replies to 4; on Windows it adopts the two inherited handles named by
/// <c>--remote-debugging-io-pipes=&lt;read&gt;,&lt;write&gt;</c> (Chromium 117 and later; ChromeDriver uses the same
/// contract). There is deliberately no fallback to <c>--remote-debugging-port</c>: an open port is what this avoids.
/// </remarks>
public interface IPipeBrowserSpawner {

	/// <summary>Starts the browser with its DevTools pipe connected to the returned streams.</summary>
	/// <param name="executablePath">The browser executable (an absolute path).</param>
	/// <param name="arguments">The browser's arguments; the spawner adds the pipe switches itself.</param>
	/// <returns>The started browser. Its standard input, output and error are the null device.</returns>
	/// <exception cref="PlatformNotSupportedException">The platform is neither Windows, macOS nor Linux.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The operating system refused to start the process.</exception>
	PipeBrowserProcess Start(string executablePath, IReadOnlyList<string> arguments);
}

/// <summary>A browser started by <see cref="IPipeBrowserSpawner"/>, and the two ends of its DevTools pipe.</summary>
/// <remarks>
/// Disposal closes nothing the caller still reads: the streams belong to whoever wraps them in a transport. It releases
/// the native process handle (Windows) or reaps the exited child (Unix), after <see cref="WaitForExit"/>.
/// </remarks>
public sealed class PipeBrowserProcess : IDisposable {

	private readonly Func<int, bool> _waitForExit;
	private readonly Action _release;
	private int _disposed;

	/// <summary>Wraps a started browser.</summary>
	/// <param name="processId">The browser's process id.</param>
	/// <param name="startedAtUtc">The browser's start time, or null when it could not be read.</param>
	/// <param name="fromBrowser">The read end of the pipe the browser writes its replies and events to.</param>
	/// <param name="toBrowser">The write end of the pipe the browser reads commands from.</param>
	/// <param name="waitForExit">Waits up to the given milliseconds for the process to exit; true once it has.</param>
	/// <param name="release">Frees the native resources held for the process.</param>
	public PipeBrowserProcess(int processId, DateTimeOffset? startedAtUtc, Stream fromBrowser, Stream toBrowser,
		Func<int, bool> waitForExit, Action release) {
		ProcessId = processId;
		StartedAtUtc = startedAtUtc;
		FromBrowser = fromBrowser ?? throw new ArgumentNullException(nameof(fromBrowser));
		ToBrowser = toBrowser ?? throw new ArgumentNullException(nameof(toBrowser));
		_waitForExit = waitForExit ?? (_ => true);
		_release = release ?? (() => { });
	}

	/// <summary>The browser's process id.</summary>
	public int ProcessId { get; }

	/// <summary>The browser's start time, which with <see cref="ProcessId"/> identifies it; null when unreadable.</summary>
	public DateTimeOffset? StartedAtUtc { get; }

	/// <summary>The stream the browser's replies and events arrive on.</summary>
	public Stream FromBrowser { get; }

	/// <summary>The stream commands are written to.</summary>
	public Stream ToBrowser { get; }

	/// <summary>Waits for the browser to exit.</summary>
	/// <param name="milliseconds">The longest wait.</param>
	/// <returns>True once the process has exited.</returns>
	public bool WaitForExit(int milliseconds) => Volatile.Read(ref _disposed) == 1 || _waitForExit(milliseconds);

	/// <inheritdoc />
	public void Dispose() {
		if (Interlocked.Exchange(ref _disposed, 1) == 0) {
			_release();
		}
	}
}

/// <inheritdoc cref="IPipeBrowserSpawner" />
public sealed class PipeBrowserSpawner : IPipeBrowserSpawner {

	internal const string PipeSwitch = "--remote-debugging-pipe";

	/// <inheritdoc />
	public PipeBrowserProcess Start(string executablePath, IReadOnlyList<string> arguments) {
		ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
		ArgumentNullException.ThrowIfNull(arguments);
		List<string> withPipe = [.. arguments];
		// The pipe switch goes before a trailing URL, like any other switch.
		withPipe.Insert(IndexBeforeTrailingUrl(withPipe), PipeSwitch);
		if (OperatingSystem.IsWindows()) {
			return WindowsPipeSpawn.Start(executablePath, withPipe);
		}
		if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) {
			return UnixPipeSpawn.Start(executablePath, withPipe);
		}
		throw new PlatformNotSupportedException(
			$"A browser DevTools pipe is not supported on {RuntimeInformation.OSDescription}.");
	}

	internal static int IndexBeforeTrailingUrl(IReadOnlyList<string> arguments) =>
		arguments.Count > 0 && !arguments[^1].StartsWith('-') ? arguments.Count - 1 : arguments.Count;
}

/// <summary>Reads a process's start time, which with its id tells it apart from a later process that reused the id.</summary>
internal static class ProcessStartTime {

	/// <summary>The start time of process <paramref name="processId"/>, or null when it has exited or is inaccessible.</summary>
	internal static DateTimeOffset? Read(int processId) {
		try {
			// Direct use of System.Diagnostics.Process is intentional: the browser is started natively (IProcessExecutor
			// cannot pass the DevTools pipe), and only its start time is read here.
#pragma warning disable CLIO004
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
			return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
#pragma warning restore CLIO004
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
			or System.ComponentModel.Win32Exception or NotSupportedException) {
			return null;
		}
	}
}
