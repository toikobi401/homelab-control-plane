namespace Hub.Api.Hosting;

/// <summary>
/// Chế độ `--healthcheck`: gọi /health trên chính container rồi trả mã thoát
/// cho Docker. Làm thế này để khỏi phải cài curl/wget vào ảnh runtime —
/// ảnh mỏng hơn và ít thứ phải vá bảo mật hơn.
/// </summary>
internal static class HealthCheckClient
{
    public static async Task<int> RunAsync()
    {
        // Gọi vào chính mình qua loopback của container, không phải qua cổng
        // đã publish — healthcheck chạy BÊN TRONG container.
        var url = Environment.GetEnvironmentVariable("HUB_HEALTHCHECK_URL")
            ?? "http://127.0.0.1:8080/health";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            await Console.Error.WriteLineAsync(
                $"healthcheck: {url} trả về {(int)response.StatusCode}.");
            return 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"healthcheck: gọi {url} thất bại — {ex.Message}");
            return 1;
        }
    }
}
