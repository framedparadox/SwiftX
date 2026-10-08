using System.Collections.Concurrent;
using System.Net.Http;
using System.Runtime.InteropServices;
using SwiftX.Interop;
using SwiftX.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace SwiftX.Services;

/// <summary>
/// Resolves a dock item's visual icon. Apps / files / folders use the Windows shell thumbnail
/// pipeline (the same crisp, theme-correct icons Explorer shows). Web links auto-fetch the
/// site's favicon over HTTP, cached to disk so it only downloads once and works offline after.
/// </summary>
public static class IconService
{
    // Shared client: favicons are tiny, follow redirects, and a UA header avoids servers that
    // reject "no user agent" requests.
    private static readonly HttpClient Http = CreateClient();

    // Alongside orbit.json, so an override of SwiftX's data directory takes the cache with it
    // rather than leaving downloaded favicons in the real profile (see OrbitStore.DataDirectory).
    private static readonly string CacheDir = Path.Combine(OrbitStore.DataDirectory, "IconCache");

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) SwiftX/1.0");
        return c;
    }

    /// <param name="decodePixelWidth">
    /// When positive, the width in device pixels to decode the picture at, instead of at its own
    /// size. The shell's jumbo icon is 256 px and the orbit draws it at 22 DIPs: left to the
    /// compositor, that 10x reduction is a bilinear sample that skips most of the source pixels,
    /// which is what put a staircase on every edge inside the icon. Decoding at the drawn size
    /// makes the decoder do the reduction, with a filter that averages them.
    /// </param>
    public static async Task<ImageSource?> LoadIconAsync(OrbitItem item, int decodePixelWidth = 0)
    {
        try
        {
            // 0. A built-in glyph the user picked from the icon picker is final — it needs no
            // resolution at all, and must not be raced by a shell/favicon fetch that would
            // silently overwrite the user's choice with a bitmap once it lands.
            if (!string.IsNullOrEmpty(item.CustomGlyph))
                return null;

            // Every existence check below is a file-system call, and on a network path it blocks
            // for as long as the network takes to give up: measured at 21 s for an unroutable
            // host, which is what an orbit item on a share looks like from a laptop off the VPN.
            // Callers start this method on the UI thread, and an async method runs synchronously up
            // to its first await — so made inline, those checks froze the whole app, once per
            // summon, until the redirector's own timeout. They are made on the thread pool and only
            // their answers come back; the decoding that follows needs this thread and stays on it.
            var probe = await ProbeOncePerItemAsync(item);

            // 1. Explicit user-supplied icon wins — but only if it actually decodes. A path that
            // has since been deleted, or an image the XAML stack can't read, falls through to the
            // normal resolution below rather than leaving the item blank.
            //
            // Two shapes share this field: a plain image file (png/jpg/ico/bmp), and — the same
            // "path,index" form a Windows shortcut's own icon location uses — one of the icons
            // embedded in a module the "Select an app or DLL…" picker browsed to.
            if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
            {
                var (modulePath, moduleIndex) = SplitModuleIconPath(item.CustomIconPath!);
                if (moduleIndex is { } index && probe.ModuleExists)
                {
                    var moduleIcon = await LoadModuleIconAsync(modulePath, index, decodePixelWidth);
                    if (moduleIcon is not null)
                        return moduleIcon;
                    Diag.Log($"IconService: module icon '{item.CustomIconPath}' didn't decode — using the default");
                }
                else if (probe.CustomFileExists)
                {
                    var custom = await FromFileAsync(item.CustomIconPath!, decodePixelWidth);
                    if (custom is not null)
                        return custom;
                    Diag.Log($"IconService: custom icon '{item.CustomIconPath}' didn't decode — using the default");
                }
            }

            // 2. A logo SwiftX ships for the site, where its favicon would not read on the orbit
            // (see BrandIcons). Ahead of the favicon so it is used offline too, and so the first
            // summon does not show a glyph that turns into a picture a download later.
            if (item.Kind == OrbitItemKind.WebLink && BrandIcons.PathFor(item) is { } brand)
            {
                var logo = await FromFileAsync(brand, decodePixelWidth);
                if (logo is not null)
                    return logo;
                Diag.Log($"IconService: bundled logo '{brand}' didn't decode — using the favicon");
            }

            // 3. Kind-specific resolution.
            switch (item.Kind)
            {
                case OrbitItemKind.Application:
                case OrbitItemKind.File:
                case OrbitItemKind.Shortcut:
                    if (probe.TargetFileExists)
                        return await ShellIconAsync(item.Target, decodePixelWidth);
                    break;

                case OrbitItemKind.Folder:
                    if (probe.TargetDirectoryExists)
                        return await ShellIconAsync(item.Target, decodePixelWidth);
                    break;

                case OrbitItemKind.WebLink:
                    return await FaviconAsync(item.Target, decodePixelWidth);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: load failed for '{item.Target}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    /// <summary>The answers <see cref="LoadIconAsync"/> needs from the file system, gathered in one
    /// place so they can be gathered off the UI thread.</summary>
    internal readonly record struct IconProbe(
        bool ModuleExists, bool CustomFileExists, bool TargetFileExists, bool TargetDirectoryExists);

    /// <summary>
    /// Runs <see cref="Probe"/> on the thread pool. <paramref name="fileExists"/> and
    /// <paramref name="directoryExists"/> default to the real checks; a test passes its own to see
    /// which thread they run on without needing a share that never answers.
    /// </summary>
    internal static Task<IconProbe> ProbeAsync(
        OrbitItem item, Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null) =>
        Task.Run(() => Probe(item, fileExists ?? File.Exists, directoryExists ?? Directory.Exists));

    private static readonly ConcurrentDictionary<string, Task<IconProbe>> ProbesInFlight = new();

    /// <summary>
    /// <see cref="ProbeAsync"/>, at most once at a time per item. An item on a dead share keeps one
    /// pool thread parked for the length of the SMB timeout, and a failed resolution is retried on
    /// every summon — so without this, summoning the orbit a few times during that window parked a
    /// thread per summon on the same unanswerable check. Callers that arrive while a probe for the
    /// same item is running share its answer; the entry is removed the moment it completes, so the
    /// next summon asks again (a share that has come back is noticed at once).
    /// </summary>
    internal static Task<IconProbe> ProbeOncePerItemAsync(OrbitItem item, Func<OrbitItem, IconProbe>? probe = null)
    {
        var key = item.IconKey;
        var task = ProbesInFlight.GetOrAdd(key, _ => Task.Run(() => (probe ?? DefaultProbe)(item)));

        // Registered after the add, so it still runs if the probe finished first, and a
        // key-and-value removal so it can never evict a newer probe that replaced this one.
        _ = task.ContinueWith(
            t => ProbesInFlight.TryRemove(new KeyValuePair<string, Task<IconProbe>>(key, t)),
            TaskScheduler.Default);
        return task;
    }

    private static IconProbe DefaultProbe(OrbitItem item) => Probe(item, File.Exists, Directory.Exists);

    /// <summary>
    /// Asks only the questions the item can actually need answered: a custom icon is one file or one
    /// module, and the target is a file or a folder depending on the kind. Blocking, by design — it
    /// is the caller's job not to be a thread that matters (see <see cref="ProbeAsync"/>).
    /// </summary>
    internal static IconProbe Probe(OrbitItem item, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        bool moduleExists = false, customFileExists = false;
        if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
        {
            var (modulePath, moduleIndex) = SplitModuleIconPath(item.CustomIconPath!);
            if (moduleIndex is not null)
                moduleExists = fileExists(modulePath);
            else
                customFileExists = fileExists(item.CustomIconPath!);
        }

        bool targetFile = false, targetDirectory = false;
        switch (item.Kind)
        {
            case OrbitItemKind.Application:
            case OrbitItemKind.File:
            case OrbitItemKind.Shortcut:
                targetFile = fileExists(item.Target);
                break;
            case OrbitItemKind.Folder:
                targetDirectory = directoryExists(item.Target);
                break;
        }

        return new IconProbe(moduleExists, customFileExists, targetFile, targetDirectory);
    }

    /// <summary>
    /// Resolves a file, app, or folder's shell icon via pure Win32 — no WinRT Storage broker
    /// involved, so this needs no <c>broadFileSystemAccess</c> capability. Prefers the 256x256
    /// "jumbo" icon (matching Explorer's large-icon views); falls back to the classic 32x32
    /// icon (<see cref="NativeMethods.SHGetFileInfo"/> with <c>SHGFI_ICON</c>) if the jumbo
    /// lookup fails for any reason.
    /// </summary>
    private static async Task<ImageSource?> ShellIconAsync(string path, int decodePixelWidth = 0)
    {
        nint hIcon = TryGetJumboIcon(path);
        if (hIcon == nint.Zero)
        {
            var info = new NativeMethods.SHFILEINFO();
            nint result = NativeMethods.SHGetFileInfo(
                path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
            if (result == 0 || info.hIcon == nint.Zero)
                return null;
            hIcon = info.hIcon;
        }

        try
        {
            return await DecodeHIconAsync(hIcon, decodePixelWidth);
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// Turns a live icon handle into a decoded image — the tail end <see cref="ShellIconAsync"/>
    /// and <see cref="LoadModuleIconAsync"/> both need once they each have an <c>HICON</c> from
    /// their own different source (the shell's image list, or a module's embedded resources).
    /// Neither owns the handle: the caller extracted or looked it up and is the one that has to
    /// destroy it.
    /// </summary>
    private static async Task<ImageSource?> DecodeHIconAsync(nint hIcon, int decodePixelWidth = 0)
    {
        byte[]? png = TryEncodeIconPng(hIcon);
        if (png is null)
        {
            // Fallback for the rare icon GetDIBits can't read as plain 32bpp (e.g. a legacy
            // icon with no real color bitmap) — the GDI+ path, buggy on jumbo/PNG-compressed
            // icons but otherwise fine, and better than nothing here.
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            png = ms.ToArray();
        }
        return await DecodeAsync(png, decodePixelWidth);
    }

    /// <summary>
    /// How many icons a module (an .exe or .dll) carries — <c>ExtractIconEx</c>'s documented
    /// trick of asking for icon index -1, which returns a count instead of extracting anything.
    /// Used by the icon picker's "Select an app or DLL…" to know how many entries its gallery has
    /// to offer; zero (a module with no icon resources, or one that couldn't be opened at all)
    /// means there is nothing to choose there.
    /// </summary>
    public static int CountModuleIcons(string path)
    {
        try
        {
            return (int)NativeMethods.ExtractIconEx(path, -1, out _, out _, 0);
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: icon count failed for '{path}': {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Extracts one icon out of a module by index (see <see cref="CountModuleIcons"/>) and decodes
    /// it — the module counterpart of <see cref="ShellIconAsync"/>'s shell lookup, for an icon the
    /// user picked directly out of an .exe or .dll rather than the file the orbit item itself
    /// launches. <see cref="OrbitItem.CustomIconPath"/> stores the choice as "path,index", the same
    /// form a Windows shortcut's own icon location field uses, so it round-trips through the same
    /// property a plain image file already does.
    /// </summary>
    public static async Task<ImageSource?> LoadModuleIconAsync(string path, int index, int decodePixelWidth = 0)
    {
        if (NativeMethods.ExtractIconEx(path, index, out nint large, out nint small, 1) == 0)
            return null;

        nint hIcon = large != nint.Zero ? large : small;
        if (hIcon == nint.Zero)
            return null;

        try
        {
            return await DecodeHIconAsync(hIcon, decodePixelWidth);
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: module icon failed for '{path}[{index}]': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            if (large != nint.Zero) NativeMethods.DestroyIcon(large);
            if (small != nint.Zero && small != large) NativeMethods.DestroyIcon(small);
        }
    }

    /// <summary>
    /// Splits the "path,index" form <see cref="OrbitItem.CustomIconPath"/> carries for a module
    /// icon back into its parts. A plain image path — no trailing ",N", or one that doesn't parse
    /// as a number — comes back with a null index, read as an ordinary file instead.
    /// </summary>
    private static (string Path, int? Index) SplitModuleIconPath(string raw)
    {
        int comma = raw.LastIndexOf(',');
        if (comma > 0 && int.TryParse(raw.AsSpan(comma + 1), out int index))
            return (raw[..comma], index);
        return (raw, null);
    }

    /// <summary>
    /// Extracts the icon's true 32bpp BGRA pixels directly via <c>GetIconInfo</c>/<c>GetDIBits</c>
    /// and encodes them as PNG — bypassing <see cref="System.Drawing.Icon.ToBitmap"/>'s
    /// long-standing alpha-channel bug on PNG-compressed ("jumbo") icon frames, which otherwise
    /// shows up as a black box or opaque halo behind the icon. Windows' 32bpp BI_RGB DIB layout
    /// and GDI+'s <c>Format32bppArgb</c> use the identical in-memory BGRA byte order, so the raw
    /// bytes copy straight across with no channel juggling. Returns null if the icon's color
    /// bitmap isn't a plain 32bpp DIB (a handful of legacy icons only carry an XOR/AND mask, no
    /// true color+alpha bitmap), leaving the caller to fall back to the GDI+ path.
    /// </summary>
    /// <summary>
    /// The largest icon edge <see cref="TryEncodeIconPng"/> will decode. Windows' own largest
    /// shell icon — the SHIL_JUMBO frame this service prefers — is 256x256; anything claiming to
    /// be four times that in each direction is not an icon SwiftX has any use for at the 44 px it
    /// draws them, and is refused before it sizes an allocation. See the check's own note.
    /// </summary>
    internal const int MaxIconPixelDimension = 2048;

    private static byte[]? TryEncodeIconPng(nint hIcon)
    {
        if (!NativeMethods.GetIconInfo(hIcon, out var info))
            return null;

        try
        {
            if (info.hbmColor == nint.Zero)
                return null;

            var bm = new NativeMethods.BITMAP();
            if (NativeMethods.GetObject(info.hbmColor, Marshal.SizeOf<NativeMethods.BITMAP>(), ref bm) == 0)
                return null;
            if (bm.bmBitsPixel != 32 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
                return null;
            // The dimensions come out of a bitmap handle, and one of the two callers feeds this
            // icons extracted from a module the user browsed to with "Select an app or DLL…" —
            // i.e. an arbitrary .exe/.dll whose resources SwiftX did not author. Sizing two
            // allocations (the pixel array and the GDI+ bitmap, 4 bytes per pixel each) from an
            // unbounded pair of numbers is the classic way that turns into an
            // OutOfMemoryException, and at 32-bit extremes `bmWidth * bmHeight * 4` overflows int
            // and asks for a negative array instead. The shell's own jumbo list tops out at
            // 256x256, so this ceiling is four times larger than any icon Windows itself will
            // hand back and still bounds the pair at 16 MB apiece.
            if (bm.bmWidth > MaxIconPixelDimension || bm.bmHeight > MaxIconPixelDimension)
            {
                Diag.Log($"IconService: ignoring a {bm.bmWidth}x{bm.bmHeight} icon bitmap — past the {MaxIconPixelDimension}px ceiling");
                return null;
            }

            int width = bm.bmWidth;
            int height = bm.bmHeight;
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative = top-down, so rows land in on-screen order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            };

            byte[] pixels = new byte[width * height * 4];
            nint hdc = NativeMethods.GetDC(0);
            if (hdc == nint.Zero)
                return null;
            try
            {
                int scanLines = NativeMethods.GetDIBits(
                    hdc, info.hbmColor, 0, (uint)height, pixels, ref header, NativeMethods.DIB_RGB_COLORS);
                if (scanLines == 0)
                    return null;
            }
            finally
            {
                NativeMethods.ReleaseDC(0, hdc);
            }

            using var bitmap = new System.Drawing.Bitmap(
                width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(
                rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        finally
        {
            if (info.hbmColor != nint.Zero) NativeMethods.DeleteObject(info.hbmColor);
            if (info.hbmMask != nint.Zero) NativeMethods.DeleteObject(info.hbmMask);
        }
    }

    /// <summary>
    /// Looks up <paramref name="path"/>'s index in the shell's system image list, then resolves
    /// that index against the jumbo (256x256) list. Returns <see cref="nint.Zero"/> on any
    /// failure — no jumbo list, path not found, etc. — which the caller treats as "fall back to
    /// the smaller per-file icon".
    /// </summary>
    private static nint TryGetJumboIcon(string path)
    {
        var info = new NativeMethods.SHFILEINFO();
        nint listHandle = NativeMethods.SHGetFileInfo(
            path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_SYSICONINDEX);
        if (listHandle == nint.Zero)
            return nint.Zero;

        var iid = NativeMethods.IID_IImageList;
        if (NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref iid, out nint imageList) != 0
            || imageList == nint.Zero)
            return nint.Zero;

        // The raw pointer, not a managed interface: see the note above SHGetImageList. That also
        // makes the reference ours to release, which a wrapper's finalizer used to do, later and on
        // a thread of its choosing.
        try
        {
            return NativeMethods.ImageListGetIcon(
                imageList, info.iIcon, NativeMethods.ILD_TRANSPARENT, out nint hIcon) == 0
                ? hIcon
                : nint.Zero;
        }
        finally
        {
            Marshal.Release(imageList);
        }
    }

    /// <summary>
    /// Decodes an image file the user pointed at (a custom icon can be any path on disk). Plain
    /// Win32 file I/O — a full-trust process needs no broker capability for it.
    /// <para>
    /// Bounded by <see cref="InputLimits.MaxIconBytes"/>, checked from the file's metadata before
    /// the read. The ceiling had been declared for this caller and never applied, which left the
    /// one icon path that takes an arbitrary user-chosen file reading it whole with
    /// <c>File.ReadAllBytesAsync</c> — and doing so once per item on every render, so a 4 GB file
    /// picked as an icon was an <see cref="OutOfMemoryException"/> every time the orbit came up.
    /// A custom icon is drawn at 44 px; anything past the ceiling is not one.
    /// </para>
    /// </summary>
    private static async Task<ImageSource?> FromFileAsync(string path, int decodePixelWidth = 0)
    {
        var read = await InputLimits.ReadBytesAsync(path, InputLimits.MaxIconBytes);
        if (!read.Ok)
        {
            Diag.Log($"IconService: custom icon '{path}' not read: {read.Error}");
            return null;
        }
        return await DecodeAsync(read.Bytes, decodePixelWidth);
    }

    // ---- Favicons ---------------------------------------------------------

    /// <summary>
    /// Resolves a web link's icon: a disk-cached favicon if present, otherwise fetched from the
    /// site itself (its /favicon.ico), falling back to a favicon service. Returns null (→ globe
    /// glyph) when nothing usable is found or there's no connectivity.
    /// </summary>
    private static async Task<ImageSource?> FaviconAsync(string url, int decodePixelWidth = 0)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        string host = uri.Host;
        string scheme = uri.Scheme is "http" or "https" ? uri.Scheme : "https";
        // The site's own favicon lives on the site's own port. Authority is host plus the port when
        // it is not the scheme's default, so a dev server on localhost:3000 asks localhost:3000 —
        // and not port 80, where there is nothing — and two servers on the same host do not share
        // one cached icon. The icon service below only ever knows hosts.
        string authority = uri.Scheme is "http" or "https" ? uri.Authority : host;
        string cacheFile = Path.Combine(CacheDir, Sanitize(authority) + ".ico");

        // 1. Cached copy. Bounded on the way back in, not only on the way out: what this cache
        // holds is whatever was written to a predictable path under the user's own data directory,
        // and the ceiling that kept the download small is no help at all against a file that did
        // not arrive as a download. ReadAllBytesAsync on it was the one unbounded read left on the
        // icon path.
        if (File.Exists(cacheFile))
        {
            var read = await InputLimits.ReadBytesAsync(cacheFile, MaxFaviconBytes);
            if (read.Ok)
            {
                var cached = await DecodeAsync(read.Bytes, decodePixelWidth);
                if (cached is not null)
                    return cached;
            }
            else
            {
                Diag.Log($"IconService: cached favicon '{cacheFile}' not read: {read.Error}");
            }
        }

        // A host that just failed is not asked again until its back-off is up: see FaviconMissCache.
        if (Misses.ShouldSkip(authority))
            return null;

        // 2. Fetch, preferring the site's own favicon (no third party), then a service fallback.
        foreach (var candidate in new[]
                 {
                     $"{scheme}://{authority}/favicon.ico",
                     $"https://icons.duckduckgo.com/ip3/{host}.ico",
                 })
        {
            var bytes = await TryDownloadAsync(candidate);
            if (bytes is null || !LooksLikeImage(bytes))
                continue;

            var image = await DecodeAsync(bytes, decodePixelWidth);
            if (image is null)
                continue;

            TrySaveCache(cacheFile, bytes);
            Misses.RecordSuccess(authority);
            return image;
        }

        Misses.RecordMiss(authority);
        return null;
    }

    /// <summary>Which hosts are currently being left alone after a failed favicon fetch.</summary>
    internal static readonly FaviconMissCache Misses = new();

    /// <summary>
    /// The ceiling on a downloaded favicon. A favicon is a few kilobytes; this is the point past
    /// which the response is not one and is dropped rather than decoded.
    /// </summary>
    internal const int MaxFaviconBytes = 2_000_000;

    /// <summary>
    /// Fetches a candidate favicon, or null for anything that isn't one — including a body that
    /// runs past <see cref="MaxFaviconBytes"/>.
    /// <para>
    /// The size ceiling is applied <em>while</em> the body is read, not after it. Reading with
    /// <c>ResponseContentRead</c> and then testing <c>bytes.Length</c> had already committed the
    /// whole response to memory by the time the test ran, so the limit bounded what was
    /// <em>used</em> and not what was <em>allocated</em>: a host serving a multi-gigabyte body
    /// under a favicon's URL was buffered in full and then discarded. This is the one place
    /// SwiftX reads bytes it did not get from the user's own disk, so it is the one place a
    /// remote party chooses the length — <c>ResponseHeadersRead</c> plus a bounded copy takes
    /// that choice back, and a declared <c>Content-Length</c> past the ceiling is refused before
    /// the stream is opened at all.
    /// </para>
    /// </summary>
    private static async Task<byte[]?> TryDownloadAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
                return null;
            // An honest server declares the length; refuse before the stream is even opened.
            // A dishonest or silent one is caught by the bounded copy below instead.
            long? declaredLength = resp.Content.Headers.ContentLength;
            if (declaredLength > MaxFaviconBytes)
                return null;

            using var stream = await resp.Content.ReadAsStreamAsync();
            return await ReadBoundedAsync(stream, MaxFaviconBytes);
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: favicon fetch failed for '{url}': {ex.GetType().Name}: {ex.Message}");
            return null; // no connectivity / DNS / TLS / timeout / oversized — glyph fallback
        }
    }

    /// <summary>
    /// Copies at most <paramref name="limit"/> bytes out of <paramref name="stream"/>, abandoning
    /// the read (and returning null) the moment the source proves longer. Never allocates more
    /// than the limit, whatever the sender claims or omits.
    /// </summary>
    internal static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.Length > 0 ? buffer.ToArray() : null;
    }

    /// <summary>
    /// How long the decode confirmation below is waited for before the icon is given up on.
    /// <para>
    /// A <see cref="BitmapImage"/> reports its outcome by raising exactly one of
    /// <c>ImageOpened</c> / <c>ImageFailed</c> — so awaiting that pair is how the decode is
    /// confirmed, and awaiting it <em>unbounded</em> is a bet that one of them always arrives.
    /// Nothing in the contract guarantees it: the events are raised on the dispatcher after
    /// <c>SetSourceAsync</c> returns, so an image whose window is torn down in between, or a
    /// decoder that answers neither way, leaves the await pending for the life of the process.
    /// That is one orphaned task and one held <see cref="BitmapImage"/> per item, and
    /// <c>LoadIconAsync</c> runs once per item on every render — so the accumulation is per
    /// summon, not one-off. A ceiling turns "never answers" into "answers no", which is what the
    /// caller already handles (the glyph fallback).
    /// </para>
    /// </summary>
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Decodes raw bytes into a BitmapImage, confirming the decode actually succeeded
    /// (so a 200-OK HTML error page never gets shown as a broken image).</summary>
    private static async Task<ImageSource?> DecodeAsync(byte[] bytes, int decodePixelWidth = 0)
    {
        // Declared out here so the finally can detach them, constructed inside the try so that a
        // BitmapImage refused on the calling thread is still the caught "no icon" this method has
        // always returned rather than a throw out of an icon load.
        BitmapImage? bmp = null;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOpened(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => tcs.TrySetResult(true);
        void OnFailed(object s, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e) => tcs.TrySetResult(false);

        try
        {
            bmp = new BitmapImage();
            // Before the source is set: the decode size cannot be changed once it has started.
            if (decodePixelWidth > 0)
                bmp.DecodePixelWidth = decodePixelWidth;
            bmp.ImageOpened += OnOpened;
            bmp.ImageFailed += OnFailed;

            using (var ras = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(ras))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                ras.Seek(0);
                await bmp.SetSourceAsync(ras);
            }

            if (await Task.WhenAny(tcs.Task, Task.Delay(DecodeTimeout)) != tcs.Task)
            {
                Diag.Log($"IconService: decode of {bytes.Length} bytes reported neither opened nor failed within {DecodeTimeout.TotalSeconds}s — using the default");
                return null;
            }
            return await tcs.Task ? bmp : null;
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: decode failed ({bytes.Length} bytes): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            // In a finally, not on the success path alone: the throwing and timing-out paths are
            // exactly the ones that leave the handlers attached to a bitmap nothing else reaches.
            if (bmp is not null)
            {
                bmp.ImageOpened -= OnOpened;
                bmp.ImageFailed -= OnFailed;
            }
        }
    }

    /// <summary>Magic-byte sniff: cheaply reject HTML/text (e.g. a soft-404) before decoding.</summary>
    private static bool LooksLikeImage(byte[] b)
    {
        if (b.Length < 4)
            return false;
        // ICO / CUR
        if (b[0] == 0x00 && b[1] == 0x00 && (b[2] == 0x01 || b[2] == 0x02) && b[3] == 0x00) return true;
        // PNG
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;
        // JPEG
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;
        // GIF
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return true;
        // BMP
        if (b[0] == 0x42 && b[1] == 0x4D) return true;
        // WEBP (RIFF....WEBP)
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true;
        return false;
    }

    private static void TrySaveCache(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex)
        {
            Diag.Log("IconService cache write failed: " + ex.Message);
        }
    }

    private static string Sanitize(string host)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            host = host.Replace(c, '_');
        return host;
    }
}
