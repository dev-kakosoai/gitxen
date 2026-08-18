namespace GitExtensions.WinUI;

/// <summary>
///  The level of UI detail the shell presents.
/// </summary>
public enum UiMode
{
    /// <summary>Only the essentials: a clean, minimal commit list and a few core actions.</summary>
    Simple,

    /// <summary>The fuller view, closer to what the WinForms app exposes.</summary>
    Advanced,

    /// <summary>
    ///  Distraction-free: no toolbar, no column headers, no chrome beyond the commits themselves.
    ///  Not offered in the mode picker — reachable only via Ctrl+Shift+Z, and Escape leaves it.
    /// </summary>
    Zen
}
