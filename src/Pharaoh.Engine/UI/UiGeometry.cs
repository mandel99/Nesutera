namespace Pharaoh.Engine.UI;

public readonly record struct UiPoint(int X, int Y);

public readonly record struct UiRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(UiPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public UiRect Offset(int x, int y) => new(Left + x, Top + y, Right + x, Bottom + y);
}
