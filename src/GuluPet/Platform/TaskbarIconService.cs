using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GuluPet.Platform;

public sealed class TaskbarIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private readonly ToolStripMenuItem _behaviorsItem;
    private readonly ToolStripMenuItem _postcardsItem;
    private readonly ToolStripMenuItem _diaryItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _memoriesItem;
    private readonly ToolStripMenuItem _updateItem;
    private bool _alwaysOnTop;
    private bool _startWithWindows;
    private bool _disposed;

    public TaskbarIconService()
    {
        _alwaysOnTopItem = new ToolStripMenuItem("咕噜置于顶层")
        {
            CheckOnClick = false,
        };
        _alwaysOnTopItem.Click += (_, _) =>
            AlwaysOnTopToggleRequested?.Invoke(this, EventArgs.Empty);

        _startWithWindowsItem = new ToolStripMenuItem("开机就来")
        {
            CheckOnClick = false,
        };
        _startWithWindowsItem.Click += (_, _) =>
            StartWithWindowsToggleRequested?.Invoke(this, EventArgs.Empty);

        _behaviorsItem = new ToolStripMenuItem("动作");
        _behaviorsItem.Click += (_, _) =>
            BehaviorsRequested?.Invoke(this, EventArgs.Empty);

        _postcardsItem = new ToolStripMenuItem("明信片");
        _postcardsItem.Click += (_, _) =>
            PostcardsRequested?.Invoke(this, EventArgs.Empty);

        _diaryItem = new ToolStripMenuItem("日记");
        _diaryItem.Click += (_, _) =>
            DiaryRequested?.Invoke(this, EventArgs.Empty);

        _settingsItem = new ToolStripMenuItem("问题反馈");
        _settingsItem.Click += (_, _) =>
            SettingsRequested?.Invoke(this, EventArgs.Empty);

        _memoriesItem = new ToolStripMenuItem("回忆");
        _memoriesItem.Click += (_, _) =>
            MemoriesRequested?.Invoke(this, EventArgs.Empty);

        _updateItem = new ToolStripMenuItem("检查新版本");
        _updateItem.Click += (_, _) =>
            ManualUpdateRequested?.Invoke(this, EventArgs.Empty);

        ToolStripMenuItem exitItem = new("退出");
        exitItem.Click += (_, _) =>
            ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => RefreshMenuState();
        _menu.Items.AddRange(
        [
            _diaryItem,
            _behaviorsItem,
            _postcardsItem,
            _memoriesItem,
            new ToolStripSeparator(),
            _alwaysOnTopItem,
            _startWithWindowsItem,
            new ToolStripSeparator(),
            _settingsItem,
            _updateItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        _notifyIcon = new NotifyIcon
        {
            Text = "咕噜",
            Icon = LoadApplicationIcon(),
            ContextMenuStrip = _menu,
            Visible = false,
        };
        _notifyIcon.DoubleClick += (_, _) =>
            ActivateRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? AlwaysOnTopToggleRequested;

    public event EventHandler? StartWithWindowsToggleRequested;

    public event EventHandler? ManualUpdateRequested;

    public event EventHandler? BehaviorsRequested;

    public event EventHandler? PostcardsRequested;

    public event EventHandler? DiaryRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? MemoriesRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? ActivateRequested;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
        }
    }

    public void SetControlState(
        bool alwaysOnTop,
        bool startWithWindows,
        bool isPetVisible,
        bool tickScoreLoggingEnabled)
    {
        _alwaysOnTop = alwaysOnTop;
        _startWithWindows = startWithWindows;
        RefreshMenuState();
    }

    public void SetUpdateCheckInProgress(bool isInProgress)
    {
        _updateItem.Enabled = !isInProgress;
        _updateItem.Text = isInProgress
            ? "正在检查…"
            : "检查新版本";
    }

    public void SetPostcardSummary(
        int unlockedCount,
        int unseenCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unlockedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unseenCount);
        if (unseenCount > unlockedCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unseenCount),
                "Unseen postcards cannot exceed unlocked postcards.");
        }

    }

    public void SetDiaryUnreadCount(int unreadCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unreadCount);
        _diaryItem.Text = unreadCount > 0
            ? $"日记({unreadCount})"
            : "日记";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void RefreshMenuState()
    {
        _alwaysOnTopItem.Checked = _alwaysOnTop;
        _startWithWindowsItem.Checked = _startWithWindows;
    }

    private static Icon LoadApplicationIcon()
    {
        string[] iconPaths =
        [
            Path.Combine(AppContext.BaseDirectory, "App.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"),
        ];

        foreach (string iconPath in iconPaths)
        {
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            Icon? executableIcon = Icon.ExtractAssociatedIcon(processPath);
            if (executableIcon is not null)
            {
                return executableIcon;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
