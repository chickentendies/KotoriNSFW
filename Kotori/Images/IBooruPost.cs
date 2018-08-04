namespace Kotori.Images
{
    public interface IBooruPost
    {
        string PostId { get; }
        string PostUrl { get; }
        string FileUrl { get; }
        string FileHash { get; }
        string FileExtension { get; }
        string Rating { get; }
    }
}
