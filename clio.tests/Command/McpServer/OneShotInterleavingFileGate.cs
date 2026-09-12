using System;
using Clio.Command.McpServer.Tools;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Passthrough stand-in for the interprocess gate that models ANOTHER writer winning the gate in the
/// gap between two acquisitions of the same caller.
/// <para>
/// It is deliberately not the <see cref="Clio.Tests.Command.InterruptionObservingFileSystem"/> harness:
/// that one models a KILL (the state on disk between two filesystem operations of ONE writer), whereas
/// the hazard here is a second, complete publication landing while the first writer is between two
/// gated sections. Modelling it with a real second thread would need the real gate's blocking to be
/// reproduced by <c>MockFileSystem</c> share semantics and a sleep to order the two threads; running
/// the interleaving writer synchronously on gate release reproduces the same state transition
/// deterministically, cross-platform, with no sleep.
/// </para>
/// <para>
/// The interleaving writer fires ONCE, on the release of the FIRST top-level acquisition — the instant
/// the gap opens. Firing on every release would hide the defect: a second firing after the second
/// acquisition would overwrite the split state with a consistent one and the test would pass either way.
/// </para>
/// <para>
/// Nested acquisitions on the same thread (a gated read inside a gated read-modify-write) pass straight
/// through and are neither counted nor treated as the gap, exactly as the real gate admits them.
/// </para>
/// </summary>
internal sealed class OneShotInterleavingFileGate : IInterprocessFileGate {

	private readonly Action _interleavingWriter;
	private int _depth;
	private bool _fired;

	/// <summary>
	/// Initializes a new instance of the <see cref="OneShotInterleavingFileGate"/> class.
	/// </summary>
	/// <param name="interleavingWriter">
	/// The competing writer, run once when the first top-level acquisition is released.
	/// </param>
	internal OneShotInterleavingFileGate(Action interleavingWriter) {
		_interleavingWriter = interleavingWriter;
	}

	/// <summary>Number of NON-nested acquisitions taken, i.e. how many gaps the caller opened plus one.</summary>
	internal int TopLevelAcquisitions { get; private set; }

	/// <inheritdoc />
	public T Enter<T>(string lockFilePath, Func<T> action) {
		bool topLevel = _depth == 0;
		if (topLevel) {
			TopLevelAcquisitions++;
		}
		_depth++;
		T result;
		try {
			result = action();
		} finally {
			_depth--;
		}
		if (topLevel && !_fired) {
			_fired = true;
			_interleavingWriter();
		}
		return result;
	}

	/// <inheritdoc />
	public void Enter(string lockFilePath, Action action) =>
		Enter(lockFilePath, () => {
			action();
			return true;
		});
}
