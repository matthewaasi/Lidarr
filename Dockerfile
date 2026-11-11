# syntax=docker/dockerfile:1
FROM node:22-bookworm-slim AS frontend
WORKDIR /src
COPY package.json yarn.lock ./
RUN npm install --global --force yarn@1.22.22 && yarn install --frozen-lockfile --network-timeout 120000
COPY frontend/ frontend/
RUN node node_modules/webpack/bin/webpack.js --config frontend/build/webpack.config.js --env production

FROM mcr.microsoft.com/dotnet/sdk:8.0.421 AS backend
WORKDIR /src
COPY global.json ./
COPY src/ src/
COPY Logo/ Logo/
RUN dotnet publish src/NzbDrone.Console/Lidarr.Console.csproj -c Release -f net8.0 -r linux-musl-x64 --self-contained true \
    -p:RuntimeIdentifiers=linux-musl-x64 -p:SolutionDir=/src/src/ -p:AssemblyVersion=3.2.1.5196 -p:AssemblyConfiguration=nightly-ai -o /out \
    && dotnet publish src/NzbDrone.Mono/Lidarr.Mono.csproj -c Release -f net8.0 -r linux-musl-x64 --self-contained true \
    -p:RuntimeIdentifiers=linux-musl-x64 -p:SolutionDir=/src/src/ -p:AssemblyVersion=3.2.1.5196 -p:AssemblyConfiguration=nightly-ai -o /out

FROM ghcr.io/linuxserver/lidarr:nightly-3.2.1.5196-ls224@sha256:d1e3b469e618fa54b56bc487442efc231142096db3188cd3a4d5b1393b919e5e
RUN rm -rf /app/lidarr/bin && mkdir -p /app/lidarr/bin
COPY --from=backend /out/ /app/lidarr/bin/
COPY --from=frontend /src/_output/UI/ /app/lidarr/bin/UI/
COPY LICENSE.md /app/lidarr/bin/LICENSE.md
COPY LICENSES/ /app/lidarr/bin/LICENSES/
RUN chmod 755 /app/lidarr/bin/Lidarr \
    && printf 'UpdateMethod=docker\nBranch=nightly\nPackageVersion=3.2.1.5196-ai.7\nPackageAuthor=Lidarr AI local fork\n' > /app/lidarr/package_info
ENV LIDARR_AI_MODE=Review \
    LIDARR_AI_BASE_URL=http://10.0.0.88:1234/v1 \
    LIDARR_AI_MODEL=qwen/qwen3.8-27b
LABEL org.opencontainers.image.title="Lidarr AI Matcher" \
      org.opencontainers.image.version="3.2.1.5196-ai.7" \
      org.opencontainers.image.revision="6af1274a11957270a308fd0448ffe624a8e264d0+ai.7" \
      org.opencontainers.image.licenses="GPL-3.0"
EXPOSE 8686
VOLUME /config
