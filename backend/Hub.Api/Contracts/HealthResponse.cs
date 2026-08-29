namespace Hub.Api.Contracts;

/// <summary>
/// Kết quả của <c>GET /health</c>.
/// </summary>
/// <remarks>
/// Là record có kiểu tường minh, không phải anonymous object: OpenAPI cần một kiểu
/// đặt tên được để sinh schema, và frontend sinh kiểu TypeScript từ schema đó
/// (CONTEXT.md §3). Anonymous object sẽ ra schema vô danh, không dùng lại được.
/// </remarks>
/// <param name="Status">Trạng thái backend. <c>"ok"</c> khi phục vụ được.</param>
/// <param name="Utc">Thời điểm máy chủ trả lời, theo UTC.</param>
public sealed record HealthResponse(string Status, DateTimeOffset Utc);
