using System.Reflection;
using Grasshopper.Kernel;

namespace DSOptimization
{
    /// <summary>
    /// Reads the active Grasshopper document without naming GH_Canvas.
    /// GH_Canvas derives from a UI toolkit control, and Core does not reference that toolkit.
    /// </summary>
    public static class GhDocuments
    {
        public static GH_Document Active()
        {
            PropertyInfo canvasProperty = typeof(Grasshopper.Instances).GetProperty("ActiveCanvas");
            object canvas = canvasProperty == null ? null : canvasProperty.GetValue(null, null);
            if (canvas == null)
                return null;
            PropertyInfo documentProperty = canvas.GetType().GetProperty("Document");
            return documentProperty == null ? null : documentProperty.GetValue(canvas, null) as GH_Document;
        }
    }
}
