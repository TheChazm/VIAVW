using System.Diagnostics;

namespace LayerHost;

internal record VisualObservationResult(bool Matched, CaptureFrame? Frame, int Attempts, double ElapsedMilliseconds, double CaptureMilliseconds);

// The caller supplies the visual condition. This component sends no input and
// does not infer general application readiness. Existing capture watchdogs apply
// to an in-flight call; cancellation/deadline cannot interrupt native rendering.
internal static class VisualObservation
{
    internal static async Task<VisualObservationResult> WaitAsync(Func<CaptureFrame> capture, Func<CaptureFrame, bool> condition,
        Action validate, TimeSpan timeout, TimeSpan interval, CancellationToken cancellation = default)
    {
        if (timeout.TotalMilliseconds is < 1 or > 30000 || interval.TotalMilliseconds is < 15 or > 1000)
            throw new ArgumentException("Use a 1–30000 ms observation budget and a 15–1000 ms inspection interval.");
        var clock = Stopwatch.StartNew(); CaptureFrame? last = null; int attempts = 0; double captureMs = 0;
        while (clock.Elapsed < timeout)
        {
            cancellation.ThrowIfCancellationRequested();
            var result = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested(); validate();
                var captureClock = Stopwatch.StartNew(); var frame = capture(); double ms = captureClock.Elapsed.TotalMilliseconds;
                cancellation.ThrowIfCancellationRequested(); validate(); bool matched = condition(frame);
                cancellation.ThrowIfCancellationRequested(); validate(); return (Frame: frame, Milliseconds: ms, Matched: matched);
            }).ConfigureAwait(false);
            last = result.Frame; attempts++; captureMs += result.Milliseconds;
            if (clock.Elapsed >= timeout) break;
            if (result.Matched) return new(true, last, attempts, clock.Elapsed.TotalMilliseconds, captureMs);
            var remaining = timeout - clock.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining < interval ? remaining : interval, cancellation).ConfigureAwait(false);
        }
        cancellation.ThrowIfCancellationRequested(); validate();
        return new(false, last, attempts, clock.Elapsed.TotalMilliseconds, captureMs);
    }
}
