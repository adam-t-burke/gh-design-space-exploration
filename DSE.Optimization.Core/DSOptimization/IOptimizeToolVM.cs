using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSOptimization
{
    public interface IOptimizeToolVM
    {
        IOptimizationHost Component { get; set; }
        List<GroupVarVM> GroupVars { get; set; }
        List<VarVM> NumVars { get; set; }
        List<List<VarVM>> GeoVars { get; set; }
    }
}
