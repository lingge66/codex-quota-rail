using System.Text;
using System.Text.Json;

return await FakeAppServer.RunAsync(args);

internal static class FakeAppServer
{
    private static readonly HashSet<string> FixtureNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "healthy",
            "single",
            "unlimited",
        };

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
        {
            await Console.Out.WriteLineAsync("fake-codex-app-server 1.0.0");
            return 0;
        }

        var fixtureName = ReadOption(args, "--fixture") ?? "healthy";
        if (!FixtureNames.Contains(fixtureName))
        {
            await Console.Error.WriteLineAsync("不支持的测试额度 fixture。");
            return 2;
        }

        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            $"{fixtureName.ToLowerInvariant()}.json");
        if (!File.Exists(fixturePath))
        {
            await Console.Error.WriteLineAsync("测试额度 fixture 不存在。");
            return 3;
        }

        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var emitUpdate = args.Contains("--emit-update", StringComparer.OrdinalIgnoreCase);
        var disconnectAfterRead = args.Contains(
            "--disconnect-after-read",
            StringComparer.OrdinalIgnoreCase);
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (!TryParseRequest(line, out var request))
            {
                continue;
            }

            using (request)
            {
                var root = request.RootElement;
                var method = root.GetProperty("method").GetString();
                if (!root.TryGetProperty("id", out var id))
                {
                    continue;
                }

                switch (method)
                {
                    case "initialize":
                        await WriteResponseAsync(
                            id,
                            new
                            {
                                userAgent = "fake-codex-app-server/1.0",
                                platformFamily = "windows",
                                platformOs = "windows",
                            });
                        break;
                    case "account/read":
                        await WriteResponseAsync(
                            id,
                            new
                            {
                                account = new
                                {
                                    type = "chatgpt",
                                    email = "fake@example.invalid",
                                    planType = "plus",
                                },
                            });
                        break;
                    case "account/rateLimits/read":
                        await WriteResponseAsync(id, fixture.RootElement);
                        if (emitUpdate)
                        {
                            await WriteNotificationAsync();
                        }

                        if (disconnectAfterRead)
                        {
                            return 0;
                        }

                        break;
                    default:
                        await WriteErrorAsync(id);
                        break;
                }
            }
        }

        return 0;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[index][(option.Length + 1)..];
            }

            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Count)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool TryParseRequest(string line, out JsonDocument request)
    {
        try
        {
            request = JsonDocument.Parse(line);
            var valid = request.RootElement.ValueKind == JsonValueKind.Object &&
                request.RootElement.TryGetProperty("method", out var method) &&
                method.ValueKind == JsonValueKind.String;
            if (!valid)
            {
                request.Dispose();
                request = null!;
            }

            return valid;
        }
        catch (JsonException)
        {
            request = null!;
            return false;
        }
    }

    private static Task WriteResponseAsync(JsonElement id, object result) =>
        Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(new { id = id.Clone(), result }));

    private static Task WriteNotificationAsync() => Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            new
            {
                method = "account/rateLimits/updated",
                @params = new { source = "fixture" },
            }));

    private static Task WriteErrorAsync(JsonElement id) => Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            new
            {
                id = id.Clone(),
                error = new { code = -32601, message = "Method not found" },
            }));
}
