namespace SwiftX.Models;

/// <summary>One locally stored key/secret pair. The secret is DPAPI-protected at rest.</summary>
public sealed class SecureValueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
