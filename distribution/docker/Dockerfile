# syntax=docker/dockerfile:1

FROM alpine:3.22

ARG TARGETARCH
ARG BRANCH
ARG VERSION

ENV XDG_CONFIG_HOME=/config/xdg \
    COMPlus_EnableDiagnostics=0

RUN apk add --no-cache icu-libs sqlite-libs tzdata && \
    mkdir -p /app /config && \
    chown 1000:1000 /config && \
    printf "UpdateMethod=docker\nBranch=%s\nPackageVersion=%s\n" "$BRANCH" "$VERSION" > /app/package_info

COPY ${TARGETARCH}/ /app/bin/

USER 1000:1000
EXPOSE 8989
VOLUME /config

ENTRYPOINT ["/app/bin/Sonarr", "-nobrowser", "-data=/config"]
