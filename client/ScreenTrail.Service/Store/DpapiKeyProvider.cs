using System.Runtime.Versioning;
using System.Security.Cryptography;
using ScreenTrail.Core.Store;

namespace ScreenTrail.Service.Store;

/// <summary>
/// The store key on Windows: 32 random bytes generated on first use and kept on disk under DPAPI with
/// <see cref="DataProtectionScope.CurrentUser"/>. Only this Windows user on this machine can unprotect
/// it, so a copied database (or key file) is useless to anyone else. Tested on the laptop runner
/// (ScreenTrail.Tests.Windows, DifferentUserCannotDecrypt); the store logic itself is tested everywhere.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProvider(string keyFilePath) : IStoreKeyProvider
{
    // Binds the protected blob to this purpose; not a secret.
    private static readonly byte[] Entropy = "ScreenTail.store.key.v1"u8.ToArray();

    public static string DefaultKeyFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTrail",
        "store.key");

    public byte[] GetKey()
    {
        // A file that holds no key beside a store that holds nothing: a first write the power cut short.
        // Anything else that is there is kept, whether or not it will open (F-10).
        StoreKeyFile.RemoveUnwritten(keyFilePath, StoreFilePath);
        if (!File.Exists(keyFilePath))
        {
            CreateKeyFile();
        }

        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(keyFilePath), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new StoreKeyException("Windows would not unprotect the store key for this account.", ex)
            {
                Reason = Shared.Ipc.StoreUnavailableReasons.KeyRefused,
            };
        }
    }

    /// <summary>The store the key opens, beside it.</summary>
    private string StoreFilePath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(keyFilePath))!, "store.db");

    private void CreateKeyFile()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);

            // As the credential is written: a file of its own, flushed, then moved into place. Never over a
            // file that is there: if another process won the race, its key is kept and read instead.
            _ = StoreKeyFile.TryPlace(keyFilePath, protectedKey);
        }
        finally
        {
            Array.Clear(key);
        }
    }
}
