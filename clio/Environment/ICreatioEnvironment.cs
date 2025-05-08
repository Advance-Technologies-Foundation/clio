using System.IO;

namespace Clio.UserEnvironment;

internal interface IResult
{

    #region Methods: Public

    void AppendMessage(string message);

    void ShowMessagesTo(TextWriter writer);

    #endregion

}

internal interface ICreatioEnvironment
{

    #region Methods: Public

    string GetAssemblyFolderPath();

    string GetRegisteredPath();

    IResult MachineRegisterPath(string path);

    IResult UserRegisterPath(string path);

    #endregion

}
