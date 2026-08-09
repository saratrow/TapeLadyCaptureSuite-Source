using System.Runtime.InteropServices;
using DirectShowLib;

namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// Owns one temporary DirectShow graph used by the live-preview and audio-level test.
/// The EZCAP is explicitly placed in NTSC-M mode before the graph is rendered.
/// Video and audio are connected from the same source filter and therefore share
/// the same DirectShow graph clock.
/// </summary>
internal sealed class DirectShowPreviewSession : IDisposable
{
    private static readonly Guid SampleGrabberClsid = new("C1F400A0-3F08-11D3-9F0B-006008039E37");
    private static readonly Guid NullRendererClsid = new("C1F400A4-3F08-11D3-9F0B-006008039E37");
    private static readonly Guid PcmSubType = new("00000001-0000-0010-8000-00AA00389B71");

    private IGraphBuilder? _graph;
    private ICaptureGraphBuilder2? _captureGraph;
    private IBaseFilter? _sourceFilter;
    private IBaseFilter? _audioSampleGrabberFilter;
    private IBaseFilter? _audioNullRenderer;
    private ISampleGrabber? _audioSampleGrabber;
    private AudioSampleCallback? _audioCallback;
    private IMediaControl? _mediaControl;
    private IVideoWindow? _videoWindow;
    private bool _disposed;

    public bool IsRunning { get; private set; }

    public bool IsAudioConnected { get; private set; }

    public string VideoStandardDescription { get; private set; } = "Not detected";

    public string AudioDescription { get; private set; } = "Not connected";

    public event EventHandler<AudioLevelEventArgs>? AudioLevelChanged;

