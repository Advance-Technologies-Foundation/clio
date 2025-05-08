using System.Collections.Generic;

namespace Clio.Common;

public interface ITemplateProvider
{
    string GetTemplate(string templateName);

    void CopyTemplateFolder(string templateCode, string destinationPath, string creatioVersion = "",
        string group = "", bool overrideFolder = true);

    void CopyTemplateFolder(string templateFolderName, string destinationPath, Dictionary<string, string> macrosValues);

    string[] GetTemplateDirectories(string templateCode);
}
