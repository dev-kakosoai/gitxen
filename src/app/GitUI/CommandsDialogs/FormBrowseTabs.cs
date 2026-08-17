using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUI.CommandsDialogs.BrowseDialog;
using GitUI.Properties;

namespace GitUI.CommandsDialogs;

/// <summary>
/// Top-level shell that hosts one or more repositories as tabs in a single window. Each tab embeds
/// an otherwise-unmodified <see cref="FormBrowse"/> instance (its own <see cref="IGitUICommands"/>,
/// <see cref="GitModule"/>, background status monitor, hotkeys, etc. all stay fully independent and
/// alive concurrently) using the standard WinForms technique of hosting a non-top-level child form
/// inside a container control.
/// </summary>
public sealed partial class FormBrowseTabs : GitExtensionsForm
{
    private static FormBrowseTabs? _current;

    private readonly uint _closeAllMessage = NativeMethods.RegisterWindowMessageW("Global.GitExtensions.CloseAllInstances");
    private readonly TabControl _tabControl;

    public FormBrowseTabs(IGitUICommands commands, BrowseArguments? args = null)
        : base(enablePositionRestore: true)
    {
        _current = this;

        Text = AppSettings.ApplicationName;
        Icon = Resources.GitExtensionsLogoIcon;
        StartPosition = FormStartPosition.WindowsDefaultBounds;
        Size = new Size(1300, 800);
        KeyPreview = true;

        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
        Controls.Add(_tabControl);

        FormClosed += (s, e) =>
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };

        AddTab(commands, args);
    }

    /// <summary>
    /// Opens <paramref name="commands"/>'s repository as a new tab in the currently running
    /// <see cref="FormBrowseTabs"/> shell, if there is one.
    /// </summary>
    /// <returns><see langword="true"/> if an existing shell accepted the tab; <see langword="false"/>
    /// if no shell is currently running (caller should create one instead).</returns>
    public static bool TryAddTab(IGitUICommands commands, BrowseArguments? args = null)
    {
        if (_current is null || _current.IsDisposed)
        {
            return false;
        }

        _current.AddTab(commands, args);

        if (_current.WindowState == FormWindowState.Minimized)
        {
            _current.WindowState = FormWindowState.Normal;
        }

        _current.Activate();

        return true;
    }

    private void AddTab(IGitUICommands commands, BrowseArguments? args = null)
    {
        FormBrowse form = new(commands, args ?? new BrowseArguments())
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill
        };

        TabPage page = new(GetTabTitle(form));
        page.Controls.Add(form);
        _tabControl.TabPages.Add(page);

        form.TextChanged += (s, e) => page.Text = GetTabTitle(form);
        form.FormClosed += (s, e) => RemoveTab(page);

        form.Show();

        _tabControl.SelectedTab = page;
        form.SetForeground(true);
    }

    private void RemoveTab(TabPage page)
    {
        _tabControl.TabPages.Remove(page);
        page.Dispose();

        if (_tabControl.TabPages.Count == 0)
        {
            Close();
        }
    }

    /// <summary>
    /// Closes the currently selected tab, same as clicking its close button.
    /// </summary>
    public void CloseActiveTab()
    {
        if (_tabControl.SelectedTab?.Controls[0] is FormBrowse form)
        {
            form.Close();
        }
    }

    private static string GetTabTitle(FormBrowse form)
    {
        string title = form.Text;
        int separatorIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex > 0 ? title[..separatorIndex] : title;
    }

    private FormBrowse? ActiveTabForm => _tabControl.SelectedTab?.Controls[0] as FormBrowse;

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        foreach (TabPage page in _tabControl.TabPages)
        {
            if (page.Controls[0] is FormBrowse form)
            {
                form.SetForeground(ReferenceEquals(page, _tabControl.SelectedTab));
            }
        }

        ActiveTabForm?.Activate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.T:
                OpenRepositoryInNewTab();
                return true;

            case Keys.Control | Keys.W:
                CloseActiveTab();
                return true;

            case Keys.Control | Keys.Tab:
                SelectRelativeTab(+1);
                return true;

            case Keys.Control | Keys.Shift | Keys.Tab:
                SelectRelativeTab(-1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SelectRelativeTab(int offset)
    {
        int count = _tabControl.TabPages.Count;
        if (count == 0)
        {
            return;
        }

        int index = (((_tabControl.SelectedIndex + offset) % count) + count) % count;
        _tabControl.SelectedIndex = index;
    }

    private void OpenRepositoryInNewTab()
    {
        IGitUICommands? baseCommands = ActiveTabForm?.UICommands;
        if (baseCommands is null)
        {
            return;
        }

        IGitModule? module = FormOpenDirectory.OpenModule(this, baseCommands.GetRequiredService<IGitExecutorProvider>(), baseCommands.Module);
        if (module is null)
        {
            return;
        }

        AddTab(baseCommands.WithGitModule(module));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _closeAllMessage || m is { Msg: NativeMethods.WM_SYSCOMMAND, WParam: NativeMethods.SC_CLOSE })
        {
            // Application close is requested, e.g. using the Taskbar context menu.
            // This request is directed to the shell also if a modal form like FormCommit is on top
            // of one of the tabs. So forward the request and try to close the modal form first.
            Form? modalForm = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Modal);
            modalForm?.Close();

            Close();
        }

        base.WndProc(ref m);
    }
}
