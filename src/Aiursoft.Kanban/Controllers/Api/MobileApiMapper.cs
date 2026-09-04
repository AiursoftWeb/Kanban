using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;

namespace Aiursoft.Kanban.Controllers.Api;

internal static class MobileApiMapper
{
    public static CardUserDto ToUserDto(User user) => new()
    {
        Id = user.Id,
        DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName
    };

    public static TaskCardDto ToTaskDto(KanbanCard card) => new()
    {
        Id = card.Id,
        BoardId = card.Column.BoardId,
        BoardName = card.Column.Board.Name,
        ColumnId = card.ColumnId,
        ColumnName = card.Column.Name,
        Status = card.Column.ColumnStatus.ToString(),
        Title = card.Title,
        Description = card.Description,
        Priority = card.Priority.ToString(),
        PlannedStartTime = card.PlannedStartTime,
        DueDate = card.DueDate,
        ActualStartTime = card.ActualStartTime,
        ActualEndTime = card.ActualEndTime,
        CreationTime = card.CreationTime,
        AssignedUser = card.AssignedUser == null ? null : ToUserDto(card.AssignedUser),
        Labels = card.CardLabels
            .OrderBy(link => link.Label.Name)
            .Select(link => new CardLabelDto
            {
                Id = link.LabelId,
                Name = link.Label.Name,
                Color = link.Label.Color
            })
            .ToList()
    };
}
