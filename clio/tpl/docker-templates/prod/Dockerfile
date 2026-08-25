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
#
# Scope of the group-0 grant, stated precisely so it is not over-read:
#   * `g=rX` tree-wide gives group READ (and execute on directories) everywhere.
#   * Group WRITE is re-granted only on /app itself, the two subtrees the chart
#     writes, and the two config files it overwrites.
#   * /app itself MUST be group-writable: the chart creates NEW files there (its
#     start-up fail-fast writes /app/.probe, and ConnectionStrings.config is
#     copied in). A writable directory implies group 0 can delete and recreate
#     any top-level entry, including assemblies. So `g=rX` on the assemblies
#     prevents IN-PLACE modification, not replacement. That is inherent to the
#     chart's write requirement, not an oversight — do not read it as immutability.
#
# `ln -sT` deliberately omits -f: if a link path already exists as a real file or
# directory the build fails loudly rather than silently replacing it. The layer is
# therefore not idempotent over an already-hardened base, which is intended.
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
