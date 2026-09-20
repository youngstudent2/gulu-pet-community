using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using GuluPet.Platform;
using GuluPet.Presentation;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace GuluPet.Tests;

internal static class NavigationMenuPresentationTests
{
    private static readonly string[] ExpectedWithTwoUnread =
    [
        "日记(2)",
        "动作",
        "明信片",
        "回忆",
        "咕噜置于顶层",
        "开机就来",
        "问题反馈",
        "检查新版本",
        "退出",
    ];

    public static void RunAll()
    {
        Run(
            nameof(PetAndTrayUseTheSameOrderedMenu),
            PetAndTrayUseTheSameOrderedMenu);
        Run(
            nameof(ZeroUnreadUsesPlainDiaryLabel),
            ZeroUnreadUsesPlainDiaryLabel);
    }

    private static void PetAndTrayUseTheSameOrderedMenu()
    {
        RunSta(
            () =>
            {
                var window = new PetWindow();
                using var tray = new TaskbarIconService();
                try
                {
                    window.SetDiaryUnreadCount(2);
                    tray.SetDiaryUnreadCount(2);

                    BehaviorTestCheck.SequenceEqual(
                        ExpectedWithTwoUnread,
                        GetPetHeaders(window));
                    BehaviorTestCheck.SequenceEqual(
                        ExpectedWithTwoUnread,
                        GetTrayHeaders(tray));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static void ZeroUnreadUsesPlainDiaryLabel()
    {
        RunSta(
            () =>
            {
                var window = new PetWindow();
                using var tray = new TaskbarIconService();
                try
                {
                    window.SetDiaryUnreadCount(0);
                    tray.SetDiaryUnreadCount(0);

                    BehaviorTestCheck.Equal("日记", GetPetHeaders(window)[0]);
                    BehaviorTestCheck.Equal("日记", GetTrayHeaders(tray)[0]);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static string[] GetPetHeaders(PetWindow window) =>
        window.PetContextMenu.Items
            .OfType<WpfMenuItem>()
            .Select(static item => item.Header?.ToString() ?? string.Empty)
            .ToArray();

    private static string[] GetTrayHeaders(TaskbarIconService tray)
    {
        FieldInfo menuField = typeof(TaskbarIconService).GetField(
            "_menu",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Taskbar menu field was not found.");
        var menu = (ContextMenuStrip)(menuField.GetValue(tray)
            ?? throw new InvalidOperationException(
                "Taskbar menu was not initialized."));
        return menu.Items
            .OfType<ToolStripMenuItem>()
            .Select(static item => item.Text ?? string.Empty)
            .ToArray();
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(NavigationMenuPresentationTests)}.{name}");
    }
}
