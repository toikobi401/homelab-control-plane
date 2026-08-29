# syntax=docker/dockerfile:1
#
# Ảnh cho Hub.Api. Build từ THƯ MỤC GỐC của repo, không phải từ thư mục này:
#     docker build -f backend/Hub.Api/Dockerfile .
# Lý do: cần global.json và Directory.Build.props ở gốc, nếu không build ra
# sẽ khác với build trên máy (khác SDK, thiếu thuộc tính chung).
#
# CONTEXT.md §3.3 — đóng gói được bằng Docker để sau chuyển sang NAS.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Chép trước phần định nghĩa dự án rồi restore. Tách bước để Docker cache được:
# sửa code mà không đụng .csproj thì không phải restore lại.
COPY global.json Directory.Build.props ./
COPY backend/Hub.Core/Hub.Core.csproj      backend/Hub.Core/
COPY backend/Hub.Data/Hub.Data.csproj      backend/Hub.Data/
COPY backend/Hub.Manga/Hub.Manga.csproj    backend/Hub.Manga/
COPY backend/Hub.Windows/Hub.Windows.csproj backend/Hub.Windows/
COPY backend/Hub.Api/Hub.Api.csproj        backend/Hub.Api/

RUN dotnet restore backend/Hub.Api/Hub.Api.csproj

COPY backend/ backend/

# Không cần -r linux-x64: ảnh runtime đã đúng nền tảng rồi.
RUN dotnet publish backend/Hub.Api/Hub.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Tạo sẵn /data và trao cho user 'app' TRƯỚC khi hạ quyền. Docker tạo volume
# rỗng theo quyền của điểm mount có sẵn trong ảnh — không làm bước này thì
# volume thuộc root và app (uid 1654) chỉ đọc được, SQLite sẽ chết khi ghi.
RUN mkdir -p /data && chown app:app /data

# Chạy bằng user không phải root. Ảnh .NET có sẵn user 'app' (uid 1654).
# Trên NAS (Synology/QNAP) uid của share thường khác — khi đó đặt
# `user: "1026:100"` trong compose và chown thư mục trên NAS cho khớp,
# không sửa Dockerfile.
USER app

# §4: trong container bind 0.0.0.0 là an toàn — đây là network namespace riêng,
# không phải mọi card mạng của máy thật. Ranh giới bảo vệ nằm ở cổng publish
# trong compose.yaml (bắt buộc gắn IP tailnet). Xem NetworkBinding.cs.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    HUB_BIND_MODE=Container \
    HUB_DATA_DIR=/data

EXPOSE 8080

# /data là volume — file SQLite nằm đây, không nằm trong lớp ảnh.
# Chuyển sang NAS chỉ là trỏ volume này sang share của NAS.
VOLUME ["/data"]

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Hub.Api.dll"]
