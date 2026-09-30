namespace CDriveSmartClean.Application.Scanning.Traversal;

public sealed class StorageRecallSensitiveException(string path)
    : IOException("Recall-sensitive directory traversal was blocked: " + path)
{
}
