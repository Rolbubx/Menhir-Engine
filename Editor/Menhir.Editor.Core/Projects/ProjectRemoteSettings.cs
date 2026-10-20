namespace Menhir.Editor.Projects;

/// <summary>What happens to collaborators' sessions when the project's owner quits.</summary>
public enum OwnerLeavePolicy
{
    /// <summary>Their sessions end and unsaved work is lost.</summary>
    TerminateWithoutSaving,

    /// <summary>Their sessions end after their work is saved (the remote keeps it in a private GitHub repository).</summary>
    TerminateAndSave,

    /// <summary>They keep working; one becomes the second admin, and the owner gets their version on reconnecting.</summary>
    HandOverToCollaborator,
}

/// <summary>How collaborators receive the project's files.</summary>
public enum AssetDelivery
{
    /// <summary>Only streamed chunks, nothing kept on their machine (security level 1).</summary>
    StreamedChunks,

    /// <summary>An encrypted copy and a secret key; files uploaded later need a newer key.</summary>
    EncryptedCopy,
}

/// <summary>
/// Settings of a project shared through a remote, saved in ProjectSettings.json.
/// Remote collaboration itself is not implemented yet: these only record how it should behave.
/// Never store tokens or keys here: this file is committed with the project.
/// </summary>
public sealed class ProjectRemoteSettings
{
    public string Url { get; set; } = "";

    public OwnerLeavePolicy OnOwnerLeave { get; set; } = OwnerLeavePolicy.TerminateAndSave;

    public AssetDelivery AssetDelivery { get; set; } = AssetDelivery.EncryptedCopy;

    /// <summary>An absolute http:// or https:// URL with a host.</summary>
    public static bool IsValidUrl(string url, out string? error)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
        {
            error = $"'{url.Trim()}' is not a valid URL (for example http://my-server:8080).";
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "The remote must be an http:// or https:// URL.";
            return false;
        }
        error = null;
        return true;
    }
}
