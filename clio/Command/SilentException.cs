using System;
using System.Runtime.Serialization;

namespace Clio.Command
{
	[Serializable]
	internal class SilentException : Exception
	{
		public SilentException() {
		}

		public SilentException(string message) : base(message) {
		}

		public SilentException(string message, Exception innerException) : base(message, innerException) {
		}

		protected SilentException(SerializationInfo info, StreamingContext context) : base(info, context) {
		}
	}
}