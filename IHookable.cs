using FortRise;

namespace CrowdControl;

internal interface IHookable
{
    abstract static void Hook(IHarmony harmony);
}