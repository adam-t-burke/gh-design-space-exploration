using Grasshopper.Kernel;

namespace DSOptimization
{
    public partial class DSOptimizerComponent : IOptimizationHost
    {
        GH_Component IOptimizationHost.Component
        {
            get { return this; }
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
