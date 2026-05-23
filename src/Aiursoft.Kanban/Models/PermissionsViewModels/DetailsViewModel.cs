using System.ComponentModel.DataAnnotations;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Kanban.Models.PermissionsViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Permission Details";
    }

    [Display(Name = "Permission")]
    public required PermissionDescriptor Permission { get; set; }

    [Display(Name = "Roles")]
    public required List<IdentityRole> Roles { get; set; }

    [Display(Name = "Users")]
    public required List<User> Users { get; set; }
}
