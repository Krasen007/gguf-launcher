using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using static GgufLauncher.LauncherPalette;

namespace GgufLauncher;

/// <summary>
/// Native quick-pick window: recent models on the left, the selected model's .args on the
/// right. The window only ever talks to <see cref="LauncherUiModel"/>; launching goes through
/// the normal headless launcher process.
/// </summary>
public sealed class LauncherForm : Form
{
    private const string ArgumentNoteHint = "Place the cursor on an argument line to see what it does.";

    private readonly LauncherUiModel _model;
    private readonly ListBox _modelList = new();
    private readonly Label _emptyListLabel = new();
    private readonly TextBox _argsEditor = new();
    private readonly Label _modelName = new();
    private readonly Label _modelDirectory = new();
    private readonly Label _argsPath = new();
    private readonly Label _argsBadge = new();
    private readonly Label _argumentNote = new();
    private readonly Label _status = new();
    private readonly Button _saveLaunchButton = new();
    private readonly Button _loadButton = new();

    private readonly Font _boldFont = BoldFont();
    private readonly Font _smallFont = SmallFont();
    private readonly Font _monoFont = MonoFont();

    private List<LauncherModelEntry> _models = [];
    private LauncherModelEntry? _current;
    private LauncherArgsContent? _currentArgs;
    private bool _dirty;
    private bool _loadingEditor;
    private bool _suppressSelection;

    public LauncherForm(LauncherUiModel model)
    {
        _model = model;

        Text = "GGUF Launcher";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        ClientSize = new Size(1160, 700);
        Font = FormFont();
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        BuildLayout();

        KeyDown += OnKeyDown;
        FormClosing += OnFormClosing;

        RefreshModelList(null, reloadEditor: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_current is not null)
        {
            _argsEditor.Focus();
        }
        else
        {
            _modelList.Focus();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont.Dispose();
            _smallFont.Dispose();
            _monoFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = HeaderBack,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 8, 18, 7),
            Margin = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        return header;
    }

    private Control BuildBody()
    {
        TableLayoutPanel body = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        Panel sidebar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = SidebarBack,
            Padding = new Padding(10, 10, 10, 10),
            Margin = Padding.Empty,
        };
        sidebar.Controls.Add(BuildModelListPanel());

