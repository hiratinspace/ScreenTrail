using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ScreenTrail.Api.Auth;

/// <summary>
/// How the API is configured to trust a token (ST-008).
///
/// There is no default signing key, and that is deliberate. A development default is a production key
/// the day somebody forgets to set one, and the failure is silent: everything works, and anyone who
/// has read the source can mint a token for any tenant. The service refuses to start without one.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "screentrail";

    public string Audience { get; set; } = "screentrail-api";

    /// <summary>
    /// Random bytes written as base64 or hex, at least 32 of them (<c>openssl rand -base64 48</c>). Read
    /// from configuration; there is no fallback. Tokens are signed with the bytes of the text as written.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// During a rotation, the key <see cref="SigningKey"/> replaced: accepted for checking device tokens,
    /// admin sessions and sign-in nonces, never for signing, so nobody is signed out at the switch. Empty
    /// outside a rotation; remove it once the longest of those has run out (docs/security/key-rotation.md).
    /// </summary>
    public string PreviousSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an access token lasts. Short, because a device holds a long-lived refresh token and
    /// exchanges it: a leaked access token is then worth minutes rather than months. Between
    /// <see cref="ShortestAccessTokenLifetime"/> and <see cref="LongestAccessTokenLifetime"/>.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Shorter, and the device's renewal margin (five minutes) would leave it almost no time holding one.</summary>
    public static readonly TimeSpan ShortestAccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Longer, and a leaked access token is a bearer credential for more than a day.</summary>
    public static readonly TimeSpan LongestAccessTokenLifetime = TimeSpan.FromHours(24);
}

/// <summary>Claims this service issues and reads. Names kept short; none of them is personal data.</summary>
public static class ScreenTrailClaims
{
    public const string TenantId = "tid";

    public const string UserId = "uid";

    public const string DeviceId = "did";

    /// <summary>The owner an admin session is for (ST-099). Never on a device token.</summary>
    public const string OwnerId = "oid";

    /// <summary>What an admin session may do: <see cref="AdminSession.OwnerRole"/>. Never on a device token.</summary>
    public const string Role = "role";

    /// <summary>The admin session's CSRF value, which every change must also carry in a header (ADR-0009).</summary>
    public const string Csrf = "csrf";

    /// <summary>The owner's <see cref="Data.Owner.SessionVersion"/> when the session was issued. Sign-out moves it on.</summary>
    public const string SessionVersion = "sv";
}

