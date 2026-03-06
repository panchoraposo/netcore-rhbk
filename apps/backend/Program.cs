using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authority = builder.Configuration["Auth:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("Missing configuration value: Auth:Authority");
        }

        options.Authority = authority.TrimEnd('/');
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", true);

        var audience = builder.Configuration["Auth:Audience"];
        if (!string.IsNullOrWhiteSpace(audience))
        {
            options.TokenValidationParameters.ValidAudience = audience;
        }

        options.TokenValidationParameters.NameClaimType =
            builder.Configuration["Auth:NameClaimType"] ?? "preferred_username";

        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(audience);
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;

        // Avoid mapping JWT claim types into legacy WS-Fed claim types.
        options.MapInboundClaims = false;

        // Accept a small clock skew for demo environments.
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(2);

        // Help troubleshooting when the token audience is a list.
        options.TokenValidationParameters.AudienceValidator = (audiences, securityToken, validationParameters) =>
        {
            if (string.IsNullOrWhiteSpace(validationParameters.ValidAudience))
            {
                return true;
            }

            return audiences.Contains(validationParameters.ValidAudience, StringComparer.Ordinal);
        };
    });

builder.Services.AddAuthorization(options =>
{
    var groupClaimType = builder.Configuration["Auth:GroupClaimType"] ?? "groups";

    options.AddPolicy("TellersOnly", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasGroup(ctx.User, groupClaimType, "/branch-tellers")));

    options.AddPolicy("RiskOnly", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasGroup(ctx.User, groupClaimType, "/risk-analysts")));

    options.AddPolicy("AdminsOnly", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasGroup(ctx.User, groupClaimType, "/it-admins")));

    options.AddPolicy("CreditApproversOnly", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasGroup(ctx.User, groupClaimType, "/credit-approvers")));

    options.AddPolicy("AuditorsOnly", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasGroup(ctx.User, groupClaimType, "/auditors")));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MateBank Backend API (Demo)");
    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseCors("frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/healthz", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var groupsClaimType = app.Configuration["Auth:GroupClaimType"] ?? "groups";
    var groups = GetGroups(user, groupsClaimType);

    var missions = GetMissionDefinitions();
    var allowedMissionKeys = missions
        .Where(m => groups.Any(g => NormalizeGroup(g).Equals(NormalizeGroup(m.RequiredGroup), StringComparison.OrdinalIgnoreCase)))
        .Select(m => m.Key)
        .OrderBy(k => k)
        .ToArray();

    return Results.Ok(new
    {
        subject = user.FindFirstValue("sub"),
        username = user.Identity?.Name,
        groups,
        allowedMissions = allowedMissionKeys,
        claims = user.Claims
            .Select(c => new { type = c.Type, value = c.Value })
            .OrderBy(c => c.type)
            .ToArray()
    });
}).RequireAuthorization();

app.MapGet("/api/missions", (ClaimsPrincipal user) =>
{
    var groupClaimType = app.Configuration["Auth:GroupClaimType"] ?? "groups";
    var groups = GetGroups(user, groupClaimType);

    var missions = GetMissionDefinitions()
        .Select(m => new
        {
            m.Key,
            m.Title,
            m.RequiredGroup,
            m.Description,
            m.EndpointPath,
            authorized = groups.Any(g => NormalizeGroup(g).Equals(NormalizeGroup(m.RequiredGroup), StringComparison.OrdinalIgnoreCase))
        })
        .OrderBy(m => m.Key)
        .ToArray();

    return Results.Ok(new
    {
        user = user.Identity?.Name,
        groupClaimType,
        groups,
        missions,
        issuedAtUtc = DateTimeOffset.UtcNow
    });
}).RequireAuthorization();

app.MapGet("/api/tellers", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        mission = new
        {
            key = "tellers",
            title = "Branch Teller Operations",
            requiredGroup = "/branch-tellers",
            briefing =
                "You are cleared for cash desk operations. This mission simulates teller-only actions decided by LDAP group membership."
        },
        actor = new { username = user.Identity?.Name },
        demoData = new
        {
            branch = "MateBank • Caballito",
            cashDrawerId = "CBL-07",
            todaysQueue = new[]
            {
                new { type = "CashDeposit", amountArs = 250_000, customer = "CU-104492" },
                new { type = "CashWithdrawal", amountArs = 90_000, customer = "CU-209781" },
                new { type = "FXExchange", amountArs = 120_000, customer = "CU-301128" }
            }
        },
        issuedAtUtc = DateTimeOffset.UtcNow
    }))
    .RequireAuthorization("TellersOnly");