    public void Start(string devicePath, IntPtr previewHostHandle, Size previewSize)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("A DirectShow device path is required.", nameof(devicePath));
        }

        if (previewHostHandle == IntPtr.Zero)
        {
            throw new ArgumentException("The preview host window is not ready.", nameof(previewHostHandle));
        }

        Stop();

        DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        DsDevice? selectedDevice = null;

        try
        {
            selectedDevice = devices.FirstOrDefault(device =>
                string.Equals(device.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));

            if (selectedDevice is null)
            {
                throw new InvalidOperationException(
                    "The selected DirectShow capture device is no longer available. Refresh the device list and try again.");
            }

            _graph = (IGraphBuilder)new FilterGraph();
            _captureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

            int hr = _captureGraph.SetFiltergraph(_graph);
            DsError.ThrowExceptionForHR(hr);

            Guid filterId = typeof(IBaseFilter).GUID;
            selectedDevice.Mon.BindToObject(null, null, ref filterId, out object filterObject);
            _sourceFilter = (IBaseFilter)filterObject;

            hr = _graph.AddFilter(_sourceFilter, selectedDevice.Name);
            DsError.ThrowExceptionForHR(hr);

            ConfigureNtscVideoStandard(_sourceFilter);

            // Prefer the Preview category when the driver provides it. The EZCAP
            // may expose only a Capture pin, so fall back to Capture if necessary.
            hr = _captureGraph.RenderStream(
                PinCategory.Preview,
                MediaType.Video,
                _sourceFilter,
                null,
                null);

            if (hr < 0)
            {
                hr = _captureGraph.RenderStream(
                    PinCategory.Capture,
                    MediaType.Video,
                    _sourceFilter,
                    null,
                    null);
            }

            DsError.ThrowExceptionForHR(hr);

            // Add a Sample Grabber and Null Renderer to the EZCAP's PCM output.
            // This verifies live embedded audio without sending it to speakers,
            // which avoids feedback while a tape is being monitored.
            TryConnectAudioLevelBranch();

            _mediaControl = (IMediaControl)_graph;
            _videoWindow = (IVideoWindow)_graph;

            hr = _videoWindow.put_Owner(previewHostHandle);
            DsError.ThrowExceptionForHR(hr);

            hr = _videoWindow.put_WindowStyle(
                WindowStyle.Child |
                WindowStyle.ClipChildren |
                WindowStyle.ClipSiblings);
            DsError.ThrowExceptionForHR(hr);

            Resize(previewSize);

            hr = _videoWindow.put_Visible(OABool.True);
            DsError.ThrowExceptionForHR(hr);

            hr = _mediaControl.Run();
            DsError.ThrowExceptionForHR(hr);

            IsRunning = true;
        }
        catch
        {
            Stop();
            throw;
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }
        }
    }

    private void TryConnectAudioLevelBranch()
    {
        IsAudioConnected = false;
        AudioDescription = "EZCAP PCM audio pin was not connected";

        if (_graph is null || _captureGraph is null || _sourceFilter is null)
        {
            return;
        }

        object? sampleGrabberObject = null;
        object? nullRendererObject = null;

        try
        {
            Type sampleGrabberType = Type.GetTypeFromCLSID(SampleGrabberClsid, throwOnError: true)!;
            sampleGrabberObject = Activator.CreateInstance(sampleGrabberType)
                ?? throw new InvalidOperationException("DirectShow Sample Grabber could not be created.");

            _audioSampleGrabber = (ISampleGrabber)sampleGrabberObject;
            _audioSampleGrabberFilter = (IBaseFilter)sampleGrabberObject;

            var requestedType = new AMMediaType
            {
                majorType = MediaType.Audio,
                subType = PcmSubType,
                formatType = FormatType.WaveEx
            };

            int hr = _audioSampleGrabber.SetMediaType(requestedType);
            DsError.ThrowExceptionForHR(hr);
            DsUtils.FreeAMMediaType(requestedType);

            hr = _graph.AddFilter(_audioSampleGrabberFilter, "Tape Lady Audio Level Sample Grabber");
            DsError.ThrowExceptionForHR(hr);

            Type nullRendererType = Type.GetTypeFromCLSID(NullRendererClsid, throwOnError: true)!;
            nullRendererObject = Activator.CreateInstance(nullRendererType)
                ?? throw new InvalidOperationException("DirectShow Null Renderer could not be created.");

            _audioNullRenderer = (IBaseFilter)nullRendererObject;
            hr = _graph.AddFilter(_audioNullRenderer, "Tape Lady Audio Null Renderer");
            DsError.ThrowExceptionForHR(hr);

            _audioCallback = new AudioSampleCallback(level =>
                AudioLevelChanged?.Invoke(this, new AudioLevelEventArgs(level)));

            hr = _audioSampleGrabber.SetOneShot(false);
            DsError.ThrowExceptionForHR(hr);
            hr = _audioSampleGrabber.SetBufferSamples(false);
            DsError.ThrowExceptionForHR(hr);
            hr = _audioSampleGrabber.SetCallback(_audioCallback, 1);
            DsError.ThrowExceptionForHR(hr);

            hr = _captureGraph.RenderStream(
                PinCategory.Capture,
                MediaType.Audio,
                _sourceFilter,
                _audioSampleGrabberFilter,
                _audioNullRenderer);
            DsError.ThrowExceptionForHR(hr);

            IsAudioConnected = true;
            AudioDescription = "EZCAP Audio / PCM (live level active)";
        }
        catch (Exception ex)
        {
            AudioDescription = $"Audio level unavailable: {ex.Message}";
            ReleaseAudioBranch();

            // Audio verification is helpful, but failure must not prevent the
            // already-working DirectShow video preview from starting.
        }
    }

    private void ConfigureNtscVideoStandard(IBaseFilter sourceFilter)
    {
        VideoStandardDescription = "Driver does not expose IAMAnalogVideoDecoder";

        if (sourceFilter is not IAMAnalogVideoDecoder decoder)
        {
            return;
        }

        int hr = decoder.get_AvailableTVFormats(out AnalogVideoStandard availableFormats);
        if (hr < 0)
        {
            VideoStandardDescription = "Unable to read available video standards";
            return;
        }

        AnalogVideoStandard requestedStandard = AnalogVideoStandard.NTSC_M;
        if ((availableFormats & requestedStandard) == 0)
        {
            VideoStandardDescription = $"NTSC-M unavailable; supported: {availableFormats}";
            return;
        }

        hr = decoder.put_TVFormat(requestedStandard);
        if (hr < 0)
        {
            VideoStandardDescription = $"Could not select NTSC-M (HRESULT 0x{hr:X8})";
            return;
        }

        hr = decoder.get_TVFormat(out AnalogVideoStandard activeStandard);
        VideoStandardDescription = hr >= 0
            ? activeStandard.ToString()
            : "NTSC-M requested";
    }

    public void Resize(Size previewSize)
    {
        if (_videoWindow is null)
        {
            return;
        }

        int hostWidth = Math.Max(1, previewSize.Width);
        int hostHeight = Math.Max(1, previewSize.Height);

        // Analog NTSC is stored as 720x480 but is intended for a 4:3 display.
        // Fit a centered 4:3 rectangle inside the host instead of stretching
        // the renderer to the window's arbitrary aspect ratio.
        const double displayAspect = 4.0 / 3.0;
        int width = hostWidth;
        int height = (int)Math.Round(width / displayAspect);

        if (height > hostHeight)
        {
            height = hostHeight;
            width = (int)Math.Round(height * displayAspect);
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        int left = Math.Max(0, (hostWidth - width) / 2);
        int top = Math.Max(0, (hostHeight - height) / 2);

        int hr = _videoWindow.SetWindowPosition(left, top, width, height);
        DsError.ThrowExceptionForHR(hr);
    }

    public void Stop()
    {
        IsRunning = false;
        IsAudioConnected = false;

        try
        {
            _mediaControl?.Stop();
        }
        catch
        {
            // Continue releasing the graph even if a driver fails during stop.
        }

        if (_videoWindow is not null)
        {
            try
            {
                _videoWindow.put_Visible(OABool.False);
                _videoWindow.put_Owner(IntPtr.Zero);
            }
            catch
            {
                // Continue releasing COM objects.
            }
        }

        ReleaseAudioBranch();
        ReleaseCom(_videoWindow);
        ReleaseCom(_mediaControl);
        ReleaseCom(_sourceFilter);
        ReleaseCom(_captureGraph);
        ReleaseCom(_graph);

        _videoWindow = null;
        _mediaControl = null;
        _sourceFilter = null;
        _captureGraph = null;
        _graph = null;
        VideoStandardDescription = "Not detected";
        AudioDescription = "Not connected";
    }

    private void ReleaseAudioBranch()
    {
        if (_audioSampleGrabber is not null)
        {
            try
            {
                _audioSampleGrabber.SetCallback(null, 0);
            }
            catch
            {
                // Continue releasing COM objects.
            }
        }

        ReleaseCom(_audioSampleGrabber);
        ReleaseCom(_audioNullRenderer);
        ReleaseCom(_audioSampleGrabberFilter);

        _audioSampleGrabber = null;
        _audioNullRenderer = null;
        _audioSampleGrabberFilter = null;
        _audioCallback = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class AudioSampleCallback : ISampleGrabberCB
    {
        private readonly Action<int> _levelHandler;
        private long _lastUpdateTicks;

        public AudioSampleCallback(Action<int> levelHandler)
        {
            _levelHandler = levelHandler;
        }

        public int SampleCB(double sampleTime, IMediaSample sample)
        {
            return 0;
        }

        public int BufferCB(double sampleTime, IntPtr buffer, int bufferLength)
        {
            if (buffer == IntPtr.Zero || bufferLength < 2)
            {
                return 0;
            }

            // Limit UI updates to roughly 20 per second.
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastUpdateTicks) < 50)
            {
                return 0;
            }
            Interlocked.Exchange(ref _lastUpdateTicks, now);

            int sampleCount = bufferLength / 2;
            long sumSquares = 0;
            int step = Math.Max(1, sampleCount / 4096);
            int measured = 0;

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex += step)
            {
                short sample = Marshal.ReadInt16(buffer, sampleIndex * 2);
                int value = sample;
                sumSquares += (long)value * value;
                measured++;
            }

            if (measured == 0)
            {
                return 0;
            }

            double rms = Math.Sqrt(sumSquares / (double)measured);
            int level = Math.Clamp((int)Math.Round(rms / short.MaxValue * 100.0), 0, 100);
            _levelHandler(level);
            return 0;
        }
    }
}

internal sealed class AudioLevelEventArgs : EventArgs
{
    public AudioLevelEventArgs(int level)
    {
        Level = Math.Clamp(level, 0, 100);
    }

    public int Level { get; }
}
