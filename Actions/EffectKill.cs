using Microsoft.Extensions.Logging;
using TowerFall;
using TowerFall.Editor;

namespace CrowdControl.Actions;

public class EffectKill : Effect
{
    public override string Code { get; } = "kill";

    public override string[] Mutex { get; } = { "life" };

    public override void Start()
    {
        base.Start();
        if (!Active || Level == null || Level.LivingPlayers == 0 || Players == null) { return; }

        for (int i = Players.Count - 1; i >= 0; i--)
        {
            if (!Players[i].Dead)
            {
                Players[i].Die(DeathCause.Explosion, i);
                TowerFall.Explosion.Spawn(Level, Players[i].Position, i, false, false, false);
            }
        }
    }
}
