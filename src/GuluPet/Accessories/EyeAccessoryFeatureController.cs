using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuluPet.Animation;
using GuluPet.Presentation;
using GuluPet.Runtime;

namespace GuluPet.Accessories;

/// <summary>
/// Routes presented animation frames to the selected eye accessory. Accessory
/// images are decoded lazily and poses fail closed whenever the current frame
/// has no validated, visible registration.
/// </summary>
public sealed class EyeAccessoryFeatureController : IDisposable
{
    // Matches the final user-approved fitting pass: slightly smaller than
    // head width and lifted just above the tracked eye midpoint.
    private const double OverallScaleFactor = 0.85;
    private const double VerticalOffsetEyeSpanFraction = -0.10;
    private const int FadeOutFrameCount = 3;

    private readonly PetWindow _petWindow;
    private readonly PetController _petController;
    private readonly EyeAccessoryCatalog _catalog;
    private readonly EyeAccessoryPoseCatalog _poseCatalog;
    private readonly Action<string?> _selectionChanged;
    private readonly Dictionary<ImageCacheKey, ImageSource> _imageCache = [];
    private readonly HashSet<ImageCacheKey> _failedImages = [];

    private AnimationPlaybackEvent? _lastPresentedFrame;
    private string? _selectedId;
    private int _transientFailureFrames;
    private bool _hasPresentedAccessory;
    private bool _disposed;

    public EyeAccessoryFeatureController(
        PetWindow petWindow,
        PetController petController,
        EyeAccessoryCatalog catalog,
        EyeAccessoryPoseCatalog poseCatalog,
        string? selectedId,
        Action<string?> selectionChanged)
    {
        _petWindow = petWindow
            ?? throw new ArgumentNullException(nameof(petWindow));
        _petController = petController
            ?? throw new ArgumentNullException(nameof(petController));
        _catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _poseCatalog = poseCatalog
            ?? throw new ArgumentNullException(nameof(poseCatalog));
        _selectionChanged = selectionChanged
            ?? throw new ArgumentNullException(nameof(selectionChanged));
        _selectedId = NormalizeSelection(selectedId);

        _petController.FramePresented += OnFramePresented;
        _petWindow.EyeAccessorySelectionRequested +=
            OnEyeAccessorySelectionRequested;
        _petWindow.ConfigureEyeAccessoryMenu(
            _catalog.Accessories,
            _selectedId);
        RefreshPresentation();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _petController.FramePresented -= OnFramePresented;
        _petWindow.EyeAccessorySelectionRequested -=
            OnEyeAccessorySelectionRequested;
        HideImmediately();
    }

    private void OnFramePresented(
        object? sender,
        AnimationPlaybackEvent frame)
    {
        if (_disposed)
        {
            return;
        }

        _lastPresentedFrame = frame;
        RefreshPresentation();
    }

    private void OnEyeAccessorySelectionRequested(
        object? sender,
        EyeAccessorySelectionRequestedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        string? selectedId = NormalizeSelection(e.Id);
        bool changed = !string.Equals(
            selectedId,
            _selectedId,
            StringComparison.Ordinal);
        _selectedId = selectedId;
        _petWindow.ConfigureEyeAccessoryMenu(
            _catalog.Accessories,
            _selectedId);
        if (!changed)
        {
            // Only presented animation frames may advance a transient fade.
            // Re-selecting the current menu item is not a new frame.
            return;
        }

        // A deliberate wardrobe change must never leave the previous item
        // lingering while the new selection is being resolved.
        HideImmediately();
        RefreshPresentation();
        _selectionChanged(_selectedId);
    }

    private string? NormalizeSelection(string? selectedId) =>
        _catalog.TryGet(selectedId, out _) ? selectedId : null;

    private void RefreshPresentation()
    {
        if (_selectedId is null
            || _lastPresentedFrame is not { } frame
            || !_catalog.TryGet(
                _selectedId,
                out EyeAccessoryDefinition? accessory)
            || accessory is null)
        {
            HideImmediately();
            return;
        }

        if (!_poseCatalog.TryGet(
                frame.ClipId,
                out EyeAccessoryClipPose? clipPose)
            || clipPose is null
            || frame.FrameIndex < 0
            || frame.FrameIndex >= clipPose.FrameCount)
        {
            FadeTransientFailure();
            return;
        }

        EyeAccessoryPoseFrame pose = clipPose[frame.FrameIndex];
        if (!pose.Visible
            || pose.ViewState is not { } viewState
            || pose.LeftEye is not { } targetLeftEye
            || pose.RightEye is not { } targetRightEye)
        {
            FadeTransientFailure();
            return;
        }

        if (!accessory.TryGetVariant(
                viewState,
                out EyeAccessoryVariantDefinition? variant)
            || variant is null)
        {
            FadeTransientFailure();
            return;
        }

        ImageSource? image = GetOrLoadImage(accessory.Id, variant);
        if (image is null)
        {
            FadeTransientFailure();
            return;
        }

        Matrix matrix = BuildPresentationMatrix(
            variant,
            pose,
            targetLeftEye,
            targetRightEye);
        _petWindow.SetEyeAccessoryPresentation(image, matrix);
        _hasPresentedAccessory = _petWindow.HasVisibleEyeAccessory;
        _transientFailureFrames = 0;
    }

