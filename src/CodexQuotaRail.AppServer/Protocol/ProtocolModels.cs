using System.Text.Json;

namespace CodexQuotaRail.AppServer.Protocol;

public sealed record JsonRpcNotification(string Method, JsonElement Params);

public sealed record JsonRpcServerError(int Code, string Message);

public sealed class AppServerProtocolException(string message) : Exception(message);

public sealed class AppServerRequestException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
