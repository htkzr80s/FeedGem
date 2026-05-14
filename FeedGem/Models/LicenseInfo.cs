namespace FeedGem.Models
{
    // JSONの1項目に対応するクラス
    public class LicenseInfo
    {
        public string? PackageId { get; set; }
        public string? PackageVersion { get; set; }
        public string? PackageProjectUrl { get; set; }
        public string? Copyright { get; set; }
        public string? Authors { get; set; }
        public string? License { get; set; }
        public string? LicenseUrl { get; set; }
    }
}