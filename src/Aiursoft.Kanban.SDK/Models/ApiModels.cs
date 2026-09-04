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

public sealed class ArchivedBoardListResponse : AiurResponse
{
    public List<ArchivedBoardDto> OwnedBoards { get; set; } = [];
    public List<ArchivedBoardDto> SharedBoards { get; set; } = [];
}

public sealed class BoardArchiveResponse : AiurResponse
{
    public int BoardId { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedTime { get; set; }
}

public sealed class BoardSharingResponse : AiurResponse
{
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public string PublicUrl { get; set; } = string.Empty;
    public List<BoardShareDto> Shares { get; set; } = [];
    public List<ShareTargetDto> AvailableUsers { get; set; } = [];
    public List<ShareTargetDto> AvailableRoles { get; set; } = [];
}

public sealed class CardResponse : AiurResponse
{
    public CardDto Card { get; set; } = new();
}

public sealed class CardDetailsResponse : AiurResponse
{
    public CardDetailsDto Card { get; set; } = new();
}

public sealed class CardCommentResponse : AiurResponse
{
    public CardCommentDto Comment { get; set; } = new();
}

public sealed class CardImageUploadGrantResponse : AiurResponse
{
    public string UploadUrl { get; set; } = string.Empty;
    public int MaxSizeInMb { get; set; }
    public List<string> AllowedExtensions { get; set; } = [];
}

public sealed class CardImageUploadResponse
{
    public string Path { get; set; } = string.Empty;
    public string InternetPath { get; set; } = string.Empty;
}

public sealed class CardLabelResponse : AiurResponse
{
    public CardLabelDto Label { get; set; } = new();
}

public sealed class CardTransferTargetsResponse : AiurResponse
{
    public List<CardTransferBoardDto> Boards { get; set; } = [];
}

public sealed class CardTransferResponse : AiurResponse
{
    public int CardId { get; set; }
    public int BoardId { get; set; }
    public int ColumnId { get; set; }
}

public sealed class CardSubscriptionResponse : AiurResponse
{
    public bool IsSubscribed { get; set; }
}

public sealed class DailyReportListResponse : AiurResponse
{
    public List<DailyReportDto> Reports { get; set; } = [];
    public DailyReportDto? TodayPlan { get; set; }
    public DailyReportDto? TodaySummary { get; set; }
    public bool CanGeneratePlan { get; set; }
    public bool CanGenerateSummary { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public sealed class DailyReportResponse : AiurResponse
{
    public DailyReportDto Report { get; set; } = new();
}

public sealed class WeeklyReportListResponse : AiurResponse
{
    public List<WeeklyReportDto> Reports { get; set; } = [];
    public WeeklyReportDto? CurrentWeekReport { get; set; }
    public DateTime CurrentWeekStart { get; set; }
    public bool CanGenerate { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public sealed class WeeklyReportResponse : AiurResponse
{
    public WeeklyReportDto Report { get; set; } = new();
}

public sealed class MyTasksResponse : AiurResponse
{
    public List<TaskCardDto> Cards { get; set; } = [];
    public CardUserDto TargetUser { get; set; } = new();
    public bool IsViewingOtherUser { get; set; }
    public bool CanViewAnyUserTasks { get; set; }
    public List<CardUserDto> AvailableUsers { get; set; } = [];
    public List<TaskLabelFilterDto> AvailableLabels { get; set; } = [];
    public List<int> SelectedLabelIds { get; set; } = [];
    public string SelectedStatus { get; set; } = string.Empty;
    public string SelectedLabelMode { get; set; } = string.Empty;
    public string SelectedSort { get; set; } = string.Empty;
}

public sealed class CardSearchResponse : AiurResponse
{
    public string Query { get; set; } = string.Empty;
    public bool UsedAi { get; set; }
    public int TotalCount { get; set; }
    public List<TaskCardDto> Cards { get; set; } = [];
}

public sealed class DashboardResponse : AiurResponse
{
    public int OwnedBoardCount { get; set; }
    public int SharedBoardCount { get; set; }
    public int AssignedTaskCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public int InProgressTaskCount { get; set; }
    public List<TaskCardDto> AssignedTasks { get; set; } = [];
    public List<DashboardBoardDto> OwnedBoards { get; set; } = [];
    public List<DashboardBoardDto> SharedBoards { get; set; } = [];
    public DailyReportDto? LatestPlan { get; set; }
    public DailyReportDto? LatestSummary { get; set; }
}

public sealed class GanttResponse : AiurResponse
{
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public List<TaskCardDto> Cards { get; set; } = [];
}

public sealed class AccountProfileResponse : AiurResponse
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string AvatarRelativePath { get; set; } = string.Empty;
    public bool CanChangeDisplayName { get; set; }
    public bool CanChangePassword { get; set; }
    public bool EnableDailyReport { get; set; }
    public bool EnableWeeklyReport { get; set; }
    public string DailyReportLanguage { get; set; } = "en";
    public int OwnedBoardCount { get; set; }
}

public sealed class NotificationListResponse : AiurResponse
{
    public int UnreadCount { get; set; }
    public List<NotificationDto> Notifications { get; set; } = [];
}

public sealed class OperationLogListResponse : AiurResponse
{
    public List<OperationLogDto> Logs { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public bool Enabled { get; set; }
}

public sealed class AgentConversationResponse : AiurResponse
{
    public Guid ConversationId { get; set; }
}

public sealed class AgentStatusResponse : AiurResponse
{
    public Guid ConversationId { get; set; }
    public int BoardId { get; set; }
    public string State { get; set; } = string.Empty;
    public List<AgentMessageDto> Messages { get; set; } = [];
    public List<AgentAdviceDto> PendingAdvice { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public sealed class AgentExcelConversionResponse : AiurResponse
{
    public string Markdown { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public class BoardSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool CanEdit { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedTime { get; set; }
    public int ColumnCount { get; set; }
    public int CardCount { get; set; }
}

public sealed class ArchivedBoardDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public DateTime? ArchivedTime { get; set; }
    public int ColumnCount { get; set; }
    public int CardCount { get; set; }
    public int IncompleteCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedCount { get; set; }
    public int OverdueCount { get; set; }
    public int UnassignedCount { get; set; }
    public string? Permission { get; set; }
    public string? SharedVia { get; set; }
}

public sealed class BoardDto : BoardSummaryDto
{
    public int Order { get; set; }
    public bool IsPublic { get; set; }
    public List<ColumnDto> Columns { get; set; } = [];
}

public sealed class BoardShareDto
{
    public Guid Id { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

public sealed class ShareTargetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
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
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public int? RecurrenceInterval { get; set; }
    public string RecurrenceUnit { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public CardUserDto? AssignedUser { get; set; }
    public List<CardLabelDto> Labels { get; set; } = [];
    public int CommentCount { get; set; }
}

public sealed class CardDetailsDto
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public int ColumnId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = string.Empty;
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public int? RecurrenceInterval { get; set; }
    public string RecurrenceUnit { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool IsSubscribed { get; set; }
    public CardUserDto? AssignedUser { get; set; }
    public CardUserDto? CreatorUser { get; set; }
    public List<CardUserDto> AvailableAssignees { get; set; } = [];
    public List<CardColumnOptionDto> AvailableColumns { get; set; } = [];
    public List<CardLabelDto> Labels { get; set; } = [];
    public List<CardLabelDto> AvailableLabels { get; set; } = [];
    public List<CardCommentDto> Comments { get; set; } = [];
}

public sealed class CardUserDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CardLabelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class CardColumnOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CardTransferBoardDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<CardColumnOptionDto> Columns { get; set; } = [];
}

public sealed class CardCommentDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Images { get; set; } = string.Empty;
    public CardUserDto Author { get; set; } = new();
    public DateTime CreationTime { get; set; }
    public bool CanDelete { get; set; }
}

public sealed class DailyReportDto
{
    public Guid Id { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public sealed class WeeklyReportDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime WeekStart { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public sealed class TaskCardDto
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public int ColumnId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = string.Empty;
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public DateTime CreationTime { get; set; }
    public CardUserDto? AssignedUser { get; set; }
    public List<CardLabelDto> Labels { get; set; } = [];
}

public sealed class TaskLabelFilterDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int UsageCount { get; set; }
}

public sealed class DashboardBoardDto
{
    public int BoardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalCards { get; set; }
    public int IncompleteCards { get; set; }
    public int InProgressCards { get; set; }
    public int CompletedCards { get; set; }
    public int OverdueCards { get; set; }
    public string? Permission { get; set; }
}

public sealed class NotificationDto
{
    public int Id { get; set; }
    public int? CardId { get; set; }
    public int? BoardId { get; set; }
    public string? CardTitle { get; set; }
    public string? BoardName { get; set; }
    public string? ColumnName { get; set; }
    public string? CommentContent { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ActorUserName { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

public sealed class OperationLogDto
{
    public DateTime EventTime { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}

public sealed class AgentMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public List<AgentToolCallDto> ToolCalls { get; set; } = [];
    public string? ToolCallId { get; set; }
}

public sealed class AgentToolCallDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Arguments { get; set; }
}

public sealed class AgentAdviceDto
{
    public Guid AdviceId { get; set; }
    public string ToolDisplayName { get; set; } = string.Empty;
    public string ParameterDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<AgentAdviceParameterDto> Parameters { get; set; } = [];
    public string? ResolvedName { get; set; }
}

public sealed class AgentAdviceParameterDto
{
    public string Key { get; set; } = string.Empty;
    public string DisplayKey { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class CreateBoardRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed class SetBoardArchiveRequest
{
    public bool Archive { get; set; }
}

public sealed class UpdateBoardRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    public int? Order { get; set; }
}

public sealed class UpdateBoardVisibilityRequest
{
    public bool IsPublic { get; set; }
}

public sealed class AddBoardShareRequest
{
    [StringLength(450)]
    public string? TargetUserId { get; set; }

    [StringLength(450)]
    public string? TargetRoleId { get; set; }

    [Required, RegularExpression("^(ReadOnly|Editable)$")]
    public string Permission { get; set; } = "ReadOnly";
}

public sealed class UpdateProfileRequest
{
    [Required, StringLength(30, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class UpdateReportSettingsRequest
{
    public bool EnableDailyReport { get; set; }
    public bool EnableWeeklyReport { get; set; }

    [Required, RegularExpression("^(en|zh|ja|ko)$")]
    public string DailyReportLanguage { get; set; } = "en";
}

public sealed class UpdateAvatarRequest
{
    [Required, StringLength(150, MinimumLength = 2)]
    [RegularExpression("^avatar[/\\\\].+")]
    public string AvatarRelativePath { get; set; } = string.Empty;
}

public sealed class CreateColumnRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateColumnRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    [RegularExpression("^(NotStarted|InProgress|Completed)$")]
    public string? Status { get; set; }
}

public sealed class MoveColumnRequest
{
    [Range(0, int.MaxValue)]
    public int NewOrder { get; set; }
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

public sealed class TransferCardRequest
{
    [Range(1, int.MaxValue)]
    public int TargetBoardId { get; set; }

    [Range(1, int.MaxValue)]
    public int TargetColumnId { get; set; }
}

public sealed class UpdateCardRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(160_000)]
    public string? Description { get; set; }

    [Required, RegularExpression("^(Urgent|High|Medium|Low|None)$")]
    public string Priority { get; set; } = "None";

    [StringLength(450)]
    public string? AssignedUserId { get; set; }

    public DateTime? PlannedStartTime { get; set; }

    public DateTime? DueDate { get; set; }

    [Range(1, 365)]
    public int? RecurrenceInterval { get; set; }

    [Required, RegularExpression("^(None|Day|Week|Month|Year)$")]
    public string RecurrenceUnit { get; set; } = "None";
}

public sealed class AddCardCommentRequest
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Images { get; set; }
}

public sealed class AddCardLabelRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed class SetCardSubscriptionRequest
{
    public bool Subscribe { get; set; }
}

public sealed class AgentSendMessageRequest
{
    [Range(0, int.MaxValue)]
    public int BoardId { get; set; }

    [Required]
    public string Message { get; set; } = string.Empty;

    public Guid? ConversationId { get; set; }
    public string? ExcelMarkdown { get; set; }
}
