//
// using System;
// using FluentAssertions;
// //using MrktApolloApp.Services;
// using Newtonsoft.Json;
// using NSubstitute;
// using NUnit.Framework;
// using Terrasoft.Configuration.Tests;
// using Terrasoft.Core;
// using Terrasoft.Core.Factories;
// using Terrasoft.Messaging.Common;
// using Terrasoft.UnitTest;
// using WebSocketDto = MrktApolloApp.Dto.WebSocket;
//
// namespace cliogate.tests
// {
// 	[Author("Kirill Krylov", "k.krylov@creatio.com")]
// 	[Category("UnitTests")]
// 	[MockSettings(RequireMock.All)]
// 	[TestFixture]
// 	public class WebSocketTests : BaseMarketplaceTestFixture
// 	{
//
// 		#region Fields: Private
//
// 		private static readonly Func<IMsg, string, bool> CompareMessages = (msg, expected) => {
//
// 			msg.Should().NotBeNull();
// 			expected.Should().NotBeNull();
// 			WebSocketDto jBody = JsonConvert.DeserializeObject<WebSocketDto>(msg.Body.ToString());
// 			WebSocketDto jExpected = JsonConvert.DeserializeObject<WebSocketDto>(expected);
//
// 			return
// 				jBody.CommandName == jExpected.CommandName &&
// 				jBody.SchemaName == jExpected.SchemaName &&
// 				jBody.RecordId == jExpected.RecordId &&
// 				jBody.Message == jExpected.Message;
// 		};
//
// 		private IMsgChannelManager _msgChannelManagerMock;
// 		private IWebSocket _sut;
//
// 		#endregion
//
// 		#region Methods: Protected
//
// 		protected override void SetUp(){
// 			base.SetUp();
// 			_msgChannelManagerMock = Substitute.For<IMsgChannelManager>();
// 			Guid contactId = Guid.NewGuid();
// 			UserConnection.SetupCurrentUser(contactId, "Supervisor");
// 			ClassFactory.RebindWithFactoryMethod(() => (UserConnection)UserConnection);
// 			_sut = new WebSocket(_msgChannelManagerMock);
// 		}
//
// 		#endregion
//
// 		[Test]
// 		public void CanSendWebSocketMessage_WithUserConnection(){
// 			//Arrange
// 			const string senderName = "senderName";
// 			WebSocketDto expectedBody = new WebSocketDto(
// 				"commandName", "schemaName", Guid.NewGuid(), "message");
//
// 			Guid currentUserId = Guid.NewGuid();
// 			UserConnection.CurrentUser.Id = currentUserId;
// 			IMsgChannel channelMock = Substitute.For<IMsgChannel>();
// 			
// 			_msgChannelManagerMock
// 				.FindItemByUId(Arg.Is(currentUserId))
// 				.Returns(channelMock);
//
// 			_sut = new WebSocket(_msgChannelManagerMock);
// 			
// 			//Act
// 			_sut.PostMessage(senderName, expectedBody.CommandName, expectedBody.SchemaName, 
// 				expectedBody.RecordId, expectedBody.Message, UserConnection);
// 			
// 			//Assert
// 			_msgChannelManagerMock
// 				.Received(1)
// 				.FindItemByUId(currentUserId);
//
// 			channelMock.Received(1)
// 				.PostMessage(Arg.Is<IMsg>(msg =>
// 					msg.Header.Sender == senderName && CompareMessages(msg, expectedBody.ToString())));
// 		}
// 		
// 		
// 		[Test]
// 		public void CanSendWebSocketMessage_WithOutUserConnection(){
// 			//Arrange
// 			const string senderName = "senderName";
// 			WebSocketDto expectedBody = new WebSocketDto(
// 				"commandName", "schemaName", Guid.NewGuid(), "message");
//
// 			Guid currentUserId = Guid.NewGuid();
// 			UserConnection.CurrentUser.Id = currentUserId;
// 			IMsgChannel channelMock = Substitute.For<IMsgChannel>();
// 			
// 			_msgChannelManagerMock
// 				.FindItemByUId(Arg.Is(currentUserId))
// 				.Returns(channelMock);
//
// 			//Act
// 			_sut.PostMessage(senderName, expectedBody.CommandName, expectedBody.SchemaName, 
// 				expectedBody.RecordId, expectedBody.Message);
// 			
// 			//Assert
// 			_msgChannelManagerMock
// 				.Received(1)
// 				.FindItemByUId(currentUserId);
//
// 			channelMock.Received(1)
// 				.PostMessage(Arg.Is<IMsg>(msg =>
// 					msg.Header.Sender == senderName && CompareMessages(msg, expectedBody.ToString())));
// 		}
//
// 		[Test]
// 		public void CanSendWebSocketMessage_WithNullUserConnection(){
// 			//Arrange
// 			const string senderName = "senderName";
// 			WebSocketDto expectedBody = new WebSocketDto(
// 				"commandName", "schemaName", Guid.NewGuid(), "message");
//
// 			Guid currentUserId = Guid.NewGuid();
// 			UserConnection.CurrentUser.Id = currentUserId;
// 			IMsgChannel channelMock = Substitute.For<IMsgChannel>();
// 			
// 			_msgChannelManagerMock
// 				.FindItemByUId(Arg.Is(currentUserId))
// 				.Returns(channelMock);
//
// 			//Act
// 			_sut.PostMessage(senderName, expectedBody.CommandName, expectedBody.SchemaName, 
// 				expectedBody.RecordId, expectedBody.Message, null);
// 			
// 			//Assert
// 			_msgChannelManagerMock
// 				.Received(1)
// 				.FindItemByUId(currentUserId);
//
// 			channelMock.Received(1)
// 				.PostMessage(Arg.Is<IMsg>(msg =>
// 					msg.Header.Sender == senderName && CompareMessages(msg, expectedBody.ToString())));
// 		}
// 		
// 		[Test]
// 		public void CanSendWebSocketMessageToAll(){const string senderName = "senderName";
// 			//Arrange
// 			WebSocketDto expectedBody = new WebSocketDto(
// 				"commandName", "schemaName", Guid.NewGuid(), "message");
// 			
// 			//Act
// 			_sut.PostMessageToAll(senderName, expectedBody.CommandName, expectedBody.SchemaName, 
// 				expectedBody.RecordId, expectedBody.Message);
// 			
// 			//Assert
// 			_msgChannelManagerMock.Received(1)
// 				.PostToAll(Arg.Is<IMsg>(msg =>
// 					msg.Header.Sender == senderName && CompareMessages(msg, expectedBody.ToString())));
// 		}
// 	}
// }