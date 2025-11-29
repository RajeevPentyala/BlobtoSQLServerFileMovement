namespace BlobReaderFunction.Models;

/// <summary>
/// Configuration model for SharePoint connection
/// </summary>
public class SharePointConfig
{
    /// <summary>
    /// SharePoint site URL (e.g., https://company.sharepoint.com/sites/YourSite)
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// Document library name (e.g., "Documents")
    /// </summary>
    public string DocumentLibrary { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Client ID (App Registration)
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Client Secret
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Tenant ID
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}
