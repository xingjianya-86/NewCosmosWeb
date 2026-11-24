using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", Health);
        app.MapGet("/info", InfoAsync);
        return app;
    }

    private static IResult Health()
        => Results.Json(new HealthResponse("ok", DateTime.UtcNow));

    private static async Task<IResult> InfoAsync(
        ServerEnvironmentOptions options,
        IDatabaseService database,
        CancellationToken ct)
    {
        var databaseStatus = "unavailable";
        var probe = await database.TestConnectionAsync(ct);
        if (probe.IsSuccess && probe.Value)
        {
            databaseStatus = "ok";
        }

        return Results.Json(new InfoResponse(
            "NewCosmos.Server",
            options.App.Version,
            databaseStatus,
            options.Mode));
    }
}
