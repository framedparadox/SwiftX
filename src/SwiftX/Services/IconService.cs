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

    // Alongside ring.json, so an override of SwiftX's data directory takes the cache with it
    // rather than leaving downloaded favicons in the real profile (see RingStore.DataDirectory).
    private static readonly string CacheDir = Path.Combine(RingStore.DataDirectory, "IconCache");

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) SwiftX/1.0");
        return c;
    }

    public static async Task<ImageSource?> LoadIconAsync(RingItem item)
    {
        try
        {
            // 0. A built-in glyph the user picked from the icon picker is final — it needs no
            // resolution at all, and must not be raced by a shell/favicon fetch that would
            // silently overwrite the user's choice with a bitmap once it lands.
            if (!string.IsNullOrEmpty(item.CustomGlyph))
                return null;

            // 1. Explicit user-supplied icon wins — but only if it actually decodes. A path that
            // has since been deleted, or an image the XAML stack can't read, falls through to the
            // normal resolution below rather than leaving the item blank.
            if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
            {
                var custom = await FromFileAsync(item.CustomIconPath!);
                if (custom is not null)
                    return custom;
                Diag.Log($"IconService: custom icon '{item.CustomIconPath}' didn't decode — using the default");
            }

            // 2. Kind-specific resolution.
            switch (item.Kind)
            {
                case RingItemKind.Application:
                case RingItemKind.File:
                case RingItemKind.Shortcut:
                    if (File.Exists(item.Target))
                        return await ShellIconAsync(item.Target);
                    break;

                case RingItemKind.Folder:
                    if (Directory.Exists(item.Target))
                        return await ShellIconAsync(item.Target);
                    break;

                case RingItemKind.WebLink:
                    return await FaviconAsync(item.Target);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: load failed for '{item.Target}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Resolves a file, app, or folder's shell icon via pure Win32 — no WinRT Storage broker
    /// involved, so this needs no <c>broadFileSystemAccess</c> capability. Prefers the 256x256
    /// "jumbo" icon (matching Explorer's large-icon views); falls back to the classic 32x32
    /// icon (<see cref="NativeMethods.SHGetFileInfo"/> with <c>SHGFI_ICON</c>) if the jumbo
    /// lookup fails for any reason.
    /// </summary>
    private static async Task<ImageSource?> ShellIconAsync(string path)
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
            return await DecodeAsync(png);
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
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
        if (NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref iid, out var imageList) != 0)
            return nint.Zero;

        return imageList.GetIcon(info.iIcon, NativeMethods.ILD_TRANSPARENT, out nint hIcon) == 0
            ? hIcon
            : nint.Zero;
    }

    /// <summary>Decodes an image file the user pointed at (a custom icon can be any path on
    /// disk). Plain Win32 file I/O — a full-trust process needs no broker capability for it.</summary>
    private static async Task<ImageSource?> FromFileAsync(string path)
    {
        try
        {
            return await DecodeAsync(await File.ReadAllBytesAsync(path));
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: direct read failed for '{path}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ---- Favicons ---------------------------------------------------------

    /// <summary>
    /// Resolves a web link's icon: a disk-cached favicon if present, otherwise fetched from the
    /// site itself (its /favicon.ico), falling back to a favicon service. Returns null (→ globe
    /// glyph) when nothing usable is found or there's no connectivity.
    /// </summary>
    private static async Task<ImageSource?> FaviconAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        string host = uri.Host;
        string scheme = uri.Scheme is "http" or "https" ? uri.Scheme : "https";
        string cacheFile = Path.Combine(CacheDir, Sanitize(host) + ".ico");

        // 1. Cached copy.
        if (File.Exists(cacheFile))
        {
            var cached = await DecodeAsync(await File.ReadAllBytesAsync(cacheFile));
            if (cached is not null)
                return cached;
        }

        // 2. Fetch, preferring the site's own favicon (no third party), then a service fallback.
        foreach (var candidate in new[]
                 {
                     $"{scheme}://{host}/favicon.ico",
                     $"https://icons.duckduckgo.com/ip3/{host}.ico",
                 })
        {
            var bytes = await TryDownloadAsync(candidate);
            if (bytes is null || !LooksLikeImage(bytes))
                continue;

            var image = await DecodeAsync(bytes);
            if (image is null)
                continue;

            TrySaveCache(cacheFile, bytes);
            return image;
        }

        return null;
    }

    private static async Task<byte[]?> TryDownloadAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode)
                return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 0 and < 2_000_000 ? bytes : null;
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: favicon fetch failed for '{url}': {ex.GetType().Name}: {ex.Message}");
            return null; // no connectivity / DNS / TLS / timeout — glyph fallback
        }
    }

    /// <summary>Decodes raw bytes into a BitmapImage, confirming the decode actually succeeded
    /// (so a 200-OK HTML error page never gets shown as a broken image).</summary>
    private static async Task<ImageSource?> DecodeAsync(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            var tcs = new TaskCompletionSource<bool>();
            void OnOpened(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => tcs.TrySetResult(true);
            void OnFailed(object s, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e) => tcs.TrySetResult(false);
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

            bool ok = await tcs.Task;
            bmp.ImageOpened -= OnOpened;
            bmp.ImageFailed -= OnFailed;
            return ok ? bmp : null;
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: decode failed ({bytes.Length} bytes): {ex.GetType().Name}: {ex.Message}");
            return null;
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
