using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;

namespace Kotori
{
    public static class Program
    {
        public const string CONSUMER_FILE = @"consumer.txt";
        public const string TEST_USER_FILE = @"test_user.txt";

        public static void Main(string[] args)
        {
            Console.WriteLine(@"Kotori REDUX v1");
            Console.WriteLine(@"THis is still experimental, don't look at it.");

            DatabaseShit();
            MigrateIni();
            //TwitterShit();

            Console.ReadLine();
        }

        public static void WriteUserCreds(string file, string token, string tokenSecret)
        {
            File.WriteAllLines(file, new[] { token, tokenSecret });
        }

        public static string[] ReadUserCreds(string file)
        {
            try
            {
                return File.ReadAllLines(file);
            } catch
            {
                return new string[0];
            }
        }

        public static void MigrateIni()
        {
            if (!File.Exists(@"Kotori.ini"))
                return;

            string[] lines = File.ReadAllLines(@"Kotori.ini");

            foreach (string line in lines)
            {
                // shitty fwini parser here
            }

            File.Move(@"Kotori.ini", @"Kotori.ini.old");
        }

        public static void DatabaseShit()
        {
            SQLiteConnection conn = new SQLiteConnection(@"Data Source=kotori_test.db");
            conn.Open();

            new SQLiteCommand(
                @"CREATE TABLE `bots` (
                    `bot_id` INTEGER PRIMARY KEY AUTOINCREMENT UNIQUE,
                    `bot_name` TEXT NOT NULL,
                    `bot_is_active` INTEGER DEFAULT 1,
                    `bot_tags` TEXT NOT NULL,
                    `bot_access_token` TEXT DEFAULT NULL,
                    `bot_access_token_secret` TEXT DEFAULT NULL
                )",
            conn);

            new SQLiteCommand(
                @"CREATE TABLE `images` (
	                `bot_id`	INTEGER,
	                `image_id`	TEXT,
	                `image_page`	TEXT,
	                `image_hash`	TEXT,
	                `image_url`	TEXT,
	                FOREIGN KEY(`bot_id`) REFERENCES `bots`(`bot_id`) ON UPDATE CASCADE ON DELETE CASCADE
                )",
            conn);

            conn.Dispose();
        }

        public static void TwitterShit()
        {
            string[] meow = ReadUserCreds(CONSUMER_FILE);

            if (meow.Length != 2)
                throw new Exception(@"no consumer key and secret found");

            IConsumerCredentials cc = new ConsumerCredentials(meow[0], meow[1]);
            ITwitterCredentials tc;

            if (!cc.AreSetupForApplicationAuthentication())
                throw new Exception(@"Application isn't set up for auth.");

            string[] userCreds = ReadUserCreds(TEST_USER_FILE);

            if (userCreds.Length != 2)
            {
                IAuthenticationContext ac = AuthFlow.InitAuthentication(cc);
                Console.WriteLine(ac.AuthorizationURL);

                string pin = Console.ReadLine();
                tc = AuthFlow.CreateCredentialsFromVerifierCode(pin, ac);
                WriteUserCreds(TEST_USER_FILE, tc.AccessToken, tc.AccessTokenSecret);
            } else
                tc = new TwitterCredentials(cc.ConsumerKey, cc.ConsumerSecret, userCreds[0], userCreds[1]);

            Auth.ExecuteOperationWithCredentials(tc, () => {
                return Tweet.PublishTweet(@"testing stored credentials");
            });
        }
    }
}
