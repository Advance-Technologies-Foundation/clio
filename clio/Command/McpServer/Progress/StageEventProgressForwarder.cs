using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Progress;

/// <summary>
/// Forwards the typed <see cref="ClioStageEvent"/> stream raised by a deploy/uninstall command onto
/// the MCP <c>notifications/progress</c> channel, carrying the full typed envelope in the
/// notification <c>_meta.clioStageEvent</c> field (story 4, FR-08/FR-13/FR-14/FR-15).
/// </summary>
/// <remarks>
/// <para>
/// This is the single, unit-testable seam shared by <c>InstallerCommandTool</c> and
/// <c>UninstallCreatioTool</c>. A tool subscribes the resolved-at-startup, injected command instance
/// (the one <c>InternalExecute(options)</c> runs) and supplies a <c>send</c> callback wired to the
/// live MCP server.
/// </para>
/// <para>
/// ADR D4 assumed the deploy/uninstall MCP tools were environment-bound and would resolve a fresh
/// per-call command via <c>InternalExecute&lt;TCommand&gt;</c>. That assumption was corrected during
/// implementation: <c>deploy-creatio</c>/<c>uninstall-creatio</c> are local-only commands with no
/// <see cref="Clio.Common.IApplicationClient"/> (deploy CREATES the instance), so the environmentless
/// resolver has nothing to bind and the tools keep <c>InternalExecute(options)</c> and subscribe to
/// the injected command instance — which itself bubbles <see cref="IStageEventSource.StageChanged"/>
/// from its underlying service.
/// </para>
/// </remarks>
public interface IStageEventProgressForwarder {

	/// <summary>
	/// Subscribes to <paramref name="source"/> and forwards every raised <see cref="ClioStageEvent"/>
	/// as a <c>notifications/progress</c> notification built by <paramref name="send"/>, with the event
	/// serialized (via <see cref="ClioStageEventContract.SerializerOptions"/>) into
	/// <c>_meta.clioStageEvent</c>.
	/// </summary>
	/// <remarks>
	/// When <paramref name="progressToken"/> is <see langword="null"/> the client did not opt into
	/// progress, so this is a no-op and returns an inert subscription — behavior is byte-for-byte
	/// identical to a non-progress client (mirrors <c>StartTool</c> / <c>McpProgressHeartbeat</c>). Send
	/// failures are swallowed so a broken progress channel never breaks the deploy/uninstall operation.
	/// </remarks>
	/// <param name="source">The command instance that raises the typed stage-event stream.</param>
	/// <param name="progressToken">The current call's progress token, or <see langword="null"/>.</param>
	/// <param name="send">Callback that emits one built <see cref="ProgressNotificationParams"/>.</param>
	/// <returns>A subscription that detaches the handler from <paramref name="source"/> on dispose.</returns>
	IDisposable Subscribe(IStageEventSource source, ProgressToken? progressToken,
		Action<ProgressNotificationParams> send);
}

/// <inheritdoc cref="IStageEventProgressForwarder" />
public sealed class StageEventProgressForwarder : IStageEventProgressForwarder {

	/// <inheritdoc />
	public IDisposable Subscribe(IStageEventSource source, ProgressToken? progressToken,
		Action<ProgressNotificationParams> send) {
		ArgumentNullException.ThrowIfNull(source);
		if (progressToken is null) {
			// No progress token → the caller did not opt into progress; forwarding is a pure no-op.
			return NoopSubscription.Instance;
		}

		ArgumentNullException.ThrowIfNull(send);
		ProgressToken token = progressToken.Value;
		ProgressCursor cursor = new();
		EventHandler<ClioStageEvent> handler = (_, stageEvent) => {
			try {
				send(ToProgressNotification(stageEvent, token, cursor));
			}
			catch {
				// Forwarding progress must never break the deploy/uninstall operation — a disconnected
				// client or a serialization fault is swallowed (mirrors McpLogNotifier / heartbeat).
			}
		};
		source.StageChanged += handler;
		return new Subscription(source, handler);
	}

	/// <summary>
	/// Builds the progress notification for one event. The load-bearing typed envelope always travels
	/// intact in <c>_meta.clioStageEvent</c>; <see cref="ProgressNotificationValue.Progress"/> /
	/// <see cref="ProgressNotificationValue.Total"/> / <see cref="ProgressNotificationValue.Message"/>
	/// are best-effort progress-bar values for generic progress-aware clients (the Ring reads
	/// <c>_meta.clioStageEvent</c>, not these).
	/// </summary>
	internal static ProgressNotificationParams ToProgressNotification(ClioStageEvent stageEvent,
		ProgressToken token, ProgressCursor cursor) {
		cursor.Observe(stageEvent);
		return new ProgressNotificationParams {
			ProgressToken = token,
			Progress = new ProgressNotificationValue {
				Progress = cursor.Progress,
				Total = cursor.Total,
				Message = cursor.Message
			},
			Meta = new JsonObject {
				["clioStageEvent"] = JsonSerializer.SerializeToNode(stageEvent, ClioStageEventContract.SerializerOptions)
			}
		};
	}

	/// <summary>
	/// Tracks the last-known bar position across one run so the terminal <c>run-completed</c> event can
	/// report a full bar even though its payload carries no index/total.
	/// </summary>
	internal sealed class ProgressCursor {

		/// <summary>The current bar position (0-based stage index; the total on completion).</summary>
		public float Progress { get; private set; }

		/// <summary>The manifest length (stable bar denominator), or <see langword="null"/> until known.</summary>
		public float? Total { get; private set; }

		/// <summary>The human-readable message for the current transition.</summary>
		public string Message { get; private set; } = string.Empty;

		/// <summary>Advances the cursor from the newly observed event.</summary>
		public void Observe(ClioStageEvent stageEvent) {
			switch (stageEvent) {
				case { Stage: { } stage }:
					Progress = stage.Index;
					Total = stage.Total;
					Message = stage.Message;
					break;
				case { Stages: { Count: > 0 } stages }:
					Progress = 0;
					Total = stages[0].Total; // every manifest entry shares the same total
					Message = string.Empty;
					break;
				case { RunCompleted: { } completed }:
					if (Total is { } total) {
						Progress = total; // terminal: the bar reaches the known total
					}

					Message = completed.Summary;
					break;
			}
		}
	}

	private sealed class Subscription(IStageEventSource source, EventHandler<ClioStageEvent> handler)
		: IDisposable {
		private IStageEventSource _source = source;
		private EventHandler<ClioStageEvent> _handler = handler;

		public void Dispose() {
			if (_source is null) {
				return;
			}

			_source.StageChanged -= _handler;
			_source = null;
			_handler = null;
		}
	}

	private sealed class NoopSubscription : IDisposable {
		public static readonly NoopSubscription Instance = new();

		public void Dispose() { }
	}
}
