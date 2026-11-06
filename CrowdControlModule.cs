using FortRise;
using Microsoft.Extensions.Logging;

namespace CrowdControl;

public class CrowdControlModule : Mod
{
    public static CrowdControlModule Instance;
    public static ILogger Logger { get; private set; }

    public CrowdControlModule(IModContent content, IModuleContext context, ILogger logger) : base(content, context, logger)
    {
        Instance = this;
        Logger = logger;

        CrowdControlHelper.Create(content, context);
    }
}