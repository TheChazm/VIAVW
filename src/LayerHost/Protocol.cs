using System.Text.Json;

namespace LayerHost;

public enum ControlOwner { Automation, Human }
public enum CaptureBackend { PrintWindow, WindowsGraphicsCapture, CooperativeFixture, WindowsGraphicsCaptureFromDefault, PrintWindowFullContent, ReusableWindowsGraphicsCapture }
public enum MouseMessage : uint { Move = 0x200, LeftDown = 0x201, LeftUp = 0x202, LeftDoubleClick = 0x203, RightDown = 0x204, RightUp = 0x205, RightDoubleClick = 0x206, Wheel = 0x20A, Hover = 0x2A1, Leave = 0x2A3 }
public record MouseStep(MouseMessage Message, int X, int Y, int Buttons = 0, int Delta = 0);
public enum PresentedInputKind { Text, KeyChord, MouseSequence }
public record PresentedInput(PresentedInputKind Kind, long Window = 0, string? Text = null, int[]? Keys = null, MouseStep[]? Steps = null);
public record PresentedInteraction(CaptureFrame? Frame, bool InitiallyHidden, bool HiddenAfter, double Milliseconds, bool ActivationNotified = false);
public record WindowInfo(long Handle, long Parent, int ProcessId, string ClassName, string Text, bool Visible, bool Enabled, int X, int Y, int Width, int Height, long Owner = 0);
public record CaptureFrame(byte[] Png, int Width, int Height, double CaptureMilliseconds, string Backend = "PrintWindow", int CaptureWorkerProcessId = 0, double? NativeRenderingMilliseconds = null, double? EncodingMilliseconds = null, int? SourceClientX = null, int? SourceClientY = null);
internal record PopupSurface(long Handle, int X, int Y, int Width, int Height, int FrameX, int FrameY, bool Clipped, uint Style, uint ExtendedStyle);
internal record PopupPresentation(CaptureFrame Frame, PopupSurface[] Surfaces);
internal record Request(string Operation, long Window = 0, string? Text = null, int Key = 0, int X = 0, int Y = 0, string? Executable = null, string[]? Arguments = null, string? Directory = null, int[]? AllowedProcessIds = null, bool Posted = false, uint Message = 0, int Buttons = 0, int Delta = 0, int[]? Keys = null, CaptureBackend Backend = CaptureBackend.PrintWindow, Dictionary<string, string>? EnvironmentOverrides = null, long TargetWindow = 0, MouseStep[]? Steps = null, bool PrimeCapture = false, PresentedInput[]? Inputs = null, bool CaptureAfter = true, int SettleMilliseconds = 150, bool NotifyActivation = false, bool FollowFocusOnKeyUp = false, bool ReleaseStateBeforeKeyUp = false, bool WarmupCapture = false, bool FollowPrivateFocus = false, bool PacedMouse = false, bool LowLatencyInput = false, long PreviewTicket = 0, bool PreciseMouseWaits = false,int PreciseChordHoldMilliseconds = 0,bool TextControlKeys=false);
internal record Reply(bool Success, JsonElement Data, string? Error = null, string? ErrorDetails = null)
{
    public static Reply Ok(object? data) => new(true, JsonSerializer.SerializeToElement(data));
    public static Reply Fail(Exception exception) => new(false, JsonSerializer.SerializeToElement<object?>(null), exception.Message, exception.ToString());
}
