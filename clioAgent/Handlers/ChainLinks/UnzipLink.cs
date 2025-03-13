using System.IO.Abstractions;
using System.IO.Compression;
using clioAgent.ChainOfResponsibility;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class UnzipLink(IFileSystem fileSystem, Settings settings) : AbstractChainLink<RequestContext, ResponseContext> {

	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;

	protected override bool ShouldProcess(RequestContext request) => 
		request.ZipFile is not null &&request.ZipFile.Exists;
	
	protected override Task<ResponseContext> ProcessAsync(RequestContext request) => 
		UnzipFile(request.ZipFile!)
			.Match<Task<ResponseContext>>(
				onValue: success => {
					request.UnzippedDirectory = success;
					return Task.FromResult(new ResponseContext {
						Result = Result.Success,
						UnzippedDirectory = success
					});
				},
				onError: err => Task.FromResult(new ResponseContext {
					Result = err
				}));
	
	/// <summary>
	///  Unzips the specified zip file to a directory within the working directory.
	/// </summary>
	/// <param name="zipFile">The zip file to be extracted.</param>
	/// <returns>
	///  An <see cref="ErrorOr{Success}" /> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	///  If the extraction fails, an error is returned with the exception details.
	/// </remarks>
	private ErrorOr<IDirectoryInfo> UnzipFile(IFileSystemInfo zipFile){
		try {
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(zipFile.Name);
			string extractPath = Path.Combine(settings.WorkingDirectoryPath, fileNameWithoutExtension);
			IDirectoryInfoFactory diFactory = fileSystem.DirectoryInfo;
			IDirectoryInfo directoryInfo = diFactory.New(extractPath);
			if(directoryInfo.Exists) {
				return directoryInfo.ToErrorOr();
			}
			directoryInfo.Create();
			ZipFile.ExtractToDirectory(Path.Combine(zipFile.FullName), extractPath);
			
			return directoryInfo.Exists
				? directoryInfo.ToErrorOr()
				: Error.Failure("UnzipFile.DirectoryNotFound", "Directory not found");
		}
		catch (Exception e) {
			return Error.Failure("UnzipFile.Exception", e.Message);
		}
	}
}
