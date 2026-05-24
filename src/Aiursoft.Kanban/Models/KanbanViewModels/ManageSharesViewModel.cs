using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class ManageSharesViewModel : UiStackLayoutViewModel
{
    public ManageSharesViewModel(string pageTitle)
    {
        PageTitle = pageTitle;
    }

    public required int BoardId { get; init; }
    public required string BoardName { get; init; }
    public required bool IsPublic { get; init; }
    public string? PublicLink { get; init; }
    public required List<BoardShare> ExistingShares { get; init; }
    public required List<IdentityRole> AvailableRoles { get; init; }
    public required List<User> AvailableUsers { get; init; }
}
