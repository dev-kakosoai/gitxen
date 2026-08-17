using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.Converters;

/// <summary>
///  Visibility helpers for <c>x:Bind</c> function bindings, e.g.
///  <c>Visibility="{x:Bind conv:Show.If(IsCurrent)}"</c>.
/// </summary>
/// <remarks>
///  x:Bind has no implicit bool-to-Visibility conversion, and the alternative — a Visibility property
///  per condition on every record — would put layout decisions in the data. Function bindings keep
///  the condition where it belongs, in the template that cares about it.
/// </remarks>
public static class Show
{
    public static Visibility If(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility IfNot(bool condition) => condition ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visible when there is at least one of something — a count badge, a warning row.</summary>
    public static Visibility IfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility IfNone(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible when a string has something in it, so empty fields collapse instead of showing blank.</summary>
    public static Visibility IfText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
