namespace Terrasoft.Configuration.Enrichment
{
	using System;
	using System.Collections.Generic;
	using System.Runtime.Serialization;
	using System.ServiceModel;
	using System.ServiceModel.Activation;
	using System.ServiceModel.Web;
	using System.Threading;
	using NLogExt;
	using Terrasoft.Common;
	using Terrasoft.Messaging.Common;
	using Terrasoft.Web.Common;

	#region Class: LoggerConfigService

	/// <summary>
	/// System logger ext configurator.
	/// </summary>
	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public class ATFLogService : BaseService//, System.Web.SessionState.IReadOnlySessionState
	{

		private enum ResetReason
		{
			Timer,
			NewListening,
			Manually
		}

		#region Constants: Private

		private const string AdminOperationName = "CanUseLoggerDashboard";

		#endregion

		#region Fields: Private

		private static readonly Timer _resetTimer = new Timer(_ => Reset(ResetReason.Timer));

		#endregion

		#region Methods: Private

		private static void Reset(ResetReason reason) {
			var prevChannelId = AsyncLoggerListener.CurrentChannelId;
			AsyncLoggerListener.StopListening();
			if (prevChannelId != Guid.Empty) {
				SendMessage(new LogMessage(
					$"Listening stopped. Caused by reason = {reason.ToString()}", "Telemetry"), prevChannelId);
			}
		}

		private static void SendMessage(LogMessage message, Guid sysAdminUnitId) {
			var logPortion = new List<LogMessage> { { message } };
			var telemetryMessage = new TelemetryMessage { LogPortion = logPortion };
			string messageBody = ServiceStackTextHelper.Serialize(telemetryMessage);
			IMsgChannel channel = MsgChannelManager.Instance.FindItemByUId(sysAdminUnitId);
			if (channel == null) {
				return;
			}
			/* If we subscribe on messages of all loggers (or just Messaging), we send message for log event,
			 Messaging logger subscribes on this sending and generates its own logging event, which we take and
			 proceed message and so on... */
			if (message.Logger == "Messaging" && message.Message == $"Posting to physical channel [{channel.Id}]") {
				return;
			}
			var simpleMessage = new SimpleMessage {
				Id = Guid.NewGuid(),
				Body = messageBody
			};
			simpleMessage.Header.Sender = "TelemetryService";
			channel.PostMessage(simpleMessage);
		}

		private void CheckOperationRights() {
			var securityEngine = UserConnection.DBSecurityEngine;
			securityEngine.CheckCanExecuteOperation(AdminOperationName);
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Starts the log broadcast.
		/// </summary>
		/// <param name="loggerName">Name of the logger.</param>
		/// <param name="logLevel">The log level.</param>
		/// <param name="bufferSize">Size of the buffer.</param>
		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped,
			ResponseFormat = WebMessageFormat.Json)]
		public void StartLogBroadcast(string loggerPattern, string logLevelStr, int bufferSize = 3) {
			Guid sysAdminUnitId = UserConnection.CurrentUser.Id;
			CheckOperationRights();
			Reset(ResetReason.NewListening);
			AsyncLoggerListener.StartListening(sysAdminUnitId, SendMessage, loggerPattern, logLevelStr, bufferSize);
			_resetTimer.Change(TimeSpan.FromHours(3), Timeout.InfiniteTimeSpan);
			SendMessage(new LogMessage(
				$"Listening started for logs by '{loggerPattern}' logger pattern with minimal level {logLevelStr}",
				"Telemetry"), sysAdminUnitId);
		}

		/// <summary>
		/// Gets the active logger list.
		/// </summary>
		/// <returns></returns>
		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped,
			ResponseFormat = WebMessageFormat.Json)]
		public List<string> GetActiveLoggerList() {
			CheckOperationRights();
			var rulePatterns = AsyncLoggerListener.GetRulePatterns();
			return new List<string>(rulePatterns);
		}

		/// <summary>
		/// Resets configuration to default state (configured by config of logging provider) and stops event
		/// triggering.
		/// </summary>
		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped,
			ResponseFormat = WebMessageFormat.Json)]
		public void ResetConfiguration() {
			CheckOperationRights();
			Guid sysAdminUnitId = UserConnection.CurrentUser.Id;
			if (AsyncLoggerListener.IsStarted && AsyncLoggerListener.CurrentChannelId != sysAdminUnitId) {
				SendMessage(new LogMessage(
					$"Can't stop listening, cause session is started by user '{AsyncLoggerListener.CurrentChannelId}'",
					"Telemetry"), sysAdminUnitId);
				return;
			}
			Reset(ResetReason.Manually);
			_resetTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}

		#endregion

	}

	#endregion

	#region Class: TelemetryMessage

	[DataContract]
	public class TelemetryMessage
	{
		[DataMember(Name = "logPortion")]
		public List<LogMessage> LogPortion {
			get;
			set;
		}

		[DataMember(Name = "cpu")]
		public int Cpu {
			get;
			set;
		}

		[DataMember(Name = "ramMb")]
		public int RamMb {
			get;
			set;
		}
	}

	#endregion

}
