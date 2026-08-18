using System.Collections.ObjectModel;
using GitExtensions.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  Type-to-jump search over everything the repository contains: branches, tags, commits, stashes,
///  remotes, submodules, worktrees, tracked files and the shell's own sections.
/// </summary>
/// <remarks>
///  <para>
///   The go-to half of the ReSharper pair: Ctrl+Shift+P answers "do something", this answers "take me
///   to something". Matching is ranked prefix, then segment initials ("fw" finds
///   feat/winui3-frontend), then substring — which also covers pasted short hashes — then
///   subsequence.
///  </para>
///  <para>
///   Sources are searched live rather than from an index. The cheap listings are handed in up front;
///   the slower ones (tracked files, and listings the user has not visited yet) stream in through
///   <see cref="AddItems"/> while the palette is already open and searchable, so opening it costs
///   nothing waiting.
///  </para>
/// </remarks>
public sealed partial class GoToPalette : ContentDialog
{
    private readonly List<GoToItem> _items = [];
    private string _query = "";

    public GoToPalette(IEnumerable<GoToItem> items)
    {
        _items.AddRange(items);
        InitializeComponent();
        Filter();
    }

    public ObservableCollection<GoToItem> Results { get; } = [];

    /// <summary>The item the user chose, or null if they dismissed the palette.</summary>
    public GoToItem? Chosen { get; private set; }

    public Visibility EmptyVisibility => Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  Adds late-arriving items and re-runs the current query over them.
    /// </summary>
    /// <remarks>
    ///  Called on the UI thread by whoever loaded the source. Arriving after the palette was
    ///  dismissed is harmless: the list mutates, nothing renders it.
    /// </remarks>
    public void AddItems(IEnumerable<GoToItem> items)
    {
        _items.AddRange(items);
        Filter();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = QueryBox.Text;
        Filter();
    }

    /// <summary>Arrows move the list while the caret stays in the box; see CommandPalette.</summary>
    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                Move(1);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                Move(-1);
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                Choose(ResultList.SelectedItem as GoToItem);
                e.Handled = true;
                break;
        }
    }

    private void ResultList_ItemClick(object sender, ItemClickEventArgs e) =>
        Choose(e.ClickedItem as GoToItem);

    private void Choose(GoToItem? item)
    {
        if (item is null)
        {
            return;
        }

        // Returned rather than run here: the jump may need a page shown, and pages cannot be
        // manipulated while this dialog is still closing.
        Chosen = item;
        Hide();
    }

    private void Move(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(ResultList.SelectedIndex + delta, 0, Results.Count - 1);
        ResultList.SelectedIndex = index;
        ResultList.ScrollIntoView(Results[index]);
    }

    private void Filter()
    {
        // A refresh from AddItems keeps the row the user had arrowed to; a keystroke resets to the
        // best match because the previous selection is unlikely to survive the narrower query.
        GoToItem? previous = ResultList.SelectedItem as GoToItem;

        Results.Clear();

        IEnumerable<GoToItem> matches = _items
            .Select(item => (Item: item, Score: item.ScoreFor(_query)))
            .Where(scored => scored.Score is not null)
            .OrderBy(scored => scored.Score)
            .ThenBy(scored => scored.Item.CategoryRank)
            .ThenBy(scored => scored.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(scored => scored.Item)
            .Take(60);

        foreach (GoToItem item in matches)
        {
            Results.Add(item);
        }

        if (Results.Count > 0)
        {
            int keep = previous is null ? -1 : Results.IndexOf(previous);
            ResultList.SelectedIndex = keep >= 0 ? keep : 0;
        }

        Bindings.Update();
    }
}
