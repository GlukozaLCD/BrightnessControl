namespace BrightnessControl.Core;

public sealed record BrightnessLevel(uint Minimum, uint Current, uint Maximum)
{
    public int Percent => Maximum <= Minimum
        ? 0
        : (int)Math.Round((Current - Minimum) * 100.0 / (Maximum - Minimum));
}
