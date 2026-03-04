using System.Windows.Input;

namespace SimsModDesktop.Presentation.ViewModels.Infrastructure;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly bool _disableWhileRunning;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, bool disableWhileRunning = true)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _disableWhileRunning = disableWhileRunning;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return (!_disableWhileRunning || !_isRunning) && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (_isRunning || !(_canExecute?.Invoke() ?? true))
        {
            return;
        }

        _isRunning = true;
        try
        {
            if (_disableWhileRunning)
            {
                NotifyCanExecuteChanged();
            }

            await _execute();
        }
        finally
        {
            _isRunning = false;
            if (_disableWhileRunning)
            {
                NotifyCanExecuteChanged();
            }
        }
    }

    public void NotifyCanExecuteChanged()
    {
        var handlers = CanExecuteChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
            }
        }
    }
}

public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly bool _disableWhileRunning;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, bool disableWhileRunning = true)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _disableWhileRunning = disableWhileRunning;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return (!_disableWhileRunning || !_isRunning) && (_canExecute?.Invoke((T?)parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (_isRunning || !(_canExecute?.Invoke((T?)parameter) ?? true))
        {
            return;
        }

        _isRunning = true;
        try
        {
            if (_disableWhileRunning)
            {
                NotifyCanExecuteChanged();
            }

            await _execute((T?)parameter);
        }
        finally
        {
            _isRunning = false;
            if (_disableWhileRunning)
            {
                NotifyCanExecuteChanged();
            }
        }
    }

    public void NotifyCanExecuteChanged()
    {
        var handlers = CanExecuteChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
            }
        }
    }
}
