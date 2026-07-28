using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClioRing.Services;

/// <summary>Windows-aware inspection of trusted clio global-tool processes.</summary>
public sealed partial class ClioToolProcessInspector : IClioToolProcessInspector {
	private const int ProcessBasicInformation = 0;
	private const int ProcessCommandLineInformation = 60;
	private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
	private const int MaximumCommandLineBytes = 64 * 1024;
	private const int ErrorMoreData = 234;
	private const int RmProcessInfoSize = 668;
	private const uint MaximumRestartManagerProcesses = 4096;

	/// <inheritdoc />
	public IReadOnlyList<ClioToolProcess> FindLockingTrustedProcesses(string trustedExecutablePath) {
		string canonicalPath = Path.GetFullPath(trustedExecutablePath);
		IReadOnlySet<(int ProcessId, long StartTimeUtcTicks)> lockers = FindRestartManagerLockers(canonicalPath);
		if (lockers.Count == 0) {
			return Array.Empty<ClioToolProcess>();
		}
		var matches = new List<ClioToolProcess>();
		foreach (Process process in Process.GetProcessesByName("clio")) {
			using (process) {
				try {
					string? path = process.MainModule?.FileName;
					if (path is null || !PathsEqual(path, canonicalPath)) {
						continue;
					}
					string commandLine = TryReadCommandLine(process.Handle) ?? string.Empty;
					long startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
					if (!lockers.Contains((process.Id, startTimeUtcTicks))) {
						continue;
					}
					matches.Add(new ClioToolProcess(process.Id, startTimeUtcTicks,
						Path.GetFullPath(path), ClassifyCommand(commandLine), DescribeParent(process.Handle)));
				}
				catch (Exception exception) when (IsRecoverableInspectionFailure(exception)) {
					// A process can exit or become inaccessible between enumeration and inspection.
				}
			}
		}
		return matches;
	}

	private static IReadOnlySet<(int ProcessId, long StartTimeUtcTicks)> FindRestartManagerLockers(
		string trustedExecutablePath) {
		var result = new HashSet<(int, long)>();
		if (!OperatingSystem.IsWindows()) {
			return result;
		}
		nint sessionKey = Marshal.AllocHGlobal(33 * sizeof(char));
		uint session;
		int startStatus = RmStartSession(out session, 0, sessionKey);
		Marshal.FreeHGlobal(sessionKey);
		if (startStatus != 0) { return result; }
		nint resourceString = nint.Zero;
		nint resourceArray = nint.Zero;
		try {
			resourceString = Marshal.StringToHGlobalUni(trustedExecutablePath);
			resourceArray = Marshal.AllocHGlobal(nint.Size);
			Marshal.WriteIntPtr(resourceArray, resourceString);
			if (RmRegisterResources(session, 1, resourceArray, 0, nint.Zero, 0, nint.Zero) != 0) {
				return result;
			}
			uint count = 0;
			uint needed;
			uint rebootReasons = 0;
			int status = RmGetList(session, out needed, ref count, nint.Zero, ref rebootReasons);
			if (status != ErrorMoreData || needed == 0) {
				return result;
			}
			for (int attempt = 0; attempt < 3 && needed <= MaximumRestartManagerProcesses; attempt++) {
				count = needed;
				nint processInfo = Marshal.AllocHGlobal(checked((int)needed * RmProcessInfoSize));
				try {
					status = RmGetList(session, out uint refreshedNeeded, ref count, processInfo,
						ref rebootReasons);
					if (status == ErrorMoreData && refreshedNeeded > count) {
						needed = refreshedNeeded;
						continue;
					}
					if (status != 0) { return result; }
					for (int index = 0; index < count; index++) {
						nint entry = processInfo + checked(index * RmProcessInfoSize);
						int processId = Marshal.ReadInt32(entry);
						uint low = unchecked((uint)Marshal.ReadInt32(entry, 4));
						uint high = unchecked((uint)Marshal.ReadInt32(entry, 8));
						long fileTime = unchecked((long)(((ulong)high << 32) | low));
						try { result.Add((processId, DateTime.FromFileTimeUtc(fileTime).Ticks)); }
						catch (ArgumentOutOfRangeException) { }
					}
					break;
				}
				finally {
					Marshal.FreeHGlobal(processInfo);
				}
			}
			return result;
		}
		finally {
			if (resourceArray != nint.Zero) { Marshal.FreeHGlobal(resourceArray); }
			if (resourceString != nint.Zero) { Marshal.FreeHGlobal(resourceString); }
			RmEndSession(session);
		}
	}

