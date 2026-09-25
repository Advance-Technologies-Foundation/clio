using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Options for compiling the package a business process lives in, via the ProcessDesignService package.
/// Consumed by the MCP <c>compile-creatio</c> tool's <c>process-name</c> mode, which sets these properties
/// directly.
/// </summary>
// CompileProcess first exists in the 1.6.6.32 archive, so an older package answers the route with a 404 rather
// than a contract error; the literal turns that into "your package is behind".
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.6.6.32",
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class CompileBusinessProcessOptions : EnvironmentOptions {
	/// <summary>Schema name (code) of the process. Provide exactly one of <see cref="ProcessName"/> or <see cref="ProcessUid"/>.</summary>
	public string ProcessName { get; set; } = string.Empty;

	/// <summary>Schema UId of the process. Provide exactly one of <see cref="ProcessName"/> or <see cref="ProcessUid"/>.</summary>
	public string ProcessUid { get; set; } = string.Empty;
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
	/// configuration assembly, which took 3.5 minutes on a local stand; one attempt, never retried, because a
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
		if (string.IsNullOrWhiteSpace(request.ProcessName) && string.IsNullOrWhiteSpace(request.ProcessUid)) {
			throw new ArgumentException("Either a process name or uid is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));

		var requestObject = new JsonObject();
		if (!string.IsNullOrWhiteSpace(request.ProcessName)) {
			requestObject["name"] = request.ProcessName;
		}
		if (!string.IsNullOrWhiteSpace(request.ProcessUid)) {
			requestObject["uid"] = request.ProcessUid;
		}

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileProcess, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the request is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = requestObject }.ToJsonString();
		string identity = string.IsNullOrWhiteSpace(request.ProcessName) ? request.ProcessUid : request.ProcessName;
		logger.WriteInfo(
			$"Compiling the package of process '{identity}' on '{environmentName}'. A compile reloads the runtime "
			+ "for every user of the environment and usually takes a few minutes...");

		string responseBody = client.ExecutePostRequest(url, requestBody, CompileTimeoutMs);
		ResponseEnvelope? envelope;
		try {
			envelope = JsonSerializer.Deserialize<ResponseEnvelope>(responseBody, JsonOptions);
		} catch (JsonException exception) {
			// The same guard its siblings carry: a body that is not this envelope makes the parser throw about a
			// .NET type and a byte offset. Here the unknown is whether a compile RAN, so the message says where to
			// read that rather than inviting a retry the platform would refuse while a compile is running.
			throw new InvalidOperationException(
				"CompileProcess returned a response clio could not read, so whether the package was compiled is "
				+ "UNKNOWN. Read last-compilation-log for this environment before compiling again. The parser "
				+ "detail is on the inner exception.",
				exception);
		}

		ResultDto result = (envelope
				?? throw new InvalidOperationException("CompileProcess returned an empty response."))
			.Result
			?? throw new InvalidOperationException("CompileProcess returned an unexpected response shape.");
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

			bool hasName = !string.IsNullOrWhiteSpace(options.ProcessName);
			bool hasUid = !string.IsNullOrWhiteSpace(options.ProcessUid);
			if (hasName == hasUid) {
				throw new InvalidOperationException(hasName
					? "Provide only one of process-name or process-uid, not both."
					: "One of process-name or process-uid is required.");
			}

			CompileBusinessProcessResult result = compileBusinessProcessService.Compile(options.Environment,
				new CompileBusinessProcessRequest(options.ProcessName, options.ProcessUid));
			if (!result.Success) {
				ReportFailure(result);
				return 1;
			}

			if (!result.CompileRequired) {
				logger.WriteInfo(
					$"Process '{result.ProcessName}' carries no C# (no script task and no process methods), so "
					+ "nothing was compiled and nothing needs to be.");
				return 0;
			}

			logger.WriteInfo(
				$"Compiled package '{result.PackageName}' ({DescribePackageType(result.PackageType)}) in "
				+ $"{TimeSpan.FromMilliseconds(result.DurationMs):m\\:ss}. Process '{result.ProcessName}' now runs "
				+ "the code it was saved with.");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static string DescribePackageType(string? packageType) =>
		string.Equals(packageType, "assembly", StringComparison.OrdinalIgnoreCase)
			? "its own assembly"
			: "the shared configuration assembly";

	// One line per error, the process's own first (the server orders them), so the caller reads what it has to
	// fix before what another schema of the package broke.
	private void ReportFailure(CompileBusinessProcessResult result) {
		logger.WriteError(result.ErrorMessage ?? "CompileProcess failed.");
		foreach (CompileBusinessProcessError error in result.Errors) {
			string where = error.InThisProcess ? string.Empty : " [another schema of the package]";
			logger.WriteError(
				$"{error.FileName}({error.Line},{error.Column}): {error.Code} {error.Message}{where}");
		}
		int omitted = result.ErrorCount - result.Errors.Count;
		if (omitted > 0) {
			logger.WriteError($"... and {omitted} more error(s) not listed.");
		}
	}
}

/// <summary>
/// Request payload for compiling the package of one business process.
/// </summary>
/// <param name="ProcessName">Schema name (code) of the process.</param>
/// <param name="ProcessUid">Schema UId of the process.</param>
public sealed record CompileBusinessProcessRequest(string ProcessName, string ProcessUid);

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
/// <param name="Success">True when the process now runs the code it was saved with, or needed no compile.</param>
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
