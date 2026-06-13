namespace TRYWebsite.Backend;

/// <summary>
/// Session key constants used throughout the application.
/// </summary>
public static class SessionKeys
{
    /// <summary>Admin username stored in session after login.</summary>
    public const string AdminUser = "AdminUser";

    /// <summary>ISO 8601 datetime string of when the admin logged in.</summary>
    public const string AdminLoginAt = "AdminLoginAt";

    /// <summary>JSON-serialized volunteer form draft saved on validation failure.</summary>
    public const string VolunteerForm = "VolunteerForm";

    /// <summary>One-time flash message in format "type|message text".</summary>
    public const string FlashMessage = "FlashMessage";
}
