using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Thread-safe <see cref="System.IProgress{T}"/> sink that records the message of every
/// <c>notifications/progress</c> the SDK delivers, so a test can assert the stage-marker text.
/// Shared across the MCP E2E tests that verify progress streaming (ENG-93087). Distinct from the
/// counting sink used by the keep-alive tests, which only tallies notification volume.
/// </summary>
internal sealed class MessageCollectingProgress : System.IProgress<ProgressNotificationValue> {
	private readonly List<string> _messages = new();
	private readonly object _gate = new();

	/// <summary>Snapshot of the messages observed so far, in delivery order.</summary>
	public IReadOnlyList<string> Messages {
		get {
			lock (_gate) {
				return _messages.ToArray();
			}
		}
	}

	/// <summary>Number of progress notifications observed so far.</summary>
	public int Count {
		get {
			lock (_gate) {
				return _messages.Count;
			}
		}
	}

	/// <inheritdoc />
	public void Report(ProgressNotificationValue value) {
		lock (_gate) {
			_messages.Add(value.Message ?? string.Empty);
		}
	}
}
