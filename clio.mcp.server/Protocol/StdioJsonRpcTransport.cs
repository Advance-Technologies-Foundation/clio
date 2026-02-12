using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Clio.McpServer;

internal sealed class StdioJsonRpcTransport {
	private static readonly byte[] HeaderDelimiter = "\r\n\r\n"u8.ToArray();
	private readonly Stream _stdin = Console.OpenStandardInput();
	private readonly Stream _stdout = Console.OpenStandardOutput();
	private readonly JsonSerializerOptions _jsonOptions;

	public StdioJsonRpcTransport(JsonSerializerOptions jsonOptions) {
		_jsonOptions = jsonOptions;
	}

	public async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken) {
		byte[]? headerBytes = await ReadUntilDelimiterAsync(_stdin, HeaderDelimiter, cancellationToken);
		if (headerBytes is null) {
			return null;
		}
		string headerText = Encoding.ASCII.GetString(headerBytes);
		int contentLength = ParseContentLength(headerText);
		if (contentLength <= 0) {
			return null;
		}

		byte[] payload = ArrayPool<byte>.Shared.Rent(contentLength);
		try {
			int read = 0;
			while (read < contentLength) {
				int bytesRead = await _stdin.ReadAsync(payload.AsMemory(read, contentLength - read), cancellationToken);
				if (bytesRead == 0) {
					return null;
				}
				read += bytesRead;
			}
			return JsonDocument.Parse(payload.AsMemory(0, contentLength));
		}
		finally {
			ArrayPool<byte>.Shared.Return(payload);
		}
	}

	public async Task WriteResponseAsync(object response, CancellationToken cancellationToken) {
		byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);
		string header = $"Content-Length: {jsonBytes.Length}\r\n\r\n";
		byte[] headerBytes = Encoding.ASCII.GetBytes(header);

		await _stdout.WriteAsync(headerBytes, cancellationToken);
		await _stdout.WriteAsync(jsonBytes, cancellationToken);
		await _stdout.FlushAsync(cancellationToken);
	}

	private static int ParseContentLength(string headerText) {
		string[] lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
		foreach (string line in lines) {
			string[] parts = line.Split(':', 2);
			if (parts.Length == 2 && parts[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
				if (int.TryParse(parts[1].Trim(), out int result)) {
					return result;
				}
			}
		}
		return -1;
	}

	private static async Task<byte[]?> ReadUntilDelimiterAsync(Stream stream, byte[] delimiter,
		CancellationToken cancellationToken) {
		List<byte> collected = new(capacity: 256);
		byte[] buffer = new byte[1];
		int matched = 0;

		while (true) {
			int read = await stream.ReadAsync(buffer, cancellationToken);
			if (read == 0) {
				return collected.Count == 0 ? null : collected.ToArray();
			}

			byte current = buffer[0];
			collected.Add(current);

			if (current == delimiter[matched]) {
				matched++;
				if (matched == delimiter.Length) {
					int headerLength = collected.Count - delimiter.Length;
					return collected.Take(headerLength).ToArray();
				}
			}
			else {
				matched = current == delimiter[0] ? 1 : 0;
			}
		}
	}
}
