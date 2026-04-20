using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace SportRental.Client.Services;

// SEC-009: wymusza `credentials: include` w fetch-ach WASM, żeby HttpOnly cookie
// (sr_access_token) było wysyłane cross-origin do API (5001/sradmin.azurewebsites.net).
public sealed class BrowserCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
