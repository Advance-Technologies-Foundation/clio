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

/// <summary>The native side of a browser clio started itself: waiting for it, ending it, and letting it go.</summary>
/// <remarks>
/// clio is the browser's parent and keeps it identifiable until <see cref="Release"/>: on Unix the exited child is not
/// reaped before then, and on Windows the process handle stays open, so in neither case can its id name another process.
/// </remarks>
public interface IPipeProcessLifetime {

	/// <summary>Waits for the process to exit.</summary>
	/// <param name="milliseconds">The longest wait.</param>
	/// <returns>True once the process has exited.</returns>
	bool WaitForExit(int milliseconds);

	/// <summary>Ends the process at once (SIGKILL, TerminateProcess) if it is still running.</summary>
	void Terminate();

	/// <summary>Frees what is held for the process: reaps it on Unix (now, or whenever it exits), closes its handle on Windows.</summary>
	void Release();
}

/// <summary>A browser started by <see cref="IPipeBrowserSpawner"/>, and the two ends of its DevTools pipe.</summary>
/// <remarks>
/// Disposal closes nothing the caller still reads: the streams belong to whoever wraps them in a transport. It releases
/// the process through its <see cref="IPipeProcessLifetime"/>.
/// </remarks>
/// <param name="processId">The browser's process id.</param>
/// <param name="startedAtUtc">The browser's start time, or null when it could not be read.</param>
/// <param name="fromBrowser">The read end of the pipe the browser writes its replies and events to.</param>
/// <param name="toBrowser">The write end of the pipe the browser reads commands from.</param>
/// <param name="lifetime">Waits for, ends and releases the process.</param>
public sealed class PipeBrowserProcess(int processId, DateTimeOffset? startedAtUtc, Stream fromBrowser, Stream toBrowser,
	IPipeProcessLifetime lifetime) : IDisposable {

	private readonly IPipeProcessLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
	private int _disposed;

	/// <summary>The browser's process id.</summary>
	public int ProcessId { get; } = processId;

	/// <summary>The browser's start time, which with <see cref="ProcessId"/> identifies it; null when unreadable.</summary>
	public DateTimeOffset? StartedAtUtc { get; } = startedAtUtc;

	/// <summary>The stream the browser's replies and events arrive on.</summary>
	public Stream FromBrowser { get; } = fromBrowser ?? throw new ArgumentNullException(nameof(fromBrowser));

	/// <summary>The stream commands are written to.</summary>
	public Stream ToBrowser { get; } = toBrowser ?? throw new ArgumentNullException(nameof(toBrowser));

	/// <inheritdoc cref="IPipeProcessLifetime.WaitForExit" />
	public bool WaitForExit(int milliseconds) => Volatile.Read(ref _disposed) == 1 || _lifetime.WaitForExit(milliseconds);

	/// <inheritdoc cref="IPipeProcessLifetime.Terminate" />
	public void Terminate() {
		if (Volatile.Read(ref _disposed) == 0) {
			_lifetime.Terminate();
		}
	}

	/// <inheritdoc />
	public void Dispose() {
		if (Interlocked.Exchange(ref _disposed, 1) == 0) {
			_lifetime.Release();
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
