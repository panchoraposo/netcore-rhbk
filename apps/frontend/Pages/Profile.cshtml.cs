using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MateBank.Frontend.Pages;

[Authorize]
public class ProfileModel(IConfiguration configuration) : PageModel
{
    public string GroupClaimType { get; private set; } = "groups";
    public string[] Groups { get; private set; } = [];
    public (string Type, string Value)[] Claims { get; private set; } = [];

    public void OnGet()
    {
        GroupClaimType = configuration["FrontendAuth:GroupClaimType"] ?? "groups";

        Groups = User.FindAll(GroupClaimType)
            .Select(c => c.Value)
            .OrderBy(v => v)
            .ToArray();

        Claims = User.Claims
            .Select(c => (c.Type, c.Value))
            .OrderBy(c => c.Type)
            .ToArray();
    }
}
