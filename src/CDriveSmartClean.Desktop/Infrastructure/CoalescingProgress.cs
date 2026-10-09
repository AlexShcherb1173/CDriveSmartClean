using CDriveSmartClean.Runtime;

namespace CDriveSmartClean.Desktop.Infrastructure;

internal sealed class CoalescingProgress : IProgress<ProductScanProgress>, IDisposable
{
    private readonly object gate = new();
    private readonly SynchronizationContext context;
    private readonly Action<ProductScanProgress> handler;
    private ProductScanProgress? latest;
    private bool deliveryPending;
    private bool disposed;

    internal CoalescingProgress(SynchronizationContext context, Action<ProductScanProgress> handler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        this.context = context;
        this.handler = handler;
    }

    public void Report(ProductScanProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool schedule;
        lock (gate)
        {
            if (disposed) return;
            latest = value;
            schedule = !deliveryPending;
            deliveryPending = true;
        }
        if (schedule) PostDelivery();
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; latest = null; }
    }

    private void PostDelivery()
    {
        try { context.Post(static state => ((CoalescingProgress)state!).Deliver(), this); }
        catch (InvalidOperationException) { Dispose(); }
    }

    private void Deliver()
    {
        ProductScanProgress? value;
        lock (gate)
        {
            if (disposed) { latest = null; deliveryPending = false; return; }
            value = latest;
            latest = null;
        }
        if (value is not null) handler(value);
        bool schedule;
        lock (gate)
        {
            if (disposed) { latest = null; deliveryPending = false; return; }
            schedule = latest is not null;
            deliveryPending = schedule;
        }
        if (schedule) PostDelivery();
    }
}
