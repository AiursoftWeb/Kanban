using System.ComponentModel.DataAnnotations;
using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class AddShareViewModel
{
    public string? TargetUserId { get; set; }

    public string? TargetRoleId { get; set; }

    [Required]
    public SharePermission Permission { get; set; }
}
