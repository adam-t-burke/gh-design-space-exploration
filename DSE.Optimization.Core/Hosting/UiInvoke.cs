using System;

namespace DSOptimization
{
    /// <summary>
    /// Marshals work onto the Rhino UI thread. The Eto UI project supplies the implementation.
    /// Core does not reference Eto or call Dispatcher.
    /// </summary>
    public interface IUiInvoker
    {
        void Invoke(Action action);
    }

    public static class UiInvoke
    {
        public static IUiInvoker Current { get; set; }

        public static void Invoke(Action action)
        {
            if (action == null)
                return;
            IUiInvoker invoker = Current;
            if (invoker == null)
                action();
            else
                invoker.Invoke(action);
        }
    }

    public enum UiVisibility
    {
        Visible,
        Collapsed,
        Hidden
    }
}
