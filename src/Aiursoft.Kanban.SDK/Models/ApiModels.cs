using System.ComponentModel.DataAnnotations;
using Aiursoft.AiurProtocol.Models;

namespace Aiursoft.Kanban.SDK.Models;

public sealed class MobileConfigurationResponse : AiurResponse
{
    public string AuthenticationMode { get; set; } = string.Empty;
    public bool AllowRegistration { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = [];
}

public sealed class LocalAuthenticationResponse : AiurResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class LocalLoginRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public string EmailOrUserName { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LocalRegistrationRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;
}

public sealed class BoardListResponse : AiurResponse
{
    public List<BoardSummaryDto> Boards { get; set; } = [];
}

public sealed class BoardResponse : AiurResponse
{
    public BoardDto Board { get; set; } = new();
}

public sealed class CardResponse : AiurResponse
{
    public CardDto Card { get; set; } = new();
}

public class BoardSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool CanEdit { get; set; }
    public int ColumnCount { get; set; }
    public int CardCount { get; set; }
}

public sealed class BoardDto : BoardSummaryDto
{
    public List<ColumnDto> Columns { get; set; } = [];
}

public sealed class ColumnDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<CardDto> Cards { get; set; } = [];
}

public sealed class CardDto
{
    public int Id { get; set; }
    public int ColumnId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; }
    public string Priority { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateTime CreationTime { get; set; }
}

public sealed class CreateBoardRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed class CreateColumnRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed class CreateCardRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(160_000)]
    public string? Description { get; set; }
}

public sealed class MoveCardRequest
{
    [Range(1, int.MaxValue)]
    public int TargetColumnId { get; set; }

    [Range(0, int.MaxValue)]
    public int NewOrder { get; set; }
}
