namespace FeedGem.Services
{
    public static class SiteUrlHelper
    {
        // サイトごとのURL補正
        public static string Normalize(string url)
        {
            // FC2対策
            if (url.Contains("blog.fc2.com"))
            {
                if (!url.Contains("?xml"))
                {
                    url = url.TrimEnd('/') + "/?xml";
                }

                if (!url.Contains("&all"))
                {
                    url += "&all";
                }
            }

            return url;
        }
    }
}