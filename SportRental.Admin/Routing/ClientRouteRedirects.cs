using Microsoft.AspNetCore.Authorization;

namespace SportRental.Admin.Routing;

/// <summary>
/// Keeps links emitted by an older, already-open Client WASM build working after
/// the application moved under <c>/_client/</c>. Authentication entry points at
/// <c>/login</c>, <c>/register</c> and <c>/logout</c> intentionally remain owned by
/// the Admin application.
/// </summary>
internal static class ClientRouteRedirects
{
    private static readonly string[] LegacyClientRoutePatterns =
    [
        "/products",
        "/products/{id:guid}",
        "/cart",
        "/checkout",
        "/checkout/success",
        "/checkout/cancel",
        "/contact",
        "/map",
        "/privacy",
        "/terms",
        "/reviews",
        "/reviews/opt-out",
        "/select-tenant",
        "/guest-access",
        "/account",
        "/profile",
        "/my-rentals",
        "/my-rentals/{id:guid}",
        "/not-found"
    ];

    internal static IEndpointRouteBuilder MapLegacyClientRouteRedirects(
        this IEndpointRouteBuilder endpoints)
    {
        foreach (var pattern in LegacyClientRoutePatterns)
        {
            endpoints.MapGet(pattern, RedirectToBundledClient)
                .AllowAnonymous();
        }

        return endpoints;
    }

    internal static string BuildBundledClientLocation(
        PathString path,
        QueryString queryString)
    {
        return $"/_client{path.ToUriComponent()}{queryString.ToUriComponent()}";
    }

    private static IResult RedirectToBundledClient(HttpRequest request)
    {
        var location = BuildBundledClientLocation(request.Path, request.QueryString);
        return Results.Redirect(location, permanent: false);
    }
}
