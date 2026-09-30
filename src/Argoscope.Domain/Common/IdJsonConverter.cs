using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argoscope.Domain.Common;

/// <summary>
/// Serialises <see cref="Id{T}"/> as a bare <see cref="Guid"/> string. Without
/// this converter the default System.Text.Json shape is
/// <c>{ "value": "..." }</c>, which is awkward for a typed identifier that
/// is semantically equivalent to a Guid.
/// </summary>
public sealed class IdJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Id<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(IdJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

internal sealed class IdJsonConverter<T> : JsonConverter<Id<T>> where T : notnull
{
    public override Id<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }
        var raw = reader.GetString();
        if (string.IsNullOrEmpty(raw) || !Guid.TryParse(raw, out var parsed))
        {
            throw new JsonException($"Expected a Guid string for Id<{typeof(T).Name}>, got '{raw}'.");
        }
        return Id<T>.From(parsed);
    }

    public override void Write(Utf8JsonWriter writer, Id<T> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