    private void FadeTransientFailure()
    {
        if (!_hasPresentedAccessory
            || !_petWindow.HasVisibleEyeAccessory)
        {
            HideImmediately();
            return;
        }

        _transientFailureFrames++;
        if (_transientFailureFrames >= FadeOutFrameCount)
        {
            HideImmediately();
            return;
        }

        double opacity =
            (FadeOutFrameCount - _transientFailureFrames)
            / (double)FadeOutFrameCount;
        _petWindow.SetEyeAccessoryOpacity(opacity);
    }

    private void HideImmediately()
    {
        _transientFailureFrames = 0;
        _hasPresentedAccessory = false;
        _petWindow.HideEyeAccessory();
    }

    private ImageSource? GetOrLoadImage(
        string accessoryId,
        EyeAccessoryVariantDefinition variant)
    {
        var key = new ImageCacheKey(accessoryId, variant.ViewState);
        if (_imageCache.TryGetValue(key, out ImageSource? cached))
        {
            return cached;
        }

        if (_failedImages.Contains(key))
        {
            return null;
        }

        try
        {
            // The startup contract validates the pack, but the file can still
            // be changed or removed before its first lazy decode. Recheck the
            // immutable content declaration at the point of use.
            EyeAccessoryContentReader.ValidatePng(
                variant.ImagePath,
                variant.Width,
                variant.Height,
                variant.Sha256,
                variant.SizeBytes,
                $"Eye accessory '{accessoryId}' " +
                $"variant '{variant.ViewState}' image");
            using FileStream stream = File.OpenRead(variant.ImagePath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            if (bitmap.PixelWidth != variant.Width
                || bitmap.PixelHeight != variant.Height)
            {
                Debug.WriteLine(
                    $"Eye accessory '{accessoryId}' variant " +
                    $"'{variant.ViewState}' changed dimensions after catalog " +
                    "validation.");
                _failedImages.Add(key);
                return null;
            }

            bitmap.Freeze();
            _imageCache.Add(key, bitmap);
            return bitmap;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or InvalidOperationException
                  or InvalidDataException
                  or ArgumentException
                  or FormatException
                  or OverflowException
                  or System.Runtime.InteropServices.COMException)
        {
            _failedImages.Add(key);
            Debug.WriteLine(
                $"Unable to decode eye accessory '{accessoryId}' variant " +
                $"'{variant.ViewState}': {exception}");
            return null;
        }
    }

    private static Matrix BuildPresentationMatrix(
        EyeAccessoryVariantDefinition variant,
        EyeAccessoryPoseFrame pose,
        EyeAccessoryPoint targetLeftEye,
        EyeAccessoryPoint targetRightEye)
    {
        double scale = pose.HeadWidth
            / variant.HeadWidthPixels
            * OverallScaleFactor;
        double radians = pose.RotationDegrees * Math.PI / 180;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double m11 = scale * cosine;
        double m12 = scale * sine;
        double m21 = -scale * sine;
        double m22 = scale * cosine;

        var sourceMidpoint = new System.Windows.Point(
            (variant.LeftEye.X + variant.RightEye.X) / 2,
            (variant.LeftEye.Y + variant.RightEye.Y) / 2);
        var targetMidpoint = new System.Windows.Point(
            (targetLeftEye.X + targetRightEye.X) / 2,
            (targetLeftEye.Y + targetRightEye.Y) / 2);
        double targetEyeDeltaX = targetRightEye.X - targetLeftEye.X;
        double targetEyeDeltaY = targetRightEye.Y - targetLeftEye.Y;
        double targetEyeSpan = Math.Sqrt(
            targetEyeDeltaX * targetEyeDeltaX
            + targetEyeDeltaY * targetEyeDeltaY);
        targetMidpoint.Y +=
            targetEyeSpan * VerticalOffsetEyeSpanFraction;
        double offsetX = targetMidpoint.X
            - (sourceMidpoint.X * m11 + sourceMidpoint.Y * m21);
        double offsetY = targetMidpoint.Y
            - (sourceMidpoint.X * m12 + sourceMidpoint.Y * m22);

        return new Matrix(
            m11,
            m12,
            m21,
            m22,
            offsetX,
            offsetY);
    }

    private readonly record struct ImageCacheKey(
        string AccessoryId,
        EyeAccessoryViewState ViewState);
}
