using ScreenTrail.Core.Privacy;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// A settings window seen through a remote session (owner, 2026-11-25): on a user's macOS System Settings
/// "Wi-Fi" and most of the sidebar were masked though nothing on the screen was a secret. Two rules did it:
/// the key rule took "Account" for a key cue and the wrap pass's join of the sidebar's one-word rows for
/// its value, and the password rule read on from "Touch ID &amp; Password" into the rows below it and
/// took every row it reached, the pane beside them included. A screen of names keeps its names; a secret
/// on it is masked as before.
/// </summary>
public sealed class SettingsScreenMaskingTests
{
    private const int Glyph = 8;
    private const int Height = 16;
    private const int Pitch = 30;

    private static readonly RedactionEngine Engine = new();

    private static readonly string[][] Sidebar =
    [
        ["Search"], ["Hirat", "Rahi"], ["Apple", "Account"], ["Wi-Fi"], ["Bluetooth"], ["Network"], ["VPN"],
        ["Battery"], ["General"], ["Accessibility"], ["Appearance"], ["Control", "Center"], ["Desktop", "&", "Dock"],
        ["Displays"], ["Screen", "Saver"], ["Siri"], ["Wallpaper"], ["Notifications"], ["Sound"], ["Focus"],
        ["Screen", "Time"], ["Lock", "Screen"], ["Privacy", "&", "Security"], ["Touch", "ID", "&", "Password"],
        ["Users", "&", "Groups"], ["Internet", "Accounts"], ["Game", "Center"], ["iCloud"], ["Wallet", "&", "Apple", "Pay"],
        ["Keyboard"], ["Mouse"], ["Trackpad"], ["Printers", "&", "Scanners"], ["Passwords"],
    ];

    private static readonly string[][] WiFiPane =
    [
        ["Wi-Fi"], ["Wi-Fi"], ["HomeNet-5G"], ["Connected"], ["Details..."], ["Known", "Network"], ["Office-Guest"],
        ["Other", "Networks"], ["Ask", "to", "join", "networks"], ["Ask", "to", "join", "hotspots"], ["Advanced..."],
        ["Security", "WPA2/WPA3", "Personal"], ["IP", "address", "192.168.1.24"], ["Router", "192.168.1.1"],
    ];

    [Fact]
    public void ASettingsWindowWithNoSecretOnItIsLeftReadable()
    {
        var words = Screen(WiFiPane);

        var redaction = Engine.RedactFrame(words);

        Assert.Empty(redaction.Regions);
        Assert.Empty(redaction.Counts);
    }

    [Fact]
    public void APasswordInThePaneIsMaskedAndTheSidebarBesideItIsNot()
    {
        string[][] pane = [.. WiFiPane, ["Password:", "Tangerine4821Wobble"]];
        var words = Screen(pane);
        var row = 40 + ((pane.Length - 1) * Pitch);

        var redaction = Engine.RedactFrame(words);

        Assert.DoesNotContain("Tangerine4821Wobble", redaction.Text, StringComparison.Ordinal);
        Assert.True(Covered(redaction, words.Single(w => w.Text == "Tangerine4821Wobble")), "the password is left unpainted");

        // Level with it in the sidebar: another column, kept.
        var beside = words.Where(w => w.Y == row && w.X < 300).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, w => Assert.False(Touched(redaction, w), $"the sidebar's \"{w.Text}\" is painted over"));
        Assert.Contains("Wi-Fi", redaction.Text, StringComparison.Ordinal);
        Assert.Contains("Bluetooth", redaction.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyInThePaneIsMaskedAndTheSidebarIsNot()
    {
        string[][] pane = [.. WiFiPane, ["api_key:", "A1b2C3d4E5f6G7h8I9j0K1l2M3n4"]];
        var words = Screen(pane);

        var redaction = Engine.RedactFrame(words);

        Assert.DoesNotContain("A1b2C3d4E5f6G7h8I9j0K1l2M3n4", redaction.Text, StringComparison.Ordinal);
        Assert.All(words.Where(w => w.X < 300), w => Assert.False(Touched(redaction, w), $"the sidebar's \"{w.Text}\" is painted over"));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("app")]
    [InlineData("auth")]
    [InlineData("Apple")]
    public void AWordThatIsNotAKeyCueDoesNotIntroduceAKey(string cue)
    {
        var text = $"{cue} WiFiBluetoothNetworkVPNBatteryGeneralAccessibility";

        Assert.Equal(text, Engine.ScrubText(text).Text);
    }

    [Theory]
    [InlineData("githubToken=")]
    [InlineData("myApiKey:")]
    [InlineData("clientSecret =")]
    [InlineData("AUTH_TOKEN=")]
    [InlineData("api_key=")]
    [InlineData("access_token:")]
    [InlineData("aws_secret_access_key =")]
    [InlineData("client-secret:")]
    [InlineData("Bearer")]
    public void AKeyCueStillIntroducesAKey(string cue)
    {
        var text = $"{cue} wJalrXUtnFEMIK7MDENGbPxRfiCYEXAMPLEKEY";

        Assert.DoesNotContain("wJalrXUtnFEMIK7MDENG", Engine.ScrubText(text).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("token:")]
    [InlineData("sessionToken=")]
    public void AKeyWithNoDigitInItIsStillMaskedAfterItsCue(string cue)
    {
        var text = $"{cue} wJalrXUtnFEMIKMDENGbPxRfiCYEXAMPLEKEY";

        Assert.DoesNotContain("wJalrXUtnFEMIKMDENG", Engine.ScrubText(text).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void APasswordFoundRowsBelowItsCueIsNotOne()
    {
        // "Touch ID & Password" ends its row; the password rule read on to "iCloud", four rows down.
        var words = Screen([["General"]]);

        var redaction = Engine.RedactFrame(words);

        Assert.Contains("iCloud", redaction.Text, StringComparison.Ordinal);
        Assert.Contains("Users & Groups", redaction.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(MaskKindPassword, redaction.Counts.Keys);
    }

    private static readonly ScreenTrail.Shared.Schema.MaskKind MaskKindPassword = ScreenTrail.Shared.Schema.MaskKind.Password;

    /// <summary>The sidebar at the left, the pane from x 320, one row every <see cref="Pitch"/> pixels.</summary>
    private static List<OcrWord> Screen(string[][] pane)
    {
        var words = new List<OcrWord>();
        void Row(int y, int x, string[] texts)
        {
            foreach (var text in texts)
            {
                words.Add(new OcrWord(text, x, y, Math.Max(Glyph, text.Length * Glyph), Height));
                x += (text.Length * Glyph) + 6;
            }
        }

        for (var i = 0; i < Sidebar.Length; i++)
        {
            Row(40 + (i * Pitch), 30, Sidebar[i]);
        }

        for (var i = 0; i < pane.Length; i++)
        {
            Row(40 + (i * Pitch), 320, pane[i]);
        }

        return words;
    }

    private static bool Covered(FrameRedaction redaction, OcrWord word) =>
        redaction.Regions.Any(r =>
            r.X <= word.X && r.Y <= word.Y
            && r.X + r.Width >= word.X + word.Width
            && r.Y + r.Height >= word.Y + word.Height);

    private static bool Touched(FrameRedaction redaction, OcrWord word) =>
        redaction.Regions.Any(r =>
            r.X < word.X + word.Width && word.X < r.X + r.Width
            && r.Y < word.Y + word.Height && word.Y < r.Y + r.Height);
}
