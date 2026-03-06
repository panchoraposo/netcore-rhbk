using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MateBank.Frontend.Pages;

[Authorize]
public class MissionsModel(IConfiguration configuration, IHttpClientFactory httpClientFactory) : PageModel
{
    public record Mission(string Key, string Title, string RequiredGroup, string EndpointPath, string Description);
    public record DemoCard(string Title, string Body);
    
    private static readonly Regex JsonKeyRegex = new(@"&quot;[^\r\n]*?&quot;(?=\s*:)", RegexOptions.Compiled);
    private static readonly Regex JsonStringRegex = new(@"&quot;[^\r\n]*?&quot;", RegexOptions.Compiled);
    private static readonly Regex JsonNumberRegex = new(@"(?<![\w>])(-?\d+(?:\.\d+)?(?:[eE][+\-]?\d+)?)(?![\w<])", RegexOptions.Compiled);
    private static readonly Regex JsonBoolRegex = new(@"\b(true|false)\b", RegexOptions.Compiled);
    private static readonly Regex JsonNullRegex = new(@"\bnull\b", RegexOptions.Compiled);

    public string GroupClaimType { get; private set; } = "groups";
    public string[] Groups { get; private set; } = [];
    public Mission[] Missions { get; } =
    [
        new("missions", "Mission Board", "", "/api/missions", "List missions and see which ones your LDAP groups authorize."),
        new("tellers", "Branch Teller Operations", "/branch-tellers", "/api/tellers", "Access cash desk operations (requires LDAP group /branch-tellers)."),
        new("risk", "Risk Analytics Console", "/risk-analysts", "/api/risk", "Run risk checks (requires LDAP group /risk-analysts)."),
        new("credit", "Credit Approval Desk", "/credit-approvers", "/api/credit", "Approve loans (requires LDAP group /credit-approvers)."),
        new("audit", "Audit & Compliance", "/auditors", "/api/audit", "Read audit trails (requires LDAP group /auditors)."),
        new("admin", "IT Admin Panel", "/it-admins", "/api/admin", "System administration (requires LDAP group /it-admins)."),
    ];

    [BindProperty(SupportsGet = true)]
    public string? Try { get; set; }

    public int? LastStatusCode { get; private set; }
    public string LastResponseBody { get; private set; } = string.Empty;
    public string LastEndpoint { get; private set; } = string.Empty;

    public bool HasGroup(string group) => Groups.Contains(group, StringComparer.Ordinal);
    public bool IsAllowed(Mission mission) => string.IsNullOrWhiteSpace(mission.RequiredGroup) || HasGroup(mission.RequiredGroup);

    public string? LastMissionTitle { get; private set; }
    public string? LastMissionBriefing { get; private set; }
    public string? LastMissionRequiredGroup { get; private set; }
    public string? LastActorUsername { get; private set; }
    public string? LastIssuedAtUtc { get; private set; }
    public DemoCard[] LastDemoCards { get; private set; } = [];
    public string? LastPrettyJson { get; private set; }

    public string FormatJsonLikeJq(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var raw = text;
        if (LooksLikeJson(raw))
        {
            try
            {
                var node = JsonNode.Parse(raw);
                if (node is not null)
                {
                    raw = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch
            {
                // Keep raw as-is if it's not valid JSON.
            }
        }

        var encoded = WebUtility.HtmlEncode(raw);

        // Replace keys with placeholders so we can colorize string values separately without nesting spans.
        var keys = new List<string>();
        encoded = JsonKeyRegex.Replace(encoded, m =>
        {
            keys.Add(m.Value);
            return $"@@MBKEY_{IndexToToken(keys.Count - 1)}@@";
        });

        encoded = JsonStringRegex.Replace(encoded, m => $"<span class=\"mb-json-string\">{m.Value}</span>");

        for (var i = 0; i < keys.Count; i++)
        {
            encoded = encoded.Replace($"@@MBKEY_{IndexToToken(i)}@@", $"<span class=\"mb-json-key\">{keys[i]}</span>", StringComparison.Ordinal);
        }

        encoded = JsonNumberRegex.Replace(encoded, "<span class=\"mb-json-number\">$1</span>");
        encoded = JsonBoolRegex.Replace(encoded, "<span class=\"mb-json-bool\">$1</span>");
        encoded = JsonNullRegex.Replace(encoded, "<span class=\"mb-json-null\">null</span>");

        return encoded;
    }

    public async Task OnGetAsync()
    {
        GroupClaimType = configuration["FrontendAuth:GroupClaimType"] ?? "groups";
        Groups = User.FindAll(GroupClaimType).Select(c => c.Value).Distinct().OrderBy(v => v).ToArray();

        var mission = Missions.FirstOrDefault(m => string.Equals(m.Key, Try, StringComparison.OrdinalIgnoreCase));
        if (mission is null)
        {
            return;
        }

        var backendBaseUrl = configuration["Backend:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            LastStatusCode = 0;
            LastResponseBody = "Missing Backend:BaseUrl configuration.";
            return;
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            LastStatusCode = 0;
            LastResponseBody = "No access token found in session. Please sign out and sign in again.";
            return;
        }

        var client = httpClientFactory.CreateClient("backend");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        LastEndpoint = mission.EndpointPath;
        using var resp = await client.GetAsync(mission.EndpointPath);
        LastStatusCode = (int)resp.StatusCode;
        LastResponseBody = await resp.Content.ReadAsStringAsync();

        TryParseMissionResponse(LastResponseBody);
    }

    private void TryParseMissionResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var looksJson = body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('[');
        if (!looksJson)
        {
            return;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch
        {
            return;
        }

        if (node is null)
        {
            return;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        LastPrettyJson = node.ToJsonString(jsonOptions);

        if (node is not JsonObject root)
        {
            return;
        }

        if (root["mission"] is JsonObject mission)
        {
            LastMissionTitle = mission["title"]?.GetValue<string?>();
            LastMissionBriefing = mission["briefing"]?.GetValue<string?>();
            LastMissionRequiredGroup = mission["requiredGroup"]?.GetValue<string?>();
        }

        LastActorUsername = (root["actor"] as JsonObject)?["username"]?.GetValue<string?>();
        LastIssuedAtUtc = root["issuedAtUtc"]?.GetValue<string?>();

        var demoCards = new List<DemoCard>();
        if (root["demoData"] is JsonObject demoObj)
        {
            foreach (var (k, v) in demoObj)
            {
                demoCards.Add(new DemoCard(k, v?.ToJsonString(jsonOptions) ?? "null"));
            }
        }
        else if (root["demoData"] is not null)
        {
            demoCards.Add(new DemoCard("demoData", root["demoData"]!.ToJsonString(jsonOptions)));
        }

        LastDemoCards = demoCards.ToArray();
    }

    private static bool LooksLikeJson(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[');
    }

    private static string IndexToToken(int index)
    {
        // Base-26 token without digits (A, B, ..., Z, AA, AB, ...).
        if (index < 0)
        {
            return "A";
        }

        var n = index;
        var chars = new Stack<char>();
        do
        {
            chars.Push((char)('A' + (n % 26)));
            n = (n / 26) - 1;
        } while (n >= 0);

        return new string(chars.ToArray());
    }
}

