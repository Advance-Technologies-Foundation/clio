using System.IO;

namespace Clio.UserEnvironment
{
	internal interface IResult
	{
		void ShowMessagesTo(TextWriter writer);
		void AppendMessage(string message);
	}

	internal interface ICreatioEnvironment
	{
		string GetRegisteredPath();
		IResult UserRegisterPath(string path);
		IResult MachineRegisterPath(string path);

		string GetAssemblyFolderPath();
	}
}
