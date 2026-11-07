using System.Configuration;
using System;
using System.Threading;
using System.Windows;

namespace openTransferWPF;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\OpenTransfer.SingleInstance";
    private const string ActivationEventName = @"Local\OpenTransfer.Activate";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirstInstance);
        _ownsInstance = isFirstInstance;

        if (!isFirstInstance)
        {
            try
            {
                using EventWaitHandle existingEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                existingEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }

            Shutdown(0);
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => Dispatcher.BeginInvoke(BringExistingWindowToFront),
            null,
            Timeout.Infinite,
            false);

        base.OnStartup(e);
    }

    private void BringExistingWindowToFront()
    {
        Window? window = MainWindow;
        if (window == null) return;

        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsInstance)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

