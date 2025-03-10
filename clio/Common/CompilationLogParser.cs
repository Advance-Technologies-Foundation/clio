using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Clio.Common;

public interface ICompilationLogParser {

	/// <summary>
	/// Parses the Creatio compilation log from a JSON input string.
	/// </summary>
	/// <param name="jsonInput">The JSON input string containing the compilation log.</param>
	/// <returns>A formatted string with the compilation log details.</returns>
	string ParseCreatioCompilationLog(string jsonInput);

}

public class CompilationLogParser : ICompilationLogParser {

	#region Methods: Public

	public string ParseCreatioCompilationLog(string jsonInput){
		CreatioCompilationLogResponse errors = JsonSerializer.Deserialize<CreatioCompilationLogResponse>(jsonInput);

		List<string> errorMessages = errors.errors
			.Select(e => $"{e.fileName}({e.line},{e.column}): Error {e.errorNumber} : {e.errorText}")
			.ToList();
		string resultMessage
			= $"------- Finished building project: Succeeded: {errors.success}. Errors: {errors.errors.Length}.";
		return (string.Join("\r\n", errorMessages) + "\r\n" + resultMessage).Trim();
	}

	#endregion

}

public record CreatioCompilationLogResponse(CreatioCompilationError[] errors,
	int buildResult,
	bool success);

public record CreatioCompilationError(int line,
	int column,
	string errorNumber,
	string errorText,
	bool warning,
	string fileName);