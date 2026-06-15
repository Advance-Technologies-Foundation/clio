using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;

namespace Clio.Common.ProcessDesigner;

/// <inheritdoc cref="IProcessDesignerDriver" />
public sealed class ProcessDesignerDriver : IProcessDesignerDriver {
	private const string RecipeName = "read-data-element";

	private readonly ICdpSession _cdpSession;
	private readonly TimeSpan _renderTimeout;
	private readonly TimeSpan _pollDelay;
	private readonly string _recipe;

	/// <summary>Initializes the driver with the CDP session used to drive the designer.</summary>
	/// <param name="cdpSession">The CDP session (connected per run to the launched browser).</param>
	public ProcessDesignerDriver(ICdpSession cdpSession)
		: this(cdpSession, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500)) {
	}

	// Test seam: lets unit tests shrink the render-poll budget so the never-rendered path fails fast.
	internal ProcessDesignerDriver(ICdpSession cdpSession, TimeSpan renderTimeout, TimeSpan pollDelay) {
		_cdpSession = cdpSession;
		_renderTimeout = renderTimeout;
		_pollDelay = pollDelay;
		_recipe = ProcessDesignerRecipes.Get(RecipeName);
	}

	/// <inheritdoc />
	public async Task<ProcessAddElementResult> AddReadDataElementAsync(ProcessAddElementRequest request, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(request);
		try {
			await _cdpSession.ConnectAsync(request.DevToolsPort, ct).ConfigureAwait(false);

			// 1. Open the existing process or bootstrap a new one (Start -> End).
			string designerUrl = BuildDesignerUrl(request.Environment, request.ProcessId);
			await _cdpSession.SendAsync("Page.navigate", new { url = designerUrl }, ct).ConfigureAwait(false);

			// 2. Wait for the canvas (the SVG renders a few seconds after the metadata card); 'prepare'
			//    also dismisses any stray create-popup and returns the Start event center.
			JsonElement? prepared = await PollAsync("prepare", "{}", element => GetBool(element, "ready"), ct).ConfigureAwait(false);
			if (prepared is null) {
				return Fail(request, "the Process Designer canvas did not render in time");
			}

			// 3. Select the Start event with a TRUSTED CDP click, then append (untrusted, QA-proven).
			await TrustedClickAsync(GetDouble(prepared.Value, "x"), GetDouble(prepared.Value, "y"), ct).ConfigureAwait(false);
			JsonElement appended = await RunRecipeAsync("append", "{}", ct).ConfigureAwait(false);
			if (!GetBool(appended, "ok")) {
				return Fail(request, GetError(appended, "failed to append the Read data element"));
			}

			// Select the newly-appended element with a trusted click so its setup card renders (do not rely
			// on auto-selection; the right-panel properties module loads async, especially on a cold browser).
			await TrustedClickAsync(GetDouble(appended, "x"), GetDouble(appended, "y"), ct).ConfigureAwait(false);

			// 4. Configure ONLY the source object, then trusted-click the matching dropdown option.
			string objectParams = JsonSerializer.Serialize(new { @object = request.ReadObject });
			JsonElement filled = await RunRecipeAsync("fillObject", objectParams, ct).ConfigureAwait(false);
			if (!GetBool(filled, "ok")) {
				return Fail(request, GetError(filled, $"could not set the object to read ('{request.ReadObject}')"));
			}
			await TrustedClickAsync(GetDouble(filled, "optionX"), GetDouble(filled, "optionY"), ct).ConfigureAwait(false);

			// 5. Set the deterministic caption.
			string captionParams = JsonSerializer.Serialize(new { caption = request.Caption });
			JsonElement captioned = await RunRecipeAsync("setCaption", captionParams, ct).ConfigureAwait(false);
			if (!GetBool(captioned, "ok")) {
				return Fail(request, GetError(captioned, "could not set the process caption"));
			}

			// 6. The designer is the final authority: abort (no SAVE) if the connection is invalid.
			JsonElement validity = await RunRecipeAsync("checkValid", "{}", ct).ConfigureAwait(false);
			if (GetBool(validity, "invalid")) {
				return Fail(request, "the appended connection is invalid (.djs-validate-outline) — not saving");
			}

			// 7. SAVE (trusted click), then confirm the real "Successfully saved" signal before reporting success.
			JsonElement saveCoords = await RunRecipeAsync("saveCoords", "{}", ct).ConfigureAwait(false);
			if (!GetBool(saveCoords, "ok")) {
				return Fail(request, GetError(saveCoords, "could not locate the SAVE control"));
			}
			await TrustedClickAsync(GetDouble(saveCoords, "x"), GetDouble(saveCoords, "y"), ct).ConfigureAwait(false);

			JsonElement saveResult = await RunRecipeAsync("saveResult",
				JsonSerializer.Serialize(new { caption = request.Caption }), ct).ConfigureAwait(false);
			if (!GetBool(saveResult, "ok")) {
				return Fail(request, GetError(saveResult, "did not observe a successful save"));
			}

			return new ProcessAddElementResult(true, GetString(saveResult, "code"), GetString(saveResult, "uid"),
				request.Caption, null);
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return Fail(request, $"the process designer driver failed: {ex.Message}");
		} finally {
			await _cdpSession.DisposeAsync().ConfigureAwait(false);
		}
	}

	internal static string BuildDesignerUrl(EnvironmentSettings env, string processId) {
		string baseUri = env.Uri.TrimEnd('/');
		string prefix = env.IsNetCore ? string.Empty : "/0";
		return $"{baseUri}{prefix}/Nui/ViewModule.aspx?vm=SchemaDesigner#process/{processId ?? string.Empty}";
	}

	// Runs one recipe phase via Runtime.evaluate; parameters are JSON (escaped) — never string-concatenated.
	private Task<JsonElement> RunRecipeAsync(string phase, string paramsJson, CancellationToken ct) {
		string expression = $"({_recipe})({JsonSerializer.Serialize(phase)},{paramsJson})";
		return _cdpSession.EvaluateAsync(expression, awaitPromise: true, ct);
	}

	// Polls a recipe phase until the predicate holds or the render budget is exhausted; null on timeout.
	private async Task<JsonElement?> PollAsync(string phase, string paramsJson, Func<JsonElement, bool> predicate, CancellationToken ct) {
		int attempts = Math.Max(1, (int)(_renderTimeout.TotalMilliseconds / Math.Max(1, _pollDelay.TotalMilliseconds)));
		for (int attempt = 0; attempt < attempts; attempt++) {
			ct.ThrowIfCancellationRequested();
			JsonElement result = await RunRecipeAsync(phase, paramsJson, ct).ConfigureAwait(false);
			if (predicate(result)) {
				return result;
			}
			await Task.Delay(_pollDelay, ct).ConfigureAwait(false);
		}
		return null;
	}

	// A trusted mouse click at viewport coordinates (CDP Input is trusted, unlike JS dispatchEvent).
	private async Task TrustedClickAsync(double x, double y, CancellationToken ct) {
		await _cdpSession.SendAsync("Input.dispatchMouseEvent",
			new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, ct).ConfigureAwait(false);
		await _cdpSession.SendAsync("Input.dispatchMouseEvent",
			new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, ct).ConfigureAwait(false);
	}

	private static ProcessAddElementResult Fail(ProcessAddElementRequest request, string reason) =>
		new(false, null, null, request.Caption, $"Error: {reason}.");

	private static bool GetBool(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object
		&& element.TryGetProperty(property, out JsonElement value)
		&& value.ValueKind == JsonValueKind.True;

	private static double GetDouble(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
			&& value.ValueKind == JsonValueKind.Number
			? value.GetDouble()
			: 0;

	private static string GetString(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static string GetError(JsonElement element, string fallback) =>
		GetString(element, "error") ?? fallback;
}
