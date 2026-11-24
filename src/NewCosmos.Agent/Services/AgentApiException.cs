namespace NewCosmos.Agent.Services;

/// <summary>服务端 API 调用失败（携带 HTTP 状态码与契约错误码）。</summary>
public sealed class AgentApiException : Exception
{
    public AgentApiException(int statusCode, string code, string message, string? traceId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Code = code;
        TraceId = traceId;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public string? TraceId { get; }

    public bool IsUnauthorized => StatusCode is 401 or 403;
}
