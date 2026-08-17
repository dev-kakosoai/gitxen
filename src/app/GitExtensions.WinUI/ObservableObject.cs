using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitExtensions.WinUI;

/// <summary>
///  Minimal <see cref="INotifyPropertyChanged"/> base. Hand-rolled rather than taken from a MVVM
///  toolkit package to keep this project's dependency graph as small as possible — the WindowsAppSDK
///  assembly resolution here is fragile enough already (see the csproj comments).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
