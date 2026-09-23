using System;
using System.Collections.Generic;

namespace Clio.Common.RuntimeAttachment;

/// <summary>Runs attachment dependencies without shell interpolation.</summary>
public interface IAttachmentProcess {
	/// <summary>Runs a bounded process, returning stdout or throwing a sanitized failure.</summary>
	string Run(string executable, IReadOnlyList<string> arguments, string input = null);
}

/// <inheritdoc/>
public class AttachmentProcess(IProcessExecutor executor) : IAttachmentProcess {
	/// <inheritdoc/>
	public string Run(string executable, IReadOnlyList<string> arguments, string input = null) {
		ProcessExecutionResult result = executor.ExecuteAndCaptureAsync(new(executable, "") {
			ArgumentList = arguments, StandardInput = input, SuppressErrors = true,
			MirrorOutputToLogger = false, Timeout = TimeSpan.FromMinutes(2),
			MaximumCapturedOutputCharacters = 4 * 1024 * 1024
		}).GetAwaiter().GetResult();
		if (result.ExitCode != 0 || result.TimedOut || result.Canceled) {
			throw new InvalidOperationException($"{executable} failed. {result.StandardError}");
		}
		return result.StandardOutput.Trim();
	}
}
