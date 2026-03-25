ARG BASE_IMAGE=creatio-base:8.0-v1
FROM ${BASE_IMAGE}

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
