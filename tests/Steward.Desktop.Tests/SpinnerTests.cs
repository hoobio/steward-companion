using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Spinner = Steward.Desktop.Views.Spinner;

namespace Steward.Desktop.Tests;

public sealed class SpinnerTests(AvaloniaSession session)
{
    [Fact]
    public void Turning_the_spinner_on_and_off_rotates_the_element() => session.Run(() =>
    {
        var glyph = new Border { Width = 16, Height = 16 };
        var window = new Window { Content = glyph };
        window.Show();
        try
        {
            Spinner.SetIsSpinning(glyph, true);
            Dispatcher.UIThread.RunJobs();
            Spinner.SetIsSpinning(glyph, false);
            Dispatcher.UIThread.RunJobs();
            Spinner.SetIsSpinning(glyph, true);
            Spinner.SetIsSpinning(glyph, false);

            Assert.IsType<RotateTransform>(glyph.RenderTransform);
        }
        finally
        {
            window.Hide();
        }
    });

    [Fact]
    public void Spinning_leaves_no_faulted_task_behind() => session.Run(() =>
    {
        var unobserved = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs args) => unobserved.AddRange(args.Exception.InnerExceptions);
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            SpinAndStop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }
    });

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void SpinAndStop()
    {
        var glyph = new Border { Width = 16, Height = 16 };
        var window = new Window { Content = glyph };
        window.Show();
        try
        {
            Spinner.SetIsSpinning(glyph, true);
            Dispatcher.UIThread.RunJobs();
            Spinner.SetIsSpinning(glyph, false);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Hide();
        }
    }
}
