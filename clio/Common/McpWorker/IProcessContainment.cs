using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.McpWorker;

/// <summary>
/// How a worker process was terminated, and — the load-bearing part — whether the operating system
/// guaranteed that its descendants went with it.
/// </summary>
/// <remarks>
/// This is reported rather than inferred because the two contained values and
/// <see cref="FallbackTreeKilled"/> differ in exactly the property the execution boundary depends on:
/// a contained kill takes the whole subtree by construction, while a tree kill is best effort and
/// cannot prove that an already reparented descendant exited (the same limitation
/// <see cref="ProcessExecutionResult.DescendantTerminationUncertain"/> documents for
/// <see cref="ProcessExecutor"/>).
/// </remarks>
public enum WorkerTerminationOutcome {

	/// <summary>The worker had already exited, so nothing was signalled.</summary>
	AlreadyExited,

	/// <summary>
	/// Unix: <c>kill(-pid, SIGKILL)</c> was delivered to the worker's own process group, which the
	/// worker had established for itself with <c>setpgid(0, 0)</c>. Every descendant it spawned after
	/// promotion inherited that group, so the whole subtree died with one signal.
	/// </summary>
	ContainedGroupKilled,

	/// <summary>
	/// Windows: the worker's job object was terminated. Job membership is kernel-enforced and
	/// inherited by descendants, so the whole subtree died with one call.
	/// </summary>
	ContainedJobTerminated,

	/// <summary>
	/// No containment was in force, so the immediate process tree was killed best effort. On Unix this
	/// is what a worker that has NOT yet promoted itself gets — see
	/// <see cref="IProcessContainment.TerminateOrphan"/> for why a group kill is refused in that state.
	/// Descendant termination is not guaranteed.
	/// </summary>
	FallbackTreeKilled,

	/// <summary>Termination was attempted and failed; the worker may still be running.</summary>
	Failed
}

/// <summary>
/// Everything needed to create one worker process, resolved: no lookup, no defaulting, no environment
/// inheritance decision is left to the containment implementation.
/// </summary>
/// <param name="Executable">Absolute path of the executable to run.</param>
/// <param name="Arguments">Argument vector, already unwrapped (never a single joined string).</param>
/// <param name="WorkingDirectory">Working directory for the child.</param>
/// <param name="Environment">
/// The COMPLETE environment for the child when <paramref name="ClearInheritedEnvironment"/> is
/// <see langword="true"/>; otherwise the set of additions and overrides on top of the parent's.
/// ADR rule 11 requires the enabled-tool generation and deadline contract handed to a worker to be
/// frozen, and a frozen payload is only frozen if the ambient environment cannot contradict it.
/// </param>
/// <param name="ClearInheritedEnvironment">
/// Whether the inherited environment is dropped before <paramref name="Environment"/> is applied.
/// </param>
public sealed record WorkerLaunchRequest(
	string Executable,
	IReadOnlyList<string> Arguments,
	string WorkingDirectory,
	IReadOnlyDictionary<string, string> Environment,
	bool ClearInheritedEnvironment);

/// <summary>
/// A started operating-system process the supervisor owns, reduced to the operations the containment
/// layer needs.
/// </summary>
/// <remarks>
/// This exists so that <see cref="System.Diagnostics.Process"/> is touched in exactly ONE class in
/// this feature (<see cref="WorkerProcessSupervisor"/>, which is exempt from <c>CLIO004</c> because it
/// implements <see cref="IProcessExecutor"/>). The containment implementations reach the child through
/// this interface and through platform system calls, and therefore never name a forbidden type. It is
/// also the seam a unit test substitutes to exercise the cap, the budget clock and the reaper without
/// creating real processes.
/// </remarks>
public interface IWorkerProcessHandle : IDisposable {

	/// <summary>Gets the operating-system process identifier.</summary>
	int ProcessId { get; }

	/// <summary>Gets the UTC start time of the process, used as half of its identity.</summary>
	DateTime StartTimeUtc { get; }

	/// <summary>Gets the absolute path of the running executable, the other half of its identity.</summary>
	string ExecutablePath { get; }

	/// <summary>Gets the writable end of the child's standard input.</summary>
	Stream StandardInput { get; }

	/// <summary>Gets the readable end of the child's standard output.</summary>
	Stream StandardOutput { get; }

	/// <summary>Gets the readable end of the child's standard error.</summary>
	Stream StandardError { get; }

	/// <summary>Gets a value indicating whether the process has exited.</summary>
	bool HasExited { get; }

	/// <summary>Gets the exit code once the process has exited, or <see langword="null"/> before that.</summary>
	int? ExitCode { get; }

	/// <summary>Waits for the process to exit.</summary>
	/// <param name="cancellationToken">Stops waiting; does not stop the process.</param>
	/// <returns>A task that completes when the process exits or the wait is cancelled.</returns>
	Task WaitForExitAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Kills the immediate process tree, best effort. Used only as the fallback when containment is
	/// not in force; it does not guarantee that reparented descendants exited.
	/// </summary>
	void KillProcessTree();
}

/// <summary>
/// A worker running inside its containment. Disposing it releases the containment, which on Windows
/// is itself lethal to the subtree (kill-on-close), and on Unix releases the handles only.
/// </summary>
public interface IContainedWorker : IDisposable {

