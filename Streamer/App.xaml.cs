using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace Streamer
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // UI thread exceptions
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Non-UI thread exceptions
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Task scheduler unobserved exceptions
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Debug.WriteLine($"DispatcherUnhandledException: {e.Exception}");
                Logger.LogError($"DispatcherUnhandledException: {e.Exception}");
                // Attempt cleanup
                try { (Current?.MainWindow as MainWindow)?.PerformShutdownCleanup(); } catch (Exception ex) { Debug.WriteLine($"Cleanup error: {ex}"); }

                // Mark handled to allow graceful shutdown
                e.Handled = true;
                // Use WPF MessageBox explicitly to avoid ambiguity
                System.Windows.MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { Current?.Shutdown(); } catch { }
            }
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Debug.WriteLine($"UnhandledException: {e.ExceptionObject}");
                Logger.LogError($"UnhandledException: {e.ExceptionObject}");
                try { (Current?.MainWindow as MainWindow)?.PerformShutdownCleanup(); } catch (Exception ex) { Debug.WriteLine($"Cleanup error: {ex}"); }
            }
            finally
            {
                try { Current?.Shutdown(); } catch { }
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                Debug.WriteLine($"UnobservedTaskException: {e.Exception}");
                Logger.LogError($"UnobservedTaskException: {e.Exception}");
                try { (Current?.MainWindow as MainWindow)?.PerformShutdownCleanup(); } catch (Exception ex) { Debug.WriteLine($"Cleanup error: {ex}"); }
                e.SetObserved();
            }
            catch { }
        }
    }
}
