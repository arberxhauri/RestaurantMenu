# Multi-arch: the SDK runs natively on the build machine (e.g. an Apple Silicon Mac) and
# cross-publishes for the target platform, e.g.
#   docker buildx build --platform linux/amd64 -t mqm:latest --load .
# Render builds this same file (amd64) and uses its own port; see ASPNETCORE_URLS below.

# -------- Build stage --------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, from the project file only, so the layer is reused until dependencies change.
# Only the app is copied: the solution also lists RestaurantMenu.Tests, so restore the .csproj.
COPY RestaurantMenu/RestaurantMenu.csproj RestaurantMenu/
RUN dotnet restore RestaurantMenu/RestaurantMenu.csproj -a $TARGETARCH

COPY RestaurantMenu/ RestaurantMenu/
RUN dotnet publish RestaurantMenu/RestaurantMenu.csproj -c Release -a $TARGETARCH --no-restore \
        --self-contained false -o /out \
    # Sources from the exFAT drive arrive as rwx------; the app runs as non-root and only reads them.
    && chmod -R u=rwX,go=rX /out

# -------- Runtime stage --------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Mount points for the server's named volumes (uploads, data-protection keys), owned by the
# image's non-root "app" user so new volumes start out writable when the container runs as it.
# /keys changes nothing by itself: the app only uses it when DataProtection__KeysPath is set.
RUN mkdir -p /var/data/images /keys && chown -R $APP_UID:$APP_UID /var/data /keys

COPY --from=build /out .

# Render's port. The Hetzner compose file overrides this with ASPNETCORE_URLS=http://+:8080.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "RestaurantMenu.dll"]
