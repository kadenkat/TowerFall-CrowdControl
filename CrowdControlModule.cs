using CrowdControl.Actions;
using FortRise;
using Microsoft.Extensions.Logging;

namespace CrowdControl;

public class CrowdControlModule : Mod
{
    public static CrowdControlModule Instance;
    public static ILogger Logger { get; private set; }

    private static Type[] Hookables = [
        typeof(EffectRevive),
    ];

    public CrowdControlModule(IModContent content, IModuleContext context, ILogger logger) : base(content, context, logger)
    {
        Instance = this;
        Logger = logger;

        CrowdControlHelper.Create(content, context);

        foreach (var hookable in Hookables)
        {
            hookable.GetMethod(nameof(IHookable.Hook))!.Invoke(null, [context.Harmony]);
        }
    }
}
