# Web client build (Vite output lands in src/Argoscope.Api/wwwroot via vite.config.ts).
FROM node:22-bookworm-slim AS webbuild
WORKDIR /src
COPY web/package.json web/package-lock.json ./web/
RUN npm ci --prefix web
COPY web/ ./web/
COPY src/ ./src/
RUN npm run build --prefix web

# .NET publish. The wwwroot output from the webbuild stage is copied in
# so the published image serves the exact revision-tagged frontend.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnetbuild
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props NuGet.config Argoscope.sln ./
COPY src/ ./src/
COPY tests/ ./tests/
COPY --from=webbuild /src/src/Argoscope.Api/wwwroot ./src/Argoscope.Api/wwwroot
RUN dotnet publish src/Argoscope.Api/Argoscope.Api.csproj -c Release -o /app/publish --nologo

# Runtime image: non-root, no SDK, no secrets baked in.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS runtime
WORKDIR /app
RUN apt-get update \
  && apt-get install -y --no-install-recommends wget \
  && rm -rf /var/lib/apt/lists/* \
  && adduser --disabled-password --gecos "" --uid 10001 argoscope \
  && chown -R 10001:10001 /app
COPY --from=dotnetbuild --chown=10001:10001 /app/publish ./
USER 10001:10001
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
  ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/api/v1/health/live | grep -q '"status":"ok"' || exit 1
ENTRYPOINT ["dotnet", "Argoscope.Api.dll"]
