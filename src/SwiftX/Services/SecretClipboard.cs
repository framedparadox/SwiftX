namespace SwiftX.Services;

/// <summary>
/// Marks a clipboard payload as a secret, so Windows keeps it out of the two places clipboard
/// content otherwise outlives the copy.
/// <para>
/// A plain <c>Clipboard.SetContent</c> hands the text to the shell, and on Windows 10/11 the
/// shell does two further things with it by default. <b>Clipboard history</b> (Win+V) retains
/// the last 25 entries and shows them to anyone who reaches that keyboard — including after the
/// screen has been unlocked by someone else — and <b>cloud clipboard</b> roams the entry to
/// every other device signed in to the same Microsoft account. Both are good defaults for
/// ordinary text and wrong for a password: SwiftX encrypts these values at rest with DPAPI
/// specifically so they never leave this account on this machine, and then Copy handed them to a
/// feature whose whole purpose is to sync them off it.
/// </para>
/// <para>
/// The opt-out is a pair of well-known properties on the data package, which is the documented
/// mechanism for exactly this case and the one password managers use. It is advisory in the
/// sense that it asks the shell rather than encrypting anything — but it is the only lever an
/// app has here, and not pulling it is a decision to roam the secret.
/// </para>
/// <para>
/// Deliberately <em>not</em> applied to the other Copy commands in the app. The formatter's
/// output, a Base64 result and a certificate's PEM are documents the user is moving between
/// tools, where losing them from Win+V would be a bug rather than a protection. Only Secure
/// values copies a secret.
/// </para>
/// </summary>
public static class SecretClipboard
{
    /// <summary>Shell property that keeps a payload out of the Win+V history list.</summary>
    public const string HistoryProperty = "CanIncludeInClipboardHistory";

    /// <summary>Shell property that stops a payload roaming to the user's other devices.</summary>
    public const string RoamingProperty = "CanUploadToCloudClipboard";

    /// <summary>
    /// Stamps both opt-outs onto a data package's property bag.
    /// <para>
    /// Takes the bag rather than the package so the decision this encodes is testable on its own:
    /// <c>DataPackage</c> needs a live WinRT activation, while the thing worth pinning down — that
    /// both properties are present and both are false — is a plain dictionary assertion.
    /// <c>DataPackagePropertySet</c> implements <see cref="IDictionary{TKey,TValue}"/>, so callers
    /// pass <c>package.Properties</c> directly.
    /// </para>
    /// <para>
    /// Assigns through the indexer rather than <c>Add</c>: a package that already carries either
    /// key must end up opted out, not throw.
    /// </para>
    /// </summary>
    public static void MarkSecret(IDictionary<string, object> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties[HistoryProperty] = false;
        properties[RoamingProperty] = false;
    }
}
