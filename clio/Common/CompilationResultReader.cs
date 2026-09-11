using System;

namespace Clio.Common;

/// <summary>
/// Reads the configuration compilation result Creatio persisted for the last build.
/// </summary>
/// <remarks>
/// Extracted from <c>LastCompilationLogCommand</c> because it has a second caller: a configuration
/// build determines its verdict from this endpoint whenever the compile request itself produced no
/// response, which on current platforms is the normal case rather than the exception.
/// <para>
/// <b>The payload carries no timestamp.</b> It answers "what was the last compilation result", not
/// "what was the result of the build I started", so a caller must establish by other means that the
/// build it is asking about has actually ended. <c>CompileConfigurationCommand</c> does that by
/// reading it only after it has observed the runtime reload that ends a configuration build.
/// </para>
/// </remarks>
public interface ICompilationResultReader {

	/// <summary>
	/// Reads the persisted compilation result and returns it in typed form.
	/// </summary>
	/// <returns>The typed result.</returns>
	/// <exception cref="Exception">The environment is unreachable, or the payload is not the expected shape.</exception>
	CreatioCompilationLogResponse Read();

	/// <summary>
	/// Reads the persisted compilation result as the raw JSON Creatio returned.
	/// </summary>
	/// <param name="timeoutMs">Request timeout; <see langword="null"/> uses the default read timeout.</param>
	/// <returns>The raw response body.</returns>
	string ReadRaw(int? timeoutMs = null);

	/// <summary>
	/// Reads the persisted compilation result, returning <see langword="null"/> instead of throwing when the
	/// environment cannot answer.
	/// </summary>
	/// <param name="timeoutMs">Request timeout; <see langword="null"/> uses the default read timeout.</param>
	/// <returns>The typed result, or <see langword="null"/> when it could not be read.</returns>
	/// <remarks>
	/// For callers on a path where a failed verdict read must not replace the outcome they already know —
	/// a build that demonstrably ran should not be reported as a clio error because the verdict lookup
	/// that follows it failed.
	/// <para>
	/// This endpoint is ALSO the one availability is measured on, though the sampling itself now belongs to
	/// <see cref="IEnvironmentAvailabilityProbe"/> and no longer to this reader. A configuration build ends
	/// by reloading the application, and this endpoint stops answering while that happens — measured on a
	/// live stand as a 44-second outage across an application-pool recycle, during which the
	/// compilation-history channel reported no failure at all. So this, and not the history poller, is where
	/// the reload is visible. The probe samples it on a short, actually-enforced budget, because during the
	/// outage the request hangs until its timeout rather than failing fast and the default read timeout
	/// would sample about once a minute and miss the whole outage.
	/// </para>
	/// </remarks>
	CreatioCompilationLogResponse TryRead(int? timeoutMs = null);

}

/// <inheritdoc cref="ICompilationResultReader"/>
public class CompilationResultReader : ICompilationResultReader {

	#region Constants: Private

	// The verdict read is a short GET against an application that has just finished reloading. It is
	// bounded rather than left at the client default of Timeout.Infinite: a caller reaches it only after
	// the build has ended, so an unbounded read here would reintroduce, at the last step, exactly the
	// never-terminating wait this whole path exists to remove.
	internal const int ReadTimeoutMs = 60_000;

	#endregion

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ICompilationLogParser _compilationLogParser;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="CompilationResultReader"/> class.
	/// </summary>
	/// <param name="applicationClient">Client for the target environment.</param>
	/// <param name="serviceUrlBuilder">Builds the endpoint URL for the target environment.</param>
	/// <param name="compilationLogParser">Parses Creatio's compilation-result payload.</param>
	public CompilationResultReader(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
		ICompilationLogParser compilationLogParser) {
		applicationClient.CheckArgumentNull(nameof(applicationClient));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		compilationLogParser.CheckArgumentNull(nameof(compilationLogParser));
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_compilationLogParser = compilationLogParser;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public string ReadRaw(int? timeoutMs = null) =>
		_applicationClient.ExecuteGetRequest(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.LastCompilationResult),
			timeoutMs ?? ReadTimeoutMs);

	/// <inheritdoc/>
	public CreatioCompilationLogResponse Read() =>
		_compilationLogParser.DeserializeCreatioCompilationLog(ReadRaw());

	/// <inheritdoc/>
	public CreatioCompilationLogResponse TryRead(int? timeoutMs = null) {
		try {
			return _compilationLogParser.DeserializeCreatioCompilationLog(ReadRaw(timeoutMs));
		} catch (Exception) {
			// Deliberately broad: every failure mode here - unreachable host, an HTML login page, a payload
			// shape the parser rejects - means the same thing to the caller, which is "the environment did
			// not give me a verdict". The caller decides what to do with that; none of them benefit from
			// telling the difference, and all of them break if this throws.
			return null;
		}
	}

	#endregion

}
