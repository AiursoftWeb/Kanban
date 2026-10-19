using System.Security.Cryptography;
using System.Text;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.ApiKeysViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Authentication;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ApiKeysController(TemplateDbContext db, UserManager<User> userManager,
    IStringLocalizer<ApiKeysController> localizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Settings", NavGroupOrder = 9998,
        CascadedLinksGroupName = "Personal", CascadedLinksIcon = "user", CascadedLinksOrder = 1,
        LinkText = "API Keys", LinkOrder = 2)]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        return this.StackView(new IndexViewModel
        {
            ApiKeys = await db.PersonalApiKeys.AsNoTracking()
                .Where(k => k.UserId == userId).OrderByDescending(k => k.CreatedAt).ToListAsync(),
            NewKey = TempData["NewKey"] as string
        });
    }

    [HttpGet]
    public IActionResult Create() => this.StackView(new CreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        var now = DateTime.UtcNow;
        if (model.ExpirationDays > (now.AddYears(15) - now).Days)
        {
            ModelState.AddModelError(nameof(model.ExpirationDays), localizer["Validity cannot exceed 15 years."]);
        }
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var rawKey = ApiKeyAuthenticationHandler.TokenPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.PersonalApiKeys.Add(new PersonalApiKey
        {
            Name = model.Name.Trim(),
            KeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))),
            Prefix = rawKey[..15],
            UserId = userManager.GetUserId(User)!,
            CreatedAt = now,
            ExpirationTime = now.AddDays(model.ExpirationDays)
        });
        await db.SaveChangesAsync();
        TempData["NewKey"] = rawKey;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var key = await db.PersonalApiKeys.SingleOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null)
        {
            return NotFound();
        }
        db.PersonalApiKeys.Remove(key);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
