using Cliogate.Services;

namespace cliogate.Files.cs {

	public enum MessageType {
		Info,
		Error,
		Warning
	}

	public interface ILiveLogger {
		void Log(string message);
		void LogInfo(string message);  // New method
		void LogError(string message); // New method
		void LogWarn(string message);  // New method
	}

	public class LiveLogger : ILiveLogger {
		private readonly IWebSocket _webSocket;

		internal LiveLogger(IWebSocket webSocket) {
			_webSocket = webSocket;
		}

		public void Log(string message) {
			if (message.StartsWith("[INF]")) {
				LogInfo(message.Substring(5)); // Remove the log type from the message
			} else if (message.StartsWith("[ERR]")) {
				LogError(message.Substring(5)); // Remove the log type from the message
			} else if (message.StartsWith("[WAR]")) {
				LogWarn(message.Substring(5)); // Remove the log type from the message
			} else {
				_webSocket.PostMessageToAll("Clio", "Show logs", message);
			}
		}

		private void Log(string message, MessageType messageType) {
			string messageTag;
			switch (messageType) {
				case MessageType.Info:
					messageTag = "<span style='color:green'>[INF]</span>";
					break;
				case MessageType.Error:
					messageTag = "<span style='color:red'>[ERR]</span>";
					break;
				case MessageType.Warning:
					messageTag = "<span style='color:yellow'>[WAR]</span>";
					break;
				default:
					messageTag = "[UNK]";
					break;
			}
			string colorizedMessage = $"{messageTag} - {message}";
			_webSocket.PostMessageToAll("Clio", "Show logs", colorizedMessage);
		}

		public void LogInfo(string message) {
			Log(message, MessageType.Info);
		}

		public void LogError(string message) {
			Log(message, MessageType.Error);
		}

		public void LogWarn(string message) {
			Log(message, MessageType.Warning);
		}
	}
}
