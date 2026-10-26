using System.ComponentModel;

namespace Radical
{
    public enum RefreshMode
    {
        [Description("Live Geometry and Data")] Live = 1,
        [Description("Live Data")] Data = 2,
        [Description("Silent")] Silent = 3
    }
}
