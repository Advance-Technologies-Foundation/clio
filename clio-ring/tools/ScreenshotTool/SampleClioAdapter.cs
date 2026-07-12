using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClioLauncher.Services;

namespace ScreenshotTool;

/// <summary>
/// Deterministic <see cref="IClioAdapter"/> for offscreen rendering. Returns the ACTUAL 23-env set
/// (names + representative hosts + .NET flavour) so the palette is exercised at real scale, and
/// never spawns a real clio process.
/// </summary>
internal sealed class SampleClioAdapter : IClioAdapter {
	public event EventHandler<ClioOutputLine>? OutputReceived;

	private static readonly ClioEnvironment[] Environments = {
		new("ve", "https://k-krylov-nb.tscrm.com:40038", true),
		new("work", "http://localhost:39800", true),
		new("work-pse", "https://ts1-pse-dev01.tscrm.com", false),
		new("rss", "http://k-krylov-nb.tscrm.com:40055", false),
		new("d1", "http://k-krylov-nb.tscrm.com:40065", true),
		new("bndl", "http://k-krylov-nb.tscrm.com:40075", true),
		new("bndl75", "http://k-krylov-nb.tscrm.com:40075", false),
		new("calc", "http://k-krylov-nb.tscrm.com:40000", true),
		new("c-dev", "https://186843-crm-bundle.creatio.com", true),
		new("mt", "https://mt-demo.creatio.com", true),
		new("bk", "http://localhost:40080", true),
		new("bank", "https://bank-uat.creatio.com", true),
		new("demo", "https://demo.creatio.com", true),
		new("ira", "http://k-krylov-nb.tscrm.com:40090", false),
		new("studioid", "https://studioid.creatio.com", true),
		new("nelnet-uat", "https://nelnet-uat.creatio.com", true),
		new("lcap-local", "http://localhost:40100", true),
		new("lcap-26", "https://lcap-26.creatio.com", true),
		new("a1", "http://k-krylov-nb.tscrm.com:41001", true),
		new("a2", "http://k-krylov-nb.tscrm.com:41002", true),
		new("a3", "http://k-krylov-nb.tscrm.com:41003", true),
		new("a4", "http://k-krylov-nb.tscrm.com:41004", true),
		new("a5", "http://k-krylov-nb.tscrm.com:41005", false)
	};

	public Task<IReadOnlyList<ClioEnvironment>> ListEnvironmentsAsync(CancellationToken cancellationToken = default) {
		IReadOnlyList<ClioEnvironment> list = Environments;
		return Task.FromResult(list);
	}

	public Task<ClioRunResult> RunAsync(
		ClioInvocation invocation,
		Action<ClioOutputLine>? onOutput = null,
		CancellationToken cancellationToken = default) {
		_ = OutputReceived;
		return Task.FromResult(new ClioRunResult(0, string.Empty, string.Empty, false));
	}
}
