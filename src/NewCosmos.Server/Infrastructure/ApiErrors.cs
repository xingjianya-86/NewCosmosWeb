using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;

namespace NewCosmos.Server.Infrastructure;

/// <summary>API_CONTRACT.md 中 P0 错误码。</summary>
public static class ApiErrorCodes
{
    public const string AuthInvalid = "AUTH_INVALID";
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string SessionReplaced = "SESSION_REPLACED";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string Forbidden = "FORBIDDEN";
    public const string PrinterNotFound = "PRINTER_NOT_FOUND";
    public const string PrinterDeviceNotFound = "PRINTER_DEVICE_NOT_FOUND";
    public const string PrinterNameNotReported = "PRINTER_NAME_NOT_REPORTED";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string DbError = "DB_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string AgentRegisterDisabled = "AGENT_REGISTER_DISABLED";
}

public static class ApiErrors
{
    public static string GetTraceId(HttpContext context)
        => Activity.Current?.Id ?? context.TraceIdentifier;

    public static IResult Write(HttpContext context, int status, string code, string message)
        => Results.Json(
            new ApiErrorResponse(code, message, GetTraceId(context)),
            statusCode: status);

    public static IResult FromResult(HttpContext context, Result result)
    {
        var mapped = Map(result.ErrorCode, result.Message);
        return Write(context, mapped.Status, mapped.Code, mapped.Message);
    }

    public static IResult FromResult<T>(HttpContext context, Result<T> result)
    {
        var mapped = Map(result.ErrorCode, result.Message);
        return Write(context, mapped.Status, mapped.Code, mapped.Message);
    }

    public static (int Status, string Code, string Message) Map(string? code, string? message)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var safeMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        return normalized switch
        {
            ApiErrorCodes.PrinterNotFound => (StatusCodes.Status404NotFound, ApiErrorCodes.PrinterNotFound,
                safeMessage ?? "打印机绑定不存在"),
            ApiErrorCodes.PrinterDeviceNotFound => (StatusCodes.Status404NotFound, ApiErrorCodes.PrinterDeviceNotFound,
                safeMessage ?? "Agent 设备不存在"),
            ApiErrorCodes.PrinterNameNotReported => (StatusCodes.Status400BadRequest,
                ApiErrorCodes.PrinterNameNotReported, safeMessage ?? "打印机不在 Agent 上报列表中"),

            ApiErrorCodes.ValidationError or "VALIDATION_FAILED" or "REQUIRED_FIELD_MISSING"
                or "INVALID_PHONE" or "INVALID_ID_CARD"
                => (StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, safeMessage ?? "请求参数校验失败"),

            "AUTHENTICATION_FAILED" or "INVALID_PASSWORD" or "ACCOUNT_LOCKED" or ApiErrorCodes.AuthInvalid
                => (StatusCodes.Status401Unauthorized, ApiErrorCodes.AuthInvalid, safeMessage ?? "用户名或密码错误"),
            ApiErrorCodes.SessionExpired =>
                (StatusCodes.Status401Unauthorized, ApiErrorCodes.SessionExpired, safeMessage ?? "登录已过期，请重新登录"),
            "SESSION_REVOKED" or ApiErrorCodes.SessionReplaced =>
                (StatusCodes.Status401Unauthorized, ApiErrorCodes.SessionReplaced, safeMessage ?? "登录会话已失效，请重新登录"),
            "SESSION_INVALID" or ApiErrorCodes.AuthRequired =>
                (StatusCodes.Status401Unauthorized, ApiErrorCodes.AuthRequired, safeMessage ?? "请先登录"),

            "PERMISSION_DENIED" or ApiErrorCodes.Forbidden =>
                (StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, safeMessage ?? "没有权限执行该操作"),

            "USER_NOT_FOUND" or "NOT_FOUND" =>
                (StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, safeMessage ?? "请求的资源不存在"),

            "DB_CONNECTION_FAILED" or "DB_TIMEOUT" or "DB_UNIQUE_VIOLATION" or "DB_FOREIGN_KEY_VIOLATION"
                or "DB_NOT_NULL_VIOLATION" or "DB_QUERY_ERROR" or "DB_TRANSACTION_ERROR" or ApiErrorCodes.DbError
                => (StatusCodes.Status500InternalServerError, ApiErrorCodes.DbError, "数据库错误"),

            ApiErrorCodes.InternalError =>
                (StatusCodes.Status500InternalServerError, ApiErrorCodes.InternalError, "服务器内部错误"),

            ApiErrorCodes.AgentRegisterDisabled =>
                (StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.AgentRegisterDisabled, "Agent 注册未启用"),

            _ => (StatusCodes.Status500InternalServerError, ApiErrorCodes.InternalError, "服务器内部错误")
        };
    }

    public static (int Status, string Code, string Message) MapException(Exception exception)
    {
        return exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "请求参数校验失败"),
            BusinessException business => Map(business.ErrorCode, business.Message),
            BadHttpRequestException badRequest when badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                => (StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.ValidationError, "请求体超出大小限制"),
            BadHttpRequestException =>
                (StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "请求体格式无效"),
            _ => (StatusCodes.Status500InternalServerError, ApiErrorCodes.InternalError, "服务器内部错误")
        };
    }

    public static bool IsDatabaseErrorCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        return normalized is "DB_CONNECTION_FAILED" or "DB_TIMEOUT" or "DB_UNIQUE_VIOLATION"
            or "DB_FOREIGN_KEY_VIOLATION" or "DB_NOT_NULL_VIOLATION" or "DB_QUERY_ERROR"
            or "DB_TRANSACTION_ERROR" or ApiErrorCodes.DbError;
    }
}
