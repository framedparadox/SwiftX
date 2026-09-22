using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwiftX.Services;

/// <summary>
/// Reads an enum by name like <c>JsonStringEnumConverter</c> does, but answers a name it does not
/// know with <c>default(T)</c> instead of throwing.
/// <para>
/// The difference matters because of what a throw costs here. <see cref="OrbitStore.Load"/> handles
/// any exception from deserializing by handing back a blank configuration, and the next save then
/// overwrites the file: one hand-edited or newer-than-this-build value in an unimportant setting
/// would take every orbit item the user had arranged with it. A setting whose worst failure is "shows
/// its default" should not be able to do that, so it opts in with a <c>[JsonConverter]</c> on the
/// property, which wins over the app-wide converter in <c>OrbitStore</c>'s options.
/// </para>
/// <para>
/// The first member of <typeparamref name="T"/> is therefore its fallback, and has to be the
/// setting's default. Writing is the same as the built-in converter's: the member name, so what
/// lands on disk is unchanged.
/// </para>
/// </summary>
public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value)
            && Enum.IsDefined(value))
        {
            return value;
        }

        // A container has to be consumed whole, or the reader is left inside it and the caller's
        // own bookkeeping throws instead. Every other token is already fully read.
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();

        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
