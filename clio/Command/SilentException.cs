using System;
using System.Runtime.Serialization;

namespace Clio.Command
{
	[Serializable]
	public class SilentException : Exception
	{
		public SilentException() {
		}

		public SilentException(string message) : base(message) {
		}

		public SilentException(string message, Exception innerException) : base(message, innerException) {
		}

		[Obsolete("Obsolete")]
		protected SilentException(SerializationInfo info, StreamingContext context) : base(info, context) {
		}
	}
}