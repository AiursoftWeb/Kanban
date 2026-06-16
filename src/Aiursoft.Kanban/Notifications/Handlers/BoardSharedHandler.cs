using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class BoardSharedHandler(TemplateDbContext db) : INotificationHandler<BoardSharedEvent>
{
    public async Task Handle(BoardSharedEvent e, CancellationToken ct)
    {
        var board = await db.KanbanBoards
            .FirstOrDefaultAsync(b => b.Id == e.BoardId, ct);
        if (board == null) return;

        var actorName = await CardCommentAddedHandler.GetUserDisplayName(db, e.ActorUserId);

        db.Notifications.Add(new Notification
        {
            BoardId = e.BoardId,
            UserId = e.SharedWithUserId,
            ActorUserId = e.ActorUserId,
            Type = NotificationType.BoardShared,
            Message = NotificationTemplateService.BuildMessage(NotificationType.BoardShared,
                new Dictionary<string, string>
                {
                    ["ActorName"] = actorName,
                    ["BoardName"] = board.Name
                })
        });

        await db.SaveChangesAsync(ct);
    }
}
