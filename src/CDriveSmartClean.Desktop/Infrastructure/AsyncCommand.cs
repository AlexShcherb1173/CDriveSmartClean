using System.Windows.Input;

namespace CDriveSmartClean.Desktop.Infrastructure;

internal sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    private bool executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !executing && canExecute();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        executing = true;
        NotifyCanExecuteChanged();
        try { await execute(); }
        finally { executing = false; NotifyCanExecuteChanged(); }
    }
    internal void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    internal void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
