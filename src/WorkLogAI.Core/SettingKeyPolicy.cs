namespace WorkLogAI.Core;

public static class SettingKeyPolicy
{
    private static readonly string[] SecretFragments =
    [
        "apikey",
        "token",
        "password",
        "secret",
        "credential"
    ];

    public static void EnsureNonSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A setting key is required.", nameof(key));
        }

        var normalized = string.Concat(
            key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
        if (SecretFragments.Any(normalized.Contains))
        {
            throw new InvalidOperationException(
                $"The setting '{key}' may contain a secret and cannot be persisted. " +
                "Use Windows Credential Manager for credentials.");
        }
    }
}
