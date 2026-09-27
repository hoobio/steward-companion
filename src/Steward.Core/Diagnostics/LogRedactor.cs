namespace Steward.Core.Diagnostics;

public static class LogRedactor
{
    // The desktop sign-in URL's challenge query parameter is a PKCE artefact, not a secret by itself, but it is dropped anyway rather than judged case by case.
    public static string PathOnly(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : url;
}
