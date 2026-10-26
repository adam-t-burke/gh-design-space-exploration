using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using DSOptimization;

namespace Stepper
{
    public partial class StepperComponent : IOptimizationHost
    {
        private readonly List<double> _constraints = new List<double>();
        private readonly List<NurbsSurface> _surfaces = new List<NurbsSurface>();
        private readonly List<NurbsCurve> _curves = new List<NurbsCurve>();
        private readonly List<IGH_Param> _emptyGeometry = new List<IGH_Param>();

        GH_Component IOptimizationHost.Component
        {
            get { return this; }
        }

        public List<double> Constraints
        {
            get { return _constraints; }
        }

        public List<NurbsSurface> SrfVariables
        {
            get { return _surfaces; }
        }

        public List<NurbsCurve> CrvVariables
        {
            get { return _curves; }
        }

        public IList<IGH_Param> CrvObjects
        {
            get { return _emptyGeometry; }
        }

        public IList<IGH_Param> SrfObjects
        {
            get { return _emptyGeometry; }
        }

        public int VariableCount
        {
            get { return numVars; }
            set { numVars = value; }
        }

        public string ObjectiveNickName(int index)
        {
            if (Params == null || Params.Input.Count == 0 || index < 0 || index >= Params.Input[0].Sources.Count)
                return "Objective " + (index + 1);
            return Params.Input[0].Sources[index].NickName;
        }
    }
}