	/// <inheritdoc />
	public async Task<bool> TerminateRevalidatedAsync(ClioToolProcess processSnapshot,
		CancellationToken cancellationToken) {
		try {
			using Process process = Process.GetProcessById(processSnapshot.ProcessId);
			string? path = process.MainModule?.FileName;
			long startTime = process.StartTime.ToUniversalTime().Ticks;
			if (path is null || !PathsEqual(path, processSnapshot.ExecutablePath)
				|| startTime != processSnapshot.StartTimeUtcTicks) {
				return false;
			}
			process.Kill(entireProcessTree: false);
			using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			waitSource.CancelAfter(TimeSpan.FromSeconds(5));
			await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
			return true;
		}
		catch (Exception exception) when (IsRecoverableTerminationFailure(exception)) {
			return false;
		}
	}

	internal static string ClassifyCommand(string commandLine) {
		return commandLine.Contains("mcp-server", StringComparison.OrdinalIgnoreCase)
			? "clio mcp-server"
			: "clio process";
	}

	private static string DescribeParent(nint processHandle) {
		if (!OperatingSystem.IsWindows()) {
			return "Started by another application";
		}
		var info = new ProcessBasicInformationData();
		int status = NtQueryInformationProcess(processHandle, ProcessBasicInformation, ref info,
			Marshal.SizeOf<ProcessBasicInformationData>(), out _);
		if (status != 0 || info.InheritedFromUniqueProcessId == nint.Zero) {
			return "Started by another application";
		}
		int parentId = checked((int)info.InheritedFromUniqueProcessId);
		try {
			using Process parent = Process.GetProcessById(parentId);
			string displayName = parent.ProcessName switch {
				"claude" => "Claude Code",
				"codex" => "Codex",
				_ => parent.ProcessName
			};
			return $"Started by {displayName} - {parent.ProcessName}.exe (PID {parentId})";
		}
		catch (Exception exception) when (IsRecoverableInspectionFailure(exception)) {
			return $"Started by process PID {parentId}";
		}
	}

	private static string? TryReadCommandLine(nint processHandle) {
		if (!OperatingSystem.IsWindows()) {
			return null;
		}
		int status = NtQueryInformationProcess(processHandle, ProcessCommandLineInformation,
			nint.Zero, 0, out int requiredLength);
		if (status != StatusInfoLengthMismatch || requiredLength <= 0
			|| requiredLength > MaximumCommandLineBytes) {
			return null;
		}
		nint buffer = Marshal.AllocHGlobal(requiredLength);
		try {
			status = NtQueryInformationProcess(processHandle, ProcessCommandLineInformation,
				buffer, requiredLength, out _);
			if (status != 0) {
				return null;
			}
			ushort byteLength = unchecked((ushort)Marshal.ReadInt16(buffer));
			int pointerOffset = nint.Size == 8 ? 8 : 4;
			nint textPointer = Marshal.ReadIntPtr(buffer, pointerOffset);
			return byteLength == 0 || textPointer == nint.Zero
				? string.Empty
				: Marshal.PtrToStringUni(textPointer, byteLength / sizeof(char));
		}
		finally {
			Marshal.FreeHGlobal(buffer);
		}
	}

	private static bool PathsEqual(string left, string right) =>
		string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

	private static bool IsRecoverableInspectionFailure(Exception exception) =>
		exception is ArgumentException or InvalidOperationException or Win32Exception
			or NotSupportedException or UnauthorizedAccessException;

	private static bool IsRecoverableTerminationFailure(Exception exception) =>
		IsRecoverableInspectionFailure(exception) || exception is OperationCanceledException;

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessBasicInformationData {
		public nint Reserved1;
		public nint PebBaseAddress;
		public nint Reserved2_0;
		public nint Reserved2_1;
		public nint UniqueProcessId;
		public nint InheritedFromUniqueProcessId;
	}

	[LibraryImport("ntdll.dll")]
	private static partial int NtQueryInformationProcess(nint processHandle, int processInformationClass,
		ref ProcessBasicInformationData processInformation, int processInformationLength,
		out int returnLength);

	[LibraryImport("ntdll.dll")]
	private static partial int NtQueryInformationProcess(nint processHandle, int processInformationClass,
		nint processInformation, int processInformationLength, out int returnLength);

	[LibraryImport("rstrtmgr.dll")]
	private static partial int RmStartSession(out uint sessionHandle, int sessionFlags,
		nint sessionKey);

	[LibraryImport("rstrtmgr.dll")]
	private static partial int RmRegisterResources(uint sessionHandle, uint fileCount, nint fileNames,
		uint applicationCount, nint applications, uint serviceCount, nint serviceNames);

	[LibraryImport("rstrtmgr.dll")]
	private static partial int RmGetList(uint sessionHandle, out uint processInfoNeeded,
		ref uint processInfoCount, nint affectedApplications, ref uint rebootReasons);

	[LibraryImport("rstrtmgr.dll")]
	private static partial int RmEndSession(uint sessionHandle);
}