app.MapGet("/api/risk", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        mission = new
        {
            key = "risk",
            title = "Risk Analytics Console",
            requiredGroup = "/risk-analysts",
            briefing = "You are cleared to run risk checks. This mission simulates analyst-only queries decided by LDAP group membership."
        },
        actor = new { username = user.Identity?.Name },
        demoData = new
        {
            portfolio = "Retail Loans • AR",
            snapshot = new
            {
                flaggedApplications = 3,
                averageRiskScore = 61,
                watchlistHits = 1
            },
            flagged = new[]
            {
                new { applicationId = "APP-90218", riskScore = 82, reason = "Velocity + device mismatch" },
                new { applicationId = "APP-90244", riskScore = 77, reason = "Income anomaly" },
                new { applicationId = "APP-90271", riskScore = 74, reason = "Geo distance > threshold" }
            }
        },
        issuedAtUtc = DateTimeOffset.UtcNow
    }))
    .RequireAuthorization("RiskOnly");

app.MapGet("/api/admin", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        mission = new
        {
            key = "admin",
            title = "IT Admin Panel",
            requiredGroup = "/it-admins",
            briefing = "You are cleared for administrative tasks. This mission simulates privileged operations decided by LDAP group membership."
        },
        actor = new { username = user.Identity?.Name },
        demoData = new
        {
            environment = "OpenShift • demo namespace",
            checks = new[]
            {
                new { name = "OIDC issuer reachable", status = "ok" },
                new { name = "LDAP federation healthy", status = "ok" },
                new { name = "Audit trail retention", status = "ok" }
            }
        },
        issuedAtUtc = DateTimeOffset.UtcNow
    }))
    .RequireAuthorization("AdminsOnly");

app.MapGet("/api/credit", (ClaimsPrincipal user) =>
        Results.Ok(new
        {
            mission = new
            {
                key = "credit",
                title = "Credit Approval Desk",
                requiredGroup = "/credit-approvers",
                briefing =
                    "You are cleared to approve credit decisions. This mission simulates approval-only actions decided by LDAP group membership."
            },
            actor = new { username = user.Identity?.Name },
            demoData = new
            {
                pendingApprovals = new[]
                {
                    new { caseId = "CR-55012", product = "Personal Loan", amountArs = 1_800_000, applicant = "CU-118090" },
                    new { caseId = "CR-55027", product = "SME Credit Line", amountArs = 8_500_000, applicant = "CU-221774" }
                }
            },
            issuedAtUtc = DateTimeOffset.UtcNow
        }))
    .RequireAuthorization("CreditApproversOnly");

app.MapGet("/api/audit", (ClaimsPrincipal user) =>
        Results.Ok(new
        {
            mission = new
            {
                key = "audit",
                title = "Audit & Compliance",
                requiredGroup = "/auditors",
                briefing = "You are cleared to review audit trails. This mission simulates read-only compliance access decided by LDAP group membership."
            },
            actor = new { username = user.Identity?.Name },
            demoData = new
            {
                sampleTrail = new[]
                {
                    new { atUtc = DateTimeOffset.UtcNow.AddMinutes(-18), action = "LOGIN", actor = user.Identity?.Name ?? "unknown", result = "OK" },
                    new { atUtc = DateTimeOffset.UtcNow.AddMinutes(-12), action = "TOKEN_ISSUED", actor = user.Identity?.Name ?? "unknown", result = "OK" },
                    new { atUtc = DateTimeOffset.UtcNow.AddMinutes(-3), action = "API_CALL", actor = user.Identity?.Name ?? "unknown", result = "ALLOWED" }
                }
            },
            issuedAtUtc = DateTimeOffset.UtcNow
        }))
    .RequireAuthorization("AuditorsOnly");

app.Run();

static bool HasGroup(ClaimsPrincipal user, string claimType, string expectedGroup)
{
    var normalizedExpected = NormalizeGroup(expectedGroup);
    foreach (var claim in user.FindAll(claimType))
    {
        if (NormalizeGroup(claim.Value).Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static string NormalizeGroup(string group)
{
    if (string.IsNullOrWhiteSpace(group))
    {
        return string.Empty;
    }

    return group.StartsWith('/') ? group : "/" + group;
}

static string[] GetGroups(ClaimsPrincipal user, string claimType) =>
    user.FindAll(claimType).Select(c => c.Value).Distinct().OrderBy(v => v).ToArray();

static MissionDefinition[] GetMissionDefinitions() =>
[
    new("tellers", "Branch Teller Operations", "/branch-tellers", "/api/tellers",
        "Cash desk operations (tellers)."),
    new("risk", "Risk Analytics Console", "/risk-analysts", "/api/risk",
        "Risk checks and portfolio insights (analysts)."),
    new("credit", "Credit Approval Desk", "/credit-approvers", "/api/credit",
        "Approve loans and credit lines (approvers)."),
    new("audit", "Audit & Compliance", "/auditors", "/api/audit",
        "Read audit trails (auditors)."),
    new("admin", "IT Admin Panel", "/it-admins", "/api/admin",
        "Administrative operations (IT admins).")
];

sealed record MissionDefinition(string Key, string Title, string RequiredGroup, string EndpointPath, string Description);
