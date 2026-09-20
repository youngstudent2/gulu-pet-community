namespace GuluPet.Runtime;

/// <summary>
/// Ensures one physical pointer stream contributes at most one accepted
/// interaction, even when the recognizers classify more than one gesture
/// during that stream (for example, a long press that turns into a drag).
/// </summary>
internal sealed class ActiveInteractionDeduplicator
{
    private long? _lastPressSessionId;
    private long? _lastPointerFocusSessionId;

    public bool TryAcceptPressSession(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        if (_lastPressSessionId == sessionId)
        {
            return false;
        }

        _lastPressSessionId = sessionId;
        return true;
    }

    public bool TryAcceptPointerFocusSession(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        if (_lastPointerFocusSessionId == sessionId)
        {
            return false;
        }

        _lastPointerFocusSessionId = sessionId;
        return true;
    }
}