/// <summary>Mints and describes access tokens (ST-008).</summary>
public sealed class TokenIssuer(JwtOptions options)
{
    /// <summary>
    /// An access token for one device.
    ///
    /// The subject is the device, and tenant and user ride along as claims so that every query can be
    /// scoped without another database round trip. No email, no display name, no machine name: a token
    /// is logged by proxies and stored in client memory, and none of that belongs there (INV-10).
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) ForDevice(Guid tenantId, Guid userId, Guid deviceId, TimeProvider? time = null)
    {
        var now = (time ?? TimeProvider.System).GetUtcNow();
        var expires = now + options.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, deviceId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ScreenTrailClaims.TenantId, tenantId.ToString()),
                new Claim(ScreenTrailClaims.UserId, userId.ToString()),
                new Claim(ScreenTrailClaims.DeviceId, deviceId.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(KeyFrom(options.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        return (new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor), expires);
    }

    /// <summary>
    /// An admin session for a tenant's owner (ST-099, ADR-0009), carried in an HttpOnly cookie.
    ///
    /// Told apart from a device token four ways, any one of which is enough on its own: it is signed with
    /// a key derived for admin sessions alone (<see cref="AdminKeyFrom"/>); its audience is
    /// <see cref="AdminSession.Audience"/>, which the device scheme does not accept; it carries an owner
    /// id and a role and no device id, so <see cref="CallerCheck"/> finds no device in it; and it arrives
    /// in a cookie the device scheme never reads. The CSRF value rides inside it, so the server keeps no
    /// session state at all.
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) ForOwner(Guid tenantId, Guid ownerId, int sessionVersion, string csrf, TimeSpan lifetime, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csrf);
        var now = (time ?? TimeProvider.System).GetUtcNow();
        var expires = now + lifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = AdminSession.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, ownerId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ScreenTrailClaims.TenantId, tenantId.ToString()),
                new Claim(ScreenTrailClaims.OwnerId, ownerId.ToString()),
                new Claim(ScreenTrailClaims.Role, AdminSession.OwnerRole),
                new Claim(ScreenTrailClaims.Csrf, csrf),
                new Claim(ScreenTrailClaims.SessionVersion, sessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]),
            SigningCredentials = new SigningCredentials(AdminKeyFrom(options.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        return (new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor), expires);
    }

    /// <summary>
    /// The device rules with the admin session's audience and the admin session's own key in place of the
    /// devices' ones. Neither scheme can check the other's signature.
    /// </summary>
    public static TokenValidationParameters AdminValidation(JwtOptions options)
    {
        var validation = Validation(options);
        validation.ValidAudience = AdminSession.Audience;
        validation.IssuerSigningKey = AdminKeyFrom(options.SigningKey);
        validation.IssuerSigningKeys = Previous(options) is { } previous ? [AdminKeyFrom(previous)] : null;
        return validation;
    }

    /// <summary>The label the sign-in nonce's MAC key is derived under (ADR-0009). Changing it voids every nonce handed out.</summary>
    public const string NonceKeyLabel = "screentail-admin-sign-in-nonce-key-v1";

    /// <summary>The sign-in nonce's MAC key: HMAC-SHA256 of its own label under the configured key, like <see cref="AdminKeyFrom"/>.</summary>
    public static byte[] NonceKeyFrom(string key) =>
        HMACSHA256.HashData(KeyFrom(key).Key, Encoding.UTF8.GetBytes(NonceKeyLabel));

    /// <summary>The nonce keys a sign-in nonce is checked against: the current key's, then the previous key's during a rotation.</summary>
    public static IReadOnlyList<byte[]> NonceKeysFrom(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Previous(options) is { } previous ? [NonceKeyFrom(options.SigningKey), NonceKeyFrom(previous)] : [NonceKeyFrom(options.SigningKey)];
    }

    /// <summary>The label the admin session's key is derived under. Changing it ends every admin session.</summary>
    public const string AdminKeyLabel = "screentail-admin-session-key-v1";

    /// <summary>
    /// The admin session's signing key: HMAC-SHA256 of a fixed label under the configured key (ADR-0009).
    /// One secret to configure, two keys in use, so a device token and an admin session never share a key
    /// and a mistake that let one scheme read the other's tokens would still fail on the signature.
    /// </summary>
    public static SymmetricSecurityKey AdminKeyFrom(string key) =>
        new(HMACSHA256.HashData(KeyFrom(key).Key, Encoding.UTF8.GetBytes(AdminKeyLabel)));

    /// <summary>
    /// The rules a token must satisfy, all of them on.
    ///
    /// The algorithm is pinned to HS256 rather than left to the token. A token that names its own
    /// algorithm is a token that can name <c>none</c>, and every one of the classic JWT breaks comes
    /// from trusting that header.
    /// </summary>
    public static TokenValidationParameters Validation(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AccessTokenLifetime < JwtOptions.ShortestAccessTokenLifetime || options.AccessTokenLifetime > JwtOptions.LongestAccessTokenLifetime)
        {
            throw new InvalidOperationException("Jwt:AccessTokenLifetime must be between 15 minutes and 24 hours.");
        }

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = KeyFrom(options.SigningKey),

            // During a rotation the key before it checks too, so tokens it signed stay good until they run out.
            IssuerSigningKeys = Previous(options) is { } previous ? [KeyFrom(previous, nameof(JwtOptions.PreviousSigningKey))] : null,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            // No grace on expiry. The default is five minutes, which is a long time for a token somebody
            // has taken the trouble to steal.
            ClockSkew = TimeSpan.Zero,
        };
    }

    /// <summary>Fewest bytes a key's text must decode to.</summary>
    public const int KeyBytes = 32;

    /// <summary>
    /// Fewest different characters a key may have. 32 or more random bytes as hex (64 characters of 16)
    /// have all but a few of the sixteen; as base64, around thirty. One letter repeated, or a short
    /// pattern, has a handful.
    /// </summary>
    public const int KeyDistinctCharacters = 12;

    /// <summary>The previous key, when one is configured.</summary>
    private static string? Previous(JwtOptions options) =>
        string.IsNullOrEmpty(options.PreviousSigningKey) ? null : options.PreviousSigningKey;

    private static SymmetricSecurityKey KeyFrom(string key) => KeyFrom(key, nameof(JwtOptions.SigningKey));

    /// <summary>
    /// The key as the bytes of its text, once its text has the shape of generated randomness. The bytes
    /// are not decoded: a key that decoded would be a different key, and every token and session signed
    /// before would stop working at that deploy.
    /// </summary>
    private static SymmetricSecurityKey KeyFrom(string key, string setting)
    {
        if (string.IsNullOrWhiteSpace(key) || !LooksGenerated(key))
        {
            // Refused at startup rather than at the first request, and refused for being short or made up as
            // well as for being missing: a memorable phrase of 32 characters signs tokens perfectly well,
            // and a dictionary finds it from one token. The key itself is never in the message.
            throw new InvalidOperationException(
                $"Jwt:{setting} must be set to at least {KeyBytes} bytes of random data written as base64 or hex, "
                + "for example the output of `openssl rand -base64 48`. There is no development default, because "
                + "a development default becomes a production key the first time somebody forgets to set one.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>
    /// Whether a key's text is hex or base64 (either alphabet, padding optional, capitals and small letters
    /// both) of at least <see cref="KeyBytes"/> bytes, with at least <see cref="KeyDistinctCharacters"/>
    /// different characters.
    /// </summary>
    internal static bool LooksGenerated(string key)
    {
        if (key.Distinct().Count() < KeyDistinctCharacters)
        {
            return false;
        }

        if (key.Length % 2 == 0 && key.All(char.IsAsciiHexDigit))
        {
            return key.Length / 2 >= KeyBytes;
        }

        var body = key.TrimEnd('=');
        if (key.Length - body.Length > 2)
        {
            return false;
        }

        // Random base64 of 32 bytes or more has capitals and small letters both, all but never one without
        // the other; words joined by dashes, all in small letters, are base64url too.
        var standard = body.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/');
        var urlSafe = body.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        return (standard || urlSafe)
            && body.Any(char.IsAsciiLetterUpper) && body.Any(char.IsAsciiLetterLower)
            && body.Length % 4 != 1 && body.Length * 6 / 8 >= KeyBytes;
    }
}
