using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Dil.Generator;

/// <summary>
/// Reads a flat JSON object of string-&gt;string with <see cref="Utf8JsonReader"/>.
/// Order-preserving (the order keys appear drives the order of generated members),
/// tolerant of comments and trailing commas. Non-string values are ignored.
/// </summary>
static class FlatJson
{
    public static List<KeyValuePair<string, string>> Parse(string text)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var utf8 = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return result;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var key = reader.GetString() ?? string.Empty;
                if (!reader.Read())
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    result.Add(new KeyValuePair<string, string>(key, reader.GetString() ?? string.Empty));
                }
                else
                {
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON: surface whatever was read so far rather than crashing the generator.
        }

        return result;
    }
}
