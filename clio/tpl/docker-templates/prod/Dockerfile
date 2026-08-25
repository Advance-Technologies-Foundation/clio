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

# OpenShift / arbitrary non-root UID hardening (mirrors core PR #1076).
# The k8s chart (tools/creatio-helm) overrides ENTRYPOINT, so entrypoint.sh
# and supervisord never run in-cluster; their root assumptions are out of
# scope here and affect standalone `docker run` (root by default) only.
# No USER directive: OpenShift assigns a UID from the project range (gid 0).
RUN mkdir -p /app/conf /app/Terrasoft.Configuration && \
    chmod -R g=rX /app && \
    chmod g=u /app && \
    chmod -R g=u /app/conf /app/Terrasoft.Configuration && \
    find /app -maxdepth 1 \( -name ConnectionStrings.config -o -name Terrasoft.WebHost.dll.config \) -exec chmod g=u {} + && \
    find /app -type f -name '*.pfx' -exec chmod 0640 {} + && \
    ln -sT /app/conf /conf && \
    ln -sT /app/Terrasoft.Configuration /Terrasoft.Configuration && \
    test -e /conf && test -e /Terrasoft.Configuration

EXPOSE 5000 5002

ENTRYPOINT ["/entrypoint.sh"]
