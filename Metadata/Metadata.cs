using ConnectorLib.JSON;
using System;
using System.Collections.Generic;
using System.Text;
using TowerFall;

namespace CrowdControl.Metadata
{
    public abstract class Metadata
    {
        public abstract string Key { get; }
        public abstract DataResponse TryGetValue();

        //protected Level? Level => CrowdControlModule.Instance.;
        //protected Player? Player => CrowdControlHelper.Instance.Player;
    }
}
