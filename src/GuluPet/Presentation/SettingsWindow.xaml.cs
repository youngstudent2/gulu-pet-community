using System.ComponentModel;
using System.Windows;
using GuluPet.Diagnostics;

namespace GuluPet.Presentation;

public partial class SettingsWindow : Window
{
    private readonly ErrorLogReportUploader _uploader;
    private readonly LocalErrorLog _errorLog;
    private bool _uploading;

    public SettingsWindow(
        ErrorLogReportUploader uploader,
        LocalErrorLog errorLog)
    {
        _uploader = uploader
            ?? throw new ArgumentNullException(nameof(uploader));
        _errorLog = errorLog
            ?? throw new ArgumentNullException(nameof(errorLog));
        InitializeComponent();
        UpdateFeedbackState();
    }

    private async void OnUploadLogsClicked(object sender, RoutedEventArgs e)
    {
        if (_uploading)
        {
            return;
        }

        string feedback = FeedbackTextBox.Text.Trim();
        if (feedback.Length == 0)
        {
            LogStatusText.Text = "请先写下遇到的问题。";
            _ = FeedbackTextBox.Focus();
            return;
        }

        _uploading = true;
        UploadLogsButton.IsEnabled = false;
        FeedbackTextBox.IsEnabled = false;
        LogStatusText.Text = "正在发送反馈…";
        try
        {
            ErrorLogReportResult result = await _uploader.UploadAsync(feedback);
            switch (result.Outcome)
            {
                case ErrorLogReportOutcome.Uploaded:
                    FeedbackTextBox.Clear();
                    LogStatusText.Text = "已发送，谢谢你的反馈。";
                    break;

                case ErrorLogReportOutcome.NoErrors:
                    LogStatusText.Text = "请先写下遇到的问题。";
                    break;

                case ErrorLogReportOutcome.Failed:
                    LogStatusText.Text =
                        "暂时无法发送。已为你打开排查文件夹，" +
                        "你也可以把其中的文件发给我们。";
                    TryOpenLogFolder();
                    break;

                default:
                    throw new InvalidEnumArgumentException(
                        nameof(result.Outcome),
                        (int)result.Outcome,
                        typeof(ErrorLogReportOutcome));
            }
        }
        catch (Exception exception)
        {
            _errorLog.Write(
                "settings",
                "upload-errors",
                "error-report-ui-failed",
                exception);
            LogStatusText.Text =
                "暂时无法发送。已为你打开排查文件夹，" +
                "你也可以把其中的文件发给我们。";
            TryOpenLogFolder();
        }
        finally
        {
            _uploading = false;
            FeedbackTextBox.IsEnabled = true;
            UpdateFeedbackState();
        }
    }

    private void OnFeedbackTextChanged(object sender, RoutedEventArgs e) =>
        UpdateFeedbackState();

    private void UpdateFeedbackState()
    {
        if (!IsInitialized)
        {
            return;
        }

        int length = FeedbackTextBox.Text.Trim().Length;
        FeedbackCountText.Text = $"{length} / {FeedbackTextBox.MaxLength}";
        UploadLogsButton.IsEnabled = !_uploading && length > 0;
    }

    private void OnOpenLogFolderClicked(object sender, RoutedEventArgs e) =>
        TryOpenLogFolder();

    private void TryOpenLogFolder()
    {
        try
        {
            _errorLog.OpenFolder();
        }
        catch (Exception exception)
        {
            _errorLog.Write(
                "settings",
                "open-log-folder",
                "open-log-folder-failed",
                exception);
            LogStatusText.Text +=
                " 文件夹也没能打开，请稍后再试。";
        }
    }
}