	/// <summary>Gets the operating-system process identifier of the worker.</summary>
	int ProcessId { get; }

	/// <summary>Gets the UTC start time of the worker, used as half of its identity.</summary>
	DateTime StartTimeUtc { get; }

	/// <summary>Gets the absolute path of the worker executable, the other half of its identity.</summary>
	string ExecutablePath { get; }

	/// <summary>Gets the writable end of the worker's standard input.</summary>
	Stream StandardInput { get; }

	/// <summary>Gets the readable end of the worker's standard output.</summary>
	Stream StandardOutput { get; }

	/// <summary>Gets the readable end of the worker's standard error.</summary>
	Stream StandardError { get; }

	/// <summary>Gets a value indicating whether the worker has exited.</summary>
	bool HasExited { get; }

	/// <summary>Gets the worker's exit code once it has exited, or <see langword="null"/> before that.</summary>
	int? ExitCode { get; }

	/// <summary>Waits for the worker to exit.</summary>
	/// <param name="cancellationToken">Stops waiting; does not stop the worker.</param>
	/// <returns>A task that completes when the worker exits or the wait is cancelled.</returns>
	Task WaitForExitAsync(CancellationToken cancellationToken);

	/// <summary>Kills the worker together with every descendant its containment covers.</summary>
	/// <returns>What was actually signalled, and whether descendants were covered.</returns>
	WorkerTerminationOutcome Kill();
}

/// <summary>
/// Platform containment for worker processes: the mechanism that makes a worker's descendants die
/// with it, including when the parent is killed without running any cleanup.
/// </summary>
/// <remarks>
/// <para>
/// <b>Containment, not EOF</b> (ADR rule 6). Closing a redirected pipe is not containment — a child
/// that ignores end-of-stream survives it, and the Stage-0 prototype leaked exactly one orphan that
/// way. Nor is <c>Process.Kill(entireProcessTree: true)</c> containment on its own: it only helps
/// while the parent is alive to call it, and the guarantee being bought here is that the subtree dies
/// when the PARENT is <c>SIGKILL</c>ed and runs nothing at all.
/// </para>
/// <para>
/// <b>The two platforms buy that guarantee in different places, which is why this seam exists.</b>
/// On Windows the kernel enforces it: a job object created with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> collapses when its last handle closes, and a dying
/// parent closes handles unconditionally. On Unix nothing collapses by itself: the worker promotes
/// itself to a process-group leader and arms its own parent-death signalling (the worker-side half,
/// which ships with the worker mode), and the parent's contribution is to address the group instead
/// of the tree when it kills deliberately.
/// </para>
/// <para>
/// <b>Windows owns process creation, Unix does not</b> — see <see cref="OwnsProcessCreation"/>. That
/// asymmetry is measured, not stylistic (ADR §2.4).
/// </para>
/// </remarks>
public interface IProcessContainment {

	/// <summary>
	/// Gets a value indicating whether containment must create the process itself
	/// (<see cref="Launch"/>) rather than adopt one the supervisor started (<see cref="Adopt"/>).
	/// </summary>
	/// <remarks>
	/// <see langword="true"/> on Windows, and the reason is a measurement: assigning a child to the job
	/// AFTER it is running leaves a window in which the child can spawn a grandchild that is not in the
	/// job, and that grandchild SURVIVED the parent force-kill in the ADR §2.4 probe. The same probe
	/// with <c>CREATE_SUSPENDED</c> → <c>AssignProcessToJobObject</c> → <c>ResumeThread</c> killed the
	/// whole subtree. <c>Process.Start</c> cannot express <c>CREATE_SUSPENDED</c>, so the Windows path
	/// creates the process itself. "Start it, then assign it" is not an implementation detail — it
	/// leaks.
	/// <see langword="false"/> on Unix, where promotion happens inside the child and the parent has
	/// nothing to do at creation time.
	/// </remarks>
	bool OwnsProcessCreation { get; }

	/// <summary>
	/// Creates the worker already inside its containment. Valid only when
	/// <see cref="OwnsProcessCreation"/> is <see langword="true"/>.
	/// </summary>
	/// <param name="request">The resolved launch request.</param>
	/// <returns>The contained worker, with its redirected duplex streams.</returns>
	/// <exception cref="NotSupportedException">This containment does not create processes.</exception>
	IContainedWorker Launch(WorkerLaunchRequest request);

	/// <summary>
	/// Wraps a process the supervisor has already started so that later kills address its containment.
	/// Valid only when <see cref="OwnsProcessCreation"/> is <see langword="false"/>.
	/// </summary>
	/// <param name="startedProcess">The started process; ownership transfers to the returned worker.</param>
	/// <returns>The contained worker.</returns>
	/// <exception cref="NotSupportedException">This containment creates its own processes.</exception>
	IContainedWorker Adopt(IWorkerProcessHandle startedProcess);

	/// <summary>
	/// Terminates a worker left behind by a previous, now dead, parent — a process this parent never
	/// started and holds no containment handle for.
	/// </summary>
	/// <param name="orphan">
	/// A handle to the surviving process, whose identity the caller has ALREADY revalidated. This
	/// method does not re-check identity and must never be called on an unverified process id.
	/// </param>
	/// <returns>What was signalled, and whether descendants were covered.</returns>
	WorkerTerminationOutcome TerminateOrphan(IWorkerProcessHandle orphan);
}
