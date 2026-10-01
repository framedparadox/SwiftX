using System.Runtime.CompilerServices;

namespace SwiftX.Tests;

/// <summary>
/// Finds the product's source files from a test, for the handful of rules that are properties of
/// the source itself rather than of any behaviour a test can drive — "this native API is only ever
/// reached through its guard", "every window unsubscribes from its own events when it closes".
/// <para>
/// The path is baked in at compile time through <see cref="CallerFilePathAttribute"/>, so it is
/// right wherever the repository was checked out or copied to build — including the scratch
/// workspace the performance-review scripts build in — with no dependence on the working directory.
/// </para>
/// </summary>
internal static class SourceTree
{
    /// <summary>
    /// This file's own path. <see cref="CallerFilePathAttribute"/> yields the file of the
    /// <em>call site</em>, so it must be called from here: a public method taking the attribute
    /// would report whichever test file happened to call it, at whatever depth that file sits.
    /// </summary>
    private static string ThisFile([CallerFilePath] string path = "") => path;

    /// <summary>The product's source root, <c>src/SwiftX</c>.</summary>
    public static string Root() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "src", "SwiftX"));

    /// <summary>Every C# source file under <see cref="Root"/>, build output excluded.</summary>
    public static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(Root(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
}
