using System;
using System.Collections.Generic;

namespace Clio.Common.Telemetry;

/// <summary>
/// Counts the guidance articles and tool contracts one clio MCP session has served the agent, so the
/// telemetry events that session records can carry what it cost.
/// </summary>
/// <remarks>
/// This is clio's own data: how many of its own responses it produced and how large they were. Nothing the
/// agent or the customer sent is kept, not even which article was read - only whether it was read before.
/// </remarks>
public interface IServedContentMeter
{
	/// <summary>
	/// Records one <c>get-guidance</c> response.
	/// </summary>
	/// <param name="articleName">
	/// The canonical name of the article the response served, or <see langword="null"/> when it served none
	/// (an unknown name, a missing parameter, no active library). Used only to tell a first read from a re-read.
	/// </param>
	/// <param name="libraryVersion">
	/// The version of the guidance library generation that served the article, or <see langword="null"/> when
	/// unknown. The most recent non-empty value is the one reported.
	/// </param>
	/// <param name="responseBytes">The size of the response as sent to the agent, in UTF-8 bytes.</param>
	void RecordGuidance(string articleName, string libraryVersion, long responseBytes);

	/// <summary>
	/// Records one <c>get-tool-contract</c> response.
	/// </summary>
	/// <param name="responseBytes">The size of the response as sent to the agent, in UTF-8 bytes.</param>
	void RecordContract(long responseBytes);

	/// <summary>
	/// Reads the running totals of this session.
	/// </summary>
	/// <param name="snapshot">The totals, when at least one response has been recorded.</param>
	/// <returns>
	/// <see langword="false"/> until something has been served, so a session that served nothing reports
	/// nothing rather than a row of zeros.
	/// </returns>
	bool TryGetSnapshot(out ServedContentSnapshot snapshot);
}

/// <inheritdoc />
/// <remarks>
/// Cumulative for the life of the process. Registered only by the stdio MCP host, where one process is one
/// agent session (see <c>BindingsModule.Register</c>).
/// </remarks>
public sealed class ServedContentMeter : IServedContentMeter
{
	private readonly object _syncRoot = new();
	private readonly HashSet<string> _articlesServed = new(StringComparer.Ordinal);
	private long _guidanceResponses;
	private long _guidanceReads;
	private long _guidanceRereads;
	private long _guidanceBytes;
	private long _contractReads;
	private long _contractBytes;
	private string _guidanceLibraryVersion;

	/// <inheritdoc />
	public void RecordGuidance(string articleName, string libraryVersion, long responseBytes)
	{
		lock (_syncRoot) {
			_guidanceResponses++;
			_guidanceBytes += responseBytes;
			if (!string.IsNullOrWhiteSpace(articleName)) {
				_guidanceReads++;
				// A second read of an article this session already received is the measurable trace of a
				// context compaction: the agent lost the article and fetched it again in full.
				if (!_articlesServed.Add(articleName)) {
					_guidanceRereads++;
				}
			}
			if (!string.IsNullOrWhiteSpace(libraryVersion)) {
				_guidanceLibraryVersion = libraryVersion;
			}
		}
	}

	/// <inheritdoc />
	public void RecordContract(long responseBytes)
	{
		lock (_syncRoot) {
			_contractReads++;
			_contractBytes += responseBytes;
		}
	}

	/// <inheritdoc />
	public bool TryGetSnapshot(out ServedContentSnapshot snapshot)
	{
		lock (_syncRoot) {
			if (_guidanceResponses == 0 && _contractReads == 0) {
				snapshot = null;
				return false;
			}
			snapshot = new ServedContentSnapshot(_guidanceReads, _guidanceRereads, _guidanceBytes,
				_contractReads, _contractBytes, _guidanceLibraryVersion);
			return true;
		}
	}
}

/// <inheritdoc />
/// <remarks>
/// The default for every container: it records nothing and reports nothing. Only the stdio MCP host
/// replaces it with <see cref="ServedContentMeter"/>. The mcp-http host serves many sessions from one
/// process, so a process-wide count there would mix them and stamp one session's cost on another's events.
/// </remarks>
public sealed class NullServedContentMeter : IServedContentMeter
{
	/// <inheritdoc />
	public void RecordGuidance(string articleName, string libraryVersion, long responseBytes)
	{
	}

	/// <inheritdoc />
	public void RecordContract(long responseBytes)
	{
	}

	/// <inheritdoc />
	public bool TryGetSnapshot(out ServedContentSnapshot snapshot)
	{
		snapshot = null;
		return false;
	}
}
