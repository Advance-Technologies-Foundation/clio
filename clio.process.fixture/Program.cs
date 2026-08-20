using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Clio.ProcessFixture;

const string holdPipesArgument = "--hold-inherited-pipes";
const string spawnDescendantArgument = "--spawn-inherited-handle-descendant";
const string selfPromotingWorkerArgument = "--self-promote-and-spawn-descendant";
const string spawnSelfPromotingWorkerArgument = "--spawn-self-promoting-worker";
const string growDirectoryArgument = "--grow-directory-with-inherited-pipes";
const string spawnGrowingDescendantArgument = "--spawn-growing-inherited-handle-descendant";
const string overflowOutputArgument = "--overflow-output-with-inherited-handle-descendant";
const string carriageReturnOutputArgument = "--write-carriage-return-output";
const string reportWorkingDirectoryArgument = "--report-working-directory";
const string invocationMarkerFileName = "invoked.marker";
const string descendantIdentityFileName = "descendant.identity";

if (IsCommand(args, holdPipesArgument, 1)) {
	await Task.Delay(TimeSpan.FromSeconds(30));
	return 0;
}

if (IsCommand(args, growDirectoryArgument, 2)) {
	await Task.Delay(TimeSpan.FromMilliseconds(300));
	Directory.CreateDirectory(args[1]);
	await File.WriteAllBytesAsync(Path.Combine(args[1], "late-growth.bin"), new byte[4096]);
	await Task.Delay(TimeSpan.FromSeconds(30));
	return 0;
}

// Stands in for a contained MCP worker (ENG-95262 Stage 2). It does, in this order, exactly what a real
// worker does at startup: promote itself to its own process-group leader, arm parent-death signalling,
// and only then spawn a descendant. The descendant is spawned as the FIRST observable act so a Windows
// containment that assigned the job AFTER the process was already running would leak it, and the test
// would see that leak instead of passing around it.
if (IsCommand(args, selfPromotingWorkerArgument, 3)) {
	PromoteToOwnProcessGroup();
	ArmParentDeathContainment();
	using Process contained = StartPipeHoldingDescendant();
	await WriteProcessIdentityAsync(args[1], Process.GetCurrentProcess());
	await WriteProcessIdentityAsync(args[2], contained);
	await WriteOutputAsync("worker-ready");
	await Task.Delay(TimeSpan.FromSeconds(60));
	return 0;
}

// The intermediate parent for the parent-death case: it starts a self-promoting worker and then does
// nothing, so a test can force-kill it and watch whether the worker and the worker's own descendant go
// with it. It is deliberately dumb — the containment being proven lives in the worker, not here.
if (IsCommand(args, spawnSelfPromotingWorkerArgument, 3)) {
	using Process worker = StartPipeHoldingDescendant(selfPromotingWorkerArgument, args[1], args[2]);
	await WriteOutputAsync("intermediate-ready");
	await Task.Delay(TimeSpan.FromSeconds(60));
	return 0;
}

if (IsCommand(args, spawnDescendantArgument, 2)) {
	using Process descendant = StartPipeHoldingDescendant();
	await WriteProcessIdentityAsync(args[1], descendant);
	await WriteOutputAsync("parent-exited");
	return 0;
}

if (IsCommand(args, spawnGrowingDescendantArgument, 3)) {
	using Process descendant = StartPipeHoldingDescendant(growDirectoryArgument, args[2]);
	await WriteProcessIdentityAsync(args[1], descendant);
	await WriteOutputAsync("parent-exited");
	return 0;
}

if (IsCommand(args, overflowOutputArgument, 2)) {
	using Process descendant = StartPipeHoldingDescendant();
	await WriteProcessIdentityAsync(args[1], descendant);
	await WriteOutputAsync(new string('x', 8192));
	return 0;
}

// Reports the directory the process was actually STARTED in, and exits immediately. It exists because
// a spawn request's stated working directory and a child's real one are different claims: the worker
// execution boundary hands children the host's directory, and everything a tool anchors on "here"
// (`.clio-pages/{schema}/` above all) lands wherever this reports.
if (IsCommand(args, reportWorkingDirectoryArgument, 2)) {
	await File.WriteAllTextAsync(args[1], Environment.CurrentDirectory);
	return 0;
}

