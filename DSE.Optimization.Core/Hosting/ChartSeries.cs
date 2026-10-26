using System;
using System.Collections;
using System.Collections.Generic;

namespace DSOptimization
{
    /// <summary>
    /// Notifying list used where the WPF UI stored LiveCharts ChartValues.
    /// </summary>
    public sealed class ChartSeries<T> : IEnumerable<T>
    {
        private readonly List<T> _items = new List<T>();
        private readonly object _gate = new object();

        public event EventHandler Changed;

        public int Count
        {
            get { lock (_gate) return _items.Count; }
        }

        public T this[int index]
        {
            get { lock (_gate) return _items[index]; }
        }

        public void Add(T item)
        {
            lock (_gate) _items.Add(item);
            Notify();
        }

        public void AddRange(IEnumerable<T> items)
        {
            lock (_gate) _items.AddRange(items);
            Notify();
        }

        public void Clear()
        {
            lock (_gate) _items.Clear();
            Notify();
        }

        public T[] Snapshot()
        {
            lock (_gate) return _items.ToArray();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)Snapshot()).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void Notify()
        {
            EventHandler handler = Changed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
