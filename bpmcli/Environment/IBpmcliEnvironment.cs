using System.IO;

namespace bpmcli.environment
{
	internal interface IResult
	{
		void ShowMessagesTo(TextWriter writer);
		void AppendMessage(string message);
	}

	internal interface IBpmcliEnvironment
	{
		string GetRegisteredPath();
		IResult UserRegisterPath(string path);
		IResult MachineRegisterPath(string path);

	}
}
