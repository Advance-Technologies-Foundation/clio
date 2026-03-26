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
