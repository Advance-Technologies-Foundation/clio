namespace Clio.Common
{
	public interface IWorkingDirectoriesProvider
	{
		string ExecutingDirectory { get; }
		string TemplateDirectory { get; }
		string BaseTempDirectory { get; }
		string GetTemplatePath(string templateName);
		string CreateTempDirectory();
		void SafeDeleteTempDirectory(string tempDirectory);
	}
}