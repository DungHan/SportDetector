using System.Runtime.InteropServices;
using NBA.Capture.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace NBA.Capture.Windows;

/// <summary>
/// Windows.Graphics.Capture-backed <see cref="IFrameSource"/>. Implements the frame-capture spec: start/switch
/// a capture session per selected window/monitor, detect the source becoming unavailable, and always expose
/// only the latest frame (never an unbounded backlog) via <see cref="LatestFrameBuffer{T}"/>.
///
/// NOT verified end-to-end on this machine (no Windows host available to run it) - see tasks.md and
/// design.md's "No trained court keypoint model exists yet" risk entry's sibling concern for capture.
///
/// <see cref="OnFrameArrived"/> (the frame pool's own callback) only does the GPU->CPU copy and
/// <see cref="_buffer"/> publish, then returns immediately - it does not raise the public <see cref="FrameArrived"/>
/// event itself. That's done by a separate <see cref="DispatchLoopAsync"/> loop, so a subscriber slower than
/// the capture rate (e.g. running ONNX inference) can never delay <c>OnFrameArrived</c> from calling
/// <c>TryGetNextFrame</c> again - <c>Direct3D11CaptureFramePool</c> only has 2 buffers, so a slow subscriber
/// blocking this callback risked stalling the capture session itself, not just delaying detection. Mirrors the
/// same split <c>MacFrameSource</c> uses, for the same reason (see its type-level doc comment).
/// </summary>
public sealed class WindowsGraphicsCaptureFrameSource : IFrameSource
{
    private readonly LatestFrameBuffer<CapturedFrame> _buffer = new();
    private readonly Lock _lifecycleLock = new();

    private ID3D11Device? _d3dDevice;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private global::Windows.Graphics.SizeInt32 _lastSize;
    private CancellationTokenSource? _dispatchCts;
    private Task? _dispatchTask;

    public CaptureSourceState State { get; private set; } = CaptureSourceState.NotStarted;

    public CaptureSourceDescriptor? ActiveSource { get; private set; }

    public event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    public event EventHandler<SourceChangedEventArgs>? SourceChanged;

    public event EventHandler? SourceLost;

    public Task StartAsync(CaptureSourceDescriptor source, CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            var previousSource = ActiveSource;
            var wasRunning = State == CaptureSourceState.Running || State == CaptureSourceState.Lost;

            if (wasRunning)
            {
                StopSessionLocked();
            }

            State = CaptureSourceState.Starting;
            ActiveSource = source;
            _buffer.Clear();

            EnsureDeviceLocked();
            _item = source.Kind == CaptureSourceKind.Window
                ? GraphicsCaptureItemInterop.CreateForWindow(source.Handle)
                : GraphicsCaptureItemInterop.CreateForMonitor(source.Handle);

            _item.Closed += OnItemClosed;
            _lastSize = _item.Size;

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                numberOfBuffers: 2,
                _lastSize);
            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(_item);
            _session.StartCapture();

            _dispatchCts = new CancellationTokenSource();
            _dispatchTask = Task.Run(() => DispatchLoopAsync(_dispatchCts.Token));

            State = CaptureSourceState.Running;

            if (wasRunning && previousSource is not null)
            {
                SourceChanged?.Invoke(this, new SourceChangedEventArgs(previousSource, source));
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            StopSessionLocked();
            State = CaptureSourceState.Stopped;
            _buffer.Clear();
        }

        return Task.CompletedTask;
    }

    public CapturedFrame? TryGetLatestFrame() => _buffer.TryGetLatest();

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        lock (_lifecycleLock)
        {
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _winrtDevice = null;
        }
    }

    private void EnsureDeviceLocked()
    {
        if (_d3dDevice is not null)
        {
            return;
        }

        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null,
            out _d3dDevice).CheckError();

        _winrtDevice = Direct3DInterop.CreateDirect3DDeviceFromD3D11Device(_d3dDevice!);
    }

    private void StopSessionLocked()
    {
        // Not awaited (this method is synchronous, called from within _lifecycleLock) - cancellation is
        // enough to stop the loop from raising any further FrameArrived events almost immediately, and the
        // dispatch loop touches no state here beyond the shared _buffer, which is cleared by the caller right
        // after this returns.
        _dispatchCts?.Cancel();
        _dispatchCts?.Dispose();
        _dispatchCts = null;
        _dispatchTask = null;

        if (_item is not null)
        {
            _item.Closed -= OnItemClosed;
        }

        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }

        _session?.Dispose();
        _session = null;
        _item = null;
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_lifecycleLock)
        {
            if (State != CaptureSourceState.Running)
            {
                return;
            }

            State = CaptureSourceState.Lost;
        }

        SourceLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null || _d3dDevice is null)
        {
            return;
        }

        if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
        {
            _lastSize = frame.ContentSize;
            sender.Recreate(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _lastSize);
        }

        using var texture = Direct3DInterop.GetD3D11Texture(frame.Surface);
        var description = texture.Description;

        var stagingDescription = description;
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.MiscFlags = ResourceOptionFlags.None;

        using var staging = _d3dDevice.CreateTexture2D(stagingDescription);
        _d3dDevice.ImmediateContext.CopyResource(staging, texture);

        var mapped = _d3dDevice.ImmediateContext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rowPitch = (int)mapped.RowPitch;
            var byteCount = rowPitch * (int)description.Height;
            var buffer = new byte[byteCount];
            Marshal.Copy(mapped.DataPointer, buffer, 0, byteCount);

            var capturedFrame = new CapturedFrame
            {
                Width = (int)description.Width,
                Height = (int)description.Height,
                Format = FramePixelFormat.Bgra8,
                Stride = rowPitch,
                Pixels = buffer,
                Timestamp = DateTimeOffset.UtcNow,
            };

            _buffer.Publish(capturedFrame);
        }
        finally
        {
            _d3dDevice.ImmediateContext.Unmap(staging, 0);
        }
    }

    /// <summary>
    /// Raises <see cref="FrameArrived"/> once per available frame, independent of <see cref="OnFrameArrived"/>'s
    /// own capture-callback cadence (see type-level doc comment). Always dispatches whatever is currently
    /// latest in <see cref="_buffer"/> rather than a queue: if a subscriber is still busy with the previous
    /// frame when several new ones land, it picks up the newest one next and the rest are silently dropped -
    /// the same latest-frame-wins contract <see cref="LatestFrameBuffer{T}"/> already gives
    /// <see cref="TryGetLatestFrame"/>, now also applied to event delivery. Falls back to
    /// <see cref="LatestFrameBuffer{T}.WaitForNextAsync"/> (rather than busy-polling) whenever it has already
    /// caught up to the newest frame, so an idle subscriber isn't woken redundantly for a frame it already saw.
    /// </summary>
    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        CapturedFrame? lastDispatched = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = _buffer.TryGetLatest();
                if (frame is null || ReferenceEquals(frame, lastDispatched))
                {
                    frame = await _buffer.WaitForNextAsync(cancellationToken).ConfigureAwait(false);
                }

                lastDispatched = frame;

                try
                {
                    FrameArrived?.Invoke(this, new FrameArrivedEventArgs(frame));
                }
                catch (Exception ex)
                {
                    // A subscriber (e.g. a detector's inference call) throwing must not kill this dispatch
                    // loop or bubble into the frame pool's own callback thread.
                    Console.WriteLine($"[frame-arrived] subscriber threw, continuing dispatch loop: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync()/StartAsync() switching away from this source.
        }
    }
}
