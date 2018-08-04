﻿using Kotori.Images;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;

namespace Kotori
{
    public sealed class TwitterBot : IDisposable
    {
        private const int CACHE_SLEEP = 250;

        public bool IsDisposed { get; private set; }

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

        public bool EnsureCacheReady()
        {
            using (SQLiteCommand check = Database.Command($@"SELECT COUNT(`post_id`) > 0 FROM `booru_posts` WHERE `bot_id` = {Id}")) // you can't really exploit a hard in anyway
            using (SQLiteDataReader dr = check.ExecuteReader())
                if (dr.Read() && dr.GetBoolean(0))
                    return true;

            return false;
        }

        public void RefreshCache()
        {
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
                Environment.Exit(-1);
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
                            if (string.IsNullOrEmpty(post.FileHash))
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

                        Thread.Sleep(CACHE_SLEEP);
                    }
                }
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
