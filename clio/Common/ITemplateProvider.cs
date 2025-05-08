using System.Collections.Generic;

namespace Clio.Common;

#region Interface: ITemplateProvider

public interface ITemplateProvider
{

    #region Methods: Public

    void CopyTemplateFolder(string templateCode, string destinationPath, string creatioVersion = "",
        string group = "", bool overrideFolder = true);

    void CopyTemplateFolder(string templateFolderName, string destinationPath, Dictionary<string, string> macrosValues);

    string GetTemplate(string templateName);

    string[] GetTemplateDirectories(string templateCode);

    #endregion

}

#endregion
