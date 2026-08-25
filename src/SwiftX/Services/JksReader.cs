using System.Buffers.Binary;
using System.Text;

namespace SwiftX.Services;

/// <summary>
/// Minimal, read-only parser for the Java KeyStore binary format (JKS, and best-effort
/// JCEKS). .NET has no built-in support for either. No password is required: a JKS entry's
/// certificate bytes sit unencrypted in the file whether the entry is a
/// <c>TrustedCertEntry</c> or a <c>PrivateKeyEntry</c> — only the private *key* material
/// (which this reader never touches) needs the store password to decrypt.
/// </summary>
public static class JksReader
{
    private const int JksMagic = unchecked((int)0xFEEDFEED);
    private const int JceksMagic = unchecked((int)0xCECECECE);
    private const int PrivateKeyTag = 1;
    private const int TrustedCertTag = 2;

    public sealed record Entry(string Alias, DateTime Timestamp, string EntryType, IReadOnlyList<byte[]> CertificateDer);

    /// <param name="IsKeyStore">False when the magic bytes don't match JKS/JCEKS at all —
    /// the caller should try another format, not report an error.</param>
    /// <param name="Truncated">True when parsing stopped early (a corrupt file, or a JCEKS
    /// secret-key entry that can't be skipped without fully parsing Java's serialization
    /// format) — whatever entries were already read are still returned.</param>
    public readonly record struct Result(bool IsKeyStore, IReadOnlyList<Entry> Entries, bool Truncated, string? Error);

    public static Result TryParse(byte[] data)
    {
        if (data.Length < 8)
            return new Result(false, [], false, null);

        var cursor = new Cursor(data);
        int magic;
        try
        {
            magic = cursor.ReadInt32();
        }
        catch (FormatException)
        {
            return new Result(false, [], false, null);
        }

        if (magic != JksMagic && magic != JceksMagic)
            return new Result(false, [], false, null);

        var entries = new List<Entry>();
        try
        {
            _ = cursor.ReadInt32(); // version — both 1 and 2 share the layout this reader handles
            int count = cursor.ReadPositiveInt32();

            for (int i = 0; i < count; i++)
            {
                int tag = cursor.ReadInt32();
                string alias = cursor.ReadJavaUtf();
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.ReadInt64()).UtcDateTime;

                if (tag == PrivateKeyTag)
                {
                    int keyLen = cursor.ReadPositiveInt32();
                    cursor.Skip(keyLen); // encrypted; unreadable without the store password
                    int chainLen = cursor.ReadPositiveInt32();
                    var chain = new List<byte[]>(chainLen);
                    for (int c = 0; c < chainLen; c++)
                    {
                        cursor.ReadJavaUtf(); // cert type ("X.509") — not surfaced
                        chain.Add(cursor.ReadBytes(cursor.ReadPositiveInt32()));
                    }
                    entries.Add(new Entry(alias, timestamp, "Private key entry", chain));
                }
                else if (tag == TrustedCertTag)
                {
                    cursor.ReadJavaUtf();
                    var der = cursor.ReadBytes(cursor.ReadPositiveInt32());
                    entries.Add(new Entry(alias, timestamp, "Trusted certificate entry", [der]));
                }
                else
                {
                    // JCEKS's SecretKeyEntry (tag 3) is a raw Java-serialization blob with no
                    // length prefix — there is no safe way to skip past one, so stop rather
                    // than misreading whatever follows as another entry.
                    return new Result(true, entries, true, null);
                }
            }
        }
        catch (FormatException)
        {
            return new Result(true, entries, true, "Corrupt or truncated keystore file.");
        }

        return new Result(true, entries, false, null);
    }

    /// <summary>A bounds-checked big-endian cursor over the keystore bytes — every read that
    /// would run past the end of the buffer throws <see cref="FormatException"/> rather than
    /// indexing out of range, so a truncated or hostile file is reported, not crashed on.</summary>
    private sealed class Cursor(byte[] data)
    {
        private int _pos;

        public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadSpan(4));

        public long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(ReadSpan(8));

        public int ReadPositiveInt32()
        {
            int value = ReadInt32();
            if (value < 0)
                throw new FormatException("Negative length field.");
            return value;
        }

        public byte[] ReadBytes(int count) => ReadSpan(count).ToArray();

        public void Skip(int count) => ReadSpan(count);

        /// <summary>Java's <c>DataInputStream.readUTF</c>: a 2-byte BE length prefix followed
        /// by that many bytes of "modified UTF-8". Decoded as plain UTF-8, which is exact for
        /// the ASCII aliases real keystores use; it only diverges from Java's own decoding on
        /// the embedded-null and supplementary-character edge cases modified UTF-8 encodes
        /// differently, which aliases don't hit in practice.</summary>
        public string ReadJavaUtf()
        {
            int len = BinaryPrimitives.ReadUInt16BigEndian(ReadSpan(2));
            return Encoding.UTF8.GetString(ReadSpan(len));
        }

        private ReadOnlySpan<byte> ReadSpan(int count)
        {
            if (count < 0 || count > data.Length - _pos)
                throw new FormatException("Unexpected end of keystore data.");
            var span = data.AsSpan(_pos, count);
            _pos += count;
            return span;
        }
    }
}
