using System.IO.Abstractions;
using clioAgent.ChainOfResponsibility;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class TemplateLink(IFileSystem fileSystem) : AbstractChainLink<RequestContext, ResponseContext> {

	protected override bool ShouldProcess(RequestContext request) => 
		fileSystem.File.Exists(request.CreatioBuildZipPath);

	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;

	protected override Task<ResponseContext> ProcessAsync(RequestContext request) => 
		GenerateTemplateName(request.CreatioBuildZipPath)
			.Match<Task<ResponseContext>>(
				onValue: success => {
					request.TemplateName = success;
					return Task.FromResult(new ResponseContext {
						Result = Result.Success,
						TemplateName = request.TemplateName
					});
				},
				onError: err => Task.FromResult(new ResponseContext {
					Result = err
				}));
	
	private ErrorOr<string> GenerateTemplateName(string creatioBuildZipPath){
		
		string templateName = string.Empty;
		//from is a fileName
		string fileName = Path.GetFileNameWithoutExtension(creatioBuildZipPath);
		
		//TemplateName should be limited to 63 caracters
		//Should include version,
		//and build name
		(Version version, string fileNameWithoutVersion) f = GetVersionFromFileName(fileName);
		var fileNameWithoutVersion = f.fileNameWithoutVersion;
		
		
		bool isNet8 = fileNameWithoutVersion.Contains("Net8", StringComparison.CurrentCultureIgnoreCase);
		bool isNet6 = fileNameWithoutVersion.Contains("Net6", StringComparison.CurrentCultureIgnoreCase);
		bool isNetFramework = !(isNet6 || isNet8);
		
		var g = Guid.CreateVersion7();
		
		
		return templateName;
	}
	
	
	private (Version version, string fileNameWithoutVersion) GetVersionFromFileName(string fileName){
		
		var i = fileName.IndexOf('_', StringComparison.OrdinalIgnoreCase);
		string vString = fileName[..i];
		bool isVersion = Version.TryParse(vString, out Version? version);
		return isVersion 
			? (version!, fileName[(i + 1)..])
			: (new Version(0, 0, 0, 0), fileName);
		
	}
	
}
