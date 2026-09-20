using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GuluPet.Presentation;

internal enum MemoryOfferDecision
{
    PlayNow,
    Later,
}

internal sealed record MemoryOfferRequest(
    string Title,
    string ThumbnailPath,
    string? ErrorMessage);

internal interface IMemoryOfferPrompt : IDisposable
{
    MemoryOfferDecision Show(MemoryOfferRequest request);

    void CloseCurrent();
}

internal sealed class AutomaticMemoryOfferPrompt : IMemoryOfferPrompt
{
    public MemoryOfferDecision Show(MemoryOfferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MemoryOfferDecision.PlayNow;
    }

    public void CloseCurrent()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class WpfMemoryOfferPrompt(PetWindow owner)
    : IMemoryOfferPrompt
{
    private readonly PetWindow _owner = owner
        ?? throw new ArgumentNullException(nameof(owner));
    private MemoryOfferWindow? _currentWindow;

    internal MemoryOfferWindow? CurrentWindowForTest => _currentWindow;

    public MemoryOfferDecision Show(MemoryOfferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _owner.Dispatcher.VerifyAccess();
        if (_currentWindow is not null)
        {
            throw new InvalidOperationException(
                "A memory offer is already being shown.");
        }

        var window = new MemoryOfferWindow(request)
        {
            Owner = _owner,
            Topmost = _owner.Topmost,
        };
        _currentWindow = window;
        try
        {
            _ = window.ShowDialog();
            return window.Decision;
        }
        finally
        {
            if (ReferenceEquals(_currentWindow, window))
            {
                _currentWindow = null;
            }
        }
    }

    public void CloseCurrent()
    {
        _owner.Dispatcher.VerifyAccess();
        _currentWindow?.Close();
    }

    public void Dispose()
    {
        CloseCurrent();
    }
}

public partial class MemoryOfferWindow : Window
{
    private long _offerEntranceGeneration;
    private bool? _offerEntranceAnimationsEnabledForTest;
    private bool _isClosing;

    internal MemoryOfferWindow(MemoryOfferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        MemoryTitle.Text = request.Title;
        PreviewImage.Source = LoadThumbnail(request.ThumbnailPath);
        if (!string.IsNullOrWhiteSpace(request.ErrorMessage))
        {
            ErrorText.Text = request.ErrorMessage;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        PrepareOfferEntrance();
    }

    internal MemoryOfferDecision Decision { get; private set; } =
        MemoryOfferDecision.Later;

    internal long OfferEntranceGenerationForTest =>
        _offerEntranceGeneration;

    internal bool OfferEntranceHasAnimatedPropertiesForTest =>
        OfferSurface.HasAnimatedProperties
        || OfferSurfaceScale.HasAnimatedProperties;

    internal void SetOfferEntranceAnimationsEnabledForTest(bool? enabled)
    {
        _offerEntranceAnimationsEnabledForTest = enabled;
        if (IsLoaded && !_isClosing)
        {
            BeginOfferEntrance();
            return;
        }

        PrepareOfferEntrance();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        BeginOfferEntrance();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        SettleOfferEntranceImmediately();
        base.OnClosing(e);
        if (e.Cancel)
        {
            _isClosing = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewImage.Source = null;
        base.OnClosed(e);
    }

    private void PrepareOfferEntrance()
    {
        RemoveOfferEntranceMotionClocks();
        if (!ShouldAnimateOfferEntrance())
        {
            OfferSurface.Opacity = 1;
            OfferSurfaceScale.ScaleX = 1;
            OfferSurfaceScale.ScaleY = 1;
            return;
        }

        OfferSurface.Opacity = 0;
        OfferSurfaceScale.ScaleX = 0.97;
        OfferSurfaceScale.ScaleY = 0.97;
    }

    private void BeginOfferEntrance()
    {
        long generation = ++_offerEntranceGeneration;
        RemoveOfferEntranceMotionClocks();
        if (_isClosing || !ShouldAnimateOfferEntrance())
        {
            CommitOfferEntrance(generation);
            return;
        }

        OfferSurface.Opacity = 0;
        OfferSurfaceScale.ScaleX = 0.97;
        OfferSurfaceScale.ScaleY = 0.97;

        var opacityAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.EmphasizedEntrance);
        var scaleXAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.EmphasizedEntrance);
        var scaleYAnimation = GuluMotion.SplineTo(
            1,
            GuluMotion.EmphasizedEntrance);
        scaleYAnimation.Completed += (_, _) =>
            CommitOfferEntrance(generation);

        OfferSurface.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
        OfferSurfaceScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleXAnimation,
            HandoffBehavior.SnapshotAndReplace);
        OfferSurfaceScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleYAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private bool ShouldAnimateOfferEntrance() =>
        _offerEntranceAnimationsEnabledForTest
        ?? GuluMotion.ShouldAnimate(
            SystemParameters.ClientAreaAnimation,
            SystemParameters.HighContrast);

    private void CommitOfferEntrance(long generation)
    {
        if (generation != _offerEntranceGeneration || _isClosing)
        {
            return;
        }

        RemoveOfferEntranceMotionClocks();
        OfferSurface.Opacity = 1;
        OfferSurfaceScale.ScaleX = 1;
        OfferSurfaceScale.ScaleY = 1;
    }

    private void SettleOfferEntranceImmediately()
    {
        _offerEntranceGeneration++;
        RemoveOfferEntranceMotionClocks();
        OfferSurface.Opacity = 1;
        OfferSurfaceScale.ScaleX = 1;
        OfferSurfaceScale.ScaleY = 1;
    }

    private void RemoveOfferEntranceMotionClocks()
    {
        OfferSurface.BeginAnimation(OpacityProperty, null);
        OfferSurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        OfferSurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void OnPlayNowClicked(object sender, RoutedEventArgs e)
    {
        Decision = MemoryOfferDecision.PlayNow;
        DialogResult = true;
    }

    private void OnLaterClicked(object sender, RoutedEventArgs e)
    {
        Decision = MemoryOfferDecision.Later;
        DialogResult = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Decision = MemoryOfferDecision.Later;
        e.Handled = true;
        DialogResult = false;
    }

    private static BitmapImage LoadThumbnail(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Memory preview image is missing.",
                fullPath);
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        if (image.PixelWidth < 1 || image.PixelHeight < 1)
        {
            throw new InvalidDataException(
                "Memory preview image has invalid dimensions.");
        }

        return image;
    }
}
