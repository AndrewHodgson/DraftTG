namespace DraftTG.App.Platform;

internal sealed record WindowCaptureBounds(int X, int Y, int Width, int Height)
{
    public ArenaDraftCrop MapCrop(ArenaWindowGeometry client, ArenaDraftCrop crop)
    {
        var x = checked(client.X - X + crop.X);
        var y = checked(client.Y - Y + crop.Y);
        if (x < 0 || y < 0 || x + (long)crop.Width > Width || y + (long)crop.Height > Height)
            throw new InvalidDataException("Arena client crop lies outside the WGC window surface.");
        return crop with { X = x, Y = y };
    }
}
