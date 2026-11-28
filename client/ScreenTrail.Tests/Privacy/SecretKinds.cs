namespace ScreenTrail.Tests.Privacy;

/// <summary>What the masking rules promise for one kind of secret or personal data (K2, DEC-08).</summary>
public enum SecretPromise
{
    /// <summary>Masked under the default policy.</summary>
    MustMask,

    /// <summary>
    /// Masked only beside its label, and only with <c>RedactionPolicy.LabelledIds</c> on. Off by default
    /// until the laptop measurement of how much honest text it masks (DEC-08); left readable until then.
    /// </summary>
    LabelOnly,

    /// <summary>
    /// Not masked, and said so in <c>docs/security/data-flow.md</c> and <c>questionnaire.md</c> 2.5. A rule
    /// that starts to mask one moves its row to <see cref="MustMask"/> and the documents with it.
    /// </summary>
    DocumentedMiss,
}

/// <summary>
/// Every kind of secret and personal data the redaction rules have been asked about, one row each, with
/// what they promise for it (K2, DEC-08: United States market). A kind that is neither masked nor listed
/// as a documented miss has no row, which is the point: a new kind is a decision, written here first.
/// Every value is made up. The key-shaped rows are assembled from parts, as <see cref="RedTeamTests"/>
/// does, so the file can be pushed.
/// </summary>
public static class SecretKinds
{
    /// <param name="Kind">A short name for the row, unique.</param>
    /// <param name="Text">The text as it is on the screen or as the speech model writes it.</param>
    /// <param name="Secret">The part that must not survive when the kind is masked.</param>
    public sealed record Row(string Kind, SecretPromise Promise, string Text, string Secret)
    {
        public override string ToString() => Kind;
    }

    private static string J(params string[] parts) => string.Concat(parts);

