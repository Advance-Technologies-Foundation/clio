using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Options for compiling the package a business process lives in, via the ProcessDesignService package.
/// Consumed by the MCP <c>compile-creatio</c> tool's <c>process-name</c> mode, which sets these properties
/// directly.
/// </summary>
// CompileProcess first ships working in the 1.6.6.33 archive (1.6.6.32 was a local cut that answered "nothing to
// compile" for a process the runtime does not interpret); an older package answers the route with a 404 rather
// than a contract error, and the literal turns that into "your package is behind".
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.6.6.33",
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class CompileBusinessProcessOptions : EnvironmentOptions {
	/// <summary>Schema name (code) of the process.</summary>
	public string ProcessName { get; set; } = string.Empty;
}

/// <summary>
/// Compiles the package a business process lives in via the ProcessDesignService package.
/// </summary>
public interface ICompileBusinessProcessService {
	/// <summary>
	/// Compiles the package of the named process when the process carries C#, and reports the compiler errors.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Identity of the process.</param>
	/// <returns>What the server compiled and what the compiler reported.</returns>
	CompileBusinessProcessResult Compile(string environmentName, CompileBusinessProcessRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="ICompileBusinessProcessService"/>.
/// </summary>
public sealed class CompileBusinessProcessService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: ICompileBusinessProcessService {
	/// <summary>
	/// The same bound compile-configuration declares. A compile of a general package rebuilds the shared
	/// configuration assembly, which took 3 min 21 s on a local stand; one attempt, never retried, because a
	/// retry of a compile that is still running is refused by the platform rather than queued.
	/// </summary>
	internal static readonly int CompileTimeoutMs = (int)TimeSpan.FromMinutes(60).TotalMilliseconds;

	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public CompileBusinessProcessResult Compile(string environmentName, CompileBusinessProcessRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.ProcessName)) {
			throw new ArgumentException("A process name is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));

		var requestObject = new JsonObject { ["name"] = request.ProcessName };

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileProcess, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the request is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = requestObject }.ToJsonString();
		logger.WriteInfo(
			$"Compiling the package of process '{request.ProcessName}' on '{environmentName}'. A compile reloads the runtime "
			+ "for every user of the environment and usually takes a few minutes...");

		string responseBody;
		try {
			responseBody = client.ExecutePostRequest(url, requestBody, CompileTimeoutMs);
		} catch (Exception exception) when (IsTransportFault(exception)) {
			// No HTTP status is classified here, because none arrives: Creatio.Client's POST reads the body without
			// checking the status (docs/knowledge/Command/creatio-client-returns-error-bodies-without-status.md),
			// so a 4xx comes back as an HTML or error body and is classified below, after the call returned.
			// A timeout or a dropped connection mid-compile says nothing about the compile itself, and a retry
			// while it still runs is refused by the platform rather than queued - so the caller is told so. The
			// transport's own text is fenced: it routinely carries the request URI, and a proxy's answer is third-
			// party prose, and this line reaches an agent through the MCP result and the compile-status tail.
			throw new InvalidOperationException(
				"CompileProcess did not answer, so whether the package was compiled is UNKNOWN - the compile may "
				+ "still be running on the server. Wait, then read last-compilation-log for this environment before "
				+ "compiling again. Transport detail: "
				+ (UntrustedText.Fenced(exception.GetReadableMessageException()) ?? "none reported"),
				exception);
		}
		if (string.IsNullOrWhiteSpace(responseBody)) {
			// Not a parser question: Deserialize throws ArgumentNullException on a null body, which reached the
			// caller as a bare "Value cannot be null" without the one thing it needs to know.
			throw new InvalidOperationException(
				"CompileProcess returned an empty body, so whether the package was compiled is UNKNOWN. Read "
				+ "last-compilation-log for this environment before compiling again.");
		}
		bool isErrorPage = CreatioResponseError.TryClassifyMarkupError(responseBody, out int? pageStatus);
		if (isErrorPage && pageStatus is 401 or 404) {
			// An authentication refusal or an unrouted request: the web server answered before the process builder
			// ran, so this outcome is known. A 403 is not: a gateway that inspects responses answers 403 after the
			// backend ran, and any other page - a 500 among them - can come after the compile too.
			throw new InvalidOperationException(
				$"CompileProcess was answered with an HTTP {pageStatus} error page: the request did not reach the "
				+ "process builder, so nothing was compiled. "
				+ (pageStatus == 404
					? "The route was not found: check the environment's URL and its IsNetCore setting, and that "
						+ "CrtProcessBuilder is installed and compiled there (install-process-builder)."
					: "Check the environment's credentials.")
				+ " Ask the user again before compiling.");
		}
		if (!(isErrorPage && pageStatus >= 500) && ReauthExecutor.IsSessionExpiredResponse(responseBody)) {
			// The sign-in page or the JSON 401 envelope: authentication refused the request before routing, so
			// the process builder never ran. Not on a 5xx page, which can link the sign-in page and come after the
			// compile. Where the client can, it has already signed in again and replayed the call once.
			throw new InvalidOperationException(
				"CompileProcess was refused by the sign-in check, so the request did not reach the process builder "
				+ "and nothing was compiled. Verify the environment's credentials (clio reg-web-app --check-login), "
				+ "then ask the user again before compiling.");
		}
		ResponseEnvelope? envelope;
		try {
			envelope = JsonSerializer.Deserialize<ResponseEnvelope>(responseBody, JsonOptions);
		} catch (JsonException exception) {
			// The same guard its siblings carry: a body that is not this envelope makes the parser throw about a
			// .NET type and a byte offset. Here the unknown is whether a compile RAN, so the message says where to
			// read that rather than inviting a retry the platform would refuse while a compile is running.
			throw new InvalidOperationException(
				"CompileProcess returned a response clio could not read, so whether the package was compiled is "
				+ "UNKNOWN. Read last-compilation-log for this environment before compiling again.",
				exception);
		}

		// Not the envelope: a JSON error body (a fault, a refusal before the handler ran) parses into one with no
		// result. The outcome is as open as for a body that does not parse at all.
		ResultDto result = envelope?.Result ?? throw DescribeMissingResult(responseBody);
		return new CompileBusinessProcessResult(
			result.Success,
			result.ErrorMessage,
			result.ProcessName,
			result.PackageName,
			result.PackageType,
			result.CompileRequired,
			result.Compiled,
			result.DurationMs,
			(result.Errors ?? [])
				.Select(error => new CompileBusinessProcessError(error.FileName, error.Line, error.Column,
					error.Code, error.Message, error.InThisProcess))
				.ToList(),
			result.ErrorCount);
	}

	// A Creatio error body answers in place of the result, and the process builder reports its own failures INSIDE
	// the result, so the error came from the platform around it - before the handler (a permission refusal, which
	// compiled nothing) or after it (a response the service could not write), and the body does not say which. The
	// server's wording is not reproduced: a service body is not trusted text in an MCP transcript. Its numeric
	// code is, because clio reads it as a number. last-compilation-log shows the LATEST compile, which may be an
	// earlier one, so the caller is told to check its time rather than to read it as this call's outcome.
	private static InvalidOperationException DescribeMissingResult(string responseBody) {
		string? code = null;
		bool isErrorBody = false;
		try {
			using JsonDocument document = JsonDocument.Parse(responseBody);
			JsonElement root = document.RootElement;
			isErrorBody = CreatioResponseError.TryClassify(root, CreatioResponseContext.Service, out bool _);
			// Either spelling, as the detector reads it; a code that is not an integer is left out rather than shown.
			JsonElement codeElement = default;
			bool hasCode = isErrorBody && root.ValueKind == JsonValueKind.Object
				&& (root.TryGetProperty("Code", out codeElement) || root.TryGetProperty("code", out codeElement));
			if (hasCode && codeElement.ValueKind == JsonValueKind.Number && codeElement.TryGetInt32(out int number)) {
				code = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
		} catch (JsonException) {
			// Deserialize parsed it a moment ago; a body that still refuses is described as one without a result.
		}
		if (!isErrorBody) {
			return new InvalidOperationException(
				"CompileProcess returned a response without its result, so whether the package was compiled is "
				+ "UNKNOWN. Read last-compilation-log for this environment before compiling again.");
		}
		string codeText = code == null ? string.Empty : $" (code {code})";
		return new InvalidOperationException(
			$"CompileProcess answered with a Creatio error{codeText} instead of its result, so whether the package "
			+ "was compiled is UNKNOWN. The process builder reports its own failures inside that result, so this "
			+ "error came from the platform around it: a refusal before the builder ran compiled nothing, but a "
			+ "failure after the compile looks the same. The server's wording is not reproduced here. Before "
			+ "compiling again, read last-compilation-log and compare its time with this call's: it shows the latest "
			+ "compile, which may be an earlier one.");
	}

	// Only a call that did not come back leaves the compile's fate open. Creatio's client reads Task.Result, so
	// an HttpClient timeout, a dropped connection or a recycled app pool arrives wrapped in AggregateException
	// (docs/knowledge/Common/webexception-derives-from-invalidoperationexception.md): unwrap it recursively,
	// and let an empty aggregate - which names no fault - surface as itself.
	private static bool IsTransportFault(Exception exception) {
		if (exception is AggregateException aggregate) {
			ReadOnlyCollection<Exception> inner = aggregate.Flatten().InnerExceptions;
			return inner.Count > 0 && inner.All(IsTransportFault);
		}
		return exception is WebException or HttpRequestException or IOException or SocketException
			or TimeoutException or OperationCanceledException;
	}

	private sealed class ResponseEnvelope {
		[JsonPropertyName("CompileProcessResult")]
		public ResultDto? Result { get; set; }
	}

	private sealed class ResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }

		[JsonPropertyName("processName")]
		public string? ProcessName { get; set; }

		[JsonPropertyName("packageName")]
		public string? PackageName { get; set; }

		[JsonPropertyName("packageType")]
		public string? PackageType { get; set; }

		[JsonPropertyName("compileRequired")]
		public bool CompileRequired { get; set; }

		[JsonPropertyName("compiled")]
		public bool Compiled { get; set; }

		[JsonPropertyName("durationMs")]
		public long DurationMs { get; set; }

		[JsonPropertyName("errors")]
		public List<ErrorDto>? Errors { get; set; }

		[JsonPropertyName("errorCount")]
		public int ErrorCount { get; set; }
	}

