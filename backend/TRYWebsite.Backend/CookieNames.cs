namespace TRYWebsite.Backend;

/// <summary>
/// Cookie name constants used throughout the application.
/// </summary>
public static class CookieNames
{
    /// <summary>Persistent "remember me" login token cookie.</summary>
    public const string RememberAdmin = "TRY_RememberAdmin";

    /// <summary>Anonymous visitor GUID cookie (no personal data).</summary>
    public const string VisitorId = "TRY_VisitorId";

    /// <summary>Cookie consent acceptance flag.</summary>
    public const string ConsentGiven = "TRY_CookieConsent";

    /// <summary>One-time flash message cookie (60-second lifespan).</summary>
    public const string Flash = "TRY_Flash";
}
