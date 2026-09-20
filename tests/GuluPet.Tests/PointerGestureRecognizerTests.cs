using GuluPet.Runtime;

namespace GuluPet.Tests;

internal static class PointerGestureRecognizerTests
{
    public static void RunAll()
    {
        Run(nameof(RecognizesApproachAndHover), RecognizesApproachAndHover);
        Run(nameof(RecognizesPettingAndEnd), RecognizesPettingAndEnd);
        Run(
            nameof(ContinuousPettingEmitsOncePerFocus),
            ContinuousPettingEmitsOncePerFocus);
        Run(
            nameof(ResumeKeepsPettingLatchedAndReanchorsTrajectory),
            ResumeKeepsPettingLatchedAndReanchorsTrajectory);
        Run(nameof(RecognizesCircle), RecognizesCircle);
        Run(
            nameof(RecognizesPettingAfterLongHover),
            RecognizesPettingAfterLongHover);
        Run(
            nameof(ExpiredCirclePathDoesNotCarryIntoCurrentWindow),
            ExpiredCirclePathDoesNotCarryIntoCurrentWindow);
        Run(
            nameof(CancelDiscardsGestureWithoutEndEvent),
            CancelDiscardsGestureWithoutEndEvent);
        Run(nameof(KeepsTrajectoryBounded), KeepsTrajectoryBounded);
    }

