using System.Drawing;

namespace GameMapMerge;

class MapInfo : IDisposable
{
    private readonly Dictionary<Point, DisposableHolder<Bitmap>> _map = [];
    public (Point, Point) LTRB
    {
        get
        {
            Point lt = Point.Empty;
            Point rb = Point.Empty;
            bool first = true;
            foreach (Point p in _map.Keys)
            {
                if (first)
                {
                    lt = p;
                    rb = p;
                    first = false;
                }
                else
                {
                    lt.X = Math.Min(lt.X, p.X);
                    lt.Y = Math.Min(lt.Y, p.Y);
                    rb.X = Math.Max(rb.X, p.X);
                    rb.Y = Math.Max(rb.Y, p.Y);
                }
            }
            return (lt, rb);
        }
    }
    public int UnitSize => _map.Values.Max(it => Math.Max(it.Value.Width, it.Value.Height));
    public bool IsEmpty => _map.Count == 0;
    public DisposableHolder<Bitmap>? Fallback
    {
        get => field;
        set
        {
            field?.Decrease();
            value?.Increase();
            field = value;
        }
    } = null;
    public DisposableHolder<Bitmap>? this[int x, int y]
    {
        get => _map.TryGetValue(new(x, y), out DisposableHolder<Bitmap>? bmp) ? bmp : Fallback;
        set
        {
            if (value is null)
                _map.Remove(new(x, y));
            else
                _map[new(x, y)] = value;
        }
    }

    public void Dispose()
    {
        Fallback?.Decrease();
        foreach (DisposableHolder<Bitmap> bmp in _map.Values)
            bmp.Decrease();
        GC.SuppressFinalize(this);
    }
}