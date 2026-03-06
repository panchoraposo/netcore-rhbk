using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddHttpClient("backend", client =>
{
    var baseUrl = builder.Configuration["Backend:BaseUrl"];
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        throw new InvalidOperationException("Missing configuration value: Backend:BaseUrl");
    }

    client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "__Host-matebankdemo";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        var authority = builder.Configuration["Oidc:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("Missing configuration value: Oidc:Authority");
        }

        options.Authority = authority.TrimEnd('/');
        options.ClientId = builder.Configuration["Oidc:ClientId"] ?? throw new InvalidOperationException("Missing configuration value: Oidc:ClientId");
        options.ClientSecret = builder.Configuration["Oidc:ClientSecret"] ?? throw new InvalidOperationException("Missing configuration value: Oidc:ClientSecret");

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        // Keycloak returns LDAP-derived group membership as a custom JSON claim ("groups").
        // The default claim actions map only standard OIDC claims; explicitly map "groups".
        options.ClaimActions.MapJsonKey("groups", "groups");

        options.CallbackPath = builder.Configuration["Oidc:CallbackPath"] ?? "/signin-oidc";
        options.SignedOutCallbackPath = builder.Configuration["Oidc:SignedOutCallbackPath"] ?? "/signout-callback-oidc";

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        var extraScopes = builder.Configuration.GetSection("Oidc:ExtraScopes").Get<string[]>() ?? [];
        foreach (var scope in extraScopes.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            options.Scope.Add(scope);
        }
    });

builder.Services.AddAuthorization();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/login", (HttpContext http, string? returnUrl) =>
{
    var props = new AuthenticationProperties
    {
        RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
    };
    return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapGet("/logout", (HttpContext http) =>
{
    var props = new AuthenticationProperties { RedirectUri = "/" };
    return Results.SignOut(props, [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
});

app.Run();
