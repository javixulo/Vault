namespace Vault.Core.Users;

/// <summary>
/// The search by name of <c>list_users_and_groups</c> (UC-13.AC5 and AC7). Case is ignored. Without a <c>*</c> the filter is a piece
/// of the name (<c>fam</c> finds <c>family</c>). With one or more <c>*</c>, each standing for any run of characters (also none), the
/// filter must match the whole name: <c>grupo-*</c> starts with <c>grupo-</c>, <c>*fotos</c> ends with <c>fotos</c>, <c>*foto*</c> contains
/// <c>foto</c>. It is not a regular expression: every other character, <c>.</c> or <c>?</c> too, means itself, and the match is a plain scan
/// with no backtracking, so no text can make it slow.
/// </summary>
public static class NameFilter
{
    public static bool Matches(string name, string filter)
    {
        if (!filter.Contains('*', StringComparison.Ordinal))
        {
            return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        var parts = filter.Split('*');
        var position = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            if (i == 0)
            {
                if (!name.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                position = part.Length;
            }
            else if (i == parts.Length - 1)
            {
                return name.Length - position >= part.Length && name.EndsWith(part, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var found = name.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                position = found + part.Length;
            }
        }

        return true;
    }
}
