using System;
using Eto.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino.UI;

namespace EtoSpike
{
    public class EtoSpikeComponent : GH_Component
    {
        public EtoSpikeComponent()
            : base(
                "Eto Spike",
                "EtoSpike",
                "Double-click to open an Eto form owned by the Rhino main window.",
                "DSE",
                "Spike")
        {
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("8f4c2a91-6d3e-4b17-9a55-c1e7d0f84b26"); }
        }

        public override void CreateAttributes()
        {
            m_attributes = new EtoSpikeAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
        }
    }

    public class EtoSpikeAttributes : GH_ComponentAttributes
    {
        public EtoSpikeAttributes(EtoSpikeComponent owner)
            : base(owner)
        {
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            Form form = new Form();
            form.Title = "Eto Spike";
            form.ClientSize = new Eto.Drawing.Size(420, 140);
            form.Content = new Label
            {
                Text = "Opened from GH_ComponentAttributes.RespondToMouseDoubleClick.",
                Wrap = WrapMode.Word
            };
            form.Owner = RhinoEtoApp.MainWindow;
            form.Show();
            return GH_ObjectResponse.Handled;
        }
    }
}
