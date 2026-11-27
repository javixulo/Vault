namespace Vault.Core.Shares;

/// <summary>
/// The <c>relative_path</c> of <c>get_folder_acl</c>: a subfolder inside a shared folder (UC-27.AC2, UC-22.AC2). It is the only text
/// of a client that reaches OMV as a path, so it is checked here: nothing in it can point outside the shared folder.
/// </summary>
internal static class RelativePath
{
    /// <summary>
    /// Splits the path into its levels; none for the shared folder's root (missing, blank or <c>/</c>). A <c>/</c> at either end is
    /// ignored. False for a <c>.</c> or <c>..</c> level, an empty level or a control character. Spaces are part of a name.
    /// </summary>
    public static bool TryParse(string? path, out string[] levels)
    {
        levels = [];
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (path.Any(char.IsControl))
        {
            return false;
        }

        var inner = path.Trim('/');
        if (inner.Length == 0)
        {
            return true;
        }

        var parts = inner.Split('/');
        if (parts.Any(part => part is "" or "." or ".."))
        {
            return false;
        }

        levels = parts;
        return true;
    }

    /// <summary>The levels as OMV takes a folder path: <c>Manuales/Timbre/</c>, and <c>/</c> for the root.</summary>
    public static string ForOmv(ReadOnlySpan<string> levels) => levels.IsEmpty ? "/" : string.Join('/', levels) + "/";
}
