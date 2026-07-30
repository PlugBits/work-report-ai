using System.Security.Cryptography;
using Microsoft.Identity.Client;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Microsoft Graph delegated (read-only) sign-in via MSAL. Tokens are cached
/// in a DPAPI-encrypted file rather than SQLite/settings/logs.
///
/// NOTE for docs: the MVP spec says tokens go to Windows Credential Manager,
/// but generic-credential blobs cap at 2560 bytes -- smaller than an MSAL
/// cache -- so this uses a DPAPI-encrypted file under
/// %LOCALAPPDATA%\WorkLog AI\Auth instead. Tokens never touch SQLite,
/// settings, or logs.
/// </summary>
public sealed class GraphAuthService : IGraphTokenProvider
{
    private static readonly string[] Scopes = ["Mail.Read", "Calendars.Read"];

    private readonly string _clientId;
    private readonly string _tenantId;
    private readonly string _cacheFilePath;
    private readonly Lazy<IPublicClientApplication> _application;

    public GraphAuthService(string clientId, string tenantId, bool sampleMode = false)
        : this(clientId, tenantId, DefaultCacheFilePath(sampleMode))
    {
    }

    public GraphAuthService(string clientId, string tenantId, string cacheFilePath)
    {
        ArgumentNullException.ThrowIfNull(cacheFilePath);
        _clientId = (clientId ?? string.Empty).Trim();
        _tenantId = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId.Trim();
        _cacheFilePath = cacheFilePath;
        _application = new Lazy<IPublicClientApplication>(BuildApplication);
    }

    public static string DefaultCacheFilePath(bool sampleMode = false)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkLog AI",
            "Auth");
        var fileName = sampleMode ? "msal.sample.cache" : "msal.cache";
        return Path.Combine(root, fileName);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            return null;
        }

        var application = _application.Value;
        var accounts = await application.GetAccountsAsync();
        var account = accounts.FirstOrDefault();
        if (account is null)
        {
            return null;
        }

        try
        {
            var result = await application
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
        catch (MsalException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the system browser for an interactive sign-in. Must only be
    /// called from a user-initiated action (e.g. the settings window) --
    /// collectors must never trigger this.
    /// </summary>
    public async Task<string?> SignInAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            throw new InvalidOperationException("クライアントIDが設定されていません。");
        }

        var application = _application.Value;
        var result = await application
            .AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken);
        return result.Account?.Username;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var application = _application.Value;
        var accounts = await application.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await application.RemoveAsync(account);
        }

        TokenCacheSerializer.TryDelete(_cacheFilePath);
    }

    public async Task<string?> GetSignedInUserAsync(CancellationToken cancellationToken = default)
    {
        var application = _application.Value;
        var accounts = await application.GetAccountsAsync();
        return accounts.FirstOrDefault()?.Username;
    }

    private IPublicClientApplication BuildApplication()
    {
        var application = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
            .WithRedirectUri("http://localhost")
            .Build();
        application.UserTokenCache.SetBeforeAccess(OnBeforeAccess);
        application.UserTokenCache.SetAfterAccess(OnAfterAccess);
        return application;
    }

    private void OnBeforeAccess(TokenCacheNotificationArgs args)
    {
        var cache = TokenCacheSerializer.ReadCache(_cacheFilePath, DpapiUnprotect);
        if (cache is not null)
        {
            args.TokenCache.DeserializeMsalV3(cache);
        }
    }

    private void OnAfterAccess(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
        {
            return;
        }
        TokenCacheSerializer.WriteCache(_cacheFilePath, args.TokenCache.SerializeMsalV3(), DpapiProtect);
    }

    private static byte[] DpapiProtect(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    private static byte[]? DpapiUnprotect(byte[] data)
    {
        try
        {
            return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
