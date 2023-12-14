namespace Clio.Common
{
	#region Interface: ITemplateProvider

	public interface ITemplateProvider
	{

		#region Methods: Public

		string GetTemplate(string templateName);

		void CopyTemplateFolder(string templateCode, string destinationPath, string creatioVersion = "",
			string group = "", bool overrideFolder = true);
		string[] GetTemplateDirectories(string templateCode);

		#endregion

	}

	#endregion
}