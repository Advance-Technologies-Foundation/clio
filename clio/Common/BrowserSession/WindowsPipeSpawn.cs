using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Starts a browser with CreateProcess so that it inherits exactly the two ends of a private DevTools pipe, named to it by
/// <c>--remote-debugging-io-pipes=&lt;read&gt;,&lt;write&gt;</c>, and nothing else of clio's.
/// </summary>
/// <remarks>
/// Chromium adopts those handles itself (content/browser/devtools/devtools_agent_host_impl.cc, AdoptPipes; Chromium 117+,
/// the contract ChromeDriver uses on Windows). Puppeteer and Playwright get the same pipe through Node's spawn instead,
/// which hands the child CRT file descriptors 3 and 4 in STARTUPINFO.lpReserved2; .NET has no such mechanism, so the
/// explicit switch is the supported path here.
/// </remarks>
internal static class WindowsPipeSpawn {

	internal const string IoPipesSwitch = "--remote-debugging-io-pipes=";
	private const int ExtendedStartupInfoPresent = 0x00080000;
	// A console browser build (chrome-headless-shell) would otherwise flash a console window.
	private const int CreateNoWindow = 0x08000000;
	private const int StartfUseStdHandles = 0x00000100;
	private static readonly IntPtr ProcThreadAttributeHandleList = new(0x00020002);
	private const uint WaitObject0 = 0;
	private const int HandleFlagInherit = 0x00000001;

	[SupportedOSPlatform("windows")]
	internal static PipeBrowserProcess Start(string executablePath, IReadOnlyList<string> arguments) {
		// Created non-inheritable: the browser's two ends are marked inheritable only for the CreateProcess call below and
		// closed in clio right after it, so a process clio starts on another thread meanwhile is very unlikely to receive
		// them, and the handle list keeps every other inheritable handle of clio's out of the browser.
		AnonymousPipeServerStream toBrowser = new(PipeDirection.Out, HandleInheritability.None);
		AnonymousPipeServerStream fromBrowser = new(PipeDirection.In, HandleInheritability.None);
		try {
			SafePipeHandle browserRead = toBrowser.ClientSafePipeHandle;
			SafePipeHandle browserWrite = fromBrowser.ClientSafePipeHandle;
			List<string> withHandles = [.. arguments];
			withHandles.Insert(PipeBrowserSpawner.IndexBeforeTrailingUrl(withHandles),
				IoPipesSwitch + HandleText(browserRead.DangerousGetHandle()) + "," + HandleText(browserWrite.DangerousGetHandle()));
			int pid;
			SafeProcessHandle process;
			try {
				MakeInheritable(browserRead);
				MakeInheritable(browserWrite);
				(pid, process) = CreateProcess(executablePath, withHandles,
					[browserRead.DangerousGetHandle(), browserWrite.DangerousGetHandle()]);
			}
			finally {
				toBrowser.DisposeLocalCopyOfClientHandle();
				fromBrowser.DisposeLocalCopyOfClientHandle();
			}
			// The process handle is held until release: while it is open, Windows never gives the id to another process.
			return new PipeBrowserProcess(pid, ProcessStartTime.Read(pid), fromBrowser, toBrowser,
				new HandleLifetime(process));
		}
		catch {
			toBrowser.Dispose();
			fromBrowser.Dispose();
			throw;
		}
	}

	private static void MakeInheritable(SafePipeHandle handle) {
		if (!SetHandleInformation(handle, HandleFlagInherit, HandleFlagInherit)) {
			throw Failure("SetHandleInformation");
		}
	}

	/// <summary>The browser through the process handle CreateProcess returned.</summary>
	private sealed class HandleLifetime(SafeProcessHandle process) : IPipeProcessLifetime {

		public bool WaitForExit(int milliseconds) =>
			WaitForSingleObject(process, (uint)Math.Max(0, milliseconds)) == WaitObject0;

		public void Terminate() => TerminateProcess(process, 1);

		public void Release() => process.Dispose();
	}

	internal static string HandleText(IntPtr handle) => handle.ToInt64().ToString(CultureInfo.InvariantCulture);

