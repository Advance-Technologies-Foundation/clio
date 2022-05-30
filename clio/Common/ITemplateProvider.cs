namespace Clio.Common
{

	#region Interface: ITemplateProvider

	public interface ITemplateProvider
	{

		#region Methods: Public

		string GetTemplate(string templateName);
		void CopyTemplateFolder(string templateFolderName, string destinationPath);

		#endregion

	}

	#endregion

}