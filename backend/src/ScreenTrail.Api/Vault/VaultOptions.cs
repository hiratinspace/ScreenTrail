using System.Security.Cryptography;

namespace ScreenTrail.Api.Vault;

/// <summary>
/// The vault's master key (ST-009), as configuration: <c>Vault:MasterKey</c>, 32 random bytes in base64.
///
/// It stands where a cloud KMS key will stand once there is a cloud (ST-007); the envelope's outer layer
/// is shaped for that swap and the rows never learn the difference. <b>No default.</b> With none set the
/// vault says it is not configured and refuses to store, which is true and actionable; a development
/// default would become a production key the first time somebody forgot, and every credential in the
/// database would be protected by a string in the repository.
///
/// <c>Vault:PreviousMasterKey</c> is set only during a rotation: rows wrapped under it still open, and
/// <c>--rotate-vault-keys</c> rewraps them under the current key. docs/security/key-rotation.md.
/// </summary>
public sealed class VaultOptions
{
    public const string Section = "Vault";

    public const int KeyBytes = 32;

    public string MasterKey { get; set; } = string.Empty;

    public string PreviousMasterKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether a row sealed before secrets were bound to their row may still be opened (2026-11-15
    /// review). On by default so nothing stored before then stops working; the first start's sweep seals
    /// every such row again bound. After that first start, turn it off, and a row marked legacy is
    /// refused rather than opened, by reads, the sweep and the rotation alike. docs/security/key-rotation.md.
    /// </summary>
    public bool AllowLegacyRows { get; set; } = true;

    /// <summary>Whether the service upgrades legacy rows once at startup (<see cref="VaultSweep"/>). On by default.</summary>
    public bool UpgradeOnStart { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(MasterKey);

    public byte[] CurrentKey() => Decode(MasterKey, nameof(MasterKey));

    public byte[]? PreviousKey() => string.IsNullOrWhiteSpace(PreviousMasterKey) ? null : Decode(PreviousMasterKey, nameof(PreviousMasterKey));

    /// <summary>
    /// Checked once, when the service starts (security review E-08): each key that is set decodes to
    /// <see cref="KeyBytes"/> bytes, and the previous key is not the current one. A key that does neither
    /// would otherwise let the service start and then fail every publish, ticket search and Test
    /// connection. The messages name the setting, never the key. No key at all is still allowed: the
    /// vault then says it is not configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">A key that is not 32 bytes of base64, or the same key twice.</exception>
    public void Validate()
    {
        var current = IsConfigured ? CurrentKey() : null;
        var previous = PreviousKey();
        try
        {
            if (current is not null && previous is not null && CryptographicOperations.FixedTimeEquals(current, previous))
            {
                throw new InvalidOperationException($"Vault:{nameof(PreviousMasterKey)} is the same key as Vault:{nameof(MasterKey)}. During a rotation it is the key in use until now and Vault:{nameof(MasterKey)} is the new one; otherwise leave it unset. docs/security/key-rotation.md.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
            CryptographicOperations.ZeroMemory(previous);
        }
    }

    private static byte[] Decode(string value, string name)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Vault:{name} is not base64.", ex);
        }

        if (key.Length != KeyBytes)
        {
            throw new InvalidOperationException($"Vault:{name} must decode to {KeyBytes} bytes; it decodes to {key.Length}.");
        }

        return key;
    }
}
