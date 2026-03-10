using System;
using System.IO;

namespace Clio.Common;

/// <summary>
/// Exposes the currently active database-operation log session and the last completed artifact path.
/// </summary>
public interface IDbOperationLogContextAccessor {
	/// <summary>
	/// Gets the currently active database-operation log session when one is running.
	/// </summary>
	IDbOperationLogSession? CurrentSession { get; }

	/// <summary>
	/// Gets the path of the last completed database-operation log artifact.
	/// </summary>
	string? LastCompletedPath { get; }

	/// <summary>
	/// Clears the last completed artifact path.
	/// </summary>
	void ClearLastCompletedPath();
}

/// <summary>
/// Creates per-invocation database-operation log sessions.
/// </summary>
public interface IDbOperationLogSessionFactory {
	/// <summary>
	/// Starts a new database-operation log session.
	/// </summary>
	/// <param name="operationName">User-facing operation name used in the artifact filename and header.</param>
	/// <returns>The created logging session.</returns>
	IDbOperationLogSession BeginSession(string operationName);
}

/// <summary>
/// Represents a per-invocation database-operation log artifact.
/// </summary>
public interface IDbOperationLogSession : IDisposable {
	/// <summary>
	/// Gets the absolute path to the artifact file.
	/// </summary>
	string LogFilePath { get; }

	/// <summary>
	/// Appends a raw database-native line to the artifact.
	/// </summary>
	/// <param name="line">The raw line to append.</param>
	void WriteNativeLine(string? line);
}

/// <summary>
/// Tracks the active database-operation log session for the current process.
/// </summary>
public sealed class DbOperationLogContextAccessor : IDbOperationLogContextAccessor {
	private readonly object _syncRoot = new();

	/// <inheritdoc />
	public IDbOperationLogSession? CurrentSession { get; private set; }

	/// <inheritdoc />
	public string? LastCompletedPath { get; private set; }

	/// <inheritdoc />
	public void ClearLastCompletedPath() {
		lock (_syncRoot) {
			LastCompletedPath = null;
		}
	}

	internal void SetCurrent(IDbOperationLogSession session) {
		lock (_syncRoot) {
			CurrentSession = session;
			LastCompletedPath = null;
		}
	}

	internal void Complete(IDbOperationLogSession session, string logFilePath) {
		lock (_syncRoot) {
			if (ReferenceEquals(CurrentSession, session)) {
				CurrentSession = null;
			}
			LastCompletedPath = logFilePath;
		}
	}
}

/// <summary>
/// Default database-operation log session factory.
/// </summary>
public sealed class DbOperationLogSessionFactory(ILogger logger, IDbOperationLogContextAccessor contextAccessor)
	: IDbOperationLogSessionFactory {
	private readonly DbOperationLogContextAccessor _typedContextAccessor =
		(DbOperationLogContextAccessor)contextAccessor;

	/// <inheritdoc />
	public IDbOperationLogSession BeginSession(string operationName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

		string safeOperationName = operationName.Replace(' ', '-').ToLowerInvariant();
		string logFilePath = Path.Combine(
			Path.GetTempPath(),
			$"clio-{safeOperationName}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.log");

		DbOperationLogSession session = new(logFilePath, logger, _typedContextAccessor, operationName);
		_typedContextAccessor.SetCurrent(session);
		return session;
	}
}

internal sealed class NullDbOperationLogContextAccessor : IDbOperationLogContextAccessor {
	public static readonly IDbOperationLogContextAccessor Instance = new NullDbOperationLogContextAccessor();

	public IDbOperationLogSession? CurrentSession => null;

	public string? LastCompletedPath => null;

	public void ClearLastCompletedPath() { }
}

internal sealed class NullDbOperationLogSessionFactory : IDbOperationLogSessionFactory {
	public static readonly IDbOperationLogSessionFactory Instance = new NullDbOperationLogSessionFactory();

	public IDbOperationLogSession BeginSession(string operationName) => NullDbOperationLogSession.Instance;
}

internal sealed class NullDbOperationLogSession : IDbOperationLogSession {
	public static readonly IDbOperationLogSession Instance = new NullDbOperationLogSession();

	public string LogFilePath => string.Empty;

	public void Dispose() { }

	public void WriteNativeLine(string? line) { }
}

internal sealed class DbOperationLogSession : IDbOperationLogSession {
	private readonly SharedAppendFileSinkLease _artifactSink;
	private readonly IDisposable _loggerSinkScope;
	private readonly DbOperationLogContextAccessor _contextAccessor;
	private bool _disposed;

	internal DbOperationLogSession(
		string logFilePath,
		ILogger logger,
		DbOperationLogContextAccessor contextAccessor,
		string operationName) {
		LogFilePath = Path.GetFullPath(logFilePath);
		_contextAccessor = contextAccessor;

		string? directory = Path.GetDirectoryName(LogFilePath);
		if (!string.IsNullOrWhiteSpace(directory)) {
			Directory.CreateDirectory(directory);
		}

		_artifactSink = SharedAppendFileSinkRegistry.Acquire(LogFilePath);
		_artifactSink.WriteLine($"[DB-OPERATION] {operationName}");
		_artifactSink.WriteLine($"[DB-LOG-PATH] {LogFilePath}");
		_artifactSink.WriteLine($"[STARTED-UTC] {DateTime.UtcNow:O}");
		_loggerSinkScope = logger.BeginScopedFileSink(LogFilePath);
	}

	/// <inheritdoc />
	public string LogFilePath { get; }

	/// <inheritdoc />
	public void WriteNativeLine(string? line) {
		if (_disposed || string.IsNullOrEmpty(line)) {
			return;
		}

		_artifactSink.WriteLine(line);
	}

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_artifactSink.WriteLine($"[COMPLETED-UTC] {DateTime.UtcNow:O}");
		_loggerSinkScope.Dispose();
		_artifactSink.Dispose();
		_contextAccessor.Complete(this, LogFilePath);
		_disposed = true;
	}
}
