namespace LayerHost;

internal static class PresentedInputValidation
{
    internal static void Check(PresentedInput[]? inputs, int settleMilliseconds)
    {
        if (inputs == null || inputs.Length is < 1 or > 8 || settleMilliseconds is < 0 or > 500) throw new ArgumentException("Use 1–8 input commands and a 0–500 ms settle interval.");
        int text = 0, steps = 0;
        foreach (var input in inputs)
        {
            if (input == null || !Enum.IsDefined(input.Kind)) throw new ArgumentException("Unsupported private presentation input.");
            switch (input.Kind)
            {
                case PresentedInputKind.Text:
                    if (input.Text == null || input.Keys != null || input.Steps != null) throw new ArgumentException("Text command requires only text."); text += input.Text.Length; break;
                case PresentedInputKind.KeyChord:
                    if (input.Text != null || input.Steps != null || input.Keys == null || input.Keys.Length is < 1 or > 8 || input.Keys.Any(key => key is < 1 or > 255)) throw new ArgumentException("Chord requires 1–8 valid keys."); break;
                case PresentedInputKind.MouseSequence:
                    if (input.Text != null || input.Keys != null || input.Steps == null || input.Steps.Length is < 1 or > 32 || input.Steps.Any(step => step == null || !Enum.IsDefined(step.Message) || step.Buttons < 0 || (step.Buttons & ~0x1f) != 0) || input.Steps[^1].Buttons != 0) throw new ArgumentException("Mouse sequence requires 1–32 steps ending with no buttons.");
                    int held = 0;
                    foreach (var step in input.Steps)
                    {
                        if (step.Message is MouseMessage.LeftDown or MouseMessage.LeftDoubleClick) held |= 1;
                        if (step.Message is MouseMessage.RightDown or MouseMessage.RightDoubleClick) held |= 2;
                        if (step.Message == MouseMessage.LeftUp) held &= ~1;
                        if (step.Message == MouseMessage.RightUp) held &= ~2;
                    }
                    if (held != 0) throw new ArgumentException("Every mouse down in a private input burst must have a matching up.");
                    steps += input.Steps.Length; break;
            }
        }
        if (text > 4096 || steps > 32) throw new ArgumentException("Bound the entire input burst to 4096 UTF-16 units and 32 mouse steps.");
    }
}
