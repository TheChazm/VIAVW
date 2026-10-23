namespace LayerHost;

internal record HiddenCaptureTrial(string Strategy, string CaptureMode, CaptureFrame? Frame, string? Error,
    bool NativeVisibleBeforeCapture, bool NativeVisibleAfterCapture, bool NativeHiddenAfter,
    long StyleBefore, long StyleAfter, double Milliseconds);
