namespace GuluPet.Tests;

internal static class TestAnimatedWebp
{
    public const int FrameCount = 3;
    public const int FrameSize = 32;

    private const string TransparentBase64 =
        "UklGRroAAABXRUJQVlA4WAoAAAACAAAAHwAAHwAAQU5JTQYAAAAAAAAAAABBTk1G" +
        "KgAAAAIAAAIAAA8AAA8AACoAAANWUDhMEQAAAC8PwAMAB1CyUpS7/4GI6H8AAEFO" +
        "TUYqAAAABAAAAgAADwAADwAAKQAAAVZQOEwRAAAALw/AAwAHULJqFbT/gYjofwAA" +
        "QU5NRioAAAAGAAACAAAPAAAPAAAqAAAAVlA4TBEAAAAvD8ADAAdQsoKWrP+BiOh/" +
        "AAA=";

    private const string OpaqueBase64 =
        "UklGRuoAAABXRUJQVlA4WAoAAAACAAAAHwAAHwAAQU5JTQYAAAAAAAAAAABBTk1G" +
        "PgAAAAAAAAAAAB8AAB8AACoAAAJWUDhMJgAAAC8fwAcAD3APo3vY38N3/gMPigGg" +
        "gTLI3zKDnscmENH/CUAs89l9QU5NRjgAAAACAAACAAATAAAPAAApAAAAVlA4TB8A" +
        "AAAvE8ADAA9wD6N78NxDcP4DD0IBBCF/ywz6EUT0P4wZAEFOTUY4AAAABAAAAgAA" +
        "EwAADwAAKgAAAFZQOEwfAAAALxPAAwAPcA+jewjew2j+Aw9CAQQhf8sM+hFE9D+" +
        "MGQA=";

    public static void Write(string path, bool transparent = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(
            path,
            Convert.FromBase64String(
                transparent ? TransparentBase64 : OpaqueBase64));
    }
}
