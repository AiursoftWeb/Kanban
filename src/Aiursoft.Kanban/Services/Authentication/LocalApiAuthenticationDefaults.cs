namespace Aiursoft.Kanban.Services.Authentication;

public static class LocalApiAuthenticationDefaults
{
    public const string AuthenticationScheme = "LocalApi";
    public const string TokenPrefix = "local.";
    public const string ApiKeyScheme = "PersonalApiKey";
    public const string ApiSchemes = "Bearer," + AuthenticationScheme + "," + ApiKeyScheme;
}
