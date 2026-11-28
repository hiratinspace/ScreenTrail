using System.Security.Cryptography;
using ScreenTrail.Core.Store;

namespace ScreenTrail.Tests.Store;

/// <summary>
/// F-10: the store key's file is written as the credential is (a file of its own, flushed, moved into place
/// without overwrite), and a file that holds no key beside a store that holds nothing is "no key yet". A key
/// file with anything in it, or beside a store with anything in it, is never replaced.
/// </summary>
public sealed class StoreKeyFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));

    public StoreKeyFileTests() => Directory.CreateDirectory(_dir);

    private string KeyPath => Path.Combine(_dir, "store.key");

    private string StorePath => Path.Combine(_dir, "store.db");

    [Fact]
    public void AnEmptyKeyFileWithNoStoreIsNoKeyYet()
    {
        File.WriteAllBytes(KeyPath, []);

        Assert.True(StoreKeyFile.NoKeyYet(KeyPath, StorePath));
    }

    [Fact]
    public void AZeroFilledKeyFileBesideAnEmptyStoreIsNoKeyYet()
    {
        // A power cut after the first write's size reached the disk and before its bytes did, with the store
        // file made moments later and empty.
        File.WriteAllBytes(KeyPath, new byte[230]);
        File.WriteAllBytes(StorePath, []);

        Assert.True(StoreKeyFile.NoKeyYet(KeyPath, StorePath));
    }

    [Fact]
    public void AnEmptyKeyFileBesideAStoreThatHoldsDataIsKept()
    {
        File.WriteAllBytes(KeyPath, []);
        File.WriteAllBytes(StorePath, RandomNumberGenerator.GetBytes(4096));

        Assert.False(StoreKeyFile.NoKeyYet(KeyPath, StorePath));
        StoreKeyFile.RemoveUnwritten(KeyPath, StorePath);
        Assert.True(File.Exists(KeyPath));
    }

    [Fact]
    public void AnEmptyKeyFileBesideALogThatHoldsDataIsKept()
    {
        File.WriteAllBytes(KeyPath, []);
        File.WriteAllBytes(StorePath + "-wal", RandomNumberGenerator.GetBytes(4096));

        Assert.False(StoreKeyFile.NoKeyYet(KeyPath, StorePath));
    }

    [Fact]
    public void AKeyFileThatHoldsAnythingIsNeverNoKeyYet()
    {
        // A key that will not unprotect is still a key: it can come back when the password does.
        var foreign = RandomNumberGenerator.GetBytes(230);
        File.WriteAllBytes(KeyPath, foreign);

        Assert.False(StoreKeyFile.NoKeyYet(KeyPath, StorePath));
        StoreKeyFile.RemoveUnwritten(KeyPath, StorePath);
        Assert.Equal(foreign, File.ReadAllBytes(KeyPath));
    }

    [Fact]
    public void NoKeyFileIsNotNoKeyYetButAbsent()
    {
        Assert.False(StoreKeyFile.NoKeyYet(KeyPath, StorePath));
    }

    [Fact]
    public void AnUnwrittenKeyFileIsRemovedSoANewOneCanBePlaced()
    {
        File.WriteAllBytes(KeyPath, []);

        StoreKeyFile.RemoveUnwritten(KeyPath, StorePath);

        Assert.False(File.Exists(KeyPath));
    }

    [Fact]
    public void AKeyIsPlacedWholeAndNothingIsLeftBeside()
    {
        var key = RandomNumberGenerator.GetBytes(230);

        Assert.True(StoreKeyFile.TryPlace(KeyPath, key));

        Assert.Equal(key, File.ReadAllBytes(KeyPath));
        Assert.Equal(["store.key"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void AKeyThatIsThereIsNeverOverwritten()
    {
        var first = RandomNumberGenerator.GetBytes(230);
        File.WriteAllBytes(KeyPath, first);

        Assert.False(StoreKeyFile.TryPlace(KeyPath, RandomNumberGenerator.GetBytes(230)));

        Assert.Equal(first, File.ReadAllBytes(KeyPath));
        Assert.Equal(["store.key"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
