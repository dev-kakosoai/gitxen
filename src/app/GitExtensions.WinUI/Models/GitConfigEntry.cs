namespace GitExtensions.WinUI.Models;

/// <summary>Which configuration file a setting lives in.</summary>
public enum GitConfigScope
{
    /// <summary>This repository's <c>.git/config</c>. Overrides the global file.</summary>
    Local,

    /// <summary>The user's <c>.gitconfig</c>, applying to every repository.</summary>
    Global
}

/// <param name="Key">Full dotted key, e.g. <c>user.email</c>.</param>
/// <param name="Value">Empty for a valueless boolean, which git treats as true.</param>
public sealed record GitConfigEntry(string Key, string Value, GitConfigScope Scope)
{
    /// <summary>The section, for grouping: "user" from "user.email".</summary>
    public string Section
    {
        get
        {
            int dot = Key.IndexOf('.');
            return dot > 0 ? Key[..dot] : Key;
        }
    }

    /// <summary>Shown when a value is empty, so the row does not look like a rendering fault.</summary>
    public string DisplayValue => Value.Length == 0 ? "(set, no value)" : Value;
}
