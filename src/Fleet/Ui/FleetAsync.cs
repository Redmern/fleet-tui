using Terminal.Gui.App;

namespace Fleet.Ui;

public static class FleetAsync
{
    public static Task<T> OnUi<T>(IApplication app, Func<T> body)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        app.Invoke(() =>
        {
            try
            {
                tcs.SetResult(body());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }
}
