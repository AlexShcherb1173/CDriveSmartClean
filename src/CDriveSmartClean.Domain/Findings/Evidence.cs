namespace CDriveSmartClean.Domain.Findings;

public sealed class Evidence
{
    public Evidence(string code, string description, Confidence confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        Code = code.Trim();
        Description = description.Trim();
        Confidence = confidence;
    }

    public string Code { get; }

    public string Description { get; }

    public Confidence Confidence { get; }
}
