using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

const string pipeName = "MemoryBloom.FirefoxCompanion.v1";
var input = Console.OpenStandardInput();
try
{
    while (true)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(input, header)) break;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid native-message length.");
        var payload = new byte[length];
        if (!await ReadExactlyOrEndAsync(input, payload)) break;

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1500);
        var line = Encoding.UTF8.GetString(payload) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        await pipe.WriteAsync(bytes);
        await pipe.FlushAsync();
    }
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"Memory Bloom native host: {exception.Message}");
}

static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset));
        if (read == 0) return false;
        offset += read;
    }
    return true;
}
