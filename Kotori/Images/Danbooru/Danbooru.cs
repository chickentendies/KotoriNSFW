using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Kotori.Images.Danbooru
{
    public sealed class Danbooru : IBooru
    {
        public readonly HttpClient HttpClient;
        
        public Danbooru(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public int MaxPostsPerPage => 1000;

        public IEnumerable<IBooruPost> GetPosts(string[] tags, int page = 0, int limit = 10)
        {
            page += 1; // danbooru's pagination starts at 1
            List<DanbooruPost> output = null;

            HttpClient.GetAsync($@"https://danbooru.donmai.us/posts.json?limit={limit}&page={page}&tags={string.Join(@"+", tags)}").ContinueWith(hrm => {
                if (!hrm.IsCompletedSuccessfully)
                    return;

                hrm.Result.Content.ReadAsStringAsync().ContinueWith(res => {
                    output = JsonConvert.DeserializeObject<List<DanbooruPost>>(res.Result);
                }).Wait();
            }).Wait();

            if (output == null)
                return null;

            output = output.Where(p => !p.IsBanned && !p.IsDeleted && !string.IsNullOrEmpty(p.FileUrl)).ToList();

            if (output.Count < 1)
                return null;

            return output;
        }
    }
}
