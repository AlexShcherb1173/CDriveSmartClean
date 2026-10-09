namespace CDriveSmartClean.Analysis;

internal sealed class AnalysisResourceGuard(long budget)
{
    private long charged;

    internal bool TryCharge(long amount, out bool overflow, out long attempted)
    {
        overflow = false;
        attempted = 0;
        try
        {
            long next = checked(charged + amount);
            attempted = next;
            if (next > budget) return false;
            charged = next;
            return true;
        }
        catch (OverflowException)
        {
            overflow = true;
            return false;
        }
    }

    internal void Clear() => charged = 0;
}
