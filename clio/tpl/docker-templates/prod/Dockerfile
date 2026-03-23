FROM mcr.microsoft.com/dotnet/sdk:8.0 AS creatio-base

ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false \
    DEBIAN_FRONTEND=noninteractive

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
      libgdiplus \
      libc6-dev \
      gss-ntlmssp \
      supervisor && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/* && \
    sed -i 's/openssl_conf/#openssl_conf/g' /etc/ssl/openssl.cnf && \
    mkdir -p /var/log/supervisor


FROM creatio-base

LABEL org.creatio.capability.ssh="false" \
      org.creatio.capability.code-server="false"
      
COPY entrypoint.sh /entrypoint.sh
COPY supervisord.conf /etc/supervisor/conf.d/supervisord.conf
RUN chmod +x /entrypoint.sh

WORKDIR /app
COPY source/ ./
RUN sed -i 's|<add key="CookiesSameSiteMode" value="[^"]*" */>|<add key="CookiesSameSiteMode" value="Lax" />|g' /app/Terrasoft.WebHost.dll.config && \
    sed -i 's/"CustomizeDataProtection": false/"CustomizeDataProtection": true/g' /app/appsettings.json

EXPOSE 5000 5002

ENTRYPOINT ["/entrypoint.sh"]
