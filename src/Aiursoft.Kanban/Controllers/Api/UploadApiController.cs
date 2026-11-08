using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services.Authentication;
using Aiursoft.Kanban.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/uploads")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class UploadApiController(StorageService storage) : ControllerBase
{
    private const int CardImageMaxSizeInMb = 10;
    private const string CardImageExtensions = "bmp,gif,jpeg,jpg,png,webp";
    private const string AvatarExtensions = "bmp,jpeg,jpg,png";

    [HttpGet("card-images")]
    public IActionResult CardImages()
    {
        return this.Protocol(new CardImageUploadGrantResponse
        {
            Code = Code.ResultShown,
            Message = "Card image upload grant created.",
            UploadUrl = storage.GetUploadUrl(
                "kanban-images",
                maxSizeInMb: CardImageMaxSizeInMb,
                allowedExtensions: CardImageExtensions),
            MaxSizeInMb = CardImageMaxSizeInMb,
            AllowedExtensions = CardImageExtensions.Split(',').ToList()
        });
    }

    [HttpGet("avatar")]
    public IActionResult Avatar()
    {
        return this.Protocol(new CardImageUploadGrantResponse
        {
            Code = Code.ResultShown,
            Message = "Avatar upload grant created.",
            UploadUrl = storage.GetUploadUrl(
                "avatar",
                maxSizeInMb: CardImageMaxSizeInMb,
                allowedExtensions: AvatarExtensions),
            MaxSizeInMb = CardImageMaxSizeInMb,
            AllowedExtensions = AvatarExtensions.Split(',').ToList()
        });
    }
}
