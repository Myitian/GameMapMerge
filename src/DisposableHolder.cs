namespace GameMapMerge;

/// <summary>
/// reference counter
/// </summary>
public sealed class DisposableHolder<T>(T value) : IDisposable where T : IDisposable
{
    public T Value { get; } = value;
    private int _count = 0;
    public void Increase()
    {
        if (_count < 0)
            return;
        _count++;
    }
    public void Decrease()
    {
        if (_count < 0)
            return;
        _count--;
        if (_count == 0)
            Dispose();
    }
    public void Dispose()
    {
        _count = -1;
        Value.Dispose();
        GC.SuppressFinalize(this);
    }
}