using Kotori.Images;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;

namespace Kotori
{
    public sealed class TwitterBot : IDisposable
    {
        public const int REQUEST_INTERVAL = 200;

        public bool IsDisposed { get; private set; }
        public bool IsRefreshingCache { get; private set; }

        public readonly int Id;
        public readonly string Name;
        public readonly DatabaseManager Database;
        public readonly TwitterClient Client;
        public readonly IEnumerable<IBooru> Boorus;
        
        public TwitterBot(DatabaseManager db, IEnumerable<IBooru> boorus, TwitterBotInfo info, TwitterClient client)
        {
            Id = info.Id;
            Name = info.Name;
            Database = db;
            Boorus = boorus;
            Client = client;
        }

        public IBooruPost GetRandomPost()
        {
            try
            {
                using (SQLiteCommand fetchPost = Database.Command($@"
                    SELECT `post_id`, `post_url`, `post_rating`, `file_url`, `file_hash`, `file_extension`
                    FROM `booru_posts`
                    WHERE `bot_id` = {Id}
                    ORDER BY RANDOM()
                    LIMIT 1
                "))
                using (SQLiteDataReader dr = fetchPost.ExecuteReader())
                    if (dr.Read())
                        return new DatabaseBooruPost
                        {
                            PostId = dr.GetString(0),
                            PostUrl = dr.GetString(1),
                            Rating = dr.GetString(2),
                            FileUrl = dr.GetString(3),
                            FileHash = dr.GetString(4),
                            FileExtension = dr.GetString(5),
                        };
            }
            catch { }

            return null;
        }

        public void DeletePost(string postId)
        {
            using (SQLiteCommand deletePost = Database.Command($@"DELETE FROM `booru_posts` WHERE `bot_id` = {Id} AND `post_id` = @post"))
            {
                deletePost.Parameters.Add(new SQLiteParameter(@"@post", postId));
                deletePost.Prepare();
                deletePost.ExecuteNonQuery();
            }
        }

        public bool EnsureCacheReady()
        {
            if (IsRefreshingCache)
                return false;

            using (SQLiteCommand check = Database.Command($@"SELECT COUNT(`post_id`) > 0 FROM `booru_posts` WHERE `bot_id` = {Id}"))
            using (SQLiteDataReader dr = check.ExecuteReader())
                if (dr.Read() && dr.GetBoolean(0))
                    return true;

            return false;
        }

        public void RefreshCache()
        {
            if (IsRefreshingCache)
                return;
            IsRefreshingCache = true;
            string[] tags = null;

            try
            {
                using (SQLiteCommand getTags = Database.Command($@"SELECT `bot_tags` FROM `bots` WHERE `bot_id` = {Id}"))
                using (SQLiteDataReader dr = getTags.ExecuteReader())
                    if (dr.Read())
                        tags = dr.GetString(0).Split(' ');
            }
            catch { }

            if (tags == null || tags.Length < 1 || tags.Any(string.IsNullOrEmpty))
            {
                Console.WriteLine(@"Unable to fetch tags.");
                IsRefreshingCache = false;
                return;
            }

            using (SQLiteCommand insert = Database.Command($@"
                INSERT INTO `booru_posts`
                    (`bot_id`, `post_id`, `post_url`, `post_rating`, `file_url`, `file_hash`, `file_extension`)
                VALUES
                    ({Id}, @id, @url, @rating, @file, @hash, @ext)
            "))
            using (SQLiteCommand check = Database.Command($@"
                SELECT COUNT(`post_id`) > 0
                FROM `booru_posts`
                WHERE `bot_id` = {Id}
                AND `file_hash` = @hash
            "))
                foreach (IBooru booru in Boorus)
                {
                    IEnumerable<IBooruPost> posts;
                    int page = 0;

                    while ((posts = booru.GetPosts(tags, page++, booru.MaxPostsPerPage)) != null)
                    {
                        foreach (IBooruPost post in posts)
                        {
                            if (string.IsNullOrEmpty(post.FileHash) || post.Rating != @"s") // hardcoding worksafe for now
                                continue;

                            check.Parameters.Clear();
                            check.Parameters.Add(new SQLiteParameter(@"@hash", post.FileHash));
                            check.Prepare();

                            using (SQLiteDataReader dr = check.ExecuteReader())
                                if (dr.Read() && dr.GetBoolean(0))
                                    continue;

                            insert.Parameters.Clear();
                            insert.Parameters.AddRange(new[] {
                                new SQLiteParameter(@"@id", post.PostId),
                                new SQLiteParameter(@"@url", post.PostUrl),
                                new SQLiteParameter(@"@rating", post.Rating),
                                new SQLiteParameter(@"@file", post.FileUrl),
                                new SQLiteParameter(@"@hash", post.FileHash),
                                new SQLiteParameter(@"@ext", post.FileExtension),
                            });
                            insert.Prepare();
                            insert.ExecuteNonQuery();
                        }

                        Thread.Sleep(REQUEST_INTERVAL);
                    }
                }

            IsRefreshingCache = false;
        }

        ~TwitterBot()
            => Dispose(false);

        public void Dispose()
            => Dispose(true);

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            Client.Dispose();

            if (disposing)
                GC.SuppressFinalize(this);
        }
    }
}
