using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Authentication;
using Aiursoft.Kanban.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/account")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class AccountApiController(
    TemplateDbContext db,
    UserManager<User> userManager,
    GlobalSettingsService settingsService,
    StorageService storage,
    ImageProcessingService image,
    IOptions<AppSettings> appSettings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await CurrentUserAsync();
        return this.Protocol(await BuildResponseAsync(user, "Account settings."));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        if (!await settingsService.GetBoolSettingAsync(SettingsMap.AllowUserAdjustNickname))
        {
            return this.Protocol(Code.Unauthorized, "Adjusting display name is disabled by the administrator.");
        }
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return this.Protocol(Code.InvalidInput, "Display name is required.");
        }
        var user = await CurrentUserAsync();
        user.DisplayName = request.DisplayName.Trim();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityError(result);
        }
        return this.Protocol(await BuildResponseAsync(user, "Profile updated."));
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!appSettings.Value.LocalEnabled)
        {
            return this.Protocol(Code.InvalidInput, "Password is managed by your organization sign-in provider.");
        }
        var user = await CurrentUserAsync();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return IdentityError(result);
        }
        return this.Protocol(Code.JobDone, "Password changed.");
    }

    [HttpPut("report-settings")]
    public async Task<IActionResult> UpdateReportSettings([FromBody] UpdateReportSettingsRequest request)
    {
        var user = await CurrentUserAsync();
        user.EnableDailyReport = request.EnableDailyReport;
        user.EnableWeeklyReport = request.EnableWeeklyReport;
        user.DailyReportLanguage = request.DailyReportLanguage;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityError(result);
        }
        return this.Protocol(await BuildResponseAsync(user, "AI report settings updated."));
    }

    [HttpPut("avatar")]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequest request)
    {
        var relativePath = request.AvatarRelativePath.Replace('\\', '/');
        if (!relativePath.StartsWith("avatar/", StringComparison.Ordinal))
        {
            return this.Protocol(Code.InvalidInput, "Invalid avatar path.");
        }
        string physicalPath;
        try
        {
            physicalPath = storage.GetFilePhysicalPath(relativePath);
        }
        catch (ArgumentException)
        {
            return this.Protocol(Code.InvalidInput, "Invalid avatar path.");
        }
        if (!System.IO.File.Exists(physicalPath) || !await image.IsValidImageAsync(physicalPath))
        {
            return this.Protocol(Code.InvalidInput, "The uploaded file is not a valid image.");
        }

        var user = await CurrentUserAsync();
        user.AvatarRelativePath = relativePath;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityError(result);
        }
        return this.Protocol(await BuildResponseAsync(user, "Avatar updated."));
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount()
    {
        var user = await CurrentUserAsync();
        if (await db.KanbanBoards.AnyAsync(board => board.UserId == user.Id))
        {
            return this.Protocol(Code.InvalidInput,
                "Delete or transfer every board you own before deleting your account.");
        }
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return IdentityError(result);
        }
        return this.Protocol(Code.JobDone, "Account deleted.");
    }

    private async Task<AccountProfileResponse> BuildResponseAsync(User user, string message) => new()
    {
        Code = Code.ResultShown,
        Message = message,
        DisplayName = user.DisplayName,
        Email = user.Email ?? string.Empty,
        AvatarRelativePath = user.AvatarRelativePath,
        AvatarUrl = storage.RelativePathToInternetUrl(user.AvatarRelativePath, HttpContext),
        CanChangeDisplayName = await settingsService.GetBoolSettingAsync(SettingsMap.AllowUserAdjustNickname),
        CanChangePassword = appSettings.Value.LocalEnabled,
        EnableDailyReport = user.EnableDailyReport,
        EnableWeeklyReport = user.EnableWeeklyReport,
        DailyReportLanguage = user.DailyReportLanguage,
        OwnedBoardCount = await db.KanbanBoards.CountAsync(board => board.UserId == user.Id)
    };

    private async Task<User> CurrentUserAsync() =>
        await userManager.GetUserAsync(User) ??
        throw new InvalidOperationException("The authenticated token is not linked to a local user.");

    private IActionResult IdentityError(IdentityResult result) => this.Protocol(
        Code.InvalidInput,
        string.Join(" ", result.Errors.Select(error => error.Description)));
}
