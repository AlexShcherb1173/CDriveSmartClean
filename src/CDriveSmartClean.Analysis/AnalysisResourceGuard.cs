namespace CDriveSmartClean.Analysis;

internal sealed class AnalysisResourceGuard(long budget)
{
    private long charged;

    internal bool TryCharge(long amount, out bool overflow)
    {
        overflow = false;
        try
        {
            long next = checked(charged + amount);
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
