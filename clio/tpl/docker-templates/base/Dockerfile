FROM mcr.microsoft.com/dotnet/sdk:8.0

ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false \
    DEBIAN_FRONTEND=noninteractive \
    TZ=Europe/Kiev

RUN apt-get update && \
    apt-get -y --no-install-recommends install \
    libgdiplus \
    libc6-dev \
    gss-ntlmssp \
    supervisor && \
    apt-get clean all && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/* && \
    sed -i 's/openssl_conf/#openssl_conf/g' /etc/ssl/openssl.cnf && \
    mkdir -p /var/log/supervisor

# GH #1161: this stage intentionally does not switch to a non-root USER. It has no
# ENTRYPOINT/CMD of its own - it exists only as the shared FROM ${BASE_IMAGE} layer
# for the dev and prod leaf templates (see BuildDockerImageService), which are out
# of scope for #1161. dev/Dockerfile legitimately needs root for its own RUN steps
# (apt-get, sshd/user setup) and to run supervisord/sshd at container start.
# prod/Dockerfile has no such steps - it only runs supervisord/creatio-webhost, an
# unprivileged process - and is swept into this same restriction only because it
# shares this base file, not because it independently needs root; a prod-only
# hardening pass (its own USER plus the chown that entails) is a separate, deferred
# follow-up. A USER switch here would silently break the dev build today since it
# does not re-elevate. A future non-root-capable leaf image built from this base is
# free to add its own USER.
