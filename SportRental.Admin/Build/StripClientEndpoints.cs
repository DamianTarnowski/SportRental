using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: StripClientEndpoints.cs <manifest-file>");
    return 2;
}

var manifestFile = Path.GetFullPath(args[0]);
if (!File.Exists(manifestFile))
{
    Console.WriteLine("[StripClient] Manifest does not exist - skipping");
    return 0;
}

var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestFile))?.AsObject()
    ?? throw new InvalidDataException($"Invalid static assets manifest: {manifestFile}");
var endpoints = root["Endpoints"] as JsonArray
    ?? throw new InvalidDataException($"Missing Endpoints array in manifest: {manifestFile}");
var before = endpoints.Count;

for (var i = endpoints.Count - 1; i >= 0; i--)
{
    var route = endpoints[i]?["Route"]?.GetValue<string>();
    if (string.Equals(route, "_client", StringComparison.Ordinal) ||
        (route?.StartsWith("_client/", StringComparison.Ordinal) ?? false))
    {
        endpoints.RemoveAt(i);
    }
}

var temporaryFile = $"{manifestFile}.{Guid.NewGuid():N}.tmp";
await File.WriteAllTextAsync(
    temporaryFile,
    root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
File.Move(temporaryFile, manifestFile, overwrite: true);

Console.WriteLine(
    $"[StripClient] Removed {before - endpoints.Count} _client entries. Before: {before} After: {endpoints.Count}");
return 0;
