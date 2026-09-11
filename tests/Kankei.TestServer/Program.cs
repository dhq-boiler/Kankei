using System.Text.Json;

while (await Console.In.ReadLineAsync() is { } line)
{
    using var document = JsonDocument.Parse(line);
    var root = document.RootElement;
    if (!root.TryGetProperty("id", out var id)) continue;
    object result;
    var method = root.GetProperty("method").GetString();
    if (method == "initialize")
        result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "Kankei.TestServer", version = "1" } };
    else if (method == "tools/call")
    {
        var parameters = root.GetProperty("params");
        var name = parameters.GetProperty("name").GetString();
        if (name == "structured")
        {
            result = new { content = Array.Empty<object>(), structuredContent = new { document = "日本語.txt" } };
            Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.Clone(), result }));
            continue;
        }
        if (name == "slow") await Task.Delay(TimeSpan.FromSeconds(20));
        var failed = name == "fail";
        if (name == "import")
            failed = parameters.GetProperty("arguments").GetProperty("state").GetProperty("document").GetString() != "日本語.txt";
        var text = name == "invalid" ? "not json" : name == "null" ? "null" : "{\"document\":\"日本語.txt\"}";
        result = new { content = new[] { new { type = "text", text } }, isError = failed };
    }
    else
    {
        Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } }));
        continue;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.Clone(), result }));
}

public sealed class TestServerMarker;
