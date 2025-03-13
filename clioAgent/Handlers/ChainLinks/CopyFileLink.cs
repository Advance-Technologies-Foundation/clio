using System.IO.Abstractions;
using clioAgent.ChainOfResponsibility;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class CopyFileLink(IFileSystem fileSystem, Settings settings) : AbstractChainLink<RequestContext, ResponseContext> {

	protected override bool ShouldProcess(RequestContext request){
		return fileSystem.File.Exists(request.CreatioBuildZipPath);
	}

	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;
	
	protected override Task<ResponseContext> ProcessAsync(RequestContext request) =>
		CopyFileToWorkingDirectory(request.CreatioBuildZipPath)
			.Match<Task<ResponseContext>>(onValue: success => {
					request.ZipFile = success;
					return Task.FromResult(new ResponseContext {
						Result = Result.Success,
						ZipFile = request.ZipFile
					});
				},
				onError: err => Task.FromResult(new ResponseContext {
					Result = err
				}));

	/// <summary>
	///  Copies a file to the working directory.
	/// </summary>
	/// <param name="from">The path of the file to be copied.</param>
	/// <returns>
	///  An <see cref="ErrorOr{TValue}" /> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	///  If the file does not exist after copying, an error is returned.
	/// </remarks>
	private ErrorOr<IFileInfo> CopyFileToWorkingDirectory(string from){
		if (string.IsNullOrEmpty(from)) {
			return Error.Validation("CopyFileToWorkingDirectory.InvalidPath",
				"Invalid path, value cannot be null or empty");
		}
		
		string fileName = Path.GetFileName(from);
		string to = Path.Combine(settings.WorkingDirectoryPath, fileName);
		IFileInfo toFileInfo = fileSystem.FileInfo.New(to);
		if (toFileInfo.Exists) {
			return toFileInfo.ToErrorOr();
		}
		
		try {
			if (!fileSystem.Directory.Exists(settings.WorkingDirectoryPath)) {
				fileSystem.Directory.CreateDirectory(settings.WorkingDirectoryPath);
			}

			IFileInfo fromFileInfo = fileSystem.FileInfo.New(from);
			if (!fromFileInfo.Exists) {
				return Error.Failure("CopyFileToWorkingDirectory.FileNotFound",
					$"File {fromFileInfo.FullName} not found");
			}

			if (toFileInfo.Exists) {
				toFileInfo.Delete();
			}

			fileSystem.File.Copy(fromFileInfo.FullName, toFileInfo.FullName);
			IFileInfoFactory fiFactory = fileSystem.FileInfo;
			IFileInfo fileInfo = fiFactory.New(to);
			return fileInfo.Exists
				? fileInfo.ToErrorOr()
				: Error.Failure("File not found", "File not found");
		}
		catch (Exception e) {
			return Error.Failure("CopyFileToWorkingDirectory.Exception", e.Message);
		}
	}
}