	private static (int Pid, SafeProcessHandle Process) CreateProcess(string executablePath, IReadOnlyList<string> arguments,
		IntPtr[] inherited) {
		IntPtr commandLine = Marshal.StringToHGlobalUni(BuildCommandLine(executablePath, arguments));
		IntPtr size = IntPtr.Zero;
		InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
		IntPtr attributeList = Marshal.AllocHGlobal(size);
		IntPtr handleList = Marshal.AllocHGlobal(IntPtr.Size * inherited.Length);
		bool attributeListReady = false;
		try {
			if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size)) {
				throw Failure("InitializeProcThreadAttributeList");
			}
			attributeListReady = true;
			for (int index = 0; index < inherited.Length; index++) {
				Marshal.WriteIntPtr(handleList, index * IntPtr.Size, inherited[index]);
			}
			if (!UpdateProcThreadAttribute(attributeList, 0, ProcThreadAttributeHandleList, handleList,
					new IntPtr(IntPtr.Size * inherited.Length), IntPtr.Zero, IntPtr.Zero)) {
				throw Failure("UpdateProcThreadAttribute");
			}
			StartupInfoEx startup = new() {
				StartupInfo = new StartupInfo {
					cb = Marshal.SizeOf<StartupInfoEx>(),
					// No standard handles at all: under the MCP server clio's stdout is the JSON-RPC stream, and a console
					// browser would otherwise be given its handle values.
					dwFlags = StartfUseStdHandles
				},
				lpAttributeList = attributeList
			};
			if (!CreateProcessW(IntPtr.Zero, commandLine, IntPtr.Zero, IntPtr.Zero, true,
					ExtendedStartupInfoPresent | CreateNoWindow, IntPtr.Zero, IntPtr.Zero, ref startup,
					out ProcessInformation information)) {
				throw Failure("CreateProcess");
			}
			CloseHandle(information.hThread);
			return (information.dwProcessId, new SafeProcessHandle(information.hProcess, ownsHandle: true));
		}
		finally {
			if (attributeListReady) {
				DeleteProcThreadAttributeList(attributeList);
			}
			Marshal.FreeHGlobal(handleList);
			Marshal.FreeHGlobal(attributeList);
			Marshal.FreeHGlobal(commandLine);
		}
	}

	/// <summary>The command line CreateProcess receives, quoted the way the CRT splits it back into the same argv.</summary>
	internal static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments) {
		StringBuilder builder = new();
		AppendArgument(builder, executablePath);
		foreach (string argument in arguments) {
			builder.Append(' ');
			AppendArgument(builder, argument);
		}
		return builder.ToString();
	}

	// The rules of dotnet/runtime's PasteArguments.AppendArgument: backslashes are literal unless they precede a quote,
	// so a run of them is doubled before a quote (and before the closing quote) and kept as is elsewhere.
	private static void AppendArgument(StringBuilder builder, string argument) {
		if (argument.Length != 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) {
			builder.Append(argument);
			return;
		}
		builder.Append('"');
		int index = 0;
		while (index < argument.Length) {
			char character = argument[index++];
			if (character == '\\') {
				int backslashes = 1;
				while (index < argument.Length && argument[index] == '\\') {
					index++;
					backslashes++;
				}
				if (index == argument.Length) {
					builder.Append('\\', backslashes * 2);
				}
				else if (argument[index] == '"') {
					builder.Append('\\', backslashes * 2 + 1).Append('"');
					index++;
				}
				else {
					builder.Append('\\', backslashes);
				}
				continue;
			}
			if (character == '"') {
				builder.Append('\\').Append('"');
				continue;
			}
			builder.Append(character);
		}
		builder.Append('"');
	}

	private static Win32Exception Failure(string function) {
		int error = Marshal.GetLastPInvokeError();
		return new Win32Exception(error, $"{function} failed: {new Win32Exception(error).Message}");
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct StartupInfo {
		public int cb;
		public IntPtr lpReserved;
		public IntPtr lpDesktop;
		public IntPtr lpTitle;
		public int dwX;
		public int dwY;
		public int dwXSize;
		public int dwYSize;
		public int dwXCountChars;
		public int dwYCountChars;
		public int dwFillAttribute;
		public int dwFlags;
		public short wShowWindow;
		public short cbReserved2;
		public IntPtr lpReserved2;
		public IntPtr hStdInput;
		public IntPtr hStdOutput;
		public IntPtr hStdError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct StartupInfoEx {
		public StartupInfo StartupInfo;
		public IntPtr lpAttributeList;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessInformation {
		public IntPtr hProcess;
		public IntPtr hThread;
		public int dwProcessId;
		public int dwThreadId;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CreateProcessW(IntPtr applicationName, IntPtr commandLine, IntPtr processAttributes,
		IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, int creationFlags,
		IntPtr environment, IntPtr currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation information);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags,
		ref IntPtr size);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, int flags, IntPtr attribute, IntPtr value,
		IntPtr size, IntPtr previousValue, IntPtr returnSize);

	[DllImport("kernel32.dll")]
	private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TerminateProcess(SafeProcessHandle handle, uint exitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetHandleInformation(SafePipeHandle handle, int mask, int flags);
}
