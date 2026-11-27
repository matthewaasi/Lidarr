# syntax=docker/dockerfile:1
FROM node:24-bookworm-slim AS frontend
WORKDIR /src
COPY package.json yarn.lock ./
RUN npm install --global --force yarn@1.22.22 && yarn install --frozen-lockfile --network-timeout 120000
COPY tsconfig.json ./
COPY frontend/ frontend/
RUN node node_modules/webpack/bin/webpack.js --config frontend/build/webpack.config.js --env production

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS backend
WORKDIR /src
COPY global.json .editorconfig ./
COPY src/ src/
COPY Logo/ Logo/
RUN dotnet publish src/NzbDrone.Console/Lidarr.Console.csproj -c Release -f net10.0 -r linux-musl-x64 --self-contained true \
    -p:RuntimeIdentifiers=linux-musl-x64 -p:SolutionDir=/src/src/ -p:AssemblyVersion=4.0.0.5247 -p:AssemblyConfiguration=nightly-ai -o /out \
    && dotnet publish src/NzbDrone.Mono/Lidarr.Mono.csproj -c Release -f net10.0 -r linux-musl-x64 --self-contained true \
    -p:RuntimeIdentifiers=linux-musl-x64 -p:SolutionDir=/src/src/ -p:AssemblyVersion=4.0.0.5247 -p:AssemblyConfiguration=nightly-ai -o /out

FROM ghcr.io/linuxserver/lidarr:nightly-4.0.0.5247-ls230
ARG FORK_VERSION=4.0.0.5247-ai.31
ARG VCS_REF=unknown
RUN rm -rf /app/lidarr/bin && mkdir -p /app/lidarr/bin
COPY --from=backend /out/ /app/lidarr/bin/
COPY --from=frontend /src/_output/UI/ /app/lidarr/bin/UI/
COPY LICENSE.md /app/lidarr/bin/LICENSE.md
COPY LICENSES/ /app/lidarr/bin/LICENSES/
RUN chmod 755 /app/lidarr/bin/Lidarr /app/lidarr/bin/fpcalc \
    && printf 'UpdateMethod=docker\nBranch=nightly\nPackageVersion=%s\nPackageAuthor=Lidarr AI local fork\n' "$FORK_VERSION" > /app/lidarr/package_info
ENV LIDARR_AI_MODE=Review \
    LIDARR_AI_BASE_URL=http://10.0.0.88:1234/v1 \
    LIDARR_AI_MODEL=qwen/qwen3.8-27b
LABEL org.opencontainers.image.title="Lidarr AI Matcher" \
      org.opencontainers.image.version="${FORK_VERSION}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.source="https://github.com/matthewaasi/Lidarr" \
      org.opencontainers.image.licenses="GPL-3.0"
EXPOSE 8686
VOLUME /config
