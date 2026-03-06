using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MateBank.Frontend.Pages;

[Authorize]
public class CallApiModel(IConfiguration configuration, IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Path { get; set; }

    public string BackendBaseUrl { get; private set; } = string.Empty;
    public bool Success { get; private set; }
    public string ResponseBody { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        BackendBaseUrl = configuration["Backend:BaseUrl"]?.TrimEnd('/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(Path) || !Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            Success = false;
            ResponseBody = "Invalid path. Expected a path starting with /api/.";
            return;
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Success = false;
            ResponseBody = "No access token available in session. Ensure SaveTokens=true and the OIDC flow completed successfully.";
            return;
        }

        var client = httpClientFactory.CreateClient("backend");
        using var req = new HttpRequestMessage(HttpMethod.Get, Path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        Success = res.IsSuccessStatusCode;
        ResponseBody = body;
    }
}
