using System.Text;

namespace Forge.Shared;

/// <summary>
/// Deterministic RFC 4180 CSV parser. Handles quoted fields, "" as an escaped quote inside
/// a quoted field, commas and line endings inside quotes, and CRLF / LF / CR record separators.
/// No NuGet CSV dependency and no heuristic column guessing.
/// </summary>
public static class CsvReader
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                field.Append(character);
                index++;
                continue;
            }

            if (character == '"' && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
                index++;
                continue;
            }

            if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                index++;
                continue;
            }

            if (character == '\r')
            {
                index++;
                if (index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                records.Add(fields.ToArray());
                fields.Clear();
                continue;
            }

            if (character == '\n')
            {
                index++;
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                records.Add(fields.ToArray());
                fields.Clear();
                continue;
            }

            field.Append(character);
            fieldStarted = true;
            index++;
        }

        if (fieldStarted || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields.ToArray());
        }

        return records;
    }
}
