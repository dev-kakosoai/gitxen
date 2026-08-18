using System.Collections.ObjectModel;
using GitExtensions.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  Type-to-run access to every command, page and branch.
/// </summary>
/// <remarks>
///  <para>
///   The palette exists because the alternative for a client this size is a menu bar. Everything here
///   is reachable by other means; what it adds is that you do not have to remember which page an
///   action lives on, which is the thing that actually slows people down in a git GUI.
///  </para>
///  <para>
///   Arrow keys move through the results and Enter runs the selection without leaving the box, so the
///   whole interaction is one uninterrupted piece of typing.
///  </para>
/// </remarks>
public sealed partial class CommandPalette : ContentDialog
{
    private readonly IReadOnlyList<PaletteCommand> _commands;

    public CommandPalette(IReadOnlyList<PaletteCommand> commands)
    {
        _commands = commands;
        InitializeComponent();
        Filter("");
    }

    public ObservableCollection<PaletteCommand> Results { get; } = [];

    /// <summary>The command the user chose, or null if they dismissed the palette.</summary>
    public PaletteCommand? Chosen { get; private set; }

    public Visibility EmptyVisibility => Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Filter(QueryBox.Text);

    /// <summary>
    ///  Keeps the caret in the box while the arrows move the list — a palette where you have to tab
    ///  into the results to choose one is not worth having.
    /// </summary>
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
                Choose(ResultList.SelectedItem as PaletteCommand);
                e.Handled = true;
                break;
        }
    }

    private void ResultList_ItemClick(object sender, ItemClickEventArgs e) =>
        Choose(e.ClickedItem as PaletteCommand);

    private void Choose(PaletteCommand? command)
    {
        if (command is null)
        {
            return;
        }

        // The command is returned rather than run here: it may open another dialog, and a dialog
        // cannot be shown while this one is still closing.
        Chosen = command;
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

    private void Filter(string query)
    {
        Results.Clear();

        IEnumerable<PaletteCommand> matches = _commands
            .Where(command => command.Matches(query))
            .OrderBy(command => command.RankFor(query))
            .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase)
            .Take(60);

        foreach (PaletteCommand command in matches)
        {
            Results.Add(command);
        }

        if (Results.Count > 0)
        {
            ResultList.SelectedIndex = 0;
        }

        Bindings.Update();
    }
}
