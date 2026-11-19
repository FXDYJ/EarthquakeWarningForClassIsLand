using System.Runtime.CompilerServices;
using System.Text;

namespace EarthquakeWarning.Services.Stations;

public sealed record SseMessage(string Event, string Data);

public static class BoundedSseReader
{
    public static async IAsyncEnumerable<SseMessage> ReadAsync(TextReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new char[2048];
        var line = new StringBuilder(); var data = new StringBuilder(); var name = "message";
        var skipLf = false;
        while (true)
        {
            var length = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (length == 0) yield break; // An incomplete event at EOF must not be applied.
            for (var i = 0; i < length; i++)
            {
                var character = buffer[i];
                if (skipLf && character == '\n') { skipLf = false; continue; }
                skipLf = character == '\r';
                if (character is not ('\r' or '\n'))
                {
                    if (line.Length >= TremStationParser.MaximumMessageLength) throw new InvalidDataException("SSE 行过长");
                    line.Append(character); continue;
                }
                var text = line.ToString(); line.Clear();
                if (text.Length == 0)
                {
                    if (data.Length > 0) yield return new(name, data.ToString(0, data.Length - 1));
                    data.Clear(); name = "message"; continue;
                }
                if (text.StartsWith(':')) continue;
                var colon = text.IndexOf(':');
                var field = colon < 0 ? text : text[..colon];
                var value = colon < 0 ? "" : text[(colon + 1)..];
                if (value.StartsWith(' ')) value = value[1..];
                if (field == "event") name = value;
                if (field != "data") continue;
                if (data.Length + value.Length + 1 > TremStationParser.MaximumMessageLength) throw new InvalidDataException("SSE 消息过长");
                data.Append(value).Append('\n');
            }
        }
    }
}
