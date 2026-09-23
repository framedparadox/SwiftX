using System.Text;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Security;

/// <summary>
/// The size ceilings, asserted at the boundary each one actually guards.
/// <para>
/// <c>InputLimits</c> declares four ceilings and the design document's §22 promises all four are
/// applied "before a byte is read". Two of them — <see cref="InputLimits.MaxConfigBytes"/> and
/// <see cref="InputLimits.MaxIconBytes"/> — were declared and never wired to a caller, so the two
/// paths they named (configuration import, and a custom icon file) read whatever the user picked
/// with no ceiling at all. A ceiling nothing calls is indistinguishable from no ceiling, and
/// nothing failed when it was absent, which is exactly why it needs a test rather than a constant.
/// </para>
/// <para>
/// The remote-input guard is here too. <c>IconService</c>'s favicon fetch is the only place in
/// SwiftX where the length of a payload is chosen by someone other than the user, and it used to
/// buffer the whole response before testing its size — so the limit governed what was used, never
/// what was allocated.
/// </para>
/// </summary>
[Collection(SharedDataDirectoryCollection.Name)]
public class BoundedInputTests
{
    private static string TempFile(string name) =>
        Path.Combine(TestEnvironment.DataDirectory, $"{Guid.NewGuid():N}-{name}");

    // =========================================================================================
    // 1. Configuration import
    // =========================================================================================

