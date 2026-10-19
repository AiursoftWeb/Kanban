using System.Diagnostics.CodeAnalysis;
using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.ApiKeysViewModels;

[ExcludeFromCodeCoverage]
public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "API Keys";
    }

    public IReadOnlyList<PersonalApiKey> ApiKeys { get; init; } = [];
    public string? NewKey { get; init; }
}