	private sealed class ErrorDto {
		[JsonPropertyName("fileName")]
		public string? FileName { get; set; }

		[JsonPropertyName("line")]
		public int Line { get; set; }

		[JsonPropertyName("column")]
		public int Column { get; set; }

		[JsonPropertyName("code")]
		public string? Code { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }

		[JsonPropertyName("inThisProcess")]
		public bool InThisProcess { get; set; }
	}
}

/// <summary>
/// Compiles the package a business process lives in and prints what the compiler reported.
/// </summary>
public class CompileBusinessProcessCommand(
	ICompileBusinessProcessService compileBusinessProcessService,
	ILogger logger)
	: Command<CompileBusinessProcessOptions> {
	/// <inheritdoc />
	public override int Execute(CompileBusinessProcessOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			if (string.IsNullOrWhiteSpace(options.ProcessName)) {
				throw new InvalidOperationException("process-name is required.");
			}

			CompileBusinessProcessResult result = compileBusinessProcessService.Compile(options.Environment,
				new CompileBusinessProcessRequest(options.ProcessName));
			if (!result.Success) {
				ReportFailure(result);
				return 1;
			}

			if (!result.CompileRequired) {
				logger.WriteInfo(
					$"Process '{ServerName(result.ProcessName)}' carries no C# (no script task and no process methods), so "
					+ "nothing was compiled and nothing needs to be.");
				return 0;
			}

			// Measured on both host kinds: on .NET Framework the reload the package triggers after a clean compile
			// made the process run the new code with no restart; on a .NET 8 host (2026-09-28) the process kept
			// answering "Publish ... before starting it" until the application restarted, so the line says so there.
			logger.WriteInfo(
				$"Compiled package '{ServerName(result.PackageName)}' ({DescribePackageType(result.PackageType)}) in "
				+ $"{DescribeDuration(result.DurationMs)}. Verify on a run that process "
				+ $"'{ServerName(result.ProcessName)}' executes the saved code; on a .NET (Core) host restart the application "
				+ "first, as after any compile.");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	// A process or package name the server echoes back: shown as it is when it has the shape of a schema or package
	// code, and fenced otherwise, because the server authored it and the line reaches an agent.
	private static string ServerName(string? name) =>
		name is not null && SchemaCode.IsMatch(name) ? name : UntrustedText.Fenced(name) ?? "(unnamed)";

	private static readonly Regex SchemaCode = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z",
		RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

	// The server's number, so it is not trusted to fit a TimeSpan: an out-of-range one would throw after a compile
	// that succeeded and report it as a failure.
	private static string DescribeDuration(long durationMs) =>
		durationMs is >= 0 and <= 86_400_000
			? TimeSpan.FromMilliseconds(durationMs).ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture)
			: "an unreported time";

	private static string DescribePackageType(string? packageType) =>
		string.Equals(packageType, "assembly", StringComparison.OrdinalIgnoreCase)
			? "its own assembly"
			: "the shared configuration assembly";

	/// <summary>
	/// The most lines one failed run writes, the service's progress line included. compile-status keeps the LAST
	/// lines of a run, and a test holds its cap at or above this budget, so the summary and the process's OWN
	/// errors - which come first - stay in the tail instead of another schema's.
	/// </summary>
	internal const int MaxOutputLines = 50;

	// Written around the list: the service's progress line before the server answers, then the summary, the "more
	// not listed" line and the consent line below. The test that fills the list counts what is actually written.
	private const int LinesAroundTheList = 4;

	/// <summary>The errors listed one per line: what <see cref="MaxOutputLines"/> leaves after the lines around them.</summary>
	internal const int MaxListedErrors = MaxOutputLines - LinesAroundTheList;

	// One line per error, the process's own first (the server orders them), so the caller reads what it has to
	// fix before what another schema of the package broke. Every server-authored segment is FENCED, not only
	// sanitized: the compile covers the whole package, so a message can be another author's #error text, and these
	// lines reach an agent through the MCP result and the compile-status tail
	// (docs/knowledge/Common/server-prose-never-reaches-a-non-debug-diagnostic-field.md). clio's own words stay
	// outside the fence.
	private void ReportFailure(CompileBusinessProcessResult result) {
		logger.WriteError("CompileProcess failed: "
			+ (UntrustedText.Fenced(result.ErrorMessage) ?? "the server reported no detail."));
		List<CompileBusinessProcessError> listed = result.Errors.Take(MaxListedErrors).ToList();
		foreach (CompileBusinessProcessError error in listed) {
			string where = error.InThisProcess ? string.Empty : " [another schema of the package]";
			// Two fences, because each is capped: one fence over the location and the message cut a long CS1503
			// message short.
			string location = UntrustedText.Fenced($"{error.FileName}({error.Line},{error.Column}): {error.Code}")
				?? "(no location)";
			string message = UntrustedText.Fenced(error.Message) ?? "(no message)";
			logger.WriteError($"{location} {message}{where}");
		}
		int omitted = Math.Max(result.ErrorCount, result.Errors.Count) - listed.Count;
		if (omitted > 0) {
			logger.WriteError($"... and {omitted} more error(s) not listed.");
		}
		// Measured in a manual run (ENG-92711 TC-06): after a failed compile the agent fixed its code and compiled
		// again on the strength of the first answer. Each compile reloads the runtime for every user, so the
		// consent covers one call; the failure says so where the retry is decided.
		if (result.Compiled) {
			logger.WriteError("Fix what the errors above name, then ask the user again before the next compile: their "
				+ "answer covered this compile only, and every compile reloads the runtime for every user of the "
				+ "environment.");
		}
	}
}

