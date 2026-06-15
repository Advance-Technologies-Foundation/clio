using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.ProcessDesigner;

/// <summary>
/// Drives the live Creatio Process Designer (diagram-js) over an already-launched, authenticated
/// browser via the Chrome DevTools Protocol, running the proven "Read data" recipe. Pure orchestration
/// over CDP — it knows nothing about MCP or CLI options.
/// </summary>
/// <remarks>
/// Feasibility baseline (channel LOCKED, not re-proven here): env <c>krestov-test</c>, process
/// <c>UsrProcess_493d4c9</c> ("AI PoC Read Contact"); see
/// <c>spec/ai-business-process-generation/ai-bp-ui-playbook.md</c> §6. CDP egress is loopback-only;
/// cookie values are never echoed.
/// </remarks>
public interface IProcessDesignerDriver {
	/// <summary>
	/// Opens (or creates, when <see cref="ProcessAddElementRequest.ProcessId"/> is null) the designer over
	/// the running browser session, appends a Read data element onto the Start→End flow, configures its
	/// source object, sets the caption, asserts the connection is valid, saves, and reads back identity.
	/// </summary>
	/// <param name="request">The drive request (environment, DevTools port, optional process id, read object, caption).</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The result; <see cref="ProcessAddElementResult.Success"/> is true only on a confirmed save.</returns>
	Task<ProcessAddElementResult> AddReadDataElementAsync(ProcessAddElementRequest request, CancellationToken ct = default);
}

/// <summary>
/// Request to add a Read data element to a process via the designer driver.
/// </summary>
/// <param name="Environment">Target environment (its <c>Uri</c>/<c>IsNetCore</c> build the designer URL).</param>
/// <param name="DevToolsPort">The loopback remote-debugging port of the already-launched browser.</param>
/// <param name="ProcessId">Existing process id to open, or <see langword="null"/> to create a new process (OQ-01).</param>
/// <param name="ReadObject">The object the Read data element should read (e.g. <c>Contact</c>) — source object only (OQ-02).</param>
/// <param name="Caption">A deterministic caption the caller supplies; the readback handle (OQ-04).</param>
public sealed record ProcessAddElementRequest(
	EnvironmentSettings Environment,
	int DevToolsPort,
	string ProcessId,
	string ReadObject,
	string Caption);

/// <summary>
/// Outcome of a designer-driver add-element run.
/// </summary>
/// <param name="Success"><see langword="true"/> only when a real save signal was observed.</param>
/// <param name="Code">The platform-generated process Name (e.g. <c>UsrProcess_493d4c9</c>), when read back.</param>
/// <param name="UId">The process UId, when read back.</param>
/// <param name="Caption">The caption that was set (the readback handle).</param>
/// <param name="Error">A user-friendly <c>Error: …</c> message on failure (no stack trace); otherwise null.</param>
public sealed record ProcessAddElementResult(
	bool Success,
	string Code,
	string UId,
	string Caption,
	string Error);
