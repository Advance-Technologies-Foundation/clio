using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Clio.Common;

internal sealed class SharedAppendFileSinkLease : IDisposable {
	private readonly SharedAppendFileSinkRegistry.Entry _entry;
	private int _disposed;

	internal SharedAppendFileSinkLease(SharedAppendFileSinkRegistry.Entry entry) {
		_entry = entry;
	}

	public string Path => _entry.Path;

	public void WriteLine(string value) {
		ObjectDisposedException.ThrowIf(_disposed != 0, this);
		_entry.WriteLine(value);
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref _disposed, 1) != 0) {
			return;
		}

		SharedAppendFileSinkRegistry.Release(_entry);
	}
}

internal static class SharedAppendFileSinkRegistry {
	private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

	public static SharedAppendFileSinkLease Acquire(string logFilePath) {
		string fullPath = Path.GetFullPath(logFilePath);
		Entry entry = Entries.AddOrUpdate(
			fullPath,
			path => new Entry(path),
			(_, existing) => {
				existing.AddReference();
				return existing;
			});

		return new SharedAppendFileSinkLease(entry);
	}

	public static void Release(Entry entry) {
		if (!entry.ReleaseReference()) {
			return;
		}

		Entries.TryRemove(new KeyValuePair<string, Entry>(entry.Path, entry));
		entry.Dispose();
	}

	internal sealed class Entry : IDisposable {
		private readonly object _syncRoot = new();
		private readonly StreamWriter _writer;
		private int _referenceCount = 1;
		private bool _disposed;

		internal Entry(string path) {
			Path = path;
			string? directory = System.IO.Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directory)) {
				Directory.CreateDirectory(directory);
			}

			FileStream stream = new(
				path,
				FileMode.Append,
				FileAccess.Write,
				FileShare.ReadWrite | FileShare.Delete);
			_writer = new StreamWriter(stream) {
				AutoFlush = true
			};
		}

		internal string Path { get; }

		internal void AddReference() {
			Interlocked.Increment(ref _referenceCount);
		}

		internal bool ReleaseReference() {
			return Interlocked.Decrement(ref _referenceCount) == 0;
		}

		internal void WriteLine(string value) {
			lock (_syncRoot) {
				ObjectDisposedException.ThrowIf(_disposed, this);
				_writer.WriteLine(value);
			}
		}

		public void Dispose() {
			lock (_syncRoot) {
				if (_disposed) {
					return;
				}

				_writer.Dispose();
				_disposed = true;
			}
		}
	}
}
