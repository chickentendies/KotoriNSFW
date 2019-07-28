using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace Kotori
{
    public static partial class Program
    {
        private const string LEGACY_CONFIG = @"Kotori.ini";

        public static void MigrateIni()
        {
            if (!File.Exists(LEGACY_CONFIG) || Cancellation.IsCancellationRequested)
                return;

            Logger.Write(@"Migrating legacy configuration...");

            string[] lines = File.ReadAllLines(LEGACY_CONFIG);
            string section = string.Empty;

            string[] consumerData = new string[2];
            Dictionary<string, Dictionary<string, string>> bots = new Dictionary<string, Dictionary<string, string>>();

            foreach (string line in lines)
            {
                if (line.StartsWith(@";"))
                    continue;

                if (line.StartsWith(@"[") && line.EndsWith(@"]"))
                {
                    section = line.TrimStart('[').TrimEnd(']');
                    continue;
                }

                string[] parts = line.Split('=', 2);

                if (parts.Length != 2)
                    continue;

                parts[0] = parts[0].Trim();
                parts[1] = parts[1].Trim();

                if (section == "Bot")
                {
                    switch (parts[0])
                    {
                        case @"ConsumerKey":
                            consumerData[0] = parts[1];
                            break;

                        case @"ConsumerSecret":
                            consumerData[1] = parts[1];
                            break;
                    }
                    continue;
                }

                if (section.StartsWith(@"Bot."))
                {
                    string botName = section.Substring(4);
                    if (botName.Contains(@"."))
                        botName = botName.Substring(0, botName.IndexOf('.'));

                    if (!bots.ContainsKey(botName))
                        bots.Add(botName, new Dictionary<string, string> {
                            { @"access_token", string.Empty },
                            { @"access_token_secret", string.Empty },
                            { @"rating", @"s" },
                        });

                    if (!section.Contains(@".Source."))
                    {
                        switch (parts[0])
                        {
                            case @"OAToken":
                                bots[botName][@"access_token"] = parts[1];
                                break;

                            case @"OASecret":
                                bots[botName][@"access_token_secret"] = parts[1];
                                break;

                            case @"MaxRating":
                                switch (parts[1])
                                {
                                    case @"Questionable":
                                        bots[botName][@"rating"] = @"q";
                                        break;
                                    case @"Explicit":
                                        bots[botName][@"rating"] = @"e";
                                        break;
                                }
                                break;
                        }
                    } else if (parts[0] == @"Tags" && !bots[botName].ContainsKey(@"tags"))
                        bots[botName].Add(@"tags", parts[1]);
                }
            }

            Database.WriteConfig("twitter_consumer_key", consumerData[0]);
            Database.WriteConfig("twitter_consumer_secret", consumerData[1]);

            SQLiteCommand cmd = Database.Command(@"
                INSERT OR IGNORE INTO `bots`
                    (`bot_name`, `bot_tags`, `bot_access_token`, `bot_access_token_secret`, `bot_rating`)
                VALUES
                    (@name, @tags, @access_token, @access_token_secret, @rating)
            ");

            foreach (KeyValuePair<string, Dictionary<string, string>> bot in bots)
            {
                Console.WriteLine($@"Importing '{bot.Key}' (will be ignored if already exists)...");

                if (!bot.Value.ContainsKey(@"tags") || string.IsNullOrEmpty(bot.Value[@"tags"]))
                {
                    Console.WriteLine($@"Failed to import: Missing tags.");
                    continue;
                }

                cmd.Parameters.Add(new SQLiteParameter(@"@name", bot.Key));

                foreach (KeyValuePair<string, string> kvp in bot.Value)
                    cmd.Parameters.Add(new SQLiteParameter($@"@{kvp.Key}", kvp.Value));

                cmd.Prepare();
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
            }

            Console.WriteLine(@"Renaming old config (obsolete)...");
            File.Move(LEGACY_CONFIG, LEGACY_CONFIG + @".old");
        }
    }
}
