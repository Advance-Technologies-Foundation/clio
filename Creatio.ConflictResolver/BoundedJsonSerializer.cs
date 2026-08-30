using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Creatio.ConflictResolver;

internal static class BoundedJsonSerializer
{
	public static string Serialize<T>(T value, JsonSerializerOptions options)
	{
		var buffer = new BoundedBufferWriter(OutputBudget.MaximumBytes);
		using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions {
			Indented = options.WriteIndented,
			Encoder = options.Encoder
		}))
		{
			JsonSerializer.Serialize(writer, value, options);
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray());
	}

	private sealed class BoundedBufferWriter(int maximumBytes) : IBufferWriter<byte>
	{
		private byte[] _buffer = new byte[Math.Min(4096, maximumBytes)];
		private int _writtenCount;

		public ReadOnlySpan<byte> WrittenSpan => new(_buffer, 0, _writtenCount);

		public void Advance(int count)
		{
			if (count < 0 || _writtenCount + count > _buffer.Length)
			{
				throw new MergeOutputLimitExceededException();
			}
			_writtenCount += count;
		}

		public Memory<byte> GetMemory(int sizeHint = 0)
		{
			EnsureCapacity(sizeHint);
			return new Memory<byte>(_buffer, _writtenCount, _buffer.Length - _writtenCount);
		}

		public Span<byte> GetSpan(int sizeHint = 0)
		{
			EnsureCapacity(sizeHint);
			return new Span<byte>(_buffer, _writtenCount, _buffer.Length - _writtenCount);
		}

		private void EnsureCapacity(int sizeHint)
		{
			var requested = Math.Max(1, sizeHint);
			if (_writtenCount + requested > maximumBytes)
			{
				throw new MergeOutputLimitExceededException();
			}

			if (_writtenCount + requested <= _buffer.Length)
			{
				return;
			}

			var newLength = Math.Min(
				maximumBytes,
				Math.Max(_writtenCount + requested, _buffer.Length * 2));
			Array.Resize(ref _buffer, newLength);
		}
	}
}

internal sealed class MergeOutputLimitExceededException : Exception;
