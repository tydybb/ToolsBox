namespace ToolsBox.Core.WebResources;

public static class NewWindowNavigationPolicy
{
    public static bool CanNavigate(string url, bool isUserInitiated)
        => isUserInitiated && !string.IsNullOrEmpty(url) && WebResourceRules.IsWebUrl(url);
}
