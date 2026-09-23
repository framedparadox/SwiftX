using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwiftX.Services;

/// <summary>
/// Reads an enum by name like <c>JsonStringEnumConverter</c> does, but answers a name it does not
/// know with a declared fallback instead of throwing.
/// <para>
/// The difference matters because of what a throw costs here. <see cref="OrbitStore.Load"/> handles
/// any exception from deserializing by handing back a blank configuration, and the next save then
/// overwrites the file: one hand-edited or newer-than-this-build value in an unimportant setting
/// would take every orbit item the user had arranged with it. A setting whose worst failure is "shows
/// its default" should not be able to do that.
/// </para>
/// <para>
/// <b>Applied to every enum, not to a chosen few.</b> This started as an opt-in
/// <c>[JsonConverter]</c> on three hover properties, which left <c>Theme</c>, <c>Language</c>,
/// <c>FrostedGlass</c>, <c>HotCorner</c>, <c>HotCornerLayout</c>, <c>ItemSize</c>,
/// <c>ItemShape</c> and <see cref="Models.OrbitItem.Kind"/> still reading through the throwing
/// built-in converter — so the disaster the opt-in prevented for three settings was fully
/// reachable through any of the other eight. The rule an opt-in list encodes ("remember to add the
/// attribute") is exactly the kind that is forgotten when the next enum is added, and the cost of
/// forgetting is the user's whole orbit. <see cref="TolerantEnumConverterFactory"/> is therefore
/// registered app-wide in <see cref="OrbitStore"/>'s options and covers every enum the config
/// carries, including ones not written yet.
/// </para>
/// <para>
/// The fallback is <c>default(T)</c> — the first declared member — unless a subclass names another
/// one. Two settings need that: <see cref="Models.AppTheme"/>'s first member is <c>Light</c> while
/// <see cref="Models.AppConfig.Theme"/> defaults to <c>Dark</c>, and
/// <see cref="Models.OrbitItemSize"/>'s is <c>Small</c> while
/// <see cref="Models.AppConfig.ItemSize"/> defaults to <c>Medium</c>. Falling back to the first
/// member there would answer an unknown name with a setting the user never had, so each declares
/// its own; see <see cref="TolerantAppThemeConverter"/> and
/// <see cref="TolerantOrbitItemSizeConverter"/>. Every other enum's first member already is its
/// property's default.
/// </para>
/// <para>
/// Writing is the same as the built-in converter's: the member name, so what lands on disk is
/// unchanged.
/// </para>
/// </summary>
public class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private readonly T _fallback;

    /// <summary>Falls back to <c>default(T)</c>, the first declared member.</summary>
    public TolerantEnumConverter() : this(default) { }

    /// <summary>Falls back to <paramref name="fallback"/>, for a property whose default is not
    /// its enum's first member.</summary>
    protected TolerantEnumConverter(T fallback) => _fallback = fallback;

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

        return _fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Supplies a <see cref="TolerantEnumConverter{T}"/> for every enum, so the tolerance is a
/// property of the configuration format rather than of the properties someone remembered to
/// annotate. Registered in <see cref="OrbitStore"/>'s serializer options in place of
/// <c>JsonStringEnumConverter</c>.
/// <para>
/// A property carrying its own <c>[JsonConverter]</c> still wins over this, which is what lets the
/// two settings with a non-first-member default name their own fallback.
/// </para>
/// </summary>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;
}

/// <summary>
/// <see cref="Models.AppConfig.Theme"/>'s converter. Falls back to <see cref="Models.AppTheme.Dark"/>
/// — the property's declared default — rather than to <c>Light</c>, which is merely the first
/// member of the enum and a visibly wrong answer for a user whose theme name this build happens
/// not to know.
/// </summary>
public sealed class TolerantAppThemeConverter : TolerantEnumConverter<Models.AppTheme>
{
    public TolerantAppThemeConverter() : base(Models.AppTheme.Dark) { }
}

/// <summary>
/// <see cref="Models.AppConfig.ItemSize"/>'s converter, falling back to
/// <see cref="Models.OrbitItemSize.Medium"/> for the same reason
/// <see cref="TolerantAppThemeConverter"/> falls back to Dark.
/// </summary>
public sealed class TolerantOrbitItemSizeConverter : TolerantEnumConverter<Models.OrbitItemSize>
{
    public TolerantOrbitItemSizeConverter() : base(Models.OrbitItemSize.Medium) { }
}
