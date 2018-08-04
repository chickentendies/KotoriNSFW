namespace Kotori.Images.Konachan
{
    public sealed class KonachanWorksafePost : KonachanPost
    {
        public override string PostUrl => $@"https://konachan.net/post/show/{PostId}";
    }
}
