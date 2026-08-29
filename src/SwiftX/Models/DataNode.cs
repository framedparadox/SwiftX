namespace SwiftX.Models;

public abstract record DataNode;

public sealed record ObjectNode(IReadOnlyList<(string Key, DataNode Value)> Members) : DataNode;

public sealed record ArrayNode(IReadOnlyList<DataNode> Items) : DataNode;

public sealed record ScalarNode(string? Raw, ScalarKind Kind) : DataNode;

public enum ScalarKind { String, Number, Boolean, Null }
