using GuluPet.Behavior;

namespace GuluPet.Diagnostics;

public interface IBehaviorTickTraceSink
{
    void Write(BehaviorTickTrace trace);
}
