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
