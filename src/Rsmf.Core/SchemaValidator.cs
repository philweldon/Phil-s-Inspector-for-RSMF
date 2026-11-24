using System.Reflection;
using System.Text;
using System.Text.Json;
using Json.Schema;

namespace Rsmf.Core;

internal sealed record SchemaViolation(string Path, string Keyword, string Message);

/// <summary>Validates a manifest against the bundled published schema for its version.</summary>
internal static class SchemaValidator
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (JsonSchema Schema, JsonDocument Raw)> Cache = new();

    public static bool IsSupported(string? version) => version is "1.0.0" or "2.0.0";

    private static (JsonSchema Schema, JsonDocument Raw) Load(string version)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(version, out var hit)) return hit;
            var name = "rsmf_schema_" + version.Replace('.', '_') + ".json";
            using var stream = typeof(SchemaValidator).GetTypeInfo().Assembly.GetManifestResourceStream(name)
                               ?? throw new InvalidOperationException("Bundled schema not found: " + name);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            var entry = (JsonSchema.FromText(text), JsonDocument.Parse(text));
            Cache[version] = entry;
            return entry;
        }
    }

    public static List<SchemaViolation> Validate(string version, JsonElement root)
    {
        var (schema, _) = Load(version);
        EvaluationResults results;
        lock (Gate)
        {
            results = schema.Evaluate(root, new EvaluationOptions { OutputFormat = OutputFormat.List });
        }
        var list = new List<SchemaViolation>();
        if (results.IsValid) return list;
        foreach (var d in results.Details)
        {
            if (!d.HasErrors || d.Errors is null) continue;
            var path = PointerToPath(d.InstanceLocation.ToString());
            foreach (var e in d.Errors) list.Add(new SchemaViolation(path, e.Key, e.Value));
        }
        return list;
    }

    /// <summary>Finds properties the schema does not define: (path of the containing object, property name).</summary>
    public static List<(string Path, string Name)> UndefinedProperties(string version, JsonElement root)
    {
        var (_, raw) = Load(version);
        var found = new List<(string, string)>();
        Walk(root, raw.RootElement, "$", found);
        return found;
    }

    private static void Walk(JsonElement inst, JsonElement schema, string path, List<(string, string)> found)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;
        if (inst.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in inst.EnumerateObject())
            {
                if (props.TryGetProperty(p.Name, out var sub)) Walk(p.Value, sub, path + "." + p.Name, found);
                else found.Add((path, p.Name));
            }
        }
        else if (inst.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var i = 0;
            foreach (var item in inst.EnumerateArray()) Walk(item, items, $"{path}[{i++}]", found);
        }
    }

    internal static string PointerToPath(string pointer)
    {
        var sb = new StringBuilder("$");
        if (string.IsNullOrEmpty(pointer)) return sb.ToString();
        foreach (var raw in pointer.Split('/').Skip(1))
        {
            var seg = raw.Replace("~1", "/").Replace("~0", "~");
            if (seg.Length > 0 && seg.All(char.IsAsciiDigit)) sb.Append('[').Append(seg).Append(']');
            else sb.Append('.').Append(seg);
        }
        return sb.ToString();
    }
}