        Panel editor = new()
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Padding = new Padding(18, 14, 18, 14),
            Margin = Padding.Empty,
        };
        editor.Controls.Add(BuildEditorPanel());

        body.Controls.Add(sidebar, 0, 0);
        body.Controls.Add(editor, 1, 0);
        return body;
    }

    private Control BuildModelListPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label heading = new()
        {
            Text = "RECENT MODELS",
            Font = _boldFont,
            ForeColor = MutedColor,
            AutoSize = true,
            Margin = new Padding(4, 2, 0, 6),
        };

        Panel listHost = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = SidebarBack,
        };

        _modelList.Dock = DockStyle.Fill;
        _modelList.BorderStyle = BorderStyle.None;
        _modelList.DrawMode = DrawMode.OwnerDrawFixed;
        _modelList.ItemHeight = 66;
        _modelList.IntegralHeight = false;
        _modelList.SelectionMode = SelectionMode.One;
        _modelList.BackColor = SidebarBack;
        _modelList.ForeColor = TextColor;
        _modelList.Margin = Padding.Empty;
        _modelList.DrawItem += ModelList_DrawItem;
        _modelList.SelectedIndexChanged += ModelList_SelectedIndexChanged;

        _emptyListLabel.Dock = DockStyle.Fill;
        _emptyListLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyListLabel.ForeColor = MutedColor;
        _emptyListLabel.Font = _smallFont;
        _emptyListLabel.Text = "No models yet.\r\nOpen a .gguf file once and it will appear here.";
        _emptyListLabel.Visible = false;

        listHost.Controls.Add(_emptyListLabel);
        listHost.Controls.Add(_modelList);
        _emptyListLabel.BringToFront();

        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(listHost, 0, 1);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

        panel.Controls.Add(BuildEditorHeader(), 0, 0);
        panel.Controls.Add(BuildArgsPathRow(), 0, 1);

        _argsEditor.Dock = DockStyle.Fill;
        _argsEditor.Multiline = true;
        _argsEditor.ScrollBars = ScrollBars.Both;
        _argsEditor.WordWrap = false;
        _argsEditor.AcceptsTab = true;
        _argsEditor.Font = EditorFont();
        _argsEditor.BorderStyle = BorderStyle.FixedSingle;
        _argsEditor.BackColor = Color.White;
        _argsEditor.ForeColor = TextColor;
        _argsEditor.Margin = new Padding(0, 4, 0, 4);
        _argsEditor.TextChanged += ArgsEditor_TextChanged;
        _argsEditor.KeyUp += (_, _) => UpdateArgumentNote();
        _argsEditor.MouseUp += (_, _) => UpdateArgumentNote();
        _argsEditor.MouseMove += (_, e) => UpdateArgumentNote(_argsEditor.GetCharIndexFromPosition(e.Location));
        panel.Controls.Add(_argsEditor, 0, 2);

        panel.Controls.Add(BuildArgumentNoteRow(), 0, 3);
        panel.Controls.Add(BuildFooter(), 0, 4);
        return panel;
    }

    private Control BuildEditorHeader()
    {
        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

        _modelName.Dock = DockStyle.Fill;
        _modelName.AutoEllipsis = true;
        _modelName.TextAlign = ContentAlignment.BottomLeft;
        _modelName.Font = ModelTitleFont();
        _modelName.ForeColor = TextColor;
        _modelName.Text = "Select a model";
        _modelName.Margin = Padding.Empty;

        _modelDirectory.Dock = DockStyle.Fill;
        _modelDirectory.AutoEllipsis = true;
        _modelDirectory.TextAlign = ContentAlignment.TopLeft;
        _modelDirectory.Font = _monoFont;
        _modelDirectory.ForeColor = MutedColor;
        _modelDirectory.Margin = Padding.Empty;

        _argsBadge.AutoSize = true;
        _argsBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _argsBadge.TextAlign = ContentAlignment.MiddleCenter;
        _argsBadge.Font = _smallFont;
        _argsBadge.ForeColor = AccentColor;
        _argsBadge.BackColor = BadgeBack;
        _argsBadge.BorderStyle = BorderStyle.FixedSingle;
        _argsBadge.Padding = new Padding(8, 3, 8, 3);
        _argsBadge.Margin = new Padding(12, 8, 0, 0);
        _argsBadge.Visible = false;

        header.Controls.Add(_modelName, 0, 0);
        header.Controls.Add(_modelDirectory, 0, 1);
        header.Controls.Add(_argsBadge, 1, 0);
        header.SetRowSpan(_argsBadge, 2);
        return header;
    }

    private Control BuildArgsPathRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        Label label = new()
        {
            Text = "ARGS FILE",
            Font = _boldFont,
            ForeColor = MutedColor,
            AutoSize = true,
            Margin = new Padding(0, 7, 12, 0),
        };

        _argsPath.Dock = DockStyle.Fill;
        _argsPath.AutoEllipsis = true;
        _argsPath.TextAlign = ContentAlignment.MiddleLeft;
        _argsPath.Font = _monoFont;
        _argsPath.ForeColor = MutedColor;
        _argsPath.Margin = Padding.Empty;
        _argsPath.Text = "";

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(_argsPath, 1, 0);
        return row;
    }

    private Control BuildArgumentNoteRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        Label label = new()
        {
            Text = "INFO",
            Font = _boldFont,
            ForeColor = MutedColor,
            AutoSize = true,
            Margin = new Padding(0, 6, 12, 0),
        };

        _argumentNote.Dock = DockStyle.Fill;
        _argumentNote.AutoEllipsis = true;
        _argumentNote.TextAlign = ContentAlignment.MiddleLeft;
        _argumentNote.Font = _smallFont;
        _argumentNote.ForeColor = MutedColor;
        _argumentNote.Margin = Padding.Empty;
        _argumentNote.Text = ArgumentNoteHint;

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(_argumentNote, 1, 0);
        return row;
    }

    private Control BuildFooter()
    {
        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = _smallFont;
        _status.ForeColor = MutedColor;
        _status.Margin = Padding.Empty;

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };

        ConfigureButton(_loadButton, "Load model...");
        ConfigureButton(_saveLaunchButton, "Save & launch");

        _loadButton.Click += (_, _) => LoadModelFromDialog();
        _saveLaunchButton.Click += (_, _) =>
        {
            if (SaveCurrent())
            {
                LaunchCurrent();
            }
        };

        actions.Controls.Add(_loadButton);
        actions.Controls.Add(_saveLaunchButton);

        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(actions, 1, 0);
        return footer;
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(10, 4, 10, 4);
        button.Margin = new Padding(6, 7, 0, 0);
        button.UseMnemonic = false;
        button.UseVisualStyleBackColor = true;
    }

    private void ModelList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _models.Count)
        {
            return;
        }

        LauncherModelEntry entry = _models[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color background = selected ? AccentColor : SidebarBack;
        Color nameColor = selected ? Color.White : (entry.Exists ? TextColor : MutedColor);
        Color detailColor = selected ? SelectedDetailColor : MutedColor;

        using (SolidBrush brush = new(background))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        if (selected)
        {
            using SolidBrush accent = new(SelectedAccentColor);
            e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 9, 3, e.Bounds.Height - 18);
        }

        Rectangle nameRect = new(
            e.Bounds.Left + 14,
            e.Bounds.Top + 7,
            Math.Max(10, e.Bounds.Width - 200),
            22);
        TextRenderer.DrawText(
            e.Graphics,
            entry.Name,
            _boldFont,
            nameRect,
            nameColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        string time = FormatRelative(entry.LastRunUtc);
        if (time.Length > 0)
        {
            Rectangle timeRect = new(e.Bounds.Right - 180, e.Bounds.Top + 8, 166, 20);
            TextRenderer.DrawText(
                e.Graphics,
                time,
                _smallFont,
                timeRect,
                detailColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        Rectangle directoryRect = new(
            e.Bounds.Left + 14,
            e.Bounds.Top + 29,
            Math.Max(10, e.Bounds.Width - 28),
            19);
        TextRenderer.DrawText(
            e.Graphics,
            entry.Directory,
            _monoFont,
            directoryRect,
            detailColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);

        string state = !entry.Exists ? "missing" : !entry.HasArgs ? "no args file" : "";
        if (state.Length > 0)
        {
            Color stateColor = selected
                ? SelectedStateColor
                : entry.Exists ? MutedColor : DangerColor;
            Rectangle stateRect = new(e.Bounds.Left + 14, e.Bounds.Top + 47, e.Bounds.Width - 28, 16);
            TextRenderer.DrawText(
                e.Graphics,
                state,
                _smallFont,
                stateRect,
                stateColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void ModelList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressSelection)
        {
            return;
        }

        int index = _modelList.SelectedIndex;
        if (index < 0 || index >= _models.Count)
        {
            ClearEditor(noModels: _models.Count == 0);
            return;
        }

        LauncherModelEntry target = _models[index];
        if (_current is not null && PathsEqual(target.Path, _current.Path))
        {
            _current = target;
            if (_currentArgs is not null)
            {
                UpdateHeader(target, _currentArgs);
            }
            UpdateActionState();
            return;
        }

        if (_dirty && !ConfirmDiscard())
        {
            RestoreSelection();
            return;
        }

        OpenModel(target);
    }

    private void ArgsEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingEditor || _current is null || _currentArgs is null)
        {
            return;
        }

        _dirty = true;
        UpdateArgumentNote();
        UpdateActionState();
        SetStatus("Unsaved changes.");
    }

    private bool OpenModel(LauncherModelEntry entry)
    {
        LauncherArgsContent args;
        try
        {
            args = _model.LoadArgs(entry.Path);
        }
        catch (Exception ex)
        {
            SetStatus("Could not load arguments: " + ex.Message, error: true);
            RestoreSelection();
            return false;
        }

        _loadingEditor = true;
        _argsEditor.Text = args.Content;
        _argsEditor.SelectionStart = 0;
        _argsEditor.SelectionLength = 0;
        _loadingEditor = false;
        UpdateArgumentNote();

        _current = entry;
        _currentArgs = args;
        _dirty = false;
        UpdateHeader(entry, args);
        UpdateActionState();

        if (entry.Exists)
        {
            SetStatus("");
        }
        else
        {
            SetStatus("The model file no longer exists; launching is disabled.", error: true);
        }

        return true;
    }

    private void ClearEditor(bool noModels)
    {
        _loadingEditor = true;
        _argsEditor.Text = "";
        _loadingEditor = false;
        _argumentNote.Text = ArgumentNoteHint;

        _current = null;
        _currentArgs = null;
        _dirty = false;
        _modelName.Text = noModels ? "No models yet" : "Select a model";
        _modelDirectory.Text = noModels
            ? "Open a .gguf file once and it will appear here for quick pick."
            : "Pick a model on the left to view and edit its .args file.";
        _argsPath.Text = "";
        _argsBadge.Visible = false;
        UpdateActionState();
        SetStatus("");
    }

    private void RestoreSelection()
    {
        _suppressSelection = true;
        int index = _current is null ? -1 : FindIndex(_current.Path);
        _modelList.SelectedIndex = index;
        _suppressSelection = false;
    }

    private bool SaveCurrent()
    {
        if (_current is null || _currentArgs is null)
        {
            return false;
        }

        try
        {
            _model.SaveArgs(_current.Path, _argsEditor.Text);
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message, error: true);
            return false;
        }

        _dirty = false;
        _current = _current with { HasArgs = true };
        _currentArgs = _currentArgs with { Exists = true, Content = _argsEditor.Text };

        int index = FindIndex(_current.Path);
        if (index >= 0)
        {
            _models[index] = _current;
            _modelList.Invalidate();
        }

        UpdateHeader(_current, _currentArgs);
        UpdateActionState();
        SetStatus("Saved " + _currentArgs.ArgsPath, success: true);
        return true;
    }

    private void LaunchCurrent()
    {
        if (_current is null || !_current.Exists)
        {
            return;
        }

        bool hadUnsavedChanges = _dirty;
        try
        {
            _model.Launch(_current.Path);
        }
        catch (Exception ex)
        {
            SetStatus("Launch failed: " + ex.Message, error: true);
            RefreshModelList(_current?.Path, reloadEditor: false);
            return;
        }

        SetStatus(
            hadUnsavedChanges
                ? "Launched with the saved .args file. Use Save && launch to include your edits."
                : "Launched in a new console window; the browser opens when the server is ready.",
            success: true);
    }

    private void LoadModelFromDialog()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Load a GGUF model",
            Filter = "GGUF models (*.gguf)|*.gguf|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_dirty && !ConfirmDiscard())
        {
            return;
        }

        LauncherModelEntry entry;
        try
        {
            entry = _model.LoadModel(dialog.FileName);
        }
        catch (Exception ex)
        {
            SetStatus("Could not load model: " + ex.Message, error: true);
            return;
        }

        RefreshModelList(entry.Path, reloadEditor: true);
        SetStatus(
            entry.HasArgs
                ? "Loaded " + entry.Name + "."
                : "Loaded " + entry.Name + ". No args file yet - the first launch uses the built-in defaults.",
            success: true);
    }

    private void RefreshModelList(string? preferredPath, bool reloadEditor)
    {
        List<LauncherModelEntry> models;
        try
        {
            models = [.. _model.LoadModels()];
        }
        catch (Exception ex)
        {
            SetStatus("Could not load models: " + ex.Message, error: true);
            return;
        }

        string? keepPath = preferredPath ?? _current?.Path;
        bool keepEditor = !reloadEditor
            && _current is not null
            && keepPath is not null
            && models.Any(m => PathsEqual(m.Path, keepPath));

        if (!keepEditor && _dirty && !ConfirmDiscard())
        {
            return;
        }

        _models = models;
        _suppressSelection = true;
        _modelList.BeginUpdate();
        _modelList.Items.Clear();
        foreach (LauncherModelEntry entry in models)
        {
            _modelList.Items.Add(entry.Name);
        }
        _modelList.EndUpdate();
        _emptyListLabel.Visible = models.Count == 0;

        int index = keepPath is null ? -1 : FindIndex(keepPath);
        if (index < 0 && models.Count > 0)
        {
            index = 0;
        }

        _modelList.SelectedIndex = index;
        _suppressSelection = false;

        if (index < 0)
        {
            ClearEditor(noModels: models.Count == 0);
            return;
        }

        if (keepEditor)
        {
            _current = models[index];
            if (_currentArgs is not null)
            {
                UpdateHeader(_current, _currentArgs);
            }
            UpdateActionState();
        }
        else
        {
            OpenModel(models[index]);
        }
    }

    private void UpdateHeader(LauncherModelEntry entry, LauncherArgsContent args)
    {
        _modelName.Text = entry.Name;
        _modelName.ForeColor = entry.Exists ? TextColor : MutedColor;
        _modelDirectory.Text = entry.Directory;
        _argsPath.Text = args.ArgsPath;
        _argsBadge.Visible = true;
        _argsBadge.Text = args.Exists ? "args file" : "preview - not saved yet";
        _argsBadge.ForeColor = args.Exists ? AccentColor : MutedColor;
        _argsBadge.BackColor = args.Exists ? BadgeBack : SystemColors.Window;
    }

    private void UpdateActionState()
    {
        bool hasModel = _current is not null && _currentArgs is not null;
        _saveLaunchButton.Enabled = hasModel;
        _argsEditor.ReadOnly = !hasModel;
    }

    private void SetStatus(string message, bool error = false, bool success = false)
    {
        _status.Text = message;
        _status.ForeColor = error ? DangerColor : success ? SuccessColor : MutedColor;
    }

    private void UpdateArgumentNote(int? charIndex = null)
    {
        int index = charIndex ?? _argsEditor.SelectionStart;
        int line = _argsEditor.GetLineFromCharIndex(index);
        string note = ArgumentNotes.DescribeLine(_argsEditor.Lines, line);
        _argumentNote.Text = note.Length == 0 ? ArgumentNoteHint : note;
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty)
        {
            return true;
        }

        DialogResult result = MessageBox.Show(
            this,
            "Save changes to the .args file before continuing?",
            "Unsaved changes",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == DialogResult.Cancel)
        {
            return false;
        }

        if (result == DialogResult.Yes && !SaveCurrent())
        {
            return false;
        }

        _dirty = false;
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            SaveCurrent();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_dirty && !ConfirmDiscard())
        {
            e.Cancel = true;
        }
    }

    private int FindIndex(string path)
    {
        for (int i = 0; i < _models.Count; i++)
        {
            if (PathsEqual(_models[i].Path, path))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRelative(DateTimeOffset? when)
    {
        if (when is null)
        {
            return "";
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - when.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromSeconds(45))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromMinutes(60))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
        }

        if (elapsed < TimeSpan.FromHours(24))
        {
            int hours = Math.Max(1, (int)elapsed.TotalHours);
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        if (elapsed < TimeSpan.FromDays(30))
        {
            int days = Math.Max(1, (int)elapsed.TotalDays);
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        return when.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }
}
