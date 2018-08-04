using System.Collections.Generic;

namespace Kotori.Images
{
    public interface IBooru
    {
        int MaxPostsPerPage { get; }
        IEnumerable<IBooruPost> GetPosts(string[] tags, int page = 0, int limit = 10);
    }
}
