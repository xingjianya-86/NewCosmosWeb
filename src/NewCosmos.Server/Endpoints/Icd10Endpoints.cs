using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 共享 ICD-10 编码查询：低收入认定、临时救助等表单共用。
/// 只返回编码对应的常见病名与一级疾病分类，不做编码有效性拦截。
/// </summary>
public static class Icd10Endpoints
{
    private const string Prefix = "/api/icd10";

    private static readonly string[] ReadPermissions =
    [
        PermissionCodes.INCOME_VIEW,
        PermissionCodes.TEMP_VIEW
    ];

    public static IEndpointRouteBuilder MapIcd10Endpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Prefix, LookupAsync);
        return app;
    }

    /// <summary>GET /api/icd10?code=Z51.800X095 → { code, name, category }</summary>
    private static async Task<IResult> LookupAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        string? code)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(
            context, permissions, ReadPermissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return Results.Json(new
            {
                code = string.Empty,
                name = string.Empty,
                category = Icd10Catalog.CategoryOther
            });
        }

        var hit = Icd10Catalog.TryGetName(normalized, out var name);
        return Results.Json(new
        {
            code = normalized,
            name = hit ? name : string.Empty,
            category = Icd10Catalog.MapCategory(normalized)
        });
    }
}