    /// <summary>
    /// Import accepts any path a picker returns, so the file on the other end is not necessarily
    /// a configuration — it can be a disk image picked by mistake. The refusal has to come from
    /// the file's metadata: <c>File.ReadAllText</c> on a multi-gigabyte file is an
    /// <see cref="OutOfMemoryException"/> thrown out of a settings command, which §22 says must
    /// never happen.
    /// <para>
    /// Written just past the ceiling rather than at gigabyte scale so the test is fast; the code
    /// path is identical either way, because the decision is made from <c>FileInfo.Length</c>
    /// before the read is attempted.
    /// </para>
    /// </summary>
    [Fact]
    public void Import_refuses_a_file_past_the_configuration_ceiling()
    {
        var path = TempFile("oversized-config.json");
        try
        {
            using (var stream = File.Create(path))
                stream.SetLength(InputLimits.MaxConfigBytes + 1);

            Assert.Null(OrbitStore.ImportFrom(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The ceiling must not cost the feature. A real configuration is kilobytes, so a normal
    /// export has to still import — otherwise the guard above would pass for the wrong reason.
    /// </summary>
    [Fact]
    public void Import_still_accepts_a_configuration_of_ordinary_size()
    {
        var path = TempFile("ordinary-config.json");
        try
        {
            var config = new AppConfig().EnsurePrimary();
            config.Primary.Items.Add(new OrbitItem { DisplayName = "Notepad", Target = @"C:\Windows\notepad.exe" });
            Assert.True(OrbitStore.ExportTo(config, path));

            Assert.True(new FileInfo(path).Length < InputLimits.MaxConfigBytes);

            var imported = OrbitStore.ImportFrom(path);
            Assert.NotNull(imported);
            Assert.Equal("Notepad", imported!.Primary.Items[0].DisplayName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =========================================================================================
    // 2. The synchronous bounded read behind it
    // =========================================================================================

    /// <summary>
    /// <see cref="InputLimits.ReadText"/> is the synchronous sibling the import path needed, and
    /// it has to keep the same three-way contract as the asynchronous one: content, a refusal
    /// naming the ceiling, or a refusal for a file that isn't readable — never an exception, and
    /// never a partial read presented as a whole one.
    /// </summary>
    [Fact]
    public void ReadText_reports_oversized_missing_and_readable_files_the_same_way()
    {
        var missing = TempFile("never-created.txt");
        var missingResult = InputLimits.ReadText(missing);
        Assert.False(missingResult.Ok);
        Assert.NotEmpty(missingResult.Error);
        Assert.Equal("", missingResult.Text);

        var path = TempFile("read-text.txt");
        try
        {
            File.WriteAllText(path, "hello", Encoding.UTF8);

            var ok = InputLimits.ReadText(path);
            Assert.True(ok.Ok);
            Assert.Equal("hello", ok.Text);
            Assert.Equal("", ok.Error);

            // The same file, refused purely because the ceiling handed in is below its length.
            var refused = InputLimits.ReadText(path, limit: 1);
            Assert.False(refused.Ok);
            Assert.NotEmpty(refused.Error);
            Assert.Equal("", refused.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A byte-order mark is consumed rather than surfacing as a stray U+FEFF at the start of the
    /// text — the same guarantee <c>ReadTextAsync</c> gives, and one a configuration parser
    /// depends on: <c>JsonSerializer</c> rejects a document that begins with one.
    /// </summary>
    [Fact]
    public void ReadText_consumes_a_byte_order_mark()
    {
        var path = TempFile("bom.json");
        try
        {
            File.WriteAllText(path, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var result = InputLimits.ReadText(path);
            Assert.True(result.Ok);
            Assert.Equal("{}", result.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =========================================================================================
    // 3. Custom icons
    // =========================================================================================

    /// <summary>
    /// A custom icon is any path on disk the user pointed at, and it is re-read whenever the orbit
    /// renders. The ceiling exists because an icon is drawn at 44 px, so a file past it is not an
    /// icon — and because image decoders are a classic place to hand a deliberately expensive
    /// file. This asserts the ceiling is a real one rather than a declared constant: the refusal
    /// comes back as a result, and the bytes are never materialised.
    /// </summary>
    [Fact]
    public async Task Icon_reads_are_refused_past_the_icon_ceiling()
    {
        var path = TempFile("huge.png");
        try
        {
            using (var stream = File.Create(path))
                stream.SetLength(InputLimits.MaxIconBytes + 1);

            var result = await InputLimits.ReadBytesAsync(path, InputLimits.MaxIconBytes);
            Assert.False(result.Ok);
            Assert.Empty(result.Bytes);
            Assert.NotEmpty(result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The icon ceiling has to sit below the general binary one, or it would never bind: a custom
    /// icon would inherit the 256 MB limit meant for hashing and Base64-encoding a file whole.
    /// </summary>
    [Fact]
    public void The_icon_ceiling_is_stricter_than_the_general_binary_ceiling()
    {
        Assert.True(InputLimits.MaxIconBytes < InputLimits.MaxBinaryBytes);
        Assert.True(InputLimits.MaxConfigBytes < InputLimits.MaxTextBytes);
    }

    // =========================================================================================
    // 4. The one payload whose length a remote party chooses
    // =========================================================================================

    /// <summary>
    /// The favicon reader stops at the ceiling instead of buffering past it. This is the property
    /// that the previous <c>ReadAsByteArrayAsync</c>-then-check-length shape did not have: with
    /// the check after the read, a host serving a body larger than the limit had already had it
    /// committed to memory by the time the limit was consulted, so the guard bounded what was
    /// used and not what was allocated.
    /// <para>
    /// Asserted against a stream one byte over, and again against one far over, because the
    /// interesting failure is the boundary and the interesting consequence is the runaway.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(4096)]
    public async Task A_response_past_the_favicon_ceiling_is_abandoned_rather_than_buffered(int excess)
    {
        const int limit = 8192;
        using var oversized = new MemoryStream(new byte[limit + excess]);

        Assert.Null(await IconService.ReadBoundedAsync(oversized, limit));
    }

    /// <summary>
    /// Exactly at the ceiling is accepted — an off-by-one here would silently drop every icon of
    /// precisely the limiting size, which is the kind of bug a "refuses big things" test alone
    /// never catches.
    /// </summary>
    [Fact]
    public async Task A_response_exactly_at_the_favicon_ceiling_is_kept()
    {
        const int limit = 8192;
        var payload = new byte[limit];
        payload[0] = 0x89; // arbitrary, just so the content is distinguishable from zero-fill
        using var stream = new MemoryStream(payload);

        var read = await IconService.ReadBoundedAsync(stream, limit);
        Assert.NotNull(read);
        Assert.Equal(limit, read!.Length);
        Assert.Equal(0x89, read[0]);
    }

    /// <summary>
    /// An empty body is "no icon", not an empty icon — a zero-length 200 response must not be
    /// handed to the decoder or written to the on-disk cache as if it were a picture.
    /// </summary>
    [Fact]
    public async Task An_empty_response_is_treated_as_no_icon()
    {
        using var empty = new MemoryStream([]);
        Assert.Null(await IconService.ReadBoundedAsync(empty, 8192));
    }

    /// <summary>
    /// A stream that hands back its content in small pieces — which is what a socket does — is
    /// reassembled whole. The bounded reader loops, so a short read must not be mistaken for the
    /// end of the body; getting this wrong would truncate real favicons rather than reject fake
    /// ones, and would do it only over a network, never in a test using a MemoryStream.
    /// </summary>
    [Fact]
    public async Task A_response_arriving_in_pieces_is_reassembled_whole()
    {
        var payload = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        using var drip = new DripStream(payload, chunkSize: 7);

        var read = await IconService.ReadBoundedAsync(drip, limit: 8192);
        Assert.NotNull(read);
        Assert.Equal(payload, read);
    }

    /// <summary>A stream that never returns more than <c>chunkSize</c> bytes per read, the way a
    /// network stream behaves and a <see cref="MemoryStream"/> does not.</summary>
    private sealed class DripStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _position;

        public DripStream(byte[] data, int chunkSize)
        {
            _data = data;
            _chunkSize = chunkSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(Math.Min(_chunkSize, count), _data.Length - _position);
            if (take <= 0)
                return 0;
            Array.Copy(_data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
