using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace DSOptimization
{
    /// <summary>
    /// Grasshopper component surface used by Design and the view models.
    /// Radical, Stepper, and DSOpt each implement this on their existing component class.
    /// </summary>
    public interface IOptimizationHost
    {
        GH_Component Component { get; }
        List<double> Objectives { get; }
        List<double> Constraints { get; }
        List<NurbsSurface> SrfVariables { get; }
        List<NurbsCurve> CrvVariables { get; }
        IList<IGH_Param> NumObjects { get; }
        IList<IGH_Param> CrvObjects { get; }
        IList<IGH_Param> SrfObjects { get; }
        bool IsWindowOpen { get; set; }
        int VariableCount { get; set; }
        string ObjectiveNickName(int index);
        void AppendToObjectives(List<double> values);
        void AppendToVariables(List<double> values);
        void AppendToGradients(List<List<double>> values);
    }
}
