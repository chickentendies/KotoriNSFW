using System;
using System.Collections.Generic;
using System.IO;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;
using IOStream = System.IO.Stream;

namespace Kotori
{
    public sealed class TwitterClient : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public readonly ITwitterCredentials TwitterCredentials;

        public TwitterClient(ITwitterCredentials twitterCredentials)
        {
            TwitterCredentials = twitterCredentials;
        }

        public static IAuthenticationContext BeginCreateClient(IConsumerCredentials consumerCredentials)
        {
            return AuthFlow.InitAuthentication(consumerCredentials);
        }

        public static ITwitterCredentials EndCreateClient(IAuthenticationContext authenticationContext, string pin)
        {
            return AuthFlow.CreateCredentialsFromVerifierCode(pin, authenticationContext);
        }

        public ITweet PostTweet(string text, IEnumerable<IMedia> media = null)
        {
            IPublishTweetOptionalParameters parms = new PublishTweetOptionalParameters();

            if (media != null)
                parms.Medias.AddRange(media);

            return Auth.ExecuteOperationWithCredentials(TwitterCredentials, () => {
                return Tweet.PublishTweet(text, parms);
            });
        }

        public void PostTweet(string text, IMedia media)
            => PostTweet(text, new[] { media });

        public IMedia UploadMedia(byte[] bytes)
        {
            return Auth.ExecuteOperationWithCredentials(TwitterCredentials, () => {
                return Upload.UploadBinary(bytes);
            });
        }

        public IMedia UploadMedia(IOStream stream)
        {
            IMedia media = null;

            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                byte[] buffer = new byte[1024];

                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);

                ms.Seek(0, SeekOrigin.Begin);
                media = UploadMedia(ms.ToArray());
            }

            return media;
        }

        ~TwitterClient()
            => Dispose(false);

        public void Dispose()
            => Dispose(true);

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            if (disposing)
                GC.SuppressFinalize(this);
        }
    }
}
