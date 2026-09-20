using System.Text.Json;

namespace GuluPet.Testing;

public static class TestControlJson
{
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, TestControlProtocol.JsonOptions);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, TestControlProtocol.JsonOptions)
        ?? throw new JsonException($"JSON did not contain a {typeof(T).Name} value.");
}
