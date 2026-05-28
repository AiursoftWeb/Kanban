using System.ComponentModel;
using System.Text.RegularExpressions;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class LabelWriteTools(
    TemplateDbContext db,
    KanbanAccessService access) : IScopedDependency
{
    private static readonly string[] LabelColors =
    [
        "#EF4444", "#F97316", "#EAB308", "#22C55E",
        "#3B82F6", "#8B5CF6", "#EC4899", "#14B8A6"
    ];

    private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    [McpServerTool, Description("Add a label to a card. Creates the label if it does not exist.")]
    [Advice]
    public async Task<string> AddLabel(
        [Description("Card ID")] int cardId,
        [Description("Label name")] string name,
        [Description("Current user ID")] string userId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Label name is required.";

        var normalizedName = name.Trim();
        if (normalizedName.Length > 100)
            return "Error: Label name is too long (max 100 characters).";

        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        var normalizedUpperName = normalizedName.ToUpperInvariant();
        var label = await db.KanbanLabels
            .FirstOrDefaultAsync(l => l.Name.ToUpper() == normalizedUpperName);

        if (label == null)
        {
            label = new KanbanLabel
            {
                Name = normalizedName,
                Color = LabelColors[Random.Shared.Next(LabelColors.Length)]
            };
            db.KanbanLabels.Add(label);
        }

        var alreadyLinked = await db.KanbanCardLabels
            .AnyAsync(link => link.CardId == cardId && link.LabelId == label.Id);
        if (!alreadyLinked)
        {
            db.KanbanCardLabels.Add(new KanbanCardLabel { CardId = cardId, Label = label });
        }

        await db.SaveChangesAsync();

        return $"Label \"{label.Name}\" (Color: {label.Color}) {(alreadyLinked ? "already exists on" : "added to")} card #{cardId} \"{card.Title}\".";
    }

    [McpServerTool, Description("Remove a label from a card")]
    [Advice]
    public async Task<string> RemoveLabel(
        [Description("Card ID")] int cardId,
        [Description("Label ID to remove")] int labelId,
        [Description("Current user ID")] string userId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        var cardLabel = await db.KanbanCardLabels
            .FirstOrDefaultAsync(link => link.CardId == cardId && link.LabelId == labelId);
        if (cardLabel == null) return "Error: Label not found on this card.";

        db.KanbanCardLabels.Remove(cardLabel);
        await db.SaveChangesAsync();

        return $"Label removed from card #{cardId} \"{card.Title}\".";
    }

    [McpServerTool, Description("Change the color of a label on a card")]
    [Advice]
    public async Task<string> UpdateLabelColor(
        [Description("Card ID")] int cardId,
        [Description("Label ID")] int labelId,
        [Description("Hex color code, e.g. #FF5733")] string color,
        [Description("Current user ID")] string userId)
    {
        var normalizedColor = color.Trim();
        if (!HexColorRegex.IsMatch(normalizedColor))
            return "Error: Color must be a hex value like #FF5733.";

        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        var label = await db.KanbanCardLabels
            .Where(link => link.CardId == cardId && link.LabelId == labelId)
            .Select(link => link.Label)
            .FirstOrDefaultAsync();
        if (label == null) return "Error: Label not found on this card.";

        label.Color = normalizedColor;
        await db.SaveChangesAsync();

        return $"Label \"{label.Name}\" color changed to {label.Color}.";
    }
}
