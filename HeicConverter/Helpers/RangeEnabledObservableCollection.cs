﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace HeicConverter.Helpers
{
    public class RangeEnabledObservableCollection<T> : ObservableCollection<T>
    {
        public void AddRange(IEnumerable<T> items)
        {
            this.CheckReentrancy();
            foreach (var item in items)
                this.Items.Add(item);
            this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