    private static void RecognizesApproachAndHover()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 5, 140));
        IReadOnlyList<PetPointerGesture> approach =
            recognizer.Observe(Sample(200, 45, 140));
        BehaviorTestCheck.True(
            approach.Contains(PetPointerGesture.Approach));

        recognizer.Begin(Sample(0, 120, 120));
        IReadOnlyList<PetPointerGesture> hover =
            recognizer.Observe(Sample(900, 122, 121));
        BehaviorTestCheck.True(hover.Contains(PetPointerGesture.Hover));
    }

    private static void RecognizesPettingAndEnd()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 80, 140));
        PetPointerGesture[] emitted = new[]
            {
                Sample(250, 130, 140),
                Sample(500, 75, 140),
                Sample(750, 135, 140),
                Sample(1_000, 70, 140),
            }
            .SelectMany(recognizer.Observe)
            .ToArray();

        BehaviorTestCheck.True(emitted.Contains(PetPointerGesture.Petting));
        BehaviorTestCheck.SequenceEqual(
            [PetPointerGesture.PettingEnded],
            recognizer.End());
    }

    private static void RecognizesCircle()
    {
        var recognizer = new PointerGestureRecognizer();
        const double center = 140;
        const double radius = 70;
        recognizer.Begin(CircleSample(0, 0, center, radius));
        var emitted = new List<PetPointerGesture>();
        for (var index = 1; index <= 16; index++)
        {
            emitted.AddRange(
                recognizer.Observe(
                    CircleSample(
                        index * 80,
                        index * Math.PI * 2 / 16,
                        center,
                        radius)));
        }

        BehaviorTestCheck.True(
            emitted.Contains(PetPointerGesture.CirclePointer));
    }

    private static void ContinuousPettingEmitsOncePerFocus()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 80, 140));
        var emitted = new List<PetPointerGesture>();
        for (var index = 1; index <= 20; index++)
        {
            emitted.AddRange(
                recognizer.Observe(
                    Sample(
                        index * 150,
                        index % 2 == 0 ? 75 : 135,
                        140)));
        }

        BehaviorTestCheck.Equal(
            1,
            emitted.Count(static gesture =>
                gesture == PetPointerGesture.Petting));
        BehaviorTestCheck.SequenceEqual(
            [PetPointerGesture.PettingEnded],
            recognizer.End());
    }

    private static void ResumeKeepsPettingLatchedAndReanchorsTrajectory()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 80, 140));
        PetPointerGesture[] initial = new[]
            {
                Sample(250, 130, 140),
                Sample(500, 75, 140),
                Sample(750, 135, 140),
                Sample(1_000, 70, 140),
            }
            .SelectMany(recognizer.Observe)
            .ToArray();
        BehaviorTestCheck.Equal(
            1,
            initial.Count(static gesture =>
                gesture == PetPointerGesture.Petting));

        recognizer.Resume(Sample(1_150, 250, 40));
        var afterResume = new List<PetPointerGesture>();
        for (var index = 1; index <= 8; index++)
        {
            afterResume.AddRange(
                recognizer.Observe(
                    Sample(
                        1_150 + index * 150,
                        index % 2 == 0 ? 75 : 135,
                        140)));
        }

        BehaviorTestCheck.False(
            afterResume.Contains(PetPointerGesture.Petting));
        BehaviorTestCheck.False(
            afterResume.Contains(PetPointerGesture.RapidPointer));
        BehaviorTestCheck.SequenceEqual(
            [PetPointerGesture.PettingEnded],
            recognizer.End());
    }

    private static void RecognizesPettingAfterLongHover()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 120, 120));
        for (var milliseconds = 500;
             milliseconds <= 5_000;
             milliseconds += 500)
        {
            _ = recognizer.Observe(
                Sample(milliseconds, 120, 120));
        }

        PetPointerGesture[] emitted = new[]
            {
                Sample(5_250, 80, 140),
                Sample(5_500, 140, 140),
                Sample(5_750, 75, 140),
                Sample(6_000, 140, 140),
            }
            .SelectMany(recognizer.Observe)
            .ToArray();

        BehaviorTestCheck.True(
            emitted.Contains(PetPointerGesture.Petting));
    }

    private static void ExpiredCirclePathDoesNotCarryIntoCurrentWindow()
    {
        var recognizer = new PointerGestureRecognizer();
        const double center = 140;
        const double radius = 70;
        double partialAngle = Math.PI * 1.45;
        recognizer.Begin(CircleSample(0, 0, center, radius));
        var emitted = new List<PetPointerGesture>();
        for (var index = 1; index <= 10; index++)
        {
            emitted.AddRange(
                recognizer.Observe(
                    CircleSample(
                        index * 80,
                        partialAngle * index / 10,
                        center,
                        radius)));
        }

        for (var milliseconds = 1_300;
             milliseconds <= 4_800;
             milliseconds += 500)
        {
            emitted.AddRange(
                recognizer.Observe(
                    CircleSample(
                        milliseconds,
                        partialAngle,
                        center,
                        radius)));
        }

        emitted.AddRange(
            recognizer.Observe(
                CircleSample(
                    5_100,
                    Math.PI * 1.65,
                    center,
                    radius)));

        BehaviorTestCheck.False(
            emitted.Contains(PetPointerGesture.CirclePointer));
    }

    private static void CancelDiscardsGestureWithoutEndEvent()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 80, 140));
        _ = new[]
            {
                Sample(250, 130, 140),
                Sample(500, 75, 140),
                Sample(750, 135, 140),
                Sample(1_000, 70, 140),
            }
            .SelectMany(recognizer.Observe)
            .ToArray();
        BehaviorTestCheck.True(recognizer.IsPetting);

        recognizer.Cancel();

        BehaviorTestCheck.Equal(0, recognizer.RecentSampleCount);
        BehaviorTestCheck.SequenceEqual(
            Array.Empty<PetPointerGesture>(),
            recognizer.End());

        recognizer.Begin(Sample(1_100, 140, 70));
        IReadOnlyList<PetPointerGesture> afterRestart =
            recognizer.Observe(Sample(1_200, 155, 72));
        BehaviorTestCheck.False(
            afterRestart.Contains(PetPointerGesture.PettingEnded));
        BehaviorTestCheck.False(
            afterRestart.Contains(PetPointerGesture.CirclePointer));
    }

    private static void KeepsTrajectoryBounded()
    {
        var recognizer = new PointerGestureRecognizer();
        recognizer.Begin(Sample(0, 10, 10));
        for (var index = 1; index <= 400; index++)
        {
            _ = recognizer.Observe(
                Sample(index * 10, 10 + index % 3, 10));
        }

        BehaviorTestCheck.True(recognizer.RecentSampleCount <= 180);
    }

    private static PetPointerSample Sample(
        int milliseconds,
        double x,
        double y) =>
        new(
            TimeSpan.FromMilliseconds(milliseconds),
            x,
            y,
            280,
            280);

    private static PetPointerSample CircleSample(
        int milliseconds,
        double angle,
        double center,
        double radius) =>
        Sample(
            milliseconds,
            center + Math.Cos(angle) * radius,
            center + Math.Sin(angle) * radius);

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine(
            $"PASS {nameof(PointerGestureRecognizerTests)}.{name}");
    }
}
