using System.Drawing;
using System.Reflection;

namespace Clicky.Bridge;

public interface IWindowsPointOverlayHost
{
    void Start();
    void FlyTo(Point point, Rectangle displayBounds, string label);
}

public sealed class WindowsPointOverlayRenderer : IWindowsPointOverlayRenderer, IDisposable
{
    private const int DefaultOverlayLingerMilliseconds = 6500;
    private readonly Func<IWindowsPointOverlayHost> _overlayHostFactory;
    private readonly bool _runOnStaThread;
    private bool _disposed;

    public WindowsPointOverlayRenderer()
        : this(() => new OverlayWindowManagerPointOverlayHost(), runOnStaThread: true)
    {
    }

    public WindowsPointOverlayRenderer(Func<IWindowsPointOverlayHost> overlayHostFactory)
        : this(overlayHostFactory, runOnStaThread: false)
    {
    }

    private WindowsPointOverlayRenderer(Func<IWindowsPointOverlayHost> overlayHostFactory, bool runOnStaThread)
    {
        _overlayHostFactory = overlayHostFactory;
        _runOnStaThread = runOnStaThread;
    }

    public Task<bool> RenderPointAsync(Point point, Rectangle displayBounds, string label, CancellationToken cancellationToken)
    {
        if (!_runOnStaThread)
        {
            try
            {
                RenderOnCurrentThread(point, displayBounds, label);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        return RenderOnStaThreadAsync(point, displayBounds, label, cancellationToken);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void RenderOnCurrentThread(Point point, Rectangle displayBounds, string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var host = _overlayHostFactory();
        host.Start();
        host.FlyTo(point, displayBounds, label);
    }

    private Task<bool> RenderOnStaThreadAsync(Point point, Rectangle displayBounds, string label, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lingerMs = ReadOverlayLingerMilliseconds();

        var thread = new Thread(() =>
        {
            IWindowsPointOverlayHost? host = null;
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var dispatcher = DispatcherReflection.CurrentDispatcher();
                host = _overlayHostFactory();
                host.Start();
                host.FlyTo(point, displayBounds, label);
                completion.TrySetResult(true);

                if (lingerMs == 0)
                {
                    (host as IDisposable)?.Dispose();
                    return;
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(lingerMs).ConfigureAwait(false);
                    DispatcherReflection.BeginInvokeShutdown(dispatcher);
                });

                DispatcherReflection.Run();
                (host as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                (host as IDisposable)?.Dispose();
                completion.TrySetResult(false);
                Console.Error.WriteLine($"clicky overlay render unavailable: {ex.Message}");
            }
        })
        {
            Name = "Clicky Hermes Bridge Overlay STA",
            IsBackground = false,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static int ReadOverlayLingerMilliseconds()
    {
        var configured = Environment.GetEnvironmentVariable("CLICKY_BRIDGE_OVERLAY_LINGER_MS");
        return int.TryParse(configured, out var value) && value >= 0
            ? value
            : DefaultOverlayLingerMilliseconds;
    }
}

public sealed class OverlayWindowManagerPointOverlayHost : IWindowsPointOverlayHost, IDisposable
{
    private readonly object _manager;
    private readonly MethodInfo _start;
    private readonly MethodInfo _flyTo;
    private readonly MethodInfo _dispose;
    private bool _started;

    public OverlayWindowManagerPointOverlayHost()
    {
        var overlayAssembly = LoadOverlayAssembly();
        var managerType = overlayAssembly.GetType("Clicky.Overlay.OverlayWindowManager", throwOnError: true)!;
        _manager = Activator.CreateInstance(managerType) ?? throw new InvalidOperationException("could not create OverlayWindowManager");
        _start = managerType.GetMethod("Start", Type.EmptyTypes) ?? throw new MissingMethodException(managerType.FullName, "Start");
        _flyTo = managerType.GetMethod("FlyTo", [PointReflection.PointType, typeof(Rectangle?), typeof(string)])
            ?? throw new MissingMethodException(managerType.FullName, "FlyTo");
        _dispose = managerType.GetMethod("Dispose", Type.EmptyTypes) ?? throw new MissingMethodException(managerType.FullName, "Dispose");
    }

    public void Start()
    {
        if (_started)
            return;

        _start.Invoke(_manager, null);
        _started = true;
    }

    public void FlyTo(Point point, Rectangle displayBounds, string label)
    {
        var wpfPoint = PointReflection.Create(point.X, point.Y);
        _flyTo.Invoke(_manager, [wpfPoint, displayBounds, label]);
    }

    public void Dispose()
    {
        _dispose.Invoke(_manager, null);
    }

    private static Assembly LoadOverlayAssembly()
    {
        try
        {
            return Assembly.Load("Clicky.Overlay");
        }
        catch
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Clicky.Overlay.dll");
            if (!File.Exists(path))
                throw new FileNotFoundException("Clicky.Overlay.dll was not found beside the bridge executable.", path);

            return Assembly.LoadFrom(path);
        }
    }
}

internal static class PointReflection
{
    public static readonly Type PointType = Type.GetType("System.Windows.Point, WindowsBase", throwOnError: true)!;

    public static object Create(double x, double y)
        => Activator.CreateInstance(PointType, x, y) ?? throw new InvalidOperationException("could not create System.Windows.Point");
}

internal static class DispatcherReflection
{
    private static readonly Type DispatcherType = Type.GetType("System.Windows.Threading.Dispatcher, WindowsBase", throwOnError: true)!;
    private static readonly Type DispatcherPriorityType = Type.GetType("System.Windows.Threading.DispatcherPriority, WindowsBase", throwOnError: true)!;

    public static object CurrentDispatcher()
        => DispatcherType.GetProperty("CurrentDispatcher", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    public static void Run()
        => DispatcherType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

    public static void BeginInvokeShutdown(object dispatcher)
    {
        var background = Enum.Parse(DispatcherPriorityType, "Background");
        DispatcherType.GetMethod("BeginInvokeShutdown", [DispatcherPriorityType])!.Invoke(dispatcher, [background]);
    }
}
