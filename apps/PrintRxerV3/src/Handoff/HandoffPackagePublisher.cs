namespace PrintRxerV3.Handoff;

public sealed class HandoffPackagePublisher
{
    public HandoffPublishResult TryPublishPending(string localOutboxRoot, string handoffRoot, string publishedRoot)
    {
        Directory.CreateDirectory(localOutboxRoot);
        Directory.CreateDirectory(publishedRoot);

        HandoffPublishResult lastPublished = HandoffPublishResult.None;
        foreach (string packageDirectory in Directory.EnumerateDirectories(localOutboxRoot).OrderBy(Directory.GetCreationTimeUtc))
        {
            if (!File.Exists(Path.Combine(packageDirectory, "READY")))
            {
                continue;
            }

            HandoffPublishResult result = TryPublish(packageDirectory, handoffRoot, publishedRoot);
            if (!result.Published)
            {
                return result;
            }

            lastPublished = result;
        }

        return lastPublished;
    }

    public HandoffPublishResult TryPublish(string localPackageDirectory, string handoffRoot, string publishedRoot)
    {
        string packageId = Path.GetFileName(localPackageDirectory);
        try
        {
            Directory.CreateDirectory(handoffRoot);
            Directory.CreateDirectory(publishedRoot);

            string finalDirectory = Path.Combine(handoffRoot, packageId);
            if (Directory.Exists(finalDirectory))
            {
                ExistingPackageState existingState = EvaluateExistingFinalPackage(localPackageDirectory, finalDirectory);
                if (existingState == ExistingPackageState.Incomplete)
                {
                    return new HandoffPublishResult(packageId, finalDirectory, false, "PackagePublishDeferred", "Final package folder already exists but is incomplete.");
                }

                if (existingState == ExistingPackageState.Mismatched)
                {
                    return new HandoffPublishResult(packageId, finalDirectory, false, "PackagePublishFailed", "Final package folder already exists but does not match the queued package.");
                }

                MoveToPublished(localPackageDirectory, publishedRoot);
                return new HandoffPublishResult(packageId, finalDirectory, true, "PackagePublished");
            }

            string uploadingDirectory = Path.Combine(handoffRoot, ".uploading-" + packageId + "-" + Guid.NewGuid().ToString("N")[..8]);
            CopyPackageWithoutReady(localPackageDirectory, uploadingDirectory);
            File.Copy(Path.Combine(localPackageDirectory, "READY"), Path.Combine(uploadingDirectory, "READY"), overwrite: false);
            Directory.Move(uploadingDirectory, finalDirectory);
            MoveToPublished(localPackageDirectory, publishedRoot);
            return new HandoffPublishResult(packageId, finalDirectory, true, "PackagePublished");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new HandoffPublishResult(packageId, null, false, "PackagePublishDeferred", ex.Message);
        }
        catch (Exception ex)
        {
            return new HandoffPublishResult(packageId, null, false, "PackagePublishFailed", ex.Message);
        }
    }

    private static ExistingPackageState EvaluateExistingFinalPackage(string localPackageDirectory, string finalDirectory)
    {
        string[] requiredFiles = ["request.json", "prescription.pdf", "request.sha256", "summary.txt", "READY"];
        foreach (string fileName in requiredFiles)
        {
            string localFile = Path.Combine(localPackageDirectory, fileName);
            string finalFile = Path.Combine(finalDirectory, fileName);
            if (!File.Exists(localFile) || !File.Exists(finalFile))
            {
                return ExistingPackageState.Incomplete;
            }

            if (!FilesMatch(localFile, finalFile))
            {
                return ExistingPackageState.Mismatched;
            }
        }

        return ExistingPackageState.CompleteMatch;
    }

    private static bool FilesMatch(string firstPath, string secondPath)
    {
        FileInfo first = new(firstPath);
        FileInfo second = new(secondPath);
        if (first.Length != second.Length)
        {
            return false;
        }

        using FileStream firstStream = File.OpenRead(firstPath);
        using FileStream secondStream = File.OpenRead(secondPath);
        byte[] firstBuffer = new byte[81920];
        byte[] secondBuffer = new byte[81920];
        while (true)
        {
            int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead)
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }

            for (int index = 0; index < firstRead; index++)
            {
                if (firstBuffer[index] != secondBuffer[index])
                {
                    return false;
                }
            }
        }
    }

    private static void CopyPackageWithoutReady(string sourceDirectory, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            if (Path.GetFileName(sourceFile).Equals("READY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(sourceFile, Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)), overwrite: false);
        }
    }

    private static void MoveToPublished(string localPackageDirectory, string publishedRoot)
    {
        string destination = Path.Combine(publishedRoot, Path.GetFileName(localPackageDirectory));
        if (Directory.Exists(destination))
        {
            destination += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        Directory.Move(localPackageDirectory, destination);
    }
}

public sealed record HandoffPublishResult(string PackageId, string? PublishedDirectory, bool Published, string Outcome, string Message = "")
{
    public static HandoffPublishResult None { get; } = new(string.Empty, null, false, "NoPendingPackage");
}

internal enum ExistingPackageState
{
    CompleteMatch,
    Incomplete,
    Mismatched
}