    public static IReadOnlyList<Row> All { get; } =
    [
        // Masked before K2, kept as rows so a regression is seen here too.
        new("us-ssn", SecretPromise.MustMask, "SSN 078-05-1120 on file", "078-05-1120"),
        new("card", SecretPromise.MustMask, "Card 4111 1111 1111 1111 exp 12/29", "4111 1111 1111 1111"),
        new("password-label", SecretPromise.MustMask, "Password: Tr0ub4dor&3xyz", "Tr0ub4dor&3xyz"),
        new("connection-string", SecretPromise.MustMask, "Server=sql01;Database=crm;User Id=sa;Pwd=Sup3rS3cret!x;", "Sup3rS3cret!x"),
        new("jwt", SecretPromise.MustMask, "Authorization: Bearer " + J("eyJ", "hbGciOiJIUzI1NiJ9", ".", "eyJzdWIiOiIxMjM0NTY3ODkwIn0", ".", "dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk"), "dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk"),
        new("private-key-block", SecretPromise.MustMask, "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAxyzExampleExampleExample\n-----END RSA PRIVATE KEY-----", "MIIEowIBAAKCAQEAxyzExampleExampleExample"),
        new("aws-secret", SecretPromise.MustMask, "aws_secret_access_key = " + J("wJalrXUtnFEMI/K7MDENG/", "bPxRfiCYEXAMPLEKEY"), "wJalrXUtnFEMI"),
        new("cmdkey", SecretPromise.MustMask, @"cmdkey /add:srv01 /user:CORP\admin /pass:Harbour7Lamp", "Harbour7Lamp"),
        new("powershell-password", SecretPromise.MustMask, "New-LocalUser -Name helpdesk -Password Harbour7Lamp", "Harbour7Lamp"),
        new("cli-password", SecretPromise.MustMask, "mysql --user=root --password=Harbour7Lamp crm", "Harbour7Lamp"),
        new("runas-prompt", SecretPromise.MustMask, @"Enter the password for CORP\admin: Harbour7Lamp", "Harbour7Lamp"),
        new("spoken-password", SecretPromise.MustMask, "I've reset it, the new password is Winter2026 with a capital W", "Winter2026"),
        new("spoken-pin", SecretPromise.MustMask, "the PIN for the safe is 4 4 7 1", "4 4 7 1"),

        // Credentials an MSP session shows (K2's fix list, DEC-08).
        new("url-userinfo", SecretPromise.MustMask, "https://admin:Hunter2pass9@router.example/login", "Hunter2pass9"),
        new("url-userinfo-ip-host", SecretPromise.MustMask, "postgres://svc_backup:Harbour7Lamp@10.20.0.5:5432/crm", "Harbour7Lamp"),
        new("net-use", SecretPromise.MustMask, @"net use Z: \\fs01\share Winter2026!! /user:CORP\svc_backup", "Winter2026!!"),
        new("net-use-after-user", SecretPromise.MustMask, @"net use \\fs01\share /user:CORP\svc_backup Harbour7Lamp /persistent:no", "Harbour7Lamp"),
        new("net-user", SecretPromise.MustMask, @"C:\> net user administrator Winter2026! /add", "Winter2026"),
        new("psexec", SecretPromise.MustMask, @"psexec \\srv01 -u acme\admin -p Harbour7Lamp cmd", "Harbour7Lamp"),
        new("convertto-securestring", SecretPromise.MustMask, "$p = ConvertTo-SecureString -String \"Harbour7Lamp\" -AsPlainText -Force", "Harbour7Lamp"),
        new("convertto-securestring-positional", SecretPromise.MustMask, "ConvertTo-SecureString 'Tulip#4471' -AsPlainText -Force", "Tulip#4471"),
        new("wifi-key-content", SecretPromise.MustMask, "Key Content            : correct-horse-battery", "correct-horse-battery"),
        new("wifi-key-spoken", SecretPromise.MustMask, "the wifi key is BlueKettle88", "BlueKettle88"),
        new("bitlocker-recovery-key", SecretPromise.MustMask, "BitLocker recovery key: 111111-222222-333333-444444-555555-666666-777777-888888", "333333-444444"),
        new("bitlocker-recovery-key-unlabelled", SecretPromise.MustMask, "110803-271612-564531-026840-442739-380710-647834-501930", "271612-564531"),
        new("bitlocker-recovery-key-spoken", SecretPromise.MustMask, "the recovery key starts 110803 271612 564531", "271612"),
        new("mfa-setup-key", SecretPromise.MustMask, "Can't scan? Enter this code: JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP", "Y3DP EHPK"),
        new("mfa-secret-key-lower-case", SecretPromise.MustMask, "Secret key: jbsw y3dp ehpk 3pxp jbsw y3dp ehpk 3pxp", "y3dp ehpk"),
        new("mfa-otpauth-uri", SecretPromise.MustMask, "otpauth://totp/Acme:jo?secret=JBSWY3DPEHPK3PXP&issuer=Acme", "JBSWY3DPEHPK3PXP"),
        new("german-password-label", SecretPromise.MustMask, "Kennwort: Winter2026!", "Winter2026"),
        new("german-password-spoken", SecretPromise.MustMask, "log in as administrator, passwort ist Winter2026", "Winter2026"),
        new("french-password-label", SecretPromise.MustMask, "Mot de passe : Soleil2026", "Soleil2026"),
        new("spanish-password-label", SecretPromise.MustMask, "Contraseña: Verano2026", "Verano2026"),

        // United States ids masked on sight (DEC-08).
        new("itin", SecretPromise.MustMask, "ITIN 912-70-1234 on the W-7", "912-70-1234"),
        new("itin-unformatted", SecretPromise.MustMask, "ITIN: 912701234", "912701234"),

        // United States ids masked only beside their label, off until measured (DEC-08).
        new("medicare-number", SecretPromise.LabelOnly, "Medicare number: 1EG4-TE5-MK73", "1EG4-TE5-MK73"),
        new("medicare-mbi", SecretPromise.LabelOnly, "MBI 1EG4TE5MK73", "1EG4TE5MK73"),
        new("passport-number", SecretPromise.LabelOnly, "Passport No. C03005988", "C03005988"),
        new("drivers-licence", SecretPromise.LabelOnly, "Driver's License #: D123-4567-8901", "D123-4567-8901"),
        new("bank-routing", SecretPromise.LabelOnly, "Routing number: 021000021", "021000021"),
        new("bank-account", SecretPromise.LabelOnly, "Bank account number: 000123456789", "000123456789"),

        // Not masked, and the documents say so (DEC-08).
        new("uk-national-insurance", SecretPromise.DocumentedMiss, "NI number QQ 12 34 56 C", "QQ 12 34 56 C"),
        new("uk-national-insurance-compact", SecretPromise.DocumentedMiss, "NINO: QQ123456C", "QQ123456C"),
        new("canada-sin", SecretPromise.DocumentedMiss, "SIN 046 454 286", "046 454 286"),
        new("iban", SecretPromise.DocumentedMiss, "IBAN GB29 NWBK 6016 1331 9268 19", "GB29 NWBK 6016 1331 9268 19"),
        new("date-of-birth", SecretPromise.DocumentedMiss, "DOB: 03/14/1987 Jane Example", "03/14/1987"),
        new("phone-number", SecretPromise.DocumentedMiss, "Mobile (217) 555-0142", "555-0142"),
        new("name-and-address", SecretPromise.DocumentedMiss, "Jane Example, 12 Elm Street, Springfield", "12 Elm Street"),
        new("hex-key-unlabelled", SecretPromise.DocumentedMiss, "5f4dcc3b5aa765d61d8327deb882cf99a1b2c3d4", "5f4dcc3b5aa765d61d8327deb882cf99"),
        new("password-hash", SecretPromise.DocumentedMiss, "Administrator:500:aad3b435b51404eeaad3b435b51404ee:8846f7eaee8fb117ad06bdd830b7586c:::", "8846f7eaee8fb117ad06bdd830b7586c"),
        new("mfa-backup-codes", SecretPromise.DocumentedMiss, "Backup codes  x7k2-m9qa  p4wd-t8zn  h3js-c6vb", "x7k2-m9qa"),
        new("windows-product-key", SecretPromise.DocumentedMiss, "Product key: VK7JG-NPHTM-C97JM-9MPGT-3V66T", "NPHTM-C97JM"),
        new("card-spoken-as-words", SecretPromise.DocumentedMiss, "card number is four one one one, one one one one, one one one one, one one one one", "four one one one"),
    ];
}
