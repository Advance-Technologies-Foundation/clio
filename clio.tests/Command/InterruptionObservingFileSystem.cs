using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;

namespace Clio.Tests.Command;

/// <summary>
/// A file system that behaves exactly like the <see cref="MockFileSystem"/> it wraps, but calls back
/// after every state-changing operation and records every path content was written through.
/// <para>
/// It exists to model the ONE bound this build gives a worker: the parent KILLS it — on Windows
/// <c>TerminateJobObject</c> over the job, on Unix <c>kill(-pid, SIGKILL)</c> to the process group. No
/// <c>finally</c> runs, so the state a kill leaves is simply the state on disk at the instant between
/// two filesystem operations. Modelling that with a thrown exception would be wrong twice over: the
/// production code catches it, and the cleanup a kill skips would still run. Snapshotting BETWEEN
/// operations reproduces it exactly, with no real process kill and no OS-specific behaviour.
/// </para>
/// <para>
/// The callback must only RECORD. Asserting inside it would be caught by the production code's own
/// <c>catch (Exception)</c> and silently turned into an error response.
/// </para>
/// <para>
/// Only the members the systems under test actually call are forwarded; everything else keeps the
/// substitute default, so a newly-introduced call shows up as an obvious test failure rather than a
/// silent divergence from the mock's state.
/// </para>
/// </summary>
internal sealed class InterruptionObservingFileSystem {

	private static readonly string[] PublishedPageFileNames = ["body.js", "bundle.json", "meta.json"];

	private readonly MockFileSystem _inner;
	private readonly Action _onMutation;
	private readonly List<string> _contentWriteTargets = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="InterruptionObservingFileSystem"/> class.
	/// </summary>
	/// <param name="inner">The mock file system that holds the real state.</param>
	/// <param name="onMutation">Called after every state-changing operation; must only record.</param>
	internal InterruptionObservingFileSystem(MockFileSystem inner, Action onMutation) {
		_inner = inner;
		_onMutation = onMutation;
		FileSystem = BuildObservingFileSystem();
	}

	/// <summary>The observing file system to hand to the system under test.</summary>
	internal IFileSystem FileSystem { get; }

	/// <summary>Every path this run opened for writing or wrote text to, in call order.</summary>
	internal IReadOnlyList<string> ContentWriteTargets => _contentWriteTargets;

	/// <summary>The file names a completed <c>.clio-pages/{schema}/</c> publication must contain.</summary>
	internal static IReadOnlyList<string> PublishedPageFiles => PublishedPageFileNames;

	private IFileSystem BuildObservingFileSystem() {
		IDirectory directory = Substitute.For<IDirectory>();
		directory.GetCurrentDirectory().Returns(_ => _inner.Directory.GetCurrentDirectory());
		directory.Exists(Arg.Any<string>()).Returns(call => _inner.Directory.Exists(call.Arg<string>()));
		directory.GetDirectories(Arg.Any<string>()).Returns(call => _inner.Directory.GetDirectories(call.Arg<string>()));
		directory.GetFiles(Arg.Any<string>()).Returns(call => _inner.Directory.GetFiles(call.Arg<string>()));
		directory.CreateDirectory(Arg.Any<string>()).Returns(call => {
			IDirectoryInfo created = _inner.Directory.CreateDirectory(call.Arg<string>());
			_onMutation();
			return created;
		});
		directory.When(d => d.Delete(Arg.Any<string>(), Arg.Any<bool>())).Do(call => {
			_inner.Directory.Delete(call.ArgAt<string>(0), call.ArgAt<bool>(1));
			_onMutation();
		});
		directory.When(d => d.Move(Arg.Any<string>(), Arg.Any<string>())).Do(call => {
			_inner.Directory.Move(call.ArgAt<string>(0), call.ArgAt<string>(1));
			_onMutation();
		});

		IFile file = Substitute.For<IFile>();
		file.Exists(Arg.Any<string>()).Returns(call => _inner.File.Exists(call.Arg<string>()));
		file.ReadAllText(Arg.Any<string>()).Returns(call => _inner.File.ReadAllText(call.Arg<string>()));
		file.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Do(call => {
			_contentWriteTargets.Add(call.ArgAt<string>(0));
			_inner.File.WriteAllText(call.ArgAt<string>(0), call.ArgAt<string>(1));
			_onMutation();
		});
		file.When(f => f.Move(Arg.Any<string>(), Arg.Any<string>())).Do(call => {
			_inner.File.Move(call.ArgAt<string>(0), call.ArgAt<string>(1));
			_onMutation();
		});
		file.When(f => f.Move(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())).Do(call => {
			_inner.File.Move(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<bool>(2));
			_onMutation();
		});
		file.When(f => f.Delete(Arg.Any<string>())).Do(call => {
			_inner.File.Delete(call.Arg<string>());
			_onMutation();
		});
		file.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>()).Returns(call => {
			_contentWriteTargets.Add(call.ArgAt<string>(0));
			FileSystemStream opened = _inner.File.Open(
				call.ArgAt<string>(0), call.ArgAt<FileMode>(1), call.ArgAt<FileAccess>(2));
			_onMutation();
			return opened;
		});

		IFileSystem observing = Substitute.For<IFileSystem>();
		observing.Path.Returns(_inner.Path);
		observing.Directory.Returns(directory);
		observing.File.Returns(file);
		observing.FileInfo.Returns(_inner.FileInfo);
		observing.DirectoryInfo.Returns(_inner.DirectoryInfo);
		observing.FileStream.Returns(_inner.FileStream);
		return observing;
	}
}
