using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.NotificationsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Notifications";
    }

    public List<NotificationItem> Notifications { get; set; } = [];
    public int UnreadCount { get; set; }
}
