using System.Collections.Generic;

namespace Clio.Common
{
	#region Interface: ITemplateProvider

	public interface ITemplateProvider
	{

		#region Methods: Public

		string GetTemplate(string templateName);
		string GetTemplateWithoutTpl(string templateName);

		void CopyTemplateFolder(string templateCode, string destinationPath, string creatioVersion = "",
			string group = "", bool overrideFolder = true);

		/// <summary>
		/// Copies template files into an existing directory without overwriting files that are already present.
		/// </summary>
		/// <param name="templateCode">Template folder name.</param>
		/// <param name="destinationPath">Destination directory.</param>
		/// <param name="creatioVersion">Optional Creatio version for versioned templates.</param>
		/// <param name="group">Optional template group.</param>
		void CopyTemplateFolderIfMissing(string templateCode, string destinationPath, string creatioVersion = "",
			string group = "");
		
		void CopyTemplateFolder(string templateFolderName, string destinationPath, Dictionary<string, string> macrosValues);
		string[] GetTemplateDirectories(string templateCode);

		#endregion

	}

	#endregion
}
