using System.Runtime.InteropServices;
using SwiftX.Interop;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The shell's jumbo icon lookup, exercised for real: <c>SHGetFileInfo</c>, <c>SHGetImageList</c> and
/// <c>IImageList::GetIcon</c> against the actual shell rather than a fake.
/// <para>
/// The list is used through its raw COM pointer with <c>GetIcon</c> called by vtable slot, not through
/// a managed <c>[ComImport]</c> interface. That was done because asking the CLR to wrap the returned
/// pointer crashed the app with a stowed E_UNEXPECTED (0xc000027b), and it makes the slot number the
/// one thing that has to be right: a wrong one would call a different method on the list, not fail
/// politely. These tests are what pins it. They are also the cheap place to see the crash if it came
/// back, since the process dies here the same way it did in the app.
/// </para>
/// </summary>
public class JumboIconTests
{
    private static readonly string Explorer =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static bool TryGetJumbo(string path, out nint hIcon)
    {
        hIcon = 0;
        var info = new NativeMethods.SHFILEINFO();
        nint list = NativeMethods.SHGetFileInfo(
            path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_SYSICONINDEX);
        if (list == 0)
            return false;

        var iid = NativeMethods.IID_IImageList;
        if (NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref iid, out nint imageList) != 0 || imageList == 0)
            return false;
        try
        {
            return NativeMethods.ImageListGetIcon(imageList, info.iIcon, NativeMethods.ILD_TRANSPARENT, out hIcon) == 0
                && hIcon != 0;
        }
        finally
        {
            Marshal.Release(imageList);
        }
    }

    private static int WidthOf(nint hIcon)
    {
        Assert.True(NativeMethods.GetIconInfo(hIcon, out var info));
        try
        {
            var bm = new NativeMethods.BITMAP();
            Assert.NotEqual(0, NativeMethods.GetObject(info.hbmColor, Marshal.SizeOf<NativeMethods.BITMAP>(), ref bm));
            return bm.bmWidth;
        }
        finally
        {
            if (info.hbmColor != 0) NativeMethods.DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) NativeMethods.DeleteObject(info.hbmMask);
        }
    }

    /// <summary>
    /// The right method is being called: GetIcon on the jumbo list hands back a 256px icon. Any other
    /// slot would return a different thing entirely, or crash.
    /// </summary>
    [Fact]
    public void The_jumbo_list_yields_a_256px_icon_through_the_raw_pointer()
    {
        Assert.True(File.Exists(Explorer));

        Assert.True(TryGetJumbo(Explorer, out nint hIcon));
        try
        {
            Assert.Equal(256, WidthOf(hIcon));
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    [Fact]
    public void A_folder_has_a_jumbo_icon_too()
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(TryGetJumbo(folder, out nint hIcon));
        NativeMethods.DestroyIcon(hIcon);
    }

    /// <summary>
    /// The lookup runs for every card on a screen and is rebuilt on every change, so it is asked over and
    /// over. Reference counting by hand is where that would show: a missing Release leaks a reference per
    /// call, and one too many is a use-after-free that takes the process down at some later point.
    /// </summary>
    [Fact]
    public void Asking_repeatedly_is_stable()
    {
        for (int i = 0; i < 300; i++)
        {
            Assert.True(TryGetJumbo(Explorer, out nint hIcon), $"iteration {i}");
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    /// <summary>The icon loader is async, so the lookup can land on a thread that is not the UI thread.</summary>
    [Fact]
    public async Task It_works_from_a_thread_pool_thread_as_well()
    {
        nint hIcon = 0;
        bool ok = await Task.Run(() => TryGetJumbo(Explorer, out hIcon));

        Assert.True(ok);
        NativeMethods.DestroyIcon(hIcon);
    }

    /// <summary>
    /// Many at once, the way a screen full of cards loads its icons. The shell serialises what it must;
    /// what is being checked is that nothing about this path is thread-affine.
    /// </summary>
    [Fact]
    public async Task Concurrent_lookups_all_succeed()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            bool ok = TryGetJumbo(Explorer, out nint hIcon);
            if (ok) NativeMethods.DestroyIcon(hIcon);
            return ok;
        })));

        Assert.All(results, Assert.True);
    }

    [Fact]
    public void A_path_that_does_not_exist_is_reported_as_no_icon_not_a_crash()
    {
        bool found = TryGetJumbo(@"Z:\definitely\not\here\nothing.exe", out nint hIcon);

        // The shell may still hand back its generic-file icon for an unknown path; either answer is
        // fine. What matters is that asking did not throw or take the process down.
        if (found) NativeMethods.DestroyIcon(hIcon);
    }
}
