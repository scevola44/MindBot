using System.Security.Cryptography;
using System.Text;

namespace MindBot.Core.Web;

/// <summary>A device allowed to use the web channel. The token itself is never stored.</summary>
public sealed record DeviceInfo(long Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? RevokedAt)
{
    public bool IsRevoked => RevokedAt is not null;
}

/// <summary>
/// Device token format and hashing.
/// <para>
/// A token is <see cref="Prefix"/> followed by 32 random bytes in base64url (256 bits), so it
/// cannot be guessed and needs no rate limiting. Only its SHA-256 hash is stored: a leaked state
/// database does not hand out working tokens. The fixed prefix is what lets the log redactor
/// recognise a token anywhere it appears.
/// </para>
/// </summary>
public static class DeviceTokens
{
    public const string Prefix = "mbt_";

    private const int RandomBytes = 32;

    public static string Generate() =>
        Prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>
    /// Lowercase hex SHA-256. Lookups compare hashes, not tokens, so a timing difference in the
    /// comparison reveals nothing an attacker can use to build a valid token.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Cheap shape check, so obvious garbage never reaches the database.</summary>
    public static bool LooksValid(string? token) =>
        token is not null
        && token.Length == Prefix.Length + 43
        && token.StartsWith(Prefix, StringComparison.Ordinal)
        && token.AsSpan(Prefix.Length).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public interface IDeviceTokenStore
{
    /// <summary>Creates a device and returns its token. The token is shown once and cannot be recovered.</summary>
    Task<(DeviceInfo Device, string Token)> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes the active device with this name. False when there is none.</summary>
    Task<bool> RevokeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active device this token belongs to, or null. Reads the database every time: devices
    /// are revoked from a separate CLI process, so an in-memory cache would keep a revoked token
    /// working.
    /// </summary>
    Task<DeviceInfo?> AuthenticateAsync(string? token, CancellationToken cancellationToken = default);
}
