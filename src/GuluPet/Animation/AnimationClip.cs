using System.Windows.Media.Imaging;

namespace GuluPet.Animation;

public sealed class AnimationClip
{
    public AnimationClip(
        AnimationClipDefinition definition,
        IReadOnlyList<BitmapSource> frames)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(frames);

        if (frames.Count == 0)
        {
            throw new ArgumentException("An animation clip must contain at least one frame.", nameof(frames));
        }

        Definition = definition;
        Frames = frames;
    }

    public AnimationClipDefinition Definition { get; }

    public IReadOnlyList<BitmapSource> Frames { get; }
}
