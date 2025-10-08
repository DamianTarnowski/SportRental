using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace SportRental.Admin.Services.Storage
{
    public sealed class S3FileStorage : IFileStorage, IDisposable
    {
        private readonly IAmazonS3 _s3;
        private readonly string _bucket;
        private readonly string _publicBaseUrl;

        public S3FileStorage(IConfiguration config)
        {
            var endpoint = config["Storage:S3:Endpoint"];
            var region = config["Storage:S3:Region"] ?? Amazon.RegionEndpoint.EUWest1.SystemName;
            var accessKey = config["Storage:S3:AccessKey"];
            var secretKey = config["Storage:S3:SecretKey"];
            _bucket = config["Storage:S3:Bucket"] ?? throw new InvalidOperationException("Missing Storage:S3:Bucket");
            _publicBaseUrl = config["Storage:S3:PublicBaseUrl"] ?? string.Empty; // np. https://cdn.example.com/

            var cfg = new AmazonS3Config {
                ServiceURL = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint,
                ForcePathStyle = true,
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            };
            _s3 = new AmazonS3Client(accessKey, secretKey, cfg);
        }

        public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream(content);
            return await SaveAsync(relativePath, ms, ct);
        }

        public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
        {
            var key = relativePath.Replace("\\", "/");
            var put = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = content,
                AutoCloseStream = false,
                CannedACL = S3CannedACL.PublicRead,
                Headers = { CacheControl = "public, max-age=31536000, immutable" }
            };
            await _s3.PutObjectAsync(put, ct);
            return BuildPublicUrl(key);
        }

        public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
        {
            var key = relativePath.Replace("\\", "/");
            using var res = await _s3.GetObjectAsync(_bucket, key, ct);
            using var ms = new MemoryStream();
            await res.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }

        public async Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        {
            try
            {
                var key = relativePath.Replace("\\", "/");
                var req = new GetObjectMetadataRequest { BucketName = _bucket, Key = key };
                await _s3.GetObjectMetadataAsync(req, ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        private string BuildPublicUrl(string key)
        {
            if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
                return _publicBaseUrl.TrimEnd('/') + "/" + key;
            // domyślny URL S3/R2, jeśli brak CDN-u
            return $"https://{_bucket}.s3.amazonaws.com/{key}";
        }

        public void Dispose() => _s3?.Dispose();
    }
}


