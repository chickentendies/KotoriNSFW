using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace Kotori
{
    public sealed class DatabaseManager : IDisposable
    {
#if DEBUG
        private const string DBNAME = @"KotoriTest.db";
#else
        private const string DBNAME = @"Kotori.db";
#endif

        public static DatabaseManager Instance { get; private set; }
        public bool IsDisposed { get; private set; }
        private readonly SQLiteConnection Connection;

        private const int DATABASE_VERSION = 1;

        public DatabaseManager()
        {
            if (Instance != null)
                throw new Exception(@"A DatabaseManager instance already exists.");
            Instance = this;
            Connection = new SQLiteConnection($@"Data Source={DBNAME}").OpenAndReturn();

            if (Connection?.State != ConnectionState.Open)
            {
                Dispose();
                throw new Exception(@"Failed to open database.");
            }
        }

        public SQLiteCommand Command(string command)
        {
            return new SQLiteCommand(command, Connection);
        }

        public string ReadConfig(string key)
        {
            string value = null;

            using (SQLiteCommand getConfig = Command(@"SELECT `value` FROM `config` WHERE `key` = @key"))
            {
                getConfig.Parameters.Add(new SQLiteParameter(@"@key", key));
                getConfig.Prepare();

                using (SQLiteDataReader dr = getConfig.ExecuteReader())
                    try
                    {
                        if (dr.Read())
                            value = dr.GetString(0);
                    }
                    catch { } // fuck the police
            }

            return value;
        }
        
        public string WriteConfig(string key, string value)
        {
            using (SQLiteCommand setConfig = Command(@"REPLACE INTO `config` VALUES (@key, @value)"))
            {
                setConfig.Parameters.AddRange(new[] {
                    new SQLiteParameter(@"@key", key),
                    new SQLiteParameter(@"@value", value),
                });
                setConfig.Prepare();
                setConfig.ExecuteNonQuery();
            }

            return value;
        }

        public IEnumerable<TwitterBotInfo> GetBots()
        {
            List<TwitterBotInfo> bots = new List<TwitterBotInfo>();

            using (SQLiteCommand getBots = Command(@"SELECT `bot_id`, `bot_name`, `bot_access_token`, `bot_access_token_secret` FROM `bots` WHERE `bot_is_active` != 0"))
            using (SQLiteDataReader dr = getBots.ExecuteReader())
                while (dr.Read())
                    try
                    {
                        object accessToken = dr.GetValue(2);
                        object accessTokenSecret = dr.GetValue(3);

                        bots.Add(new TwitterBotInfo
                        {
                            Id = dr.GetInt32(0),
                            Name = dr.GetString(1),
                            AccessToken = accessToken.GetType() == typeof(DBNull) ? null : (string)accessToken,
                            AccessTokenSecret = accessTokenSecret.GetType() == typeof(DBNull) ? null : (string)accessTokenSecret,
                        });
                    } catch { }

            return bots;
        }

        public void RunMigrations()
        {
            int version = 0;

            using (SQLiteCommand getVersion = Command(@"PRAGMA user_version;"))
            using (SQLiteDataReader dr = getVersion.ExecuteReader())
                if (dr.Read())
                    version = dr.GetInt32(0);

            if (version > DATABASE_VERSION)
                throw new Exception(@"This database is incompatible with this version of Kotori.");
            else if (version == DATABASE_VERSION)
                return;

            if (version < 1)
            {
                using (SQLiteCommand config = Command(@"CREATE TABLE `config` (`key` TEXT UNIQUE, `value` TEXT)"))
                    config.ExecuteNonQuery();

                using (SQLiteCommand bots = Command(
                    @"CREATE TABLE `bots` (
                        `bot_id`                    INTEGER PRIMARY KEY AUTOINCREMENT UNIQUE,
                        `bot_name`                  TEXT NOT NULL UNIQUE,
                        `bot_is_active`             INTEGER DEFAULT 1,
                        `bot_tags`                  TEXT NOT NULL,
                        `bot_access_token`          TEXT DEFAULT NULL,
                        `bot_access_token_secret`   TEXT DEFAULT NULL,
                        `bot_rating`                TEXT DEFAULT 's'
                    )"
                ))
                    bots.ExecuteNonQuery();

                using (SQLiteCommand posts = Command(
                    @"CREATE TABLE `booru_posts` (
	                    `bot_id`            INTEGER,
	                    `post_id`           TEXT,
	                    `post_url`          TEXT,
                        `post_rating`       TEXT,
	                    `file_url`          TEXT,
	                    `file_hash`         TEXT,
	                    `file_extension`    TEXT,
	                    FOREIGN KEY(`bot_id`) REFERENCES `bots`(`bot_id`) ON UPDATE CASCADE ON DELETE CASCADE
                    )"
                ))
                    posts.ExecuteNonQuery();
            }

            using (SQLiteCommand setVersion = Command($@"PRAGMA user_version = {DATABASE_VERSION}"))
                setVersion.ExecuteNonQuery();
        }

        ~DatabaseManager()
            => Dispose(false);

        public void Dispose()
            => Dispose(true);

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            Connection?.Dispose();
            Instance = null;

            if (disposing)
                GC.SuppressFinalize(this);
        }
    }
}
