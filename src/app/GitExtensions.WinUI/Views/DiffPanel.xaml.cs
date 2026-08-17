using GitExtensions.WinUI.Diff;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The diff reader: a file's unified or side-by-side diff, with its own view-mode switch.
/// </summary>
/// <remarks>
///  Shared by the History and Changes pages, which show different diffs at the same time. Each host
///  binds its own <see cref="DiffViewModel"/>, so the two panels never fight over one set of lines.
/// </remarks>
public sealed partial class DiffPanel : UserControl
{
    public static readonly DependencyProperty DiffProperty = DependencyProperty.Register(
        nameof(Diff), typeof(DiffViewModel), typeof(DiffPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsContentProperty = DependencyProperty.Register(
        nameof(ActionsContent), typeof(object), typeof(DiffPanel), new PropertyMetadata(null));

    public DiffPanel()
    {
        InitializeComponent();
    }

    /// <summary>The panel's state. Owned by the host page, not by this control.</summary>
    public DiffViewModel? Diff
    {
        get => (DiffViewModel?)GetValue(DiffProperty);
        set => SetValue(DiffProperty, value);
    }

    /// <summary>
    ///  Extra buttons for the header — Blame and File history on the History page, nothing on the
    ///  Changes page, where they would refer to a commit that does not exist yet.
    /// </summary>
    public object? ActionsContent
    {
        get => GetValue(ActionsContentProperty);
        set => SetValue(ActionsContentProperty, value);
    }

    /// <summary>
    ///  Hands the hunk back to whoever owns the diff. The panel deliberately does no git work itself —
    ///  it is shown on pages where staging is meaningless, and the owner is what decides direction.
    /// </summary>
    private void HunkAction_Click(object sender, RoutedEventArgs e)
    {
        if (Diff is DiffViewModel diff && sender is Button { Tag: DiffHunk hunk })
        {
            diff.RequestHunkAction(hunk);
        }
    }

    private void ViewModeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (Diff is DiffViewModel diff)
        {
            diff.IsSideBySide = ReferenceEquals(sender.SelectedItem, SideBySideItem);
        }
    }
}