if (IsCommand(args, carriageReturnOutputArgument, 1)) {
	await WriteOutputAsync("first\r");
	await Task.Delay(TimeSpan.FromSeconds(1));
	await WriteOutputAsync("second");
	return 0;
}

string fixtureDirectory = AppContext.BaseDirectory;
await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, invocationMarkerFileName),
	string.Join(' ', args));
using Process gitDescendant = StartPipeHoldingDescendant();
await WriteProcessIdentityAsync(Path.Combine(fixtureDirectory, descendantIdentityFileName), gitDescendant);
await WriteOutputAsync("fake-git-parent-exited");
return 1;

// One guard for every dispatch branch below: the fixture's contract is "verb plus exactly N arguments",
// and spelling that out inline eight times is what pushed the top-level file over the complexity budget.
static bool IsCommand(string[] commandArguments, string verb, int expectedLength) =>
	commandArguments.Length == expectedLength
	&& string.Equals(commandArguments[0], verb, StringComparison.Ordinal);

static Process StartPipeHoldingDescendant(params string[] arguments) {
	ProcessStartInfo descendantStartInfo = new(GetCurrentExecutablePath()) {
		UseShellExecute = false,
		CreateNoWindow = true
	};
	if (arguments.Length == 0) {
		descendantStartInfo.ArgumentList.Add(holdPipesArgument);
	} else {
		foreach (string argument in arguments) {
			descendantStartInfo.ArgumentList.Add(argument);
		}
	}
	return Process.Start(descendantStartInfo)
		?? throw new InvalidOperationException("The inherited-pipe fixture descendant did not start.");
}

static async Task WriteProcessIdentityAsync(string path, Process process) {
	ProcessIdentity identity = new(process.Id, process.StartTime.ToUniversalTime().Ticks,
		Path.GetFullPath(GetCurrentExecutablePath()));
	await File.WriteAllTextAsync(path, JsonSerializer.Serialize(identity));
}

static async Task WriteOutputAsync(string output) {
	await Console.Out.WriteAsync(output);
	await Console.Out.FlushAsync();
}

static string GetCurrentExecutablePath() => Environment.ProcessPath
	?? throw new InvalidOperationException("The fixture executable path is unavailable.");

// setpgid(0, 0) makes this process its own group leader, so every descendant it starts from now on
// inherits that group and one kill(-pid) reaches all of them. Without it the process would still be in
// the LAUNCHING shell's group, and a group kill would hit the shell and its siblings.
static void PromoteToOwnProcessGroup() {
	if (OperatingSystem.IsWindows()) {
		return;
	}
	NativeMethods.setpgid(0, 0);
}

// Parent-death containment, the half a parent cannot install for itself: a SIGKILLed parent runs no
// code at all. getppid is polled rather than using PR_SET_PDEATHSIG because that call does not exist on
// macOS, and the comparison is against the ORIGINAL parent rather than against 1, because on Linux a
// reparented orphan is adopted by the nearest subreaper (a container init, a service manager) and never
// reaches process 1. The reaction is a group kill, not a self-kill: this process has children of its
// own, and "both disappear" is the requirement.
static void ArmParentDeathContainment() {
	if (OperatingSystem.IsWindows()) {
		return;
	}
	int originalParent = NativeMethods.getppid();
	Thread watcher = new(() => {
		while (NativeMethods.getppid() == originalParent) {
			Thread.Sleep(100);
		}
		NativeMethods.killpg(0, NativeMethods.SignalKill);
	}) {
		IsBackground = true
	};
	watcher.Start();
}

namespace Clio.ProcessFixture {

	internal static class NativeMethods {

		internal const int SignalKill = 9;

		[DllImport("libc", SetLastError = true)]
		internal static extern int setpgid(int pid, int pgid);

		[DllImport("libc", SetLastError = true)]
		internal static extern int getppid();

		[DllImport("libc", SetLastError = true)]
		internal static extern int killpg(int pgrp, int sig);
	}
}
