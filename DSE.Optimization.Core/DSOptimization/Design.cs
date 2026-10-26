using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace DSOptimization
{
    public class Design
    {
        public IOptimizationHost Host { get; private set; }
        public GH_Component MyComponent { get { return Host.Component; } }
        public IList<IGH_Param> NumObjects { get { return Host.NumObjects; } }
        public IList<IGH_Param> CrvObjects { get { return Host.CrvObjects; } }
        public IList<IGH_Param> SrfObjects { get { return Host.SrfObjects; } }

        public List<IVariable> Variables { get; set; }
        public List<IDesignGeometry> Geometries { get; set; }
        public List<Constraint> Constraints { get; set; }

        public List<double> Objectives { get { return Host.Objectives; } }
        public List<IVariable> ActiveVariables { get { return Variables.Where(var => var.IsActive).ToList(); } }

        public Design(IOptimizationHost host)
        {
            this.Host = host;
            this.Variables = new List<IVariable>();
            this.Geometries = new List<IDesignGeometry>();
            this.Constraints = new List<Constraint>();

            IList<IGH_Param> numeric = host.NumObjects ?? new List<IGH_Param>();
            foreach (IGH_Param param in numeric)
            {
                SliderVariable probe = new SliderVariable(param);
                if (probe.CurrentValue == 0)
                {
                    if (probe.Max >= 0.001)
                        probe.UpdateValue(0.001);
                    else
                        probe.UpdateValue(-0.001);
                }
                this.Variables.Add(new SliderVariable(param));
            }

            GH_Document document = GhDocuments.Active();
            if (document != null)
                document.NewSolution(false, GH_SolutionMode.Silent);

            IList<IGH_Param> surfaces = host.SrfObjects ?? new List<IGH_Param>();
            IList<NurbsSurface> surfaceValues = host.SrfVariables ?? new List<NurbsSurface>();
            for (int i = 0; i < surfaces.Count && i < surfaceValues.Count; i++)
                Geometries.Add(new DesignSurface(surfaces[i], surfaceValues[i]));

            IList<IGH_Param> curves = host.CrvObjects ?? new List<IGH_Param>();
            IList<NurbsCurve> curveValues = host.CrvVariables ?? new List<NurbsCurve>();
            for (int i = 0; i < curves.Count && i < curveValues.Count; i++)
                this.Geometries.Add(new DesignCurve(curves[i], curveValues[i]));

            if (Geometries.Any())
                this.Variables.AddRange(Geometries.Select(x => x.Variables).SelectMany(x => x).ToList());

            IList<double> constraintValues = host.Constraints ?? new List<double>();
            for (int i = 0; i < constraintValues.Count; i++)
                this.Constraints.Add(new Constraint(host, Constraint.ConstraintType.morethan, i));

            host.VariableCount = this.Variables.Where(var => var.IsActive).Count();
        }

        public void UpdateComponentOutputs(List<List<double>> GradientData)
        {
            Host.AppendToObjectives(this.Objectives);
            Host.AppendToVariables(this.Variables.Select(var => var.CurrentValue).ToList());
            Host.AppendToGradients(GradientData);
        }
    }
}
