using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services.Authentication;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/auth/local")]
[AllowAnonymous]
[LimitPerMin]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class MobileAuthenticationController(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    LocalApiTokenService tokenService,
    IOptions<AppSettings> appSettings) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LocalLoginRequest request)
    {
        if (!appSettings.Value.LocalEnabled)
        {
            return this.Protocol(Code.Unauthorized, "Local authentication is disabled on this server.");
        }

        var user = await userManager.FindByEmailAsync(request.EmailOrUserName.Trim())
            ?? await userManager.FindByNameAsync(request.EmailOrUserName.Trim());
        if (user == null || !(await signInManager.CheckPasswordSignInAsync(
                user, request.Password, lockoutOnFailure: true)).Succeeded)
        {
            return this.Protocol(Code.Unauthorized, "Invalid username or password.");
        }

        return AuthenticationResponse(user, "Signed in.");
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] LocalRegistrationRequest request)
    {
        var settings = appSettings.Value;
        if (!settings.LocalEnabled || !settings.Local.AllowRegister)
        {
            return this.Protocol(Code.Unauthorized, "Local registration is disabled on this server.");
        }

        var email = request.Email.Trim();
        var displayName = email.Split('@')[0];
        if (displayName.Length < 2)
        {
            displayName = email;
        }
        displayName = displayName[..Math.Min(displayName.Length, 30)];
        var user = new User
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return this.Protocol(Code.InvalidInput,
                string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        if (!string.IsNullOrWhiteSpace(settings.DefaultRole))
        {
            var roleResult = await userManager.AddToRoleAsync(user, settings.DefaultRole);
            if (!roleResult.Succeeded)
            {
                return this.Protocol(Code.InvalidInput,
                    string.Join(" ", roleResult.Errors.Select(error => error.Description)));
            }
        }

        return AuthenticationResponse(user, "Account created.");
    }

    private IActionResult AuthenticationResponse(User user, string message)
    {
        var token = tokenService.Issue(user.Id);
        return this.Protocol(new LocalAuthenticationResponse
        {
            Code = Code.JobDone,
            Message = message,
            AccessToken = token.AccessToken,
            ExpiresAt = token.ExpiresAt,
            DisplayName = user.DisplayName
        });
    }
}
