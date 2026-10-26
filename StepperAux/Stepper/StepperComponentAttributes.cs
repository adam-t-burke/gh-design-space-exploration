using DSE.Optimization.UI;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Stepper
{
    public class StepperComponentAttributes : GH_ComponentAttributes
    {
        public StepperComponentAttributes(IGH_Component component) : base(component)
        {
            this.MyComponent = (StepperComponent)component;
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!this.MyComponent.IsWindowOpen && this.MyComponent.InputsSatisfied)
            {
                this.MyComponent.IsWindowOpen = true;
                try
                {
                    OptimizationWindows.ShowStepper(this.MyComponent);
                }
                catch
                {
                    this.MyComponent.IsWindowOpen = false;
                    throw;
                }
            }
            return base.RespondToMouseDoubleClick(sender, e);
        }

        private StepperComponent MyComponent;
    }
}
