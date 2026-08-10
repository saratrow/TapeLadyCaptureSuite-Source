using System.Runtime.InteropServices;
using System.Text;
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
    private IBaseFilter? _videoRenderer;
    private IBaseFilter? _audioSampleGrabberFilter;
    private IBaseFilter? _audioNullRenderer;
    private ISampleGrabber? _audioSampleGrabber;
    private AudioSampleCallback? _audioCallback;
    private IMediaControl? _mediaControl;
    private IVideoWindow? _videoWindow;
    private IVMRWindowlessControl9? _vmrWindowlessControl;
    private IntPtr _previewHostHandle;
    private DirectShowPreviewStartDiagnostics? _diagnostics;
    private string _runningGraphTopologyReport = "No active DirectShow graph.";
    private readonly object _lifecycleLock = new();
    private Task? _stopTask;
    private bool _disposed;

    public bool IsRunning { get; private set; }

    public bool IsAudioConnected { get; private set; }

    public string VideoStandardDescription { get; private set; } = "Not detected";

    public string VideoFormatDescription { get; private set; } = "Driver default";

    public string VideoPinDescription { get; private set; } = "Not connected";

    public string RunningGraphReport
    {
        get => _diagnostics?.BuildReport(_runningGraphTopologyReport) ?? _runningGraphTopologyReport;
        private set => _runningGraphTopologyReport = value;
    }

    public string AudioDescription { get; private set; } = "Not connected";

    public string Vmr9DiagnosticsReport => _diagnostics?.BuildReport(_runningGraphTopologyReport)
        ?? "No VMR9 diagnostics were captured for this preview session.";

    public event EventHandler<AudioLevelEventArgs>? AudioLevelChanged;

    public async Task StartAsync(
        string devicePath,
        IntPtr previewHostHandle,
        Size previewSize,
        DirectShowRendererMode rendererMode = DirectShowRendererMode.Default,
        DirectShowPreviewStartDiagnostics? diagnostics = null)
    {
        ThrowIfDisposed();
        await StopAsync();
        StartCore(devicePath, previewHostHandle, previewSize, rendererMode, diagnostics);
    }

    public void Start(
        string devicePath,
        IntPtr previewHostHandle,
        Size previewSize,
        DirectShowRendererMode rendererMode = DirectShowRendererMode.Default,
        DirectShowPreviewStartDiagnostics? diagnostics = null)
    {
        ThrowIfDisposed();
        StartCore(devicePath, previewHostHandle, previewSize, rendererMode, diagnostics);
    }

    private void StartCore(
        string devicePath,
        IntPtr previewHostHandle,
        Size previewSize,
        DirectShowRendererMode rendererMode,
        DirectShowPreviewStartDiagnostics? diagnostics)
    {
        _diagnostics = diagnostics;
        _previewHostHandle = previewHostHandle;
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("A DirectShow device path is required.", nameof(devicePath));
        }

        if (previewHostHandle == IntPtr.Zero)
        {
            throw new ArgumentException("The preview host window is not ready.", nameof(previewHostHandle));
        }

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

            diagnostics?.BeginPhase("graph builder creation: FilterGraph");
            _graph = (IGraphBuilder)new FilterGraph();
            diagnostics?.CompletePhase("graph builder creation: FilterGraph");

            diagnostics?.BeginPhase("graph builder creation: CaptureGraphBuilder2");
            _captureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            diagnostics?.CompletePhase("graph builder creation: CaptureGraphBuilder2");

            diagnostics?.BeginPhase("capture graph builder: SetFiltergraph");
            int hr = _captureGraph.SetFiltergraph(_graph);
            diagnostics?.RecordHResult("ICaptureGraphBuilder2.SetFiltergraph", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("capture graph builder: SetFiltergraph");

            Guid filterId = typeof(IBaseFilter).GUID;
            diagnostics?.BeginPhase("source filter add: BindToObject");
            selectedDevice.Mon.BindToObject(null, null, ref filterId, out object filterObject);
            _sourceFilter = (IBaseFilter)filterObject;
            diagnostics?.CompletePhase("source filter add: BindToObject");

            diagnostics?.BeginPhase("source filter add: AddFilter");
            hr = _graph.AddFilter(_sourceFilter, selectedDevice.Name);
            diagnostics?.RecordHResult("IGraphBuilder.AddFilter(source)", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("source filter add: AddFilter");

            ConfigureNtscVideoStandard(_sourceFilter);
            ConfigureRenderer(rendererMode, previewHostHandle, diagnostics);

            // Prefer the Preview category when the driver provides it. The EZCAP
            // may expose only a Capture pin, so fall back to Capture if necessary.
            string videoRoute = "Preview";
            diagnostics?.BeginPhase("RenderStream: video Preview");
            hr = _captureGraph.RenderStream(
                PinCategory.Preview,
                MediaType.Video,
                _sourceFilter,
                null,
                _videoRenderer);
            diagnostics?.RecordHResult("ICaptureGraphBuilder2.RenderStream(video Preview)", hr);
            diagnostics?.CompletePhase("RenderStream: video Preview");

            if (hr < 0)
            {
                videoRoute = "Capture fallback";
                diagnostics?.BeginPhase("RenderStream: video Capture fallback");
                hr = _captureGraph.RenderStream(
                    PinCategory.Capture,
                    MediaType.Video,
                    _sourceFilter,
                    null,
                    _videoRenderer);
                diagnostics?.RecordHResult("ICaptureGraphBuilder2.RenderStream(video Capture fallback)", hr);
                diagnostics?.CompletePhase("RenderStream: video Capture fallback");
            }

            DsError.ThrowExceptionForHR(hr);

            TryConnectAudioLevelBranch(diagnostics);
            CaptureRunningGraphDiagnostics(
                videoRoute,
                preserveOwnedFilterRcws: rendererMode == DirectShowRendererMode.Vmr9Windowless);

            _mediaControl = (IMediaControl)_graph;
            if (rendererMode == DirectShowRendererMode.Default)
            {
                diagnostics?.BeginPhase("video window hookup: IVideoWindow cast");
                _videoWindow = (IVideoWindow)_graph;
                diagnostics?.CompletePhase("video window hookup: IVideoWindow cast");

                diagnostics?.BeginPhase("video window hookup: put_Owner");
                hr = _videoWindow.put_Owner(previewHostHandle);
                DsError.ThrowExceptionForHR(hr);
                diagnostics?.CompletePhase("video window hookup: put_Owner");

                diagnostics?.BeginPhase("video window hookup: put_WindowStyle");
                hr = _videoWindow.put_WindowStyle(
                    WindowStyle.Child |
                    WindowStyle.ClipChildren |
                    WindowStyle.ClipSiblings);
                DsError.ThrowExceptionForHR(hr);
                diagnostics?.CompletePhase("video window hookup: put_WindowStyle");
            }

            diagnostics?.BeginPhase("renderer layout: initial Resize");
            Resize(previewSize);
            diagnostics?.CompletePhase("renderer layout: initial Resize");

            if (_videoWindow is not null)
            {
                diagnostics?.BeginPhase("video window hookup: put_Visible");
                hr = _videoWindow.put_Visible(OABool.True);
                DsError.ThrowExceptionForHR(hr);
                diagnostics?.CompletePhase("video window hookup: put_Visible");
            }

            diagnostics?.BeginPhase("graph run: IMediaControl.Run");
            hr = _mediaControl.Run();
            diagnostics?.RecordHResult("IMediaControl.Run", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("graph run: IMediaControl.Run");

            if (_vmrWindowlessControl is not null)
            {
                hr = _vmrWindowlessControl.GetNativeVideoSize(
                    out int nativeWidth,
                    out int nativeHeight,
                    out int aspectWidth,
                    out int aspectHeight);
                diagnostics?.RecordHResult("IVMRWindowlessControl9.GetNativeVideoSize", hr);
                diagnostics?.Record($"VMR9 native video size: {nativeWidth}x{nativeHeight}; aspect ratio: {aspectWidth}x{aspectHeight}");
            }

            IsRunning = true;
            diagnostics?.Record("Graph running state: true");
            diagnostics?.Record("IVMRWindowlessControl9.RepaintVideo: not called by this preview path");
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

    private void TryConnectAudioLevelBranch(DirectShowPreviewStartDiagnostics? diagnostics)
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
            diagnostics?.BeginPhase("audio branch: Sample Grabber creation");
            Type sampleGrabberType = Type.GetTypeFromCLSID(SampleGrabberClsid, throwOnError: true)!;
            sampleGrabberObject = Activator.CreateInstance(sampleGrabberType)
                ?? throw new InvalidOperationException("DirectShow Sample Grabber could not be created.");

            _audioSampleGrabber = (ISampleGrabber)sampleGrabberObject;
            _audioSampleGrabberFilter = (IBaseFilter)sampleGrabberObject;
            diagnostics?.CompletePhase("audio branch: Sample Grabber creation");

            var requestedType = new AMMediaType
            {
                majorType = MediaType.Audio,
                subType = PcmSubType,
                formatType = FormatType.WaveEx
            };

            diagnostics?.BeginPhase("audio branch: Sample Grabber SetMediaType");
            int hr = _audioSampleGrabber.SetMediaType(requestedType);
            diagnostics?.RecordHResult("ISampleGrabber.SetMediaType", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("audio branch: Sample Grabber SetMediaType");
            DsUtils.FreeAMMediaType(requestedType);

            diagnostics?.BeginPhase("audio branch: Add Sample Grabber");
            hr = _graph.AddFilter(_audioSampleGrabberFilter, "Tape Lady Audio Level Sample Grabber");
            diagnostics?.RecordHResult("IGraphBuilder.AddFilter(audio Sample Grabber)", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("audio branch: Add Sample Grabber");

            diagnostics?.BeginPhase("audio branch: Null Renderer creation");
            Type nullRendererType = Type.GetTypeFromCLSID(NullRendererClsid, throwOnError: true)!;
            nullRendererObject = Activator.CreateInstance(nullRendererType)
                ?? throw new InvalidOperationException("DirectShow Null Renderer could not be created.");

            _audioNullRenderer = (IBaseFilter)nullRendererObject;
            diagnostics?.CompletePhase("audio branch: Null Renderer creation");
            diagnostics?.BeginPhase("audio branch: Add Null Renderer");
            hr = _graph.AddFilter(_audioNullRenderer, "Tape Lady Audio Null Renderer");
            diagnostics?.RecordHResult("IGraphBuilder.AddFilter(audio Null Renderer)", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("audio branch: Add Null Renderer");

            _audioCallback = new AudioSampleCallback(level =>
                AudioLevelChanged?.Invoke(this, new AudioLevelEventArgs(level)));

            diagnostics?.BeginPhase("audio branch: Sample Grabber configuration");
            hr = _audioSampleGrabber.SetOneShot(false);
            DsError.ThrowExceptionForHR(hr);
            hr = _audioSampleGrabber.SetBufferSamples(false);
            DsError.ThrowExceptionForHR(hr);
            hr = _audioSampleGrabber.SetCallback(_audioCallback, 1);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("audio branch: Sample Grabber configuration");

            diagnostics?.BeginPhase("audio branch: RenderStream");
            hr = _captureGraph.RenderStream(
                PinCategory.Capture,
                MediaType.Audio,
                _sourceFilter,
                _audioSampleGrabberFilter,
                _audioNullRenderer);
            diagnostics?.RecordHResult("ICaptureGraphBuilder2.RenderStream(audio)", hr);
            DsError.ThrowExceptionForHR(hr);
            diagnostics?.CompletePhase("audio branch: RenderStream");

            IsAudioConnected = true;
            AudioDescription = "EZCAP Audio / PCM (live level active)";
        }
        catch (Exception ex)
        {
            diagnostics?.LogNonFatalFailure(ex);
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

    private void ConfigureRenderer(
        DirectShowRendererMode rendererMode,
        IntPtr previewHostHandle,
        DirectShowPreviewStartDiagnostics? diagnostics)
    {
        if (rendererMode == DirectShowRendererMode.Default)
        {
            return;
        }

        if (_graph is null)
        {
            throw new InvalidOperationException("The DirectShow graph is not ready.");
        }

        diagnostics?.BeginPhase("renderer creation: VideoMixingRenderer9");
        var renderer = new VideoMixingRenderer9();
        _videoRenderer = (IBaseFilter)renderer;
        diagnostics?.CompletePhase("renderer creation: VideoMixingRenderer9");

        diagnostics?.BeginPhase("VMR9 configuration: IVMRFilterConfig9 cast");
        var configuration = (IVMRFilterConfig9)renderer;
        diagnostics?.CompletePhase("VMR9 configuration: IVMRFilterConfig9 cast");

        diagnostics?.BeginPhase("VMR9 configuration: SetRenderingMode");
        int hr = configuration.SetRenderingMode(VMR9Mode.Windowless);
        diagnostics?.RecordHResult("IVMRFilterConfig9.SetRenderingMode", hr);
        DsError.ThrowExceptionForHR(hr);
        diagnostics?.CompletePhase("VMR9 configuration: SetRenderingMode");

        diagnostics?.BeginPhase("VMR9 configuration: IVMRWindowlessControl9 cast");
        _vmrWindowlessControl = (IVMRWindowlessControl9)renderer;
        diagnostics?.CompletePhase("VMR9 configuration: IVMRWindowlessControl9 cast");

        diagnostics?.BeginPhase("VMR9 configuration: SetVideoClippingWindow");
        hr = _vmrWindowlessControl.SetVideoClippingWindow(previewHostHandle);
        diagnostics?.RecordHResult("IVMRWindowlessControl9.SetVideoClippingWindow", hr);
        DsError.ThrowExceptionForHR(hr);
        diagnostics?.CompletePhase("VMR9 configuration: SetVideoClippingWindow");

        diagnostics?.BeginPhase("VMR9 configuration: SetAspectRatioMode");
        hr = _vmrWindowlessControl.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox);
        diagnostics?.RecordHResult("IVMRWindowlessControl9.SetAspectRatioMode", hr);
        DsError.ThrowExceptionForHR(hr);
        diagnostics?.CompletePhase("VMR9 configuration: SetAspectRatioMode");

        diagnostics?.BeginPhase("renderer creation: AddFilter");
        hr = _graph.AddFilter(_videoRenderer, "Tape Lady VMR9 Windowless Renderer");
        diagnostics?.RecordHResult("IGraphBuilder.AddFilter(VMR9 renderer)", hr);
        DsError.ThrowExceptionForHR(hr);
        diagnostics?.CompletePhase("renderer creation: AddFilter");

    }

    private void CaptureRunningGraphDiagnostics(string videoRoute, bool preserveOwnedFilterRcws = false)
    {
        VideoPinDescription = videoRoute;
        RunningGraphReport = "Running DirectShow graph diagnostics were unavailable.";

        if (_graph is null || _sourceFilter is null)
        {
            return;
        }

        IEnumFilters? filterEnumerator = null;
        var report = new StringBuilder();

        try
        {
            report.AppendLine("Tape Lady Capture Suite - Running DirectShow Graph");
            report.AppendLine($"Requested video route: {videoRoute}");
            report.AppendLine("Filters and connected pins:");

            int hr = _graph.EnumFilters(out filterEnumerator);
            DsError.ThrowExceptionForHR(hr);

            var filters = new IBaseFilter[1];
            while (filterEnumerator.Next(1, filters, IntPtr.Zero) == 0)
            {
                var filter = filters[0];
                try
                {
                    AppendFilterConnections(
                        report,
                        filter,
                        ReferenceEquals(filter, _sourceFilter),
                        preserveOwnedFilterRcws);
                }
                finally
                {
                    ReleaseGraphReportFilterReference(filter, preserveOwnedFilterRcws);
                    filters[0] = null!;
                }
            }

            RunningGraphReport = report.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            RunningGraphReport = $"Running DirectShow graph diagnostics failed: {ex.Message}";
        }
        finally
        {
            ReleaseCom(filterEnumerator);
        }
    }

    private void AppendFilterConnections(
        StringBuilder report,
        IBaseFilter filter,
        bool isSourceFilter,
        bool preserveOwnedFilterRcws)
    {
        string filterName = GetFilterName(filter);
        report.AppendLine($"  {filterName}");

        IEnumPins? pinEnumerator = null;
        try
        {
            int hr = filter.EnumPins(out pinEnumerator);
            DsError.ThrowExceptionForHR(hr);

            var pins = new IPin[1];
            while (pinEnumerator.Next(1, pins, IntPtr.Zero) == 0)
            {
                var pin = pins[0];
                try
                {
                    AppendConnectedPin(report, filterName, pin, isSourceFilter, preserveOwnedFilterRcws);
                }
                finally
                {
                    ReleaseCom(pin);
                    pins[0] = null!;
                }
            }
        }
        finally
        {
            ReleaseCom(pinEnumerator);
        }
    }

    private void AppendConnectedPin(
        StringBuilder report,
        string filterName,
        IPin pin,
        bool isSourceFilter,
        bool preserveOwnedFilterRcws)
    {
        if (pin.QueryDirection(out var direction) < 0 || direction != PinDirection.Output ||
            pin.ConnectedTo(out var connectedPin) < 0)
        {
            return;
        }

        try
        {
            string pinName = GetPinName(pin, preserveOwnedFilterRcws);
            string connectedFilterName = "Unknown filter";
            string connectedPinName = GetPinName(connectedPin, preserveOwnedFilterRcws);

            if (connectedPin.QueryPinInfo(out var connectedInfo) >= 0)
            {
                try
                {
                    connectedFilterName = GetFilterName(connectedInfo.filter);
                }
                finally
                {
                    ReleaseGraphReportFilterReference(connectedInfo.filter, preserveOwnedFilterRcws);
                }
            }

            var mediaType = new AMMediaType();
            try
            {
                string mediaDescription = pin.ConnectionMediaType(mediaType) >= 0
                    ? DescribeMediaType(mediaType)
                    : "Media type unavailable";

                report.AppendLine(
                    $"    {filterName} [{pinName}] -> {connectedFilterName} [{connectedPinName}] : {mediaDescription}");

                if (isSourceFilter && mediaType.majorType == MediaType.Video)
                {
                    VideoPinDescription = $"{VideoPinDescription} ({pinName})";
                    VideoFormatDescription = mediaDescription;
                }
            }
            finally
            {
                DsUtils.FreeAMMediaType(mediaType);
            }
        }
        finally
        {
            ReleaseCom(connectedPin);
        }
    }

    private static string GetFilterName(IBaseFilter? filter)
    {
        if (filter is null || filter.QueryFilterInfo(out var filterInfo) < 0)
        {
            return "Unknown filter";
        }

        try
        {
            return string.IsNullOrWhiteSpace(filterInfo.achName) ? "Unnamed filter" : filterInfo.achName;
        }
        finally
        {
        }
    }

    private string GetPinName(IPin pin, bool preserveOwnedFilterRcws)
    {
        if (pin.QueryPinInfo(out var pinInfo) < 0)
        {
            return "Unnamed pin";
        }

        try
        {
            return string.IsNullOrWhiteSpace(pinInfo.name) ? "Unnamed pin" : pinInfo.name;
        }
        finally
        {
            ReleaseGraphReportFilterReference(pinInfo.filter, preserveOwnedFilterRcws);
        }
    }

    private void ReleaseGraphReportFilterReference(IBaseFilter? filter, bool preserveOwnedFilterRcws)
    {
        if (preserveOwnedFilterRcws &&
            (ReferenceEquals(filter, _sourceFilter) ||
             ReferenceEquals(filter, _videoRenderer) ||
             ReferenceEquals(filter, _audioSampleGrabberFilter) ||
             ReferenceEquals(filter, _audioNullRenderer)))
        {
            return;
        }

        ReleaseCom(filter);
    }

    private static string DescribeMediaType(AMMediaType mediaType)
    {
        string subtype = mediaType.subType == MediaSubType.YUY2 ? "YUY2"
            : mediaType.subType == MediaSubType.UYVY ? "UYVY"
            : mediaType.subType == MediaSubType.RGB24 ? "RGB24"
            : mediaType.subType == MediaSubType.RGB32 ? "RGB32"
            : mediaType.subType == MediaSubType.MJPG ? "MJPEG"
            : mediaType.subType.ToString("D");

        if (mediaType.majorType != MediaType.Video || mediaType.formatPtr == IntPtr.Zero)
        {
            return subtype;
        }

        if (mediaType.formatType == FormatType.VideoInfo)
        {
            var videoInfo = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr);
            if (videoInfo.BmiHeader is null)
            {
                return subtype;
            }

            return DescribeVideoFormat(subtype, videoInfo.BmiHeader.Width, videoInfo.BmiHeader.Height,
                videoInfo.AvgTimePerFrame, "Not reported");
        }

        if (mediaType.formatType == FormatType.VideoInfo2)
        {
            var videoInfo = Marshal.PtrToStructure<VideoInfoHeader2>(mediaType.formatPtr);
            if (videoInfo.BmiHeader is null)
            {
                return subtype;
            }

            return DescribeVideoFormat(subtype, videoInfo.BmiHeader.Width, videoInfo.BmiHeader.Height,
                videoInfo.AvgTimePerFrame, videoInfo.InterlaceFlags.ToString());
        }

        return subtype;
    }

    private static string DescribeVideoFormat(
        string subtype,
        int width,
        int height,
        long averageTimePerFrame,
        string interlace)
    {
        double framesPerSecond = averageTimePerFrame > 0
            ? 10_000_000d / averageTimePerFrame
            : 0;

        return $"{width}x{Math.Abs(height)} {subtype} @ {framesPerSecond:0.###} fps; interlace: {interlace}";
    }

    public void Resize(Size previewSize)
    {
        if (_videoWindow is null && _vmrWindowlessControl is null)
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
        _diagnostics?.Record(
            $"SetVideoPosition destination rectangle: left={left}; top={top}; right={left + width}; bottom={top + height}");

        int hr = _videoWindow is not null
            ? _videoWindow.SetWindowPosition(left, top, width, height)
            : _vmrWindowlessControl!.SetVideoPosition(null, new DsRect(left, top, left + width, top + height));
        _diagnostics?.RecordHResult(
            _videoWindow is not null
                ? "IVideoWindow.SetWindowPosition"
                : "IVMRWindowlessControl9.SetVideoPosition",
            hr);
        DsError.ThrowExceptionForHR(hr);
    }

    public void RepaintVideo(IntPtr hdc)
    {
        IVMRWindowlessControl9? windowlessControl = _vmrWindowlessControl;
        if (_disposed || !IsRunning || windowlessControl is null ||
            _previewHostHandle == IntPtr.Zero || hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = windowlessControl.RepaintVideo(_previewHostHandle, hdc);
        }
        catch (COMException)
        {
        }
        catch (InvalidComObjectException)
        {
        }
    }

    public void RecordPreviewHostEvent(string eventName, Control previewHost)
    {
        _diagnostics?.RecordHostEvent(eventName, previewHost);
    }

    public Task StopAsync()
    {
        GraphResources? resources;

        lock (_lifecycleLock)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            resources = DetachGraph();
            if (resources is null)
            {
                return Task.CompletedTask;
            }

            _stopTask = Task.Run(() => StopGraph(resources));
        }

        return CompleteStopAsync(_stopTask);
    }

    public void Stop()
    {
        _ = StopAsync();
    }

    private async Task CompleteStopAsync(Task stopTask)
    {
        try
        {
            await stopTask.ConfigureAwait(false);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_stopTask, stopTask))
                {
                    _stopTask = null;
                }
            }
        }
    }

    private GraphResources? DetachGraph()
    {
        IsRunning = false;
        IsAudioConnected = false;
        VideoStandardDescription = "Not detected";
        VideoFormatDescription = "Driver default";
        VideoPinDescription = "Not connected";
        RunningGraphReport = "No active DirectShow graph.";
        AudioDescription = "Not connected";

        if (_graph is null && _captureGraph is null && _sourceFilter is null &&
            _videoRenderer is null && _audioSampleGrabberFilter is null && _audioNullRenderer is null &&
            _audioSampleGrabber is null && _mediaControl is null && _videoWindow is null &&
            _vmrWindowlessControl is null)
        {
            return null;
        }

        var resources = new GraphResources(
            _graph,
            _captureGraph,
            _sourceFilter,
            _videoRenderer,
            _audioSampleGrabberFilter,
            _audioNullRenderer,
            _audioSampleGrabber,
            _mediaControl,
            _videoWindow,
            _vmrWindowlessControl);

        _graph = null;
        _captureGraph = null;
        _sourceFilter = null;
        _videoRenderer = null;
        _audioSampleGrabber = null;
        _audioNullRenderer = null;
        _audioSampleGrabberFilter = null;
        _audioCallback = null;
        _mediaControl = null;
        _videoWindow = null;
        _vmrWindowlessControl = null;
        _previewHostHandle = IntPtr.Zero;
        return resources;
    }

    private static void StopGraph(GraphResources resources)
    {
        try
        {
            resources.MediaControl?.Stop();
        }
        catch
        {
            // A failed driver stop must not prevent the application from exiting.
        }

        if (resources.VideoWindow is not null)
        {
            try
            {
                resources.VideoWindow.put_Visible(OABool.False);
                resources.VideoWindow.put_Owner(IntPtr.Zero);
            }
            catch
            {
                // Continue releasing COM objects.
            }
        }

        ReleaseAudioBranch(resources);
        ReleaseCom(resources.VmrWindowlessControl);
        ReleaseCom(resources.VideoWindow);
        ReleaseCom(resources.VideoRenderer);
        ReleaseCom(resources.MediaControl);
        ReleaseCom(resources.SourceFilter);
        ReleaseCom(resources.CaptureGraph);
        ReleaseCom(resources.Graph);
    }

    private static void ReleaseAudioBranch(GraphResources resources)
    {
        if (resources.AudioSampleGrabber is not null)
        {
            try
            {
                resources.AudioSampleGrabber.SetCallback(null, 0);
            }
            catch
            {
                // Continue releasing COM objects.
            }
        }

        ReleaseCom(resources.AudioSampleGrabber);
        ReleaseCom(resources.AudioNullRenderer);
        ReleaseCom(resources.AudioSampleGrabberFilter);
    }

    private void ReleaseAudioBranch()
    {
        ReleaseAudioBranch(new GraphResources(
            null,
            null,
            null,
            null,
            _audioSampleGrabberFilter,
            _audioNullRenderer,
            _audioSampleGrabber,
            null,
            null,
            null));

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

        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            // Do not invoke a capture-driver method while WinForms is closing.
            // The OS will reclaim this process-owned graph after the window exits.
            DetachGraph();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private sealed record GraphResources(
        IGraphBuilder? Graph,
        ICaptureGraphBuilder2? CaptureGraph,
        IBaseFilter? SourceFilter,
        IBaseFilter? VideoRenderer,
        IBaseFilter? AudioSampleGrabberFilter,
        IBaseFilter? AudioNullRenderer,
        ISampleGrabber? AudioSampleGrabber,
        IMediaControl? MediaControl,
        IVideoWindow? VideoWindow,
        IVMRWindowlessControl9? VmrWindowlessControl);

    private static void ReleaseCom(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
            // Continue releasing the remaining graph interfaces.
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

internal enum DirectShowRendererMode
{
    Default,
    Vmr9Windowless
}

internal sealed class AudioLevelEventArgs : EventArgs
{
    public AudioLevelEventArgs(int level)
    {
        Level = Math.Clamp(level, 0, 100);
    }

    public int Level { get; }
}

internal sealed class DirectShowPreviewStartDiagnostics
{
    private readonly StringBuilder _report = new();
    private string _currentPhase = "not started";

    public DirectShowPreviewStartDiagnostics(
        string deviceName,
        string devicePath,
        DirectShowRendererMode rendererMode,
        bool startedAfterPreviousStop,
        Control? previewHost = null)
    {
        Record("Tape Lady Capture Suite - VMR9 Preview Diagnostics");
        Record($"Renderer mode: {rendererMode}");
        Record($"Started after previous Stop: {startedAfterPreviousStop}");
        Record($"Device name: {deviceName}");
        Record($"Device path: {devicePath}");
        if (previewHost is not null)
        {
            RecordHostEvent("Start host state", previewHost);
        }
    }

    public void BeginPhase(string phase)
    {
        _currentPhase = phase;
        Record($"PHASE BEGIN: {phase}");
    }

    public void CompletePhase(string phase) => Record($"PHASE COMPLETE: {phase}");

    public void LogStartFailure(Exception exception) => LogException("START FAILURE", exception);

    public void LogNonFatalFailure(Exception exception) => LogException("NON-FATAL FAILURE", exception);

    public void RecordHResult(string operation, int hr) =>
        Record($"HRESULT {operation}: 0x{hr:X8}");

    public void RecordHostEvent(string eventName, Control previewHost)
    {
        string parentType = previewHost.Parent?.GetType().FullName ?? "<none>";
        IntPtr parentHandle = previewHost.Parent?.IsHandleCreated == true
            ? previewHost.Parent.Handle
            : IntPtr.Zero;
        Record(
            $"Host event: {eventName}; HWND=0x{previewHost.Handle.ToInt64():X}; " +
            $"IsHandleCreated={previewHost.IsHandleCreated}; ClientSize={previewHost.ClientSize}; " +
            $"Bounds={previewHost.Bounds}; Visible={previewHost.Visible}; " +
            $"ParentType={parentType}; ParentHWND=0x{parentHandle.ToInt64():X}; " +
            $"UIThreadId={Environment.CurrentManagedThreadId}");
    }

    public void Record(string message) => _report.AppendLine(message);

    public string BuildReport(string graphTopology)
    {
        var report = new StringBuilder(_report.ToString());
        report.AppendLine();
        report.AppendLine("Graph topology and connected video media type:");
        report.AppendLine(graphTopology);
        return report.ToString().TrimEnd();
    }

    private void LogException(string category, Exception exception)
    {
        Record($"{category}; phase={_currentPhase}; exceptionType={exception.GetType().FullName}; message={exception.Message}");
        Record($"{category}; stackTrace={exception.StackTrace ?? "<none>"}");
        if (exception.InnerException is not null)
        {
            Record($"{category}; innerExceptionType={exception.InnerException.GetType().FullName}; innerMessage={exception.InnerException.Message}; innerStackTrace={exception.InnerException.StackTrace ?? "<none>"}");
        }
        Record($"{category}; completeException={exception}");
    }
}
