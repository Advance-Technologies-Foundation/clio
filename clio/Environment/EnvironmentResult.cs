using System.Collections.Generic;
using System.IO;

namespace Clio.UserEnvironment
{
	class EnvironmentResult : IResult
	{

		private readonly List<string> _messages = new List<string>();

		public void ShowMessagesTo(TextWriter writer) {
			_messages.ForEach(writer.WriteLine);
		}

		public void AppendMessage(string message) {
			_messages.Add(message);
		}
	}
}