/// <summary>
/// Request payload for compiling the package of one business process.
/// </summary>
/// <param name="ProcessName">Schema name (code) of the process.</param>
public sealed record CompileBusinessProcessRequest(string ProcessName);

/// <summary>
/// One compiler error, with the server's path stripped from the file.
/// </summary>
/// <param name="FileName">The generated file's name.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
/// <param name="Code">The compiler's code, for example <c>CS0103</c>.</param>
/// <param name="Message">The compiler's message.</param>
/// <param name="InThisProcess">True when the error is in the file generated for the requested process.</param>
public sealed record CompileBusinessProcessError(string? FileName, int Line, int Column, string? Code,
	string? Message, bool InThisProcess);

/// <summary>
/// What the server compiled for a process and what the compiler reported.
/// </summary>
/// <param name="Success">True when the compile succeeded, or the process needed no compile.</param>
/// <param name="ErrorMessage">Why the call failed.</param>
/// <param name="ProcessName">The process resolved.</param>
/// <param name="PackageName">The package compiled.</param>
/// <param name="PackageType"><c>general</c> or <c>assembly</c>.</param>
/// <param name="CompileRequired">False when the process carries no C#.</param>
/// <param name="Compiled">True when a compile ran.</param>
/// <param name="DurationMs">How long the compile took.</param>
/// <param name="Errors">The compiler errors reported, the process's own first; capped by the server.</param>
/// <param name="ErrorCount">The total number of errors, including any past the cap.</param>
public sealed record CompileBusinessProcessResult(bool Success, string? ErrorMessage, string? ProcessName,
	string? PackageName, string? PackageType, bool CompileRequired, bool Compiled, long DurationMs,
	IReadOnlyList<CompileBusinessProcessError> Errors, int ErrorCount);
