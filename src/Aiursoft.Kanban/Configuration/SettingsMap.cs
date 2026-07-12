using Aiursoft.Kanban.Models;

namespace Aiursoft.Kanban.Configuration;

public class SettingsMap
{
    public const string ProjectName = "ProjectName";
    public const string BrandName = "BrandName";
    public const string BrandHomeUrl = "BrandHomeUrl";
    public const string ProjectLogo = "ProjectLogo";
    public const string AllowUserAdjustNickname = "Allow_User_Adjust_Nickname";
    public const string Icp = "Icp";
    public const string DummyNumber = "DummyNumber";
    public const string DummyChoice = "DummyChoice";
    public const string OpenAiChatEndpoint = "OpenAiChatEndpoint";
    public const string OpenAiModel = "OpenAiModel";
    public const string OpenAiApiToken = "OpenAiApiToken";
    public const string AgentSystemPrompt = "AgentSystemPrompt";
    public const string AutoSetPlannedStartTime = "AutoSetPlannedStartTime";
    public const string PlannedStartTimeAdvanceDays = "PlannedStartTimeAdvanceDays";

    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    private static readonly FakeLocalizer Localizer = new();

    public static readonly List<GlobalSettingDefinition> Definitions = new()
    {
        new GlobalSettingDefinition
        {
            Key = ProjectName,
            Name = Localizer["Project Name"],
            Description = Localizer["The name of the project displayed in the frontend."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft Kanban"
        },
        new GlobalSettingDefinition
        {
            Key = BrandName,
            Name = Localizer["Brand Name"],
            Description = Localizer["The brand name displayed in the footer."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft"
        },
        new GlobalSettingDefinition
        {
            Key = BrandHomeUrl,
            Name = Localizer["Brand Home URL"],
            Description = Localizer[" The link to the brand's home page."],
            Type = SettingType.Text,
            DefaultValue = "https://www.aiursoft.com/"
        },
        new GlobalSettingDefinition
        {
            Key = ProjectLogo,
            Name = Localizer["Project Logo"],
            Description = Localizer["The logo of the project displayed in the navbar and footer. Support jpg, png, svg."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "project-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = AllowUserAdjustNickname,
            Name = Localizer["Allow User Adjust Nickname"],
            Description = Localizer["Allow users to adjust their nickname in the profile management page."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = Icp,
            Name = Localizer["ICP Number"],
            Description = Localizer["The ICP license number for China mainland users. Leave empty to hide."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = DummyNumber,
            Name = Localizer["Dummy Number"],
            Description = Localizer["A dummy number for testing."],
            Type = SettingType.Number,
            DefaultValue = "0"
        },
        new GlobalSettingDefinition
        {
            Key = DummyChoice,
            Name = Localizer["Dummy Choice"],
            Description = Localizer["A dummy choice for testing."],
            Type = SettingType.Choice,
            DefaultValue = "A",
            ChoiceOptions = new Dictionary<string, string>
            {
                { "A", "Option A" },
                { "B", "Option B" }
            }
        },
        // ── AI: Chat / Agent (4 settings) ──────────────────────────────────────────
        new GlobalSettingDefinition
        {
            Key = OpenAiChatEndpoint,
            Name = Localizer["OpenAI Chat Endpoint"],
            Description = Localizer["The OpenAI-compatible chat completions endpoint used by the Kanban AI agent. Must be the full URL including /v1/messages, e.g. https://ollama.example.com/v1/messages. Leave empty to disable the AI agent."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiModel,
            Name = Localizer["AI Model"],
            Description = Localizer["The LLM model name used for the Kanban AI agent, e.g. aiursoft-instruct:latest, deepseek-chat, or gpt-4o. Must be available at the OpenAI Chat Endpoint above."],
            Type = SettingType.Text,
            DefaultValue = "aiursoft-instruct:latest"
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiApiToken,
            Name = Localizer["OpenAI API Token"],
            Description = Localizer["The bearer token for authenticating with the OpenAI Chat Endpoint, e.g. sk-abc123... or 5a0fbdefa19f.... Leave empty if the endpoint does not require authentication."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = AgentSystemPrompt,
            Name = Localizer["Agent System Prompt"],
            Description = Localizer["The system prompt sent to the LLM as the role definition for the Kanban AI agent. Supports placeholder: {userContext} — injected at runtime with the current user's name, roles, owned boards, and the active board name/ID. Supports {currentDateTime} for the current UTC time."],
            Type = SettingType.Text,
            DefaultValue = @"You are an expert Kanban project assistant embedded in the Aiursoft Kanban application. You work alongside real project teams — product managers, developers, designers — and help them manage their boards efficiently.

Your tone is professional but friendly, like a capable colleague messaging in a work chat. You never use Markdown formatting: no bold, no italics, no lists with dashes or asterisks, no code blocks. Write in plain natural language. When enumerating items, just number them with plain digits like ""1. First item. 2. Second item.""

Core principles you follow strictly:
1. Guess before acting. When the user's request is vague or missing details,  You can guess what user is intend to do and only ask the user when necessary,don't worry about making mistakes.
2. Read before writing. Before any create, update, move, or delete operation, use the read tools to check the current state. This avoids data races and stale assumptions.
3. One step at a time. If a task requires multiple operations, explain the plan briefly, then execute step by step.
4. One tool per turn. You MUST call exactly one tool per response. Never call multiple tools at once. After receiving the tool result, you may call the next tool.
5. Verify before reporting. After making a change, re-read the affected data to confirm it was applied correctly. Only then tell the user it is done.
6. Be transparent. Tell the user what you are doing and why. If you lack information, say so.

When the user asks about cards, columns, or board state, always call the relevant read tool first — even if you think you remember the answer from earlier in the conversation. Data may have changed.

For operations that modify data, the system will ask the user to approve before executing. This is normal and expected — do not apologize for it.

{userContext}"
        },
        new GlobalSettingDefinition
        {
            Key = AutoSetPlannedStartTime,
            Name = Localizer["Auto Set Planned Start Time"],
            Description = Localizer["When enabled, cards with a due date but no planned start time will automatically have their planned start time set based on the advance days setting."],
            Type = SettingType.Bool,
            DefaultValue = "False"
        },
        new GlobalSettingDefinition
        {
            Key = PlannedStartTimeAdvanceDays,
            Name = Localizer["Planned Start Time Advance Days"],
            Description = Localizer["When auto-setting planned start time, the number of days to advance before the due date."],
            Type = SettingType.Number,
            DefaultValue = "4"
        }
    };
}
