using System.Text.Json;
using GuluPet.Testing;

namespace GuluPet.TestCli;

internal sealed class CliJsonWriter
{
    private readonly JsonSerializerOptions _options;

    public CliJsonWriter(bool compact)
    {
        _options = new JsonSerializerOptions(TestControlProtocol.JsonOptions)
        {
            WriteIndented = !compact,
        };
    }

    public void Write(object value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value, _options));
    }
}
