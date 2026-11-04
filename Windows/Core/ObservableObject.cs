using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MacGuard.Core;

/// <summary>SwiftUI'daki ObservableObject'in karşılığı: değişen özelliği arayüze yayınlar.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
