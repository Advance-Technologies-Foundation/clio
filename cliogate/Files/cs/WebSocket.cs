using System;
using Terrasoft.Core;
using Terrasoft.Core.Factories;
using Terrasoft.Messaging.Common;

namespace Cliogate.Services
{
	internal interface IWebSocket
	{

		#region Methods: Public

		void PostMessage(string senderName, string commandName, string message,
			UserConnection userConnection);

		void PostMessage(string senderName, string commandName, string message);

		void PostMessageToAll(string senderName, string commandName, string message);

		#endregion

	}

	internal class WebSocket : IWebSocket
	{

		#region Fields: Private

		private readonly IMsgChannelManager _msgChannelManager;

		#endregion

		#region Constructors: Public

		public WebSocket(){
			_msgChannelManager = MsgChannelManager.Instance;
		}

		#endregion

		#region Methods: Private

		private static IMsg CreateMessage(string sender, string msg){
			IMsg simpleMessage = new SimpleMessage {
				Id = Guid.NewGuid(),
				Body = msg
			};
			simpleMessage.Header.Sender = sender;
			return simpleMessage;
		}

		#endregion

		#region Methods: Public

		public void PostMessage(string senderName, string commandName, string message,
			UserConnection userConnection){
			if (userConnection == null) {
				userConnection = ClassFactory.Get<UserConnection>();
			}

			IMsgChannel userChannel = _msgChannelManager.FindItemByUId(userConnection.CurrentUser.Id);
			string msgText = new Dto.WebSocket(commandName, message).ToString();
			IMsg msg = CreateMessage(senderName, msgText);
			userChannel.PostMessage(msg);
		}

		public void PostMessage(string senderName, string commandName, string message) =>
			PostMessage(senderName, commandName, message, ClassFactory.Get<UserConnection>());

		public void PostMessageToAll(string senderName, string commandName, string message){
			string msgText = new Dto.WebSocket(commandName, message).ToString();
			IMsg msg = CreateMessage(senderName, msgText);
			_msgChannelManager.PostToAll(msg);
		}

		#endregion

	}
}