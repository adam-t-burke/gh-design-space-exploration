using System;
using DSOptimization;
using Rhino;

namespace DSE.Optimization.UI
{
    public sealed class RhinoUiInvoker : IUiInvoker
    {
        public static readonly RhinoUiInvoker Instance = new RhinoUiInvoker();

        public void Invoke(Action action)
        {
            if (action == null)
                return;
            Exception error = null;
            Action invoke = delegate
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            };
            RhinoApp.InvokeOnUiThread(invoke);
            if (error != null)
                throw error;
        }
    }
}
